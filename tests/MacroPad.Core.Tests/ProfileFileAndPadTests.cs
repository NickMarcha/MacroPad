namespace MacroPad.Core.Tests;

public class ProfileFileAndPadTests
{
    static readonly Target Office = new(TargetKind.Area, "office", "Office");
    static readonly Target Desk = new(TargetKind.Audio, "headset/desk/playback", "Desk Headset · Speakers");

    static AppSettings WithProfiles()
    {
        var s = new AppSettings { HomeAssistantUrl = "https://ha.example/", Token = "secret-token", BragiKey = "secret-key" };
        s.SetAction(Triggers.Code(1, 1), new PadAction(PadCommand.Select, Office));
        s.Default.SelectedTarget = Office;
        var music = new Profile { Name = "Music", SelectedTarget = Desk };
        music.Actions[Triggers.Key(Triggers.Code(1, 1))] = new PadAction(PadCommand.Select, Desk);
        s.Profiles.Add(music);
        return s;
    }

    [Fact]
    public void Export_has_the_profiles_and_no_secrets()
    {
        var s = WithProfiles();
        var json = ProfileFile.Write(s.Profiles);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("ha.example", json);
        Assert.DoesNotContain("dpapi", json);
        var back = ProfileFile.Read(json);
        Assert.Equal(["Default", "Music"], back.Select(p => p.Name));
        Assert.Equal(Desk, back[1].SelectedTarget);
        Assert.Equal(PadCommand.Select, back[1].Actions.Single().Value.Command);
    }

    [Fact]
    public void Import_renames_taken_names_and_keeps_the_own_default_first()
    {
        var s = WithProfiles();
        var added = ProfileFile.AddTo(s, ProfileFile.Read(ProfileFile.Write(s.Profiles)));
        Assert.Equal(["Default (2)", "Music (2)"], added.Select(p => p.Name));
        Assert.Equal(["Default", "Music", "Default (2)", "Music (2)"], s.Profiles.Select(p => p.Name));
        Assert.Same(s.Profiles[0], s.Default);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"Format": "macropad-profiles", "Version": 1, "Profiles": []}""")]
    [InlineData("""{"Format": "macropad-profiles", "Version": 99, "Profiles": [{"Name": "x"}]}""")]
    public void Refuses_files_that_are_not_profiles(string json) =>
        Assert.Throws<FormatException>(() => ProfileFile.Read(json));

    [Fact]
    public void Each_pad_gets_its_own_block_of_codes()
    {
        Assert.Equal(0x0F01, Triggers.Code(1, 1));            // pad 0: what the first pad has always sent
        Assert.Equal(0x0F61, Triggers.Code(1, 1, pad: 1));
        Assert.Equal(1, Triggers.PadOf(Triggers.Code(3, 18, pad: 1)));
        Assert.Equal(0, Triggers.PadOf(Triggers.Code(3, 24, pad: 0)));   // the last knob slot stays in its block
        Assert.True(Triggers.IsTrigger(Triggers.Code(3, 24, pad: Triggers.MaxPads - 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Triggers.Code(1, 1, pad: Triggers.MaxPads));
    }

    [Fact]
    public void Pads_keep_their_numbers_and_new_ones_take_the_lowest_free()
    {
        var s = new AppSettings();
        Assert.Equal(0, s.PadFor("serial:a").Number);
        Assert.Equal(1, s.PadFor("serial:b").Number);
        Assert.Equal(0, s.PadFor("serial:a").Number);
        s.Pads.RemoveAll(p => p.Key == "serial:a");
        Assert.Equal(0, s.PadFor("serial:c").Number);
        Assert.Equal("Pad 1", s.PadFor("serial:c").Label);
    }
}
