using System.Net;
using MacroPad.Core.Bragi;

namespace MacroPad.Core.Tests;

public class BragiTests
{
    // Shaped like GET /api/v1/devices on a Bragi server.
    const string Devices = """
        {"devices": [
          {"id": "headset:usb-desk-headset", "kind": "headset", "key": "usb-desk-headset", "name": "Desk Headset", "enabled": true,
           "controls": {
             "playback": {"direction": "playback", "label": "Speakers", "volume": 0.56, "muted": false, "connected": true, "pannable": false},
             "capture":  {"direction": "capture", "label": "Mic", "volume": null, "muted": false, "connected": false, "pannable": false}}},
          {"id": "peer:studio", "kind": "peer", "key": "studio", "name": "studio", "protocol": "vban", "client_connected": null,
           "controls": {
             "outgoing": {"direction": "outgoing", "label": "Mic to studio", "volume": 1.0, "muted": false, "connected": true, "pannable": true, "balance": 0.0},
             "incoming": {"direction": "incoming", "label": "studio to headset", "volume": 0.6, "muted": true, "connected": true, "pannable": true, "balance": -0.2}}}
        ]}
        """;

    static readonly Target Speakers = new(TargetKind.Audio, "headset/usb-desk-headset/playback", "Desk Headset · Speakers");
    static readonly Target Mic = new(TargetKind.Audio, "headset/usb-desk-headset/capture", "Desk Headset · Mic");
    static readonly Target StudioIn = new(TargetKind.Audio, "peer/studio/incoming", "studio to headset");

    sealed class FakeBragi(HttpStatusCode patchStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Line, string Body, string? Auth)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(($"{request.Method} {request.RequestUri!.AbsolutePath}", body, request.Headers.Authorization?.ToString()));
            if (request.Method == HttpMethod.Patch)
                return new HttpResponseMessage(patchStatus) { Content = new StringContent("{}") };
            var path = request.RequestUri.AbsolutePath;
            var json = path == "/api/v1/devices" ? Devices : DeviceJson(path.Split('/')[^1]);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }

        static string DeviceJson(string key)
        {
            var devices = System.Text.Json.Nodes.JsonNode.Parse(Devices)!["devices"]!.AsArray();
            return devices.First(d => (string)d!["key"]! == key)!.ToJsonString();
        }
    }

    static BragiClient Client(FakeBragi fake) => new(new HttpClient(fake), new Uri("https://bragi.example/"), "k3y");

    [Fact]
    public async Task Every_control_is_a_target_named_for_people()
    {
        var targets = await Client(new FakeBragi()).GetTargetsAsync();
        Assert.Equal(
            ["Desk Headset · Speakers", "Desk Headset · Mic", "Mic to studio", "studio to headset"],
            targets.Select(t => t.Name));
        Assert.All(targets, t => Assert.Equal(TargetKind.Audio, t.Kind));
        Assert.Contains(StudioIn, targets);
    }

    [Fact]
    public async Task Reads_volume_as_percent_and_mute_as_off()
    {
        var fake = new FakeBragi();
        var speakers = await Client(fake).GetLevelAsync(Speakers);
        var studio = await Client(fake).GetLevelAsync(StudioIn);
        Assert.Equal(new Level(true, 56, 100, 150), speakers);
        Assert.False(studio.IsOn);
        Assert.Equal(60, studio.Percent);
        Assert.Equal("Bearer k3y", fake.Requests[0].Auth);
        Assert.Equal("GET /api/v1/devices/headset/usb-desk-headset", fake.Requests[0].Line);
    }

    [Fact]
    public async Task A_control_without_audio_says_so()
    {
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => Client(new FakeBragi()).GetLevelAsync(Mic));
        Assert.Contains("isn't connected", e.Message);
    }

    [Fact]
    public async Task Sets_volume_as_gain()
    {
        var fake = new FakeBragi();
        await Client(fake).SetValueAsync(StudioIn, 75);
        var (line, body, _) = fake.Requests.Single();
        Assert.Equal("PATCH /api/v1/devices/peer/studio/controls/incoming", line);
        Assert.Equal("{\"volume\":0.75}", body);
    }

    [Fact]
    public async Task Toggle_reads_mute_then_flips_it()
    {
        var fake = new FakeBragi();
        await Client(fake).RunAsync(PadCommand.Toggle, StudioIn);      // muted -> unmute
        await Client(fake).RunAsync(PadCommand.TurnOff, Speakers);   // mute
        Assert.Equal("{\"muted\":false}", fake.Requests[1].Body);
        Assert.Equal("{\"muted\":true}", fake.Requests[2].Body);
    }

    [Fact]
    public async Task Busy_controls_give_a_readable_error()
    {
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => Client(new FakeBragi(HttpStatusCode.Conflict)).SetValueAsync(Speakers, 50));
        Assert.Equal("Desk Headset · Speakers isn't connected right now.", e.Message);
    }

    [Fact]
    public void Describes_audio_actions_in_audio_words()
    {
        Assert.Equal("Mute or unmute studio to headset", new PadAction(PadCommand.Toggle, StudioIn).Describe());
        Assert.Equal("Studio to headset louder (step 5)", new PadAction(PadCommand.Up, StudioIn).Describe());
    }
}
