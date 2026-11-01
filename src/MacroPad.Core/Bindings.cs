namespace MacroPad.Core;

[Flags]
public enum Modifiers : byte
{
    None = 0,
    LeftCtrl = 0x01,
    LeftShift = 0x02,
    LeftAlt = 0x04,
    LeftGui = 0x08,
    RightCtrl = 0x10,
    RightShift = 0x20,
    RightAlt = 0x40,
    RightGui = 0x80,
}

[Flags]
public enum MouseButtons : byte
{
    None = 0,
    Left = 0x01,
    Right = 0x02,
    Middle = 0x04,
}

/// <summary>One step of a keyboard macro: modifiers held while a key (HID usage, 0 = none) is pressed.</summary>
public sealed record KeyChord(Modifiers Modifiers, byte Code);

/// <summary>What one slot (key or knob action) on one layer does.</summary>
public abstract record Binding;

/// <summary>Slot that does nothing.</summary>
public sealed record EmptyBinding : Binding;

/// <summary>Keyboard macro: up to <see cref="Protocol.MaxChords"/> chords typed in order.</summary>
public sealed record KeyboardBinding(IReadOnlyList<KeyChord> Chords, ushort DelayMs = 0) : Binding
{
    public bool Equals(KeyboardBinding? other) =>
        other is not null && DelayMs == other.DelayMs && Chords.SequenceEqual(other.Chords);

    public override int GetHashCode() => HashCode.Combine(DelayMs, Chords.Count);
}

/// <summary>Consumer-control key (volume, play/pause, …) identified by its HID consumer usage.</summary>
public sealed record MediaBinding(ushort Usage) : Binding;

/// <summary>Mouse action: buttons click, optional movement and wheel, with modifiers held.</summary>
public sealed record MouseBinding(Modifiers Modifiers, MouseButtons Buttons, sbyte DeltaX = 0, sbyte DeltaY = 0, sbyte Wheel = 0) : Binding;

/// <summary>A binding type this app does not understand; kept verbatim so it can be shown.</summary>
public sealed record UnknownBinding(byte Type, byte[] Payload) : Binding;

public enum LedMode : byte
{
    Off = 0,
    Backlight = 1,
    Shock = 2,
    Shock2 = 3,
    Press = 4,
    WhiteBacklight = 5,
}

public enum LedColor : byte
{
    None = 0,
    Red = 1,
    Orange = 2,
    Yellow = 3,
    Green = 4,
    Cyan = 5,
    Blue = 6,
    Purple = 7,
}
