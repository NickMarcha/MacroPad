namespace MacroPad.Core;

/// <summary>
/// Turns knob clicks into a level (brightness or volume) for one target at a time. Relative steps go wrong when clicks
/// come fast: each is worked out from the last reported value, which lags, so steps get lost and the light jumped.
/// The dial reads the level once when turning starts, keeps the running value itself, and sends absolute values.
/// While one is on its way only the newest waits, so the target follows the knob without a queue building up.
/// <para>
/// Clicks come in fine steps of 1/255 of full scale, whatever the target: 5 is about 2%, for a light (5 of 255) and
/// for audio (2 of 100 percent) alike, so a Selected knob feels the same on a room and on a headset.
/// </para>
/// </summary>
public sealed class LevelDial(
    Func<Target, Task<Level>> read,
    Func<Target, int, Task> set,
    Func<TimeSpan> clock)
{
    public const int FineSteps = 255;

    /// <summary>After this long without a click the next one reads the target again (something else may have changed it).</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(3);

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly object _queue = new();
    Target? _target;
    Level? _level;
    TimeSpan _last;
    (Target Target, int Value)? _queued;
    bool _sending;

    /// <summary>Raised with the new level after each click, before the service has it.</summary>
    public event Action<Target, Level>? Changed;

    /// <summary>Raised when sending fails.</summary>
    public event Action<Target, Exception>? Failed;

    /// <summary>Forget the running value, for when the target changed some other way (a toggle, the slider).</summary>
    public void Forget() => _level = null;

    /// <param name="fineDelta">The turn in 1/255ths of full scale; negative turns down.</param>
    public async Task TurnAsync(Target target, int fineDelta)
    {
        int value;
        await _gate.WaitAsync();
        try
        {
            var now = clock();
            if (_target != target || _level is null || now - _last > Fresh)
            {
                _level = await read(target);
                _target = target;
            }
            _last = now;
            var units = (int)Math.Round(fineDelta * (double)_level.Full / FineSteps);
            if (units == 0) units = Math.Sign(fineDelta);
            // A light that is off starts from 0; muted audio keeps its volume and stays muted.
            var current = _level.Total > 0 && !_level.IsOn ? 0 : _level.Value;
            value = Math.Clamp(current + units, 0, _level.Max);
            _level = _level.Total > 0
                ? Level.Lights(value > 0 ? _level.Total : 0, _level.Total, value)
                : _level with { Value = value };
            Changed?.Invoke(target, _level);
        }
        finally
        {
            _gate.Release();
        }
        await SendAsync(target, value);
    }

    async Task SendAsync(Target target, int value)
    {
        lock (_queue)
        {
            if (_sending)
            {
                _queued = (target, value);
                return;
            }
            _sending = true;
        }
        (Target Target, int Value)? next = (target, value);
        while (next is { } n)
        {
            try
            {
                await set(n.Target, n.Value);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                _level = null;
                Failed?.Invoke(n.Target, e);
            }
            lock (_queue)
            {
                next = _queued;
                _queued = null;
                if (next is null) _sending = false;
            }
        }
    }
}
