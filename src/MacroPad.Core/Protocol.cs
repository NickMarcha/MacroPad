namespace MacroPad.Core;

/// <summary>
/// Packet encoding for the vendor-specific HID interface of the "MINI_KEYBOARD" macropads
/// (VID 0x514C / 0x1189, PID 0x88xx). Every packet is a 65-byte output report with report ID 3.
/// See docs/protocol.md for how this was worked out.
/// </summary>
public static class Protocol
{
    public const int ReportLength = 65;
    public const byte ReportId = 0x03;
    public const int LayerCount = 3;
    public const int MaxChords = 18;

    /// <summary>Slots the device reports per layer: buttons 1–15, then knob actions 16–24.</summary>
    public const int SlotCount = 18;

    const byte CmdWrite = 0xFE;
    const byte CmdCommit = 0xFD;
    const byte CmdRead = 0xFA;
    const byte CmdInfo = 0xFB;
    const byte LedSlot = 0xB0;

    const byte TypeKeyboard = 1;
    const byte TypeMedia = 2;
    const byte TypeMouse = 3;
    const byte TypeLed = 8;

    public static int ButtonSlot(int buttonIndex) => buttonIndex + 1;

    /// <summary>Knob actions: 0 = rotate left, 1 = press, 2 = rotate right.</summary>
    public static int KnobSlot(int knobIndex, int action) => 16 + 3 * knobIndex + action;

    public static byte[] Info() => Packet(CmdInfo, 0xFB, 0xFB);

    /// <summary>Asks for every slot of a layer (1-based); the device answers with <paramref name="slots"/> reports.</summary>
    public static byte[] ReadLayer(int layer, int slots = SlotCount) => Packet(CmdRead, (byte)slots, 0, (byte)layer);

    public static byte[] Commit() => Packet(CmdCommit, 0xFE, 0xFF);

    public static byte[] WriteBinding(int layer, int slot, Binding binding)
    {
        var p = Packet(CmdWrite, (byte)slot, (byte)layer);
        switch (binding)
        {
            case EmptyBinding:
                p[4] = TypeKeyboard;
                break;
            case KeyboardBinding k:
                if (k.Chords.Count > MaxChords)
                    throw new ArgumentException($"A macro holds at most {MaxChords} key presses.");
                p[4] = TypeKeyboard;
                p[5] = (byte)k.DelayMs;
                p[6] = (byte)(k.DelayMs >> 8);
                // A lone modifier (e.g. just Shift) is sent with a count of 0.
                p[10] = (byte)(k.Chords is [{ Code: 0 }] ? 0 : k.Chords.Count);
                for (int i = 0; i < k.Chords.Count; i++)
                {
                    p[11 + 2 * i] = (byte)k.Chords[i].Modifiers;
                    p[12 + 2 * i] = k.Chords[i].Code;
                }
                break;
            case MediaBinding m:
                p[4] = TypeMedia;
                p[10] = 2;
                p[11] = (byte)m.Usage;
                p[12] = (byte)(m.Usage >> 8);
                break;
            case MouseBinding m:
                p[4] = TypeMouse;
                p[10] = 1;
                p[11] = (byte)m.Modifiers;
                p[12] = (byte)m.Buttons;
                p[13] = (byte)m.DeltaX;
                p[14] = (byte)m.DeltaY;
                p[15] = (byte)m.Wheel;
                break;
            default:
                throw new ArgumentException($"Cannot write {binding.GetType().Name}.");
        }
        return p;
    }

    public static byte[] WriteLed(int layer, LedMode mode, LedColor color) =>
        Packet(CmdWrite, LedSlot, (byte)layer, TypeLed, 0, 0, 0, 0, 0, 1, 0, (byte)(((byte)color << 4) | (byte)mode));

    /// <summary>Decodes one answer to <see cref="ReadLayer"/>. Input starts with the report ID.</summary>
    public static (int Layer, int Slot, Binding Binding) ParseSlot(ReadOnlySpan<byte> r)
    {
        if (r.Length < 16 || r[0] != ReportId || r[1] != CmdRead)
            throw new FormatException("Not a slot report.");
        int slot = r[2], layer = r[3];
        byte type = r[4];
        int count = r[10];
        Binding b = type switch
        {
            TypeKeyboard => ParseKeyboard(r, count),
            TypeMedia => (r[11] | r[12] << 8) == 0 ? new EmptyBinding() : new MediaBinding((ushort)(r[11] | r[12] << 8)),
            TypeMouse => new MouseBinding((Modifiers)r[11], (MouseButtons)r[12], (sbyte)r[13], (sbyte)r[14], (sbyte)r[15]),
            _ => new UnknownBinding(type, r[4..].ToArray()),
        };
        return (layer, slot, b);
    }

    static Binding ParseKeyboard(ReadOnlySpan<byte> r, int count)
    {
        // A count of 0 with a modifier set is a lone-modifier binding.
        if (count == 0)
            return r[11] == 0 ? new EmptyBinding() : new KeyboardBinding([new KeyChord((Modifiers)r[11], 0)]);
        count = Math.Min(count, Math.Min(MaxChords, (r.Length - 11) / 2));
        var chords = new KeyChord[count];
        for (int i = 0; i < count; i++)
            chords[i] = new KeyChord((Modifiers)r[11 + 2 * i], r[12 + 2 * i]);
        if (chords.All(c => c.Code == 0 && c.Modifiers == 0))
            return new EmptyBinding();
        return new KeyboardBinding(chords, (ushort)(r[5] | r[6] << 8));
    }

    static byte[] Packet(params byte[] body)
    {
        var p = new byte[ReportLength];
        p[0] = ReportId;
        body.CopyTo(p, 1);
        return p;
    }
}
