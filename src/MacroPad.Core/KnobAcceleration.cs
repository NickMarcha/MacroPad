namespace MacroPad.Core;

/// <summary>
/// Makes brightness steps grow with turning speed, like mouse acceleration: a click on its own moves exactly the
/// configured step, and clicks that follow each other quickly in the same direction move up to <see cref="MaxFactor"/>
/// times as much.
/// </summary>
public sealed class KnobAcceleration
{
    public const double MaxFactor = 8;

    /// <summary>Clicks further apart than this count as slow and move just the base step.</summary>
    public static readonly TimeSpan SlowGap = TimeSpan.FromMilliseconds(250);

    TimeSpan? _last;
    int _lastDirection;

    /// <summary>The step for a click at <paramref name="now"/> (any monotonic clock) turning in <paramref name="direction"/> (±1).</summary>
    public int Step(int baseStep, int direction, TimeSpan now)
    {
        double factor = 1;
        if (_last is { } last && direction == _lastDirection)
        {
            var gap = Math.Max(1, (now - last).TotalMilliseconds);
            factor = Math.Clamp(SlowGap.TotalMilliseconds / gap, 1, MaxFactor);
        }
        _last = now;
        _lastDirection = direction;
        return Math.Clamp((int)Math.Round(baseStep * factor), 1, PadAction.MaxStep);
    }
}
