using System.Net;
using MacroPad.Core.HomeAssistant;

namespace MacroPad.Core.Tests;

public class HomeAssistantTests
{
    static readonly HaTarget Office = new(HaTargetKind.Area, "office", "Office");
    static readonly HaTarget Panel = new(HaTargetKind.Light, "light.living_panel", "Living Panel");

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
        s.SetAction(Triggers.Code(1, 2), new HaAction(HaCommand.Dimmer, Panel, 15));
        s.Save(path);
        Assert.DoesNotContain(OperatingSystem.IsWindows() ? "secret-token" : "nothing-to-hide", File.ReadAllText(path));

        var loaded = AppSettings.Load(path);
        Assert.Equal("secret-token", loaded.Token);
        Assert.False(loaded.KeepRunningInTray);
        Assert.Equal(new HaAction(HaCommand.Dimmer, Panel, 15), loaded.ActionFor(Triggers.Code(1, 2)));
        Assert.Null(loaded.ActionFor(Triggers.Code(1, 3)));
    }

    [Theory]
    [InlineData("https://ha.example.ts.net:8444/home/overview", "https://ha.example.ts.net:8444/")]
    [InlineData("homeassistant.local:8123", "https://homeassistant.local:8123/")]
    public void Normalizes_pasted_addresses(string input, string expected) =>
        Assert.Equal(expected, HomeAssistantClient.NormalizeUrl(input).ToString());

    [Fact]
    public async Task Area_toggle_turns_everything_off_when_anything_is_on()
    {
        var fake = new Recorder(templateAnswer: "1");
        await Client(fake).RunAsync(new HaAction(HaCommand.Toggle, Office));
        Assert.Equal("POST /api/template", fake.Requests[0].Line);
        Assert.Equal("POST /api/services/light/turn_off", fake.Requests[1].Line);
        Assert.Contains("\"area_id\":\"office\"", fake.Requests[1].Body);
    }

    [Fact]
    public async Task Area_toggle_turns_on_when_all_are_off()
    {
        var fake = new Recorder(templateAnswer: "0");
        await Client(fake).RunAsync(new HaAction(HaCommand.Toggle, Office));
        Assert.Equal("POST /api/services/light/turn_on", fake.Requests[1].Line);
    }

    [Fact]
    public async Task Dimmer_steps_brightness_down()
    {
        var fake = new Recorder("");
        await Client(fake).RunAsync(new HaAction(HaCommand.Dimmer, Panel, 10));
        var (line, body, auth) = fake.Requests.Single();
        Assert.Equal("POST /api/services/light/turn_on", line);
        Assert.Contains("\"brightness_step_pct\":-10", body);
        Assert.Contains("\"entity_id\":\"light.living_panel\"", body);
        Assert.Equal("Bearer t0ken", auth);
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
