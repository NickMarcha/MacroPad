using System.Collections.Generic;
using Avalonia.Data.Converters;
using Avalonia.Input;
using MacroPad.Core;

namespace MacroPad.App;

/// <summary>Maps Avalonia's physical (layout-independent) keys to HID keyboard usages.</summary>
public static class KeyMap
{
    static readonly Dictionary<PhysicalKey, byte> Map = Build();

    public static bool ToHid(PhysicalKey key, out byte code) => Map.TryGetValue(key, out code);

    public static bool IsModifier(PhysicalKey key) => key is PhysicalKey.ControlLeft or PhysicalKey.ControlRight
        or PhysicalKey.ShiftLeft or PhysicalKey.ShiftRight or PhysicalKey.AltLeft or PhysicalKey.AltRight
        or PhysicalKey.MetaLeft or PhysicalKey.MetaRight;

    static Dictionary<PhysicalKey, byte> Build()
    {
        var d = new Dictionary<PhysicalKey, byte>();
        for (int i = 0; i < 26; i++) d[PhysicalKey.A + i] = (byte)(0x04 + i);
        for (int i = 1; i <= 9; i++) d[PhysicalKey.Digit1 + (i - 1)] = (byte)(0x1E + i - 1);
        d[PhysicalKey.Digit0] = 0x27;
        (PhysicalKey, byte)[] rest =
        [
            (PhysicalKey.Enter, 0x28), (PhysicalKey.Escape, 0x29), (PhysicalKey.Backspace, 0x2A), (PhysicalKey.Tab, 0x2B),
            (PhysicalKey.Space, 0x2C), (PhysicalKey.Minus, 0x2D), (PhysicalKey.Equal, 0x2E), (PhysicalKey.BracketLeft, 0x2F),
            (PhysicalKey.BracketRight, 0x30), (PhysicalKey.Backslash, 0x31), (PhysicalKey.Semicolon, 0x33), (PhysicalKey.Quote, 0x34),
            (PhysicalKey.Backquote, 0x35), (PhysicalKey.Comma, 0x36), (PhysicalKey.Period, 0x37), (PhysicalKey.Slash, 0x38),
            (PhysicalKey.CapsLock, 0x39), (PhysicalKey.PrintScreen, 0x46), (PhysicalKey.ScrollLock, 0x47), (PhysicalKey.Pause, 0x48),
            (PhysicalKey.Insert, 0x49), (PhysicalKey.Home, 0x4A), (PhysicalKey.PageUp, 0x4B), (PhysicalKey.Delete, 0x4C),
            (PhysicalKey.End, 0x4D), (PhysicalKey.PageDown, 0x4E), (PhysicalKey.ArrowRight, 0x4F), (PhysicalKey.ArrowLeft, 0x50),
            (PhysicalKey.ArrowDown, 0x51), (PhysicalKey.ArrowUp, 0x52), (PhysicalKey.NumLock, 0x53), (PhysicalKey.NumPadDivide, 0x54),
            (PhysicalKey.NumPadMultiply, 0x55), (PhysicalKey.NumPadSubtract, 0x56), (PhysicalKey.NumPadAdd, 0x57), (PhysicalKey.NumPadEnter, 0x58),
            (PhysicalKey.NumPad0, 0x62), (PhysicalKey.NumPadDecimal, 0x63), (PhysicalKey.IntlBackslash, 0x64), (PhysicalKey.ContextMenu, 0x65),
        ];
        foreach (var (k, v) in rest) d[k] = v;
        for (int i = 1; i <= 9; i++) d[PhysicalKey.NumPad1 + (i - 1)] = (byte)(0x59 + i - 1);
        for (int i = 0; i < 12; i++) d[PhysicalKey.F1 + i] = (byte)(0x3A + i);
        for (int i = 0; i < 12; i++) d[PhysicalKey.F13 + i] = (byte)(0x68 + i);
        return d;
    }
}

public static class Converters
{
    /// <summary>For radio buttons over an enum: checked when the value equals the parameter; checking sets it.</summary>
    public static readonly IValueConverter Is = new EnumIs();

    sealed class EnumIs : IValueConverter
    {
        public object? Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            Equals(value, parameter);

        public object? ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is true ? parameter : Avalonia.Data.BindingOperations.DoNothing;
    }

    public static readonly FuncValueConverter<KeyChord, string> Chord = new(c => c is null ? "" : Usages.Format(c));

    /// <summary>Enum values as words: Action → "MacroPad action", TurnOn → "Turn on".</summary>
    public static readonly FuncValueConverter<object, string> Words = new(v =>
    {
        var name = v?.ToString() ?? "";
        if (name == "Action") return "MacroPad action";
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    });
}
