using System.Text.Json.Serialization;

namespace MacroPad.Core;

/// <summary>
/// What MacroPad does when a pad slot sends its trigger code. Toggle, TurnOn and TurnOff switch lights or mute audio;
/// Up and Down turn brightness or volume (the knob); Select remembers a target for actions on
/// <see cref="Target.Selected"/>; NextProfile and Profile switch MacroPad's active profile.
/// </summary>
public enum PadCommand { Select, Toggle, TurnOn, TurnOff, Up, Down, NextProfile, Profile }

/// <summary>
/// One action. <paramref name="Target"/> is what it works on. <paramref name="Step"/> is for Up and Down, in 1/255ths
/// of full scale (see <see cref="LevelDial"/>); quick knob turns multiply it. <paramref name="ProfileName"/> is for
/// <see cref="PadCommand.Profile"/>.
/// </summary>
public sealed record PadAction(
    [property: JsonConverter(typeof(JsonStringEnumConverter<PadCommand>))] PadCommand Command,
    Target? Target = null,
    int Step = 5,
    string? ProfileName = null)
{
    public const int MaxStep = 255;

    public bool UsesTarget => Command is not (PadCommand.NextProfile or PadCommand.Profile);

    public string Describe()
    {
        var text = Words();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    string Words()
    {
        var name = Target?.Name ?? "(nothing)";
        var audio = Target?.Kind == TargetKind.Audio;
        var light = Target?.IsLight == true;
        return Command switch
        {
            PadCommand.Select => $"Knob controls {name}",
            PadCommand.Toggle => audio ? $"Mute or unmute {name}" : $"Toggle {name}",
            PadCommand.TurnOn => audio ? $"Unmute {name}" : $"Turn on {name}",
            PadCommand.TurnOff => audio ? $"Mute {name}" : $"Turn off {name}",
            PadCommand.Up => $"{name} {(audio ? "louder" : light ? "brighter" : "up")} (step {Step})",
            PadCommand.Down => $"{name} {(audio ? "quieter" : light ? "dimmer" : "down")} (step {Step})",
            PadCommand.NextProfile => "Next profile",
            PadCommand.Profile => $"Profile {ProfileName ?? "(none)"}",
            _ => Command.ToString(),
        };
    }
}

/// <summary>
/// MacroPad's own layer of actions on top of the pad. A slot's trigger code is looked up in the active profile, then in
/// the first one (Default), so a profile only lists what it changes.
/// </summary>
public sealed class Profile
{
    public string Name { get; set; } = "Default";

    /// <summary>Actions by trigger code (hex, e.g. "0F23").</summary>
    public Dictionary<string, PadAction> Actions { get; set; } = [];
}

/// <summary>
/// The pad can only send key codes, so a slot MacroPad handles is written as a consumer usage from a range no OS acts
/// on (0x0F00–0x0F7F). MacroPad, running in the background, sees the press and runs the matching action.
/// </summary>
public static class Triggers
{
    public const ushort First = 0x0F00;
    public const ushort Last = 0x0F7F;

    public static ushort Code(int layer, int slot) => (ushort)(First + (layer - 1) * 0x20 + slot);

    public static bool IsTrigger(ushort usage) => usage is >= First and <= Last;

    public static string Key(ushort code) => code.ToString("X4");
}
