namespace MacroPad.Core;

/// <summary>How a pad's configuration packets are laid out.</summary>
public enum PacketFormat
{
    /// <summary>Chords of modifier bits and a key (<see cref="Protocol"/>): 514C:8851 and the 1189 pads.</summary>
    Chords,

    /// <summary>Three-byte steps, modifiers as steps of their own (<see cref="StepProtocol"/>): 514C:8850.</summary>
    Steps,
}

/// <summary>How a pad's lights are set.</summary>
public enum LightKind
{
    /// <summary>One mode and one of seven colours per layer, write-only (<see cref="Protocol.WriteLed"/>).</summary>
    PerLayer,

    /// <summary>A mode per layer and an RGB colour per key, readable (<see cref="StepProtocol.WriteLights"/>).</summary>
    PerKey,
}

/// <summary>What differs between pad models: the packet format and what can be set. Where keys sit is <see cref="PadLayout"/>.</summary>
public sealed record PadModel(PacketFormat Format, LightKind Lights, bool HasDelay)
{
    public static PadModel For(int vendorId, int productId) => (vendorId, productId) switch
    {
        (0x514C, 0x8850) => new PadModel(PacketFormat.Steps, LightKind.PerKey, HasDelay: false),
        _ => new PadModel(PacketFormat.Chords, LightKind.PerLayer, HasDelay: true),
    };
}
