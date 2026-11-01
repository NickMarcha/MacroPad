using System.Text.Json.Serialization;
using MacroPad.Core.HomeAssistant;

namespace MacroPad.Core;

/// <summary>
/// What MacroPad does when a pad slot sends its trigger code. Select remembers a Home Assistant target for actions on
/// <see cref="HaTarget.Selected"/> (the knob); NextProfile and Profile switch MacroPad's active profile.
/// </summary>
public enum PadCommand { Select, Toggle, TurnOn, TurnOff, Brighter, Dimmer, NextProfile, Profile }

/// <summary>
/// One action. <paramref name="Target"/> is for the Home Assistant commands. <paramref name="Step"/> is for Brighter and
/// Dimmer, in Home Assistant's brightness units: 1 is 1/255 of full brightness, the finest step a light can take.
/// <paramref name="ProfileName"/> is for <see cref="PadCommand.Profile"/>.
/// </summary>
public sealed record PadAction(
    [property: JsonConverter(typeof(JsonStringEnumConverter<PadCommand>))] PadCommand Command,
    HaTarget? Target = null,
    int Step = 5,
    string? ProfileName = null)
{
    public const int MaxStep = 255;

    public bool IsHomeAssistant => Command is not (PadCommand.NextProfile or PadCommand.Profile);

    public string Describe()
    {
        var text = Words();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    string Words()
    {
        var name = Target?.Name ?? "(nothing)";
        return Command switch
        {
            PadCommand.Select => $"Knob controls {name}",
            PadCommand.Toggle => $"Toggle {name}",
            PadCommand.TurnOn => $"Turn on {name}",
            PadCommand.TurnOff => $"Turn off {name}",
            PadCommand.Brighter => $"{name} brighter ({Step}/255)",
            PadCommand.Dimmer => $"{name} dimmer ({Step}/255)",
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
