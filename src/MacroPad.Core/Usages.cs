namespace MacroPad.Core;

/// <summary>Names for HID keyboard and consumer usages, plus text formatting of bindings.</summary>
public static class Usages
{
    public static readonly IReadOnlyDictionary<byte, string> Keys = BuildKeys();

    public static readonly IReadOnlyDictionary<ushort, string> Media = new Dictionary<ushort, string>
    {
        [0x00CD] = "Play/Pause",
        [0x00B5] = "Next Track",
        [0x00B6] = "Previous Track",
        [0x00B7] = "Stop",
        [0x00E2] = "Mute",
        [0x00E9] = "Volume Up",
        [0x00EA] = "Volume Down",
        [0x006F] = "Brightness Up",
        [0x0070] = "Brightness Down",
        [0x0183] = "Media Player",
        [0x018A] = "Mail",
        [0x0192] = "Calculator",
        [0x0194] = "File Explorer",
        [0x0221] = "Search",
        [0x0223] = "Browser Home",
        [0x0224] = "Browser Back",
        [0x0225] = "Browser Forward",
        [0x0227] = "Browser Refresh",
        [0x022A] = "Bookmarks",
    };

    static readonly (Modifiers Flag, string Name)[] ModifierNames =
    [
        (Modifiers.LeftCtrl, "Ctrl"), (Modifiers.LeftShift, "Shift"), (Modifiers.LeftAlt, "Alt"), (Modifiers.LeftGui, "Win"),
        (Modifiers.RightCtrl, "RCtrl"), (Modifiers.RightShift, "RShift"), (Modifiers.RightAlt, "RAlt"), (Modifiers.RightGui, "RWin"),
    ];

    public static string KeyName(byte code) => Keys.TryGetValue(code, out var n) ? n : $"0x{code:X2}";

    public static string MediaName(ushort usage) => Media.TryGetValue(usage, out var n) ? n : $"Consumer 0x{usage:X3}";

    public static string Format(KeyChord chord)
    {
        var parts = ModifierNames.Where(m => chord.Modifiers.HasFlag(m.Flag)).Select(m => m.Name).ToList();
        if (chord.Code != 0) parts.Add(KeyName(chord.Code));
        return parts.Count == 0 ? "(none)" : string.Join("+", parts);
    }

    public static string Format(Binding binding) => binding switch
    {
        EmptyBinding => "—",
        KeyboardBinding k => string.Join(", ", k.Chords.Select(Format)) + (k.DelayMs > 0 ? $" ({k.DelayMs} ms)" : ""),
        MediaBinding m => MediaName(m.Usage),
        MouseBinding m => FormatMouse(m),
        UnknownBinding u => $"Unknown type {u.Type}",
        _ => binding.ToString(),
    };

    static string FormatMouse(MouseBinding m)
    {
        var parts = ModifierNames.Where(x => m.Modifiers.HasFlag(x.Flag)).Select(x => x.Name).ToList();
        if (m.Buttons.HasFlag(MouseButtons.Left)) parts.Add("Left Click");
        if (m.Buttons.HasFlag(MouseButtons.Right)) parts.Add("Right Click");
        if (m.Buttons.HasFlag(MouseButtons.Middle)) parts.Add("Middle Click");
        if (m.Wheel != 0) parts.Add(m.Wheel > 0 ? $"Wheel Up {m.Wheel}" : $"Wheel Down {-m.Wheel}");
        if (m.DeltaX != 0 || m.DeltaY != 0) parts.Add($"Move ({m.DeltaX}, {m.DeltaY})");
        return parts.Count == 0 ? "Mouse (nothing)" : string.Join("+", parts);
    }

    static Dictionary<byte, string> BuildKeys()
    {
        var d = new Dictionary<byte, string>();
        for (int i = 0; i < 26; i++) d[(byte)(0x04 + i)] = ((char)('A' + i)).ToString();
        for (int i = 0; i < 9; i++) d[(byte)(0x1E + i)] = (i + 1).ToString();
        d[0x27] = "0";
        string[] named =
        [
            "Enter", "Esc", "Backspace", "Tab", "Space", "-", "=", "[", "]", "\\", "#", ";", "'", "`", ",", ".", "/", "CapsLock",
        ];
        for (int i = 0; i < named.Length; i++) d[(byte)(0x28 + i)] = named[i];
        for (int i = 0; i < 12; i++) d[(byte)(0x3A + i)] = $"F{i + 1}";
        string[] nav =
        [
            "PrintScreen", "ScrollLock", "Pause", "Insert", "Home", "PageUp", "Delete", "End", "PageDown",
            "Right", "Left", "Down", "Up", "NumLock", "Num/", "Num*", "Num-", "Num+", "NumEnter",
            "Num1", "Num2", "Num3", "Num4", "Num5", "Num6", "Num7", "Num8", "Num9", "Num0", "Num.",
        ];
        for (int i = 0; i < nav.Length; i++) d[(byte)(0x46 + i)] = nav[i];
        d[0x64] = "IntlBackslash";
        d[0x65] = "Menu";
        for (int i = 0; i < 12; i++) d[(byte)(0x68 + i)] = $"F{i + 13}";
        return d;
    }
}
