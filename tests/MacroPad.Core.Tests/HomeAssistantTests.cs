using System.Net;
using MacroPad.Core.HomeAssistant;

namespace MacroPad.Core.Tests;

public class HomeAssistantTests
{
    static readonly Target Office = new(TargetKind.Area, "office", "Office");
    static readonly Target Panel = new(TargetKind.Light, "light.living_panel", "Living Panel");

    [Fact]
    public void Trigger_codes_are_unique_and_in_range()
    {
        var codes = new HashSet<ushort>();
        for (int layer = 1; layer <= Protocol.LayerCount; layer++)
            for (int slot = 1; slot <= 24; slot++)
            {
                var code = Triggers.Code(layer, slot);
                Assert.True(Triggers.IsTrigger(code));
                Assert.True(codes.Add(code));
            }
        Assert.False(Triggers.IsTrigger(0x00E9));
    }

    [Fact]
    public void Settings_round_trip()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("macropad-settings").FullName, "settings.json");
        var s = new AppSettings { HomeAssistantUrl = "https://ha.example/", Token = "secret-token", KeepRunningInTray = false };
        s.SetAction(Triggers.Code(1, 2), new PadAction(PadCommand.Down, Panel, 15));
        s.Save(path);
        Assert.DoesNotContain(OperatingSystem.IsWindows() ? "secret-token" : "nothing-to-hide", File.ReadAllText(path));

        var loaded = AppSettings.Load(path);
        Assert.Equal("secret-token", loaded.Token);
        Assert.False(loaded.KeepRunningInTray);
        Assert.Equal(new PadAction(PadCommand.Down, Panel, 15), loaded.ActionFor(Triggers.Code(1, 2)));
        Assert.Null(loaded.ActionFor(Triggers.Code(1, 3)));
    }

    [Theory]
    [InlineData("https://ha.example.ts.net:8444/home/overview", "https://ha.example.ts.net:8444/")]
    [InlineData("homeassistant.local:8123", "https://homeassistant.local:8123/")]
    public void Normalizes_pasted_addresses(string input, string expected) =>
        Assert.Equal(expected, ServiceAddress.Normalize(input).ToString());

    [Fact]
    public async Task Area_toggle_turns_everything_off_when_anything_is_on()
    {
        var fake = new Recorder(templateAnswer: "1");
        await Client(fake).RunAsync(PadCommand.Toggle, Office);
        Assert.Equal("POST /api/template", fake.Requests[0].Line);
        Assert.Equal("POST /api/services/light/turn_off", fake.Requests[1].Line);
        Assert.Contains("\"area_id\":\"office\"", fake.Requests[1].Body);
    }

    [Fact]
    public async Task Area_toggle_turns_on_when_all_are_off()
    {
        var fake = new Recorder(templateAnswer: "0");
        await Client(fake).RunAsync(PadCommand.Toggle, Office);
        Assert.Equal("POST /api/services/light/turn_on", fake.Requests[1].Line);
    }

    [Fact]
    public async Task Knob_and_slider_set_absolute_brightness_without_a_fade()
    {
        var fake = new Recorder("");
        await Client(fake).SetValueAsync(Panel, 140);
        var (line, body, auth) = fake.Requests.Single();
        Assert.Equal("POST /api/services/light/turn_on", line);
        Assert.Contains("\"brightness\":140", body);
        Assert.Contains("\"transition\":0", body);
        Assert.Contains("\"entity_id\":\"light.living_panel\"", body);
        Assert.Equal("Bearer t0ken", auth);
    }

    [Fact]
    public async Task Only_lights_reach_home_assistant()
    {
        var fake = new Recorder("");
        var headset = new Target(TargetKind.Audio, "headset/x/playback", "Speakers");
        await Assert.ThrowsAsync<ArgumentException>(() => Client(fake).RunAsync(PadCommand.Toggle, Target.Selected));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(fake).RunAsync(PadCommand.Toggle, headset));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Client(fake).RunAsync(PadCommand.Up, Panel));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void Selected_target_survives_a_save()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("macropad-settings").FullName, "settings.json");
        var s = new AppSettings();
        s.Default.SelectedTarget = Office;
        s.Profiles.Add(new Profile { Name = "Audio" });
        s.Save(path);
        var loaded = AppSettings.Load(path);
        Assert.Equal(Office, loaded.Default.SelectedTarget);
        Assert.Null(loaded.Find("Audio")!.SelectedTarget);   // each profile keeps its own
    }

    [Fact]
    public void Knob_speeds_up_when_turned_fast()
    {
        var accel = new KnobAcceleration();
        TimeSpan T(int ms) => TimeSpan.FromMilliseconds(ms);
        Assert.Equal(5, accel.Step(5, 1, T(0)));        // first click
        Assert.Equal(5, accel.Step(5, 1, T(1000)));     // slow
        Assert.Equal(25, accel.Step(5, 1, T(1050)));    // 50 ms later: 5x
        Assert.Equal(40, accel.Step(5, 1, T(1060)));    // very fast: capped at 8x
        Assert.Equal(5, accel.Step(5, -1, T(1070)));    // changing direction starts slow again
    }

    [Fact]
    public async Task Reads_how_lit_an_area_is()
    {
        var fake = new Recorder(templateAnswer: "{\"on\": 1, \"total\": 2, \"brightness\": 127.5}");
        var level = await Client(fake).GetLevelAsync(Office);
        Assert.Equal(Level.Lights(1, 2, 128), level);
        Assert.Equal(50, level.Percent);
        Assert.Contains("area_entities", fake.Requests.Single().Body);
        Assert.Contains("office", fake.Requests.Single().Body);
    }

    [Fact]
    public async Task Slider_at_zero_turns_off_and_otherwise_sets_brightness()
    {
        var fake = new Recorder("");
        await Client(fake).SetBrightnessAsync(Panel, 0);
        await Client(fake).SetBrightnessAsync(Panel, 128);
        Assert.Equal("POST /api/services/light/turn_off", fake.Requests[0].Line);
        Assert.Equal("POST /api/services/light/turn_on", fake.Requests[1].Line);
        Assert.Contains("\"brightness\":128", fake.Requests[1].Body);
        await Client(fake).SetBrightnessAsync(Panel, 128, transition: 0);
        Assert.Contains("\"transition\":0", fake.Requests[2].Body);
        Assert.DoesNotContain("transition", fake.Requests[1].Body);
    }

    static HomeAssistantClient Client(Recorder fake) => new(new HttpClient(fake), new Uri("https://ha.example/"), "t0ken");

    sealed class Recorder(string templateAnswer) : HttpMessageHandler
    {
        public List<(string Line, string Body, string? Auth)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(($"{request.Method} {request.RequestUri!.AbsolutePath}", body, request.Headers.Authorization?.ToString()));
            var answer = request.RequestUri.AbsolutePath == "/api/template" ? templateAnswer : "[]";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
        }
    }
}
