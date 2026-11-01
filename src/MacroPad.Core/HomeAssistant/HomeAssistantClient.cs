using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MacroPad.Core.HomeAssistant;

public enum HaCommand { Toggle, TurnOn, TurnOff, Brighter, Dimmer }

[JsonConverter(typeof(JsonStringEnumConverter<HaTargetKind>))]
public enum HaTargetKind { Area, Light }

/// <summary>What an action works on: every light in an area, or one light.</summary>
public sealed record HaTarget(HaTargetKind Kind, string Id, string Name)
{
    public override string ToString() => Kind == HaTargetKind.Area ? $"{Name} (area)" : Name;
}

/// <summary>What a pad slot does in Home Assistant.</summary>
public sealed record HaAction(
    [property: JsonConverter(typeof(JsonStringEnumConverter<HaCommand>))] HaCommand Command,
    HaTarget Target,
    int StepPercent = 10)
{
    public string Describe() => Command switch
    {
        HaCommand.Toggle => $"Toggle {Target.Name}",
        HaCommand.TurnOn => $"Turn on {Target.Name}",
        HaCommand.TurnOff => $"Turn off {Target.Name}",
        HaCommand.Brighter => $"{Target.Name} +{StepPercent}%",
        HaCommand.Dimmer => $"{Target.Name} −{StepPercent}%",
        _ => Command.ToString(),
    };
}

/// <summary>
/// The pad can only send key codes, so a Home Assistant slot is written as a consumer usage from a range no OS acts on
/// (0x0F00–0x0F7F). MacroPad, running in the background, sees the press and runs the matching action.
/// </summary>
public static class Triggers
{
    public const ushort First = 0x0F00;
    public const ushort Last = 0x0F7F;

    public static ushort Code(int layer, int slot) => (ushort)(First + (layer - 1) * 0x20 + slot);

    public static bool IsTrigger(ushort usage) => usage is >= First and <= Last;
}

/// <summary>Talks to Home Assistant's REST API with a long-lived access token.</summary>
public sealed class HomeAssistantClient(HttpClient http, Uri baseUrl, string token)
{
    // Asks Home Assistant for every area and the lights in it, as JSON, in one request.
    const string AreasTemplate =
        "[{% for a in areas() %}{\"id\":{{ a|tojson }},\"name\":{{ area_name(a)|tojson }}," +
        "\"lights\":{{ area_entities(a)|select('match','light\\\\.')|list|tojson }}}{% if not loop.last %},{% endif %}{% endfor %}]";

    public Uri BaseUrl { get; } = baseUrl;

    public static Uri NormalizeUrl(string url)
    {
        var u = url.Trim();
        if (!u.Contains("://")) u = "https://" + u;
        var parsed = new Uri(u);
        // Accept a pasted dashboard link such as https://host:8444/home/overview.
        return new Uri(parsed.GetLeftPart(UriPartial.Authority) + "/");
    }

    /// <summary>Throws with a readable message when the address or token is wrong.</summary>
    public async Task CheckAsync(CancellationToken cancel = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/", null, cancel);
    }

    /// <summary>Areas that have lights, then every light, sorted by name.</summary>
    public async Task<List<HaTarget>> GetTargetsAsync(CancellationToken cancel = default)
    {
        using var areasResponse = await SendAsync(HttpMethod.Post, "api/template", new JsonObject { ["template"] = AreasTemplate }, cancel);
        var areas = JsonNode.Parse(await areasResponse.Content.ReadAsStringAsync(cancel))!.AsArray();

        using var statesResponse = await SendAsync(HttpMethod.Get, "api/states", null, cancel);
        var states = JsonNode.Parse(await statesResponse.Content.ReadAsStringAsync(cancel))!.AsArray();

        var targets = new List<HaTarget>();
        foreach (var a in areas)
            if (a!["lights"]!.AsArray().Count > 0)
                targets.Add(new HaTarget(HaTargetKind.Area, (string)a["id"]!, (string)a["name"]!));
        targets.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));

        var lights = states
            .Where(s => ((string?)s!["entity_id"])?.StartsWith("light.", StringComparison.Ordinal) == true)
            .Select(s => new HaTarget(HaTargetKind.Light, (string)s!["entity_id"]!,
                (string?)s["attributes"]?["friendly_name"] ?? (string)s["entity_id"]!))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase);
        targets.AddRange(lights);
        return targets;
    }

    public async Task RunAsync(HaAction action, CancellationToken cancel = default)
    {
        var target = action.Target;
        string service;
        var data = new JsonObject();
        switch (action.Command)
        {
            case HaCommand.Toggle when target.Kind == HaTargetKind.Area:
                // light.toggle on an area flips each light on its own, so a half-lit room would stay half-lit.
                // Like a wall switch instead: anything on turns everything off, else everything on.
                service = await AnyOnAsync(target.Id, cancel) ? "turn_off" : "turn_on";
                break;
            case HaCommand.Toggle:
                service = "toggle";
                break;
            case HaCommand.TurnOn:
                service = "turn_on";
                break;
            case HaCommand.TurnOff:
                service = "turn_off";
                break;
            case HaCommand.Brighter:
            case HaCommand.Dimmer:
                service = "turn_on";
                data["brightness_step_pct"] = action.Command == HaCommand.Brighter ? action.StepPercent : -action.StepPercent;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
        data[target.Kind == HaTargetKind.Area ? "area_id" : "entity_id"] = target.Id;
        using var _ = await SendAsync(HttpMethod.Post, $"api/services/light/{service}", data, cancel);
    }

    async Task<bool> AnyOnAsync(string areaId, CancellationToken cancel)
    {
        var template = "{{ area_entities(" + JsonSerializer.Serialize(areaId) + ")|select('match','light\\\\.')|select('is_state','on')|list|count }}";
        using var response = await SendAsync(HttpMethod.Post, "api/template", new JsonObject { ["template"] = template }, cancel);
        return int.TryParse((await response.Content.ReadAsStringAsync(cancel)).Trim(), out var n) && n > 0;
    }

    async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(method, new Uri(BaseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var response = await http.SendAsync(request, timeout.Token);
        if (response.IsSuccessStatusCode) return response;
        var status = (int)response.StatusCode;
        response.Dispose();
        throw new HttpRequestException(status == 401
            ? "Home Assistant refused the token (401). Make a new long-lived access token."
            : $"Home Assistant answered {status} for {path}.");
    }
}
