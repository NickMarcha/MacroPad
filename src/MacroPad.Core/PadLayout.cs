namespace MacroPad.Core;

/// <summary>A key on the drawn pad: its slot and top-left corner, in units of one key.</summary>
public sealed record KeyPlace(int Slot, double X, double Y);

/// <summary>A knob on the drawn pad (0-based index) and its top-left corner; it takes <see cref="PadLayout.KnobSize"/>.</summary>
public sealed record KnobPlace(int Index, double X, double Y);

/// <summary>
/// Where the keys and knobs sit on a pad, in units of one key (1 × 1; a knob is <see cref="KnobSize"/> square). The
/// unrotated layout is the vendor's: the 12-key pad lies with its knobs on the right and keys 1–4 along the top.
/// </summary>
public sealed record PadLayout(IReadOnlyList<KeyPlace> Keys, IReadOnlyList<KnobPlace> Knobs)
{
    public const double KnobSize = 1.6;
    const double Pitch = 1.15;

    public double Width => Math.Max(Keys.Select(k => k.X + 1).DefaultIfEmpty(0).Max(), Knobs.Select(k => k.X + KnobSize).DefaultIfEmpty(0).Max());
    public double Height => Math.Max(Keys.Select(k => k.Y + 1).DefaultIfEmpty(0).Max(), Knobs.Select(k => k.Y + KnobSize).DefaultIfEmpty(0).Max());

    /// <summary>The layout for a pad with these keys and knobs: rows of four keys (slot 1 top left), knobs in a column on the right.</summary>
    public static PadLayout For(int buttons, int knobs)
    {
        int columns = Math.Min(Math.Max(buttons, 1), 4);
        var keys = Enumerable.Range(0, buttons)
            .Select(i => new KeyPlace(Protocol.ButtonSlot(i), i % columns * Pitch, i / columns * Pitch)).ToList();
        double knobX = (columns - 1) * Pitch + 1 + 0.3;
        var knobList = Enumerable.Range(0, knobs).Select(k => new KnobPlace(k, knobX, k * (KnobSize + 0.1))).ToList();
        return new PadLayout(keys, knobList);
    }

    /// <summary>The layout turned clockwise by <paramref name="quarterTurns"/> × 90°. Keys and knobs stay upright.</summary>
    public PadLayout Rotated(int quarterTurns)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        if (turns == 0) return this;
        (double X, double Y) Turn(double cx, double cy)
        {
            for (int i = 0; i < turns; i++) (cx, cy) = (-cy, cx);
            return (cx, cy);
        }
        var keys = Keys.Select(k => { var (x, y) = Turn(k.X + 0.5, k.Y + 0.5); return k with { X = x - 0.5, Y = y - 0.5 }; }).ToList();
        var knobs = Knobs.Select(k => { var (x, y) = Turn(k.X + KnobSize / 2, k.Y + KnobSize / 2); return k with { X = x - KnobSize / 2, Y = y - KnobSize / 2 }; }).ToList();
        double minX = Math.Min(keys.Select(k => k.X).DefaultIfEmpty(0).Min(), knobs.Select(k => k.X).DefaultIfEmpty(0).Min());
        double minY = Math.Min(keys.Select(k => k.Y).DefaultIfEmpty(0).Min(), knobs.Select(k => k.Y).DefaultIfEmpty(0).Min());
        return new PadLayout(
            keys.Select(k => k with { X = Math.Round(k.X - minX, 6), Y = Math.Round(k.Y - minY, 6) }).ToList(),
            knobs.Select(k => k with { X = Math.Round(k.X - minX, 6), Y = Math.Round(k.Y - minY, 6) }).ToList());
    }
}
