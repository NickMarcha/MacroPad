using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacroPad.Core.HomeAssistant;

/// <summary>Talks to Home Assistant's REST API with a long-lived access token.</summary>
public sealed class HomeAssistantClient(HttpClient http, Uri baseUrl, string token) : ITargetService
{
    // Asks Home Assistant for every area and the lights in it, as JSON, in one request.
    const string AreasTemplate =
        "[{% for a in areas() %}{\"id\":{{ a|tojson }},\"name\":{{ area_name(a)|tojson }}," +
        "\"lights\":{{ area_entities(a)|select('match','light\\\\.')|list|tojson }}}{% if not loop.last %},{% endif %}{% endfor %}]";

    public Uri BaseUrl { get; } = baseUrl;

    /// <summary>Throws with a readable message when the address or token is wrong.</summary>
    public async Task CheckAsync(CancellationToken cancel = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/", null, cancel);
    }

    /// <summary>Areas that have lights, then every light, sorted by name.</summary>
    public async Task<List<Target>> GetTargetsAsync(CancellationToken cancel = default)
    {
        using var areasResponse = await SendAsync(HttpMethod.Post, "api/template", new JsonObject { ["template"] = AreasTemplate }, cancel);
        var areas = JsonNode.Parse(await areasResponse.Content.ReadAsStringAsync(cancel))!.AsArray();

        using var statesResponse = await SendAsync(HttpMethod.Get, "api/states", null, cancel);
        var states = JsonNode.Parse(await statesResponse.Content.ReadAsStringAsync(cancel))!.AsArray();

        var targets = new List<Target>();
        foreach (var a in areas)
            if (a!["lights"]!.AsArray().Count > 0)
                targets.Add(new Target(TargetKind.Area, (string)a["id"]!, (string)a["name"]!));
        targets.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));

        var lights = states
            .Where(s => ((string?)s!["entity_id"])?.StartsWith("light.", StringComparison.Ordinal) == true)
            .Select(s => new Target(TargetKind.Light, (string)s!["entity_id"]!,
                (string?)s["attributes"]?["friendly_name"] ?? (string)s["entity_id"]!))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase);
        targets.AddRange(lights);
        return targets;
    }

    public bool Handles(Target target) => target.IsLight;

    /// <summary>Toggle, TurnOn or TurnOff on an area or a light.</summary>
    public async Task RunAsync(PadCommand command, Target target, CancellationToken cancel = default)
    {
        if (!target.IsLight) throw new ArgumentException("Home Assistant only handles areas and lights.", nameof(target));
        var service = command switch
        {
            // light.toggle on an area flips each light on its own, so a half-lit room would stay half-lit.
            // Like a wall switch instead: anything on turns everything off, else everything on.
            PadCommand.Toggle when target.Kind == TargetKind.Area => await AnyOnAsync(target.Id, cancel) ? "turn_off" : "turn_on",
            PadCommand.Toggle => "toggle",
            PadCommand.TurnOn => "turn_on",
            PadCommand.TurnOff => "turn_off",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Home Assistant runs Toggle, TurnOn and TurnOff."),
        };
        var data = new JsonObject { [target.Kind == TargetKind.Area ? "area_id" : "entity_id"] = target.Id };
        using var _ = await SendAsync(HttpMethod.Post, $"api/services/light/{service}", data, cancel);
    }

    /// <summary>Reads how lit an area or a light is now. Home Assistant answers a service call after the state has changed.</summary>
    public async Task<Level> GetLevelAsync(Target target, CancellationToken cancel = default)
    {
        var lights = target.Kind == TargetKind.Area
            ? "area_entities(" + JsonValue.Create(target.Id).ToJsonString() + ")|select('match','light\\.')|list"
            : "[" + JsonValue.Create(target.Id).ToJsonString() + "]";
        var template = "{% set ls = " + lights + " %}{% set on = ls|select('is_state','on')|list %}" +
            "{{ {'on': on|count, 'total': ls|count, 'brightness': on|map('state_attr','brightness')|reject('none')|list|average(0)}|tojson }}";
        using var response = await SendAsync(HttpMethod.Post, "api/template", new JsonObject { ["template"] = template }, cancel);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel))!;
        return Level.Lights((int)json["on"]!, (int)json["total"]!, (int)Math.Round((double)json["brightness"]!));
    }

    /// <summary>
    /// Sets every light in the target to one brightness (0–255); 0 turns them off. <paramref name="transition"/> is the
    /// fade in seconds; 0 makes the light jump straight there, which the knob wants so overlapping fades can't overshoot.
    /// Lights that can't fade ignore it.
    /// </summary>
    public async Task SetBrightnessAsync(Target target, int brightness, double? transition = null, CancellationToken cancel = default)
    {
        if (!target.IsLight) throw new ArgumentException("Home Assistant only handles areas and lights.", nameof(target));
        var data = new JsonObject { [target.Kind == TargetKind.Area ? "area_id" : "entity_id"] = target.Id };
        if (transition is { } t) data["transition"] = t;
        if (brightness > 0) data["brightness"] = Math.Clamp(brightness, 1, 255);
        using var _ = await SendAsync(HttpMethod.Post, $"api/services/light/{(brightness > 0 ? "turn_on" : "turn_off")}", data, cancel);
    }

    /// <summary>The knob and the pop-up's slider: no fade, so quick changes can't overshoot.</summary>
    public Task SetValueAsync(Target target, int value, CancellationToken cancel = default) =>
        SetBrightnessAsync(target, value, transition: 0, cancel);

    async Task<bool> AnyOnAsync(string areaId, CancellationToken cancel)
    {
        var template = "{{ area_entities(" + JsonValue.Create(areaId).ToJsonString() + ")|select('match','light\\\\.')|select('is_state','on')|list|count }}";
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
