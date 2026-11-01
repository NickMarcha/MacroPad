using MacroPad.Core.HomeAssistant;

namespace MacroPad.Core.Tests;

public class ProfileTests
{
    static readonly Target Office = new(TargetKind.Area, "office", "Office");
    static readonly Target Kitchen = new(TargetKind.Area, "kitchen", "Kitchen");
    static readonly ushort Key1 = Triggers.Code(1, 1);
    static readonly ushort Knob = Triggers.Code(1, 17);

    static AppSettings TwoProfiles()
    {
        var s = new AppSettings();
        s.SetAction(Key1, new PadAction(PadCommand.Toggle, Office));
        s.SetAction(Knob, new PadAction(PadCommand.NextProfile));
        var cooking = new Profile { Name = "Cooking" };
        s.Profiles.Add(cooking);
        s.SetAction(cooking, Key1, new PadAction(PadCommand.Toggle, Kitchen));
        return s;
    }

    [Fact]
    public void Active_profile_overrides_and_falls_back_to_default()
    {
        var s = TwoProfiles();
        Assert.Equal(Office, s.ActionFor(Key1)!.Target);
        s.ActiveProfile = "Cooking";
        Assert.Equal(Kitchen, s.ActionFor(Key1)!.Target);
        // Cooking doesn't set the knob, so Default's "next profile" still works there.
        Assert.Equal(PadCommand.NextProfile, s.ActionFor(Knob)!.Command);
    }

    [Fact]
    public void Next_profile_wraps_round()
    {
        var s = TwoProfiles();
        Assert.Equal("Cooking", s.NextProfile().Name);
        Assert.Equal("Default", s.NextProfile().Name);
        Assert.Equal("Default", s.Active.Name);
    }

    [Fact]
    public void Unknown_active_profile_means_default()
    {
        var s = TwoProfiles();
        s.ActiveProfile = "Gone";
        Assert.Equal("Default", s.Active.Name);
    }

    [Fact]
    public void Profiles_survive_a_save()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("macropad-profiles").FullName, "settings.json");
        var s = TwoProfiles();
        s.ActiveProfile = "Cooking";
        s.PopupPosition = PopupPosition.TopRight;
        s.NextProfileShortcut = "Ctrl+Alt+P";
        s.Save(path);

        var loaded = AppSettings.Load(path);
        Assert.Equal(["Default", "Cooking"], loaded.Profiles.Select(p => p.Name));
        Assert.Equal(Kitchen, loaded.ActionFor(Key1)!.Target);
        Assert.Equal(PopupPosition.TopRight, loaded.PopupPosition);
        Assert.Equal("Ctrl+Alt+P", loaded.NextProfileShortcut);
        Assert.Contains("\"NextProfile\"", File.ReadAllText(path));
    }

    [Fact]
    public void Profile_actions_describe_themselves()
    {
        Assert.Equal("Next profile", new PadAction(PadCommand.NextProfile).Describe());
        Assert.Equal("Profile Cooking", new PadAction(PadCommand.Profile, ProfileName: "Cooking").Describe());
    }
}
