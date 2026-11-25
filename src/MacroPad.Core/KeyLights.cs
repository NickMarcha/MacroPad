namespace MacroPad.Core;

/// <summary>A key's LED colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public static Rgb Parse(string hex)
    {
        var h = hex.TrimStart('#');
        return new Rgb(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16));
    }
}

/// <summary>The light modes of pads with a colour per key (514C:8850), as they look on the pad.</summary>
public enum KeyLightMode : byte
{
    /// <summary>Lights off.</summary>
    Off = 0,

    /// <summary>Every key lit in its own colour.</summary>
    On = 1,

    /// <summary>A key lights in its colour while pressed.</summary>
    Press = 2,

    /// <summary>A press lights the key, then spreads along its row and column to the edges.</summary>
    Spread = 3,

    /// <summary>An animated rainbow; ignores the key colours.</summary>
    Rainbow = 4,

    /// <summary>A slower colour wave; ignores the key colours.</summary>
    Wave = 5,
}

/// <summary>One layer's lights on a pad with a colour per key: the mode and a colour for each key slot 1–16.</summary>
public sealed record KeyLights(KeyLightMode Mode, IReadOnlyList<Rgb> Colors)
{
    public const int Count = 16;

    /// <summary>What the pad comes with: these colours, lights off.</summary>
    public static KeyLights Factory { get; } = new(KeyLightMode.Off,
        "FF0000 FF8030 FFFF30 00FF00 00FFFF 0000FF 800080 8B0000 FFA500 FFFF96 7DFF00 008B8B 00008B FF00FF FF6666 FFC864"
            .Split(' ').Select(Rgb.Parse).ToList());

    public bool Equals(KeyLights? other) => other is not null && Mode == other.Mode && Colors.SequenceEqual(other.Colors);

    public override int GetHashCode() => HashCode.Combine(Mode, Colors.Count);
}
