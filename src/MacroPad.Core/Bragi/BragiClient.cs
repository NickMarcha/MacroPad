using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace MacroPad.Core.Bragi;

/// <summary>
/// Talks to Bragi, a PipeWire audio mixer (github.com/NickMarcha/bragi; its server documents the API at /api/v1/help). Every control of every device is a target:
/// a headset's playback and capture, a peer's outgoing and incoming. A target's id is "kind/key/direction". Volume is
/// linear gain 0.0–1.5, handled here as percent 0–150; mute is the on/off.
/// </summary>
public sealed class BragiClient(HttpClient http, Uri baseUrl, string key) : ITargetService
{
    public Uri BaseUrl { get; } = baseUrl;

    public bool Handles(Target target) => target.Kind == TargetKind.Audio;

    /// <summary>Throws with a readable message when the address or key is wrong.</summary>
    public async Task CheckAsync(CancellationToken cancel = default)
    {
        using var _ = await SendAsync(HttpMethod.Get, "api/v1/devices", null, null, cancel);
    }

    public async Task<List<Target>> GetTargetsAsync(CancellationToken cancel = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/devices", null, null, cancel);
        if (JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel))?["devices"] is not JsonArray devices)
            throw new HttpRequestException("That address answered, but not like Bragi's API.");
        var targets = new List<Target>();
        foreach (var device in devices)
        {
            var kind = (string)device!["kind"]!;
            var key = (string)device["key"]!;
            var name = (string)device["name"]!;
            foreach (var (direction, control) in device["controls"]!.AsObject())
            {
                var label = (string?)control!["label"] ?? direction;
                // Peer labels already say whose ("studio to headset"); headset labels don't ("Speakers").
                var title = kind == "headset" ? $"{name} · {label}" : label;
                targets.Add(new Target(TargetKind.Audio, $"{kind}/{key}/{direction}", title));
            }
        }
        return targets;
    }

    public async Task<Level> GetLevelAsync(Target target, CancellationToken cancel = default)
    {
        var (kind, key, direction) = Parse(target);
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/devices/{kind}/{Uri.EscapeDataString(key)}", null, target, cancel);
        var control = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel))!["controls"]?[direction]
            ?? throw new HttpRequestException($"Bragi has no {direction} on {key}.");
        if (control["volume"] is not JsonValue volume)
            throw new HttpRequestException($"{target.Name} isn't connected right now.");
        return new Level(!(bool)control["muted"]!, (int)Math.Round((double)volume * 100), 100, 150);
    }

    /// <summary>Sets the volume in percent (0–150).</summary>
    public Task SetValueAsync(Target target, int value, CancellationToken cancel = default) =>
        PatchAsync(target, new JsonObject { ["volume"] = Math.Round(Math.Clamp(value, 0, 150) / 100.0, 3) }, cancel);

    /// <summary>Toggle flips mute; TurnOn unmutes; TurnOff mutes.</summary>
    public async Task RunAsync(PadCommand command, Target target, CancellationToken cancel = default)
    {
        bool muted = command switch
        {
            PadCommand.Toggle => (await GetLevelAsync(target, cancel)).IsOn,
            PadCommand.TurnOn => false,
            PadCommand.TurnOff => true,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Bragi runs Toggle, TurnOn and TurnOff."),
        };
        await PatchAsync(target, new JsonObject { ["muted"] = muted }, cancel);
    }

    async Task PatchAsync(Target target, JsonObject body, CancellationToken cancel)
    {
        var (kind, key, direction) = Parse(target);
        using var _ = await SendAsync(HttpMethod.Patch, $"api/v1/devices/{kind}/{Uri.EscapeDataString(key)}/controls/{direction}", body, target, cancel);
    }

    static (string Kind, string Key, string Direction) Parse(Target target)
    {
        if (target.Kind != TargetKind.Audio) throw new ArgumentException("Bragi only handles audio controls.", nameof(target));
        var parts = target.Id.Split('/');
        if (parts.Length != 3) throw new ArgumentException($"Not a Bragi control id: {target.Id}", nameof(target));
        return (parts[0], parts[1], parts[2]);
    }

    async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, Target? target, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(method, new Uri(BaseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var response = await http.SendAsync(request, timeout.Token);
        if (response.IsSuccessStatusCode) return response;
        var status = (int)response.StatusCode;
        response.Dispose();
        var what = target?.Name ?? "Bragi";
        throw new HttpRequestException(status switch
        {
            401 => "Bragi refused the key (401). Make a new one under Security, API connections.",
            404 => $"Bragi doesn't know {what} any more.",
            409 => $"{what} isn't connected right now.",
            422 => $"Bragi refused that value for {what}.",
            _ => string.Create(CultureInfo.InvariantCulture, $"Bragi answered {status} for {path}."),
        });
    }
}
