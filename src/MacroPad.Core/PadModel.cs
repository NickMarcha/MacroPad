namespace MacroPad.Core;

/// <summary>How a pad's configuration packets are laid out.</summary>
public enum PacketFormat
{
    /// <summary>Chords of modifier bits and a key (<see cref="Protocol"/>): 514C:8851 and the 1189 pads.</summary>
    Chords,

    /// <summary>Three-byte steps, modifiers as steps of their own (<see cref="StepProtocol"/>): 514C:8850.</summary>
    Steps,
}

/// <summary>What differs between pad models: the packet format, how the keys sit on the pad, and what can be set.</summary>
public sealed record PadModel(PacketFormat Format, int KeyColumns, bool HasLights, bool HasDelay)
{
    // The 12-key two-knob 514C:8850 is three keys across and four down; its slots count up each column from the bottom.
    static readonly int[] Steps12Keys = [4, 8, 12, 3, 7, 11, 2, 6, 10, 1, 5, 9];

    public static PadModel For(int vendorId, int productId) => (vendorId, productId) switch
    {
        (0x514C, 0x8850) => new PadModel(PacketFormat.Steps, KeyColumns: 3, HasLights: false, HasDelay: false),
        _ => new PadModel(PacketFormat.Chords, KeyColumns: 1, HasLights: true, HasDelay: true),
    };

    /// <summary>The keys' slots in reading order on the pad (top left first).</summary>
    public IReadOnlyList<int> KeySlots(int buttons) =>
        Format == PacketFormat.Steps && buttons == Steps12Keys.Length
            ? Steps12Keys
            : Enumerable.Range(0, buttons).Select(Protocol.ButtonSlot).ToList();
}
