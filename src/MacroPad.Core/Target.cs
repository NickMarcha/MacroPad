using System.Text.Json.Serialization;

namespace MacroPad.Core;

[JsonConverter(typeof(JsonStringEnumConverter<TargetKind>))]
public enum TargetKind
{
    /// <summary>Every light in a Home Assistant area.</summary>
    Area,
    /// <summary>One Home Assistant light.</summary>
    Light,
    /// <summary>One Bragi control: a headset's speakers or mic, or a peer's incoming or outgoing audio.</summary>
    Audio,
    /// <summary>Whatever a Select key picked last.</summary>
    Selected,
}

/// <summary>What an action works on. <see cref="Id"/> is the service's own id (area id, entity id, kind/key/direction).</summary>
public sealed record Target(TargetKind Kind, string Id, string Name)
{
    public static readonly Target Selected = new(TargetKind.Selected, "", "the selected target");

    public bool IsLight => Kind is TargetKind.Area or TargetKind.Light;

    public override string ToString() => Kind switch
    {
        TargetKind.Area => $"{Name} (area)",
        TargetKind.Audio => $"{Name} (audio)",
        TargetKind.Selected => "Selected target (picked with a Select key)",
        _ => Name,
    };
}

/// <summary>
/// How far up a target is. <see cref="Value"/> is in the service's units: <see cref="Full"/> of them is 100% and
/// <see cref="Max"/> the most (lights: 255 and 255; audio: percent, 100 and 150). For lights <see cref="On"/> and
/// <see cref="Total"/> count the lights; for audio <see cref="IsOn"/> means not muted.
/// </summary>
public sealed record Level(bool IsOn, int Value, int Full, int Max, int On = 0, int Total = 0)
{
    public int Percent => (int)Math.Round(Value * 100.0 / Full);
    public int MaxPercent => (int)Math.Round(Max * 100.0 / Full);

    /// <summary>Lights: a level for "brightness b, all on" (or all off at 0).</summary>
    public static Level Lights(int on, int total, int brightness) => new(on > 0, brightness, 255, 255, on, total);
}

/// <summary>A service MacroPad drives: Home Assistant for lights, Bragi for audio.</summary>
public interface ITargetService
{
    bool Handles(Target target);

    /// <summary>Everything this service can act on, for the editor's list.</summary>
    Task<List<Target>> GetTargetsAsync(CancellationToken cancel = default);

    Task<Level> GetLevelAsync(Target target, CancellationToken cancel = default);

    /// <summary>Sets an absolute value in the level's units (brightness 0–255, volume percent 0–150).</summary>
    Task SetValueAsync(Target target, int value, CancellationToken cancel = default);

    /// <summary>Toggle, TurnOn or TurnOff (lights on/off; audio unmute/mute).</summary>
    Task RunAsync(PadCommand command, Target target, CancellationToken cancel = default);
}

public static class ServiceAddress
{
    /// <summary>Accepts "host:port", a full URL, or a pasted page link, and keeps scheme, host and port.</summary>
    public static Uri Normalize(string url)
    {
        var u = url.Trim();
        if (!u.Contains("://")) u = "https://" + u;
        var parsed = new Uri(u);
        return new Uri(parsed.GetLeftPart(UriPartial.Authority) + "/");
    }
}
