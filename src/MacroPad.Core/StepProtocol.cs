namespace MacroPad.Core;

/// <summary>
/// Slot packets of the 514C:8850 pad: <c>03 FD &lt;slot&gt; &lt;layer&gt; &lt;type&gt; &lt;flag&gt; &lt;n&gt;</c> and then
/// <c>n</c> steps of <c>00 00 &lt;code&gt;</c>. A keyboard step is a key or a modifier (<c>F1</c>–<c>F8</c>); the pad
/// answers reads in the same layout. Info, read and save are as in <see cref="Protocol"/>. See docs/protocol.md.
/// </summary>
public static class StepProtocol
{
    const byte CmdWrite = 0xFD;
    const byte CmdRead = 0xFA;
    const byte TypeKeyboard = 1;
    const byte TypeMedia = 2;
    const byte TypeMouse = 3;
    const int FirstStep = 7;
    const byte FirstModifier = 0xF1;

    /// <summary>Modifier steps: F1 LCtrl, F2 LShift, F3 LAlt, F4 LGui, F5–F8 the right-hand ones.</summary>
    static readonly Modifiers[] ModifierSteps =
    [
        Modifiers.LeftCtrl, Modifiers.LeftShift, Modifiers.LeftAlt, Modifiers.LeftGui,
        Modifiers.RightCtrl, Modifiers.RightShift, Modifiers.RightAlt, Modifiers.RightGui,
    ];

    public static byte[] WriteBinding(int layer, int slot, Binding binding)
    {
        var p = new byte[Protocol.ReportLength];
        (p[0], p[1], p[2], p[3]) = (Protocol.ReportId, CmdWrite, (byte)slot, (byte)layer);
        List<byte> steps;
        switch (binding)
        {
            case EmptyBinding:
                p[4] = TypeKeyboard;
                steps = [];
                break;
            case KeyboardBinding k:
                if (k.DelayMs != 0)
                    throw new ArgumentException("This pad has no delay between key presses.");
                p[4] = TypeKeyboard;
                steps = [];
                foreach (var chord in k.Chords)
                {
                    for (int i = 0; i < ModifierSteps.Length; i++)
                        if (chord.Modifiers.HasFlag(ModifierSteps[i])) steps.Add((byte)(FirstModifier + i));
                    if (chord.Code != 0) steps.Add(chord.Code);
                }
                if (steps.Count > Protocol.MaxChords)
                    throw new ArgumentException($"A macro holds at most {Protocol.MaxChords} steps on this pad (each modifier is one).");
                break;
            case MediaBinding m:
                p[4] = TypeMedia;
                steps = [(byte)m.Usage, (byte)(m.Usage >> 8)];
                break;
            case MouseBinding m:
                // As ch57x-keyboard-tool writes it: flag 1, n = 4, then modifier, buttons, dx, dy, wheel.
                p[4] = TypeMouse;
                p[5] = 1;
                p[6] = 4;
                byte modifier = m.Modifiers switch
                {
                    Modifiers.None => 0,
                    Modifiers.LeftCtrl => 0xF1,
                    Modifiers.LeftShift => 0xF2,
                    Modifiers.LeftAlt => 0xF3,
                    _ => throw new ArgumentException("On this pad a mouse action takes at most one of Ctrl, Shift or Alt."),
                };
                byte[] values = [modifier, (byte)m.Buttons, (byte)m.DeltaX, (byte)m.DeltaY, (byte)m.Wheel];
                for (int i = 0; i < values.Length; i++) p[FirstStep + 2 + 3 * i] = values[i];
                return p;
            default:
                throw new ArgumentException($"Cannot write {binding.GetType().Name}.");
        }
        p[6] = (byte)steps.Count;
        for (int i = 0; i < steps.Count; i++) p[FirstStep + 2 + 3 * i] = steps[i];
        return p;
    }

    /// <summary>Decodes one answer to <see cref="Protocol.ReadLayer"/>. Bytes past the step count are left over from older bindings.</summary>
    public static (int Layer, int Slot, Binding Binding) ParseSlot(ReadOnlySpan<byte> r)
    {
        if (r.Length < 30 || r[0] != Protocol.ReportId || r[1] != CmdRead)
            throw new FormatException("Not a slot report.");
        int slot = r[2], layer = r[3];
        int count = Math.Min((int)r[6], Protocol.MaxChords);
        var steps = new byte[Protocol.MaxChords];
        for (int i = 0; i < steps.Length && FirstStep + 2 + 3 * i < r.Length; i++) steps[i] = r[FirstStep + 2 + 3 * i];
        byte Step(int i) => steps[i];
        Binding b = r[4] switch
        {
            TypeKeyboard => ParseKeyboard(steps.AsSpan(0, count)),
            TypeMedia => (Step(0) | Step(1) << 8) is var u and not 0 ? new MediaBinding((ushort)u) : new EmptyBinding(),
            TypeMouse => new MouseBinding(
                Step(0) is >= 0xF1 and <= 0xF3 ? ModifierSteps[Step(0) - FirstModifier] : Modifiers.None,
                (MouseButtons)Step(1), (sbyte)Step(2), (sbyte)Step(3), (sbyte)Step(4)),
            _ => new UnknownBinding(r[4], r[4..].ToArray()),
        };
        return (layer, slot, b);
    }

    static Binding ParseKeyboard(ReadOnlySpan<byte> steps)
    {
        // Modifier steps gather until a key, which ends the chord. Modifiers left at the end are a lone-modifier chord.
        var chords = new List<KeyChord>();
        var held = Modifiers.None;
        foreach (var code in steps)
        {
            if (code is >= FirstModifier and <= FirstModifier + 7)
                held |= ModifierSteps[code - FirstModifier];
            else if (code != 0)
            {
                chords.Add(new KeyChord(held, code));
                held = Modifiers.None;
            }
        }
        if (held != Modifiers.None) chords.Add(new KeyChord(held, 0));
        return chords.Count == 0 ? new EmptyBinding() : new KeyboardBinding(chords);
    }
}
