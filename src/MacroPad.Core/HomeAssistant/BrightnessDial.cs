namespace MacroPad.Core.HomeAssistant;

/// <summary>
/// Turns knob clicks into brightness for one target at a time. Relative steps (Home Assistant's brightness_step) go
/// wrong when clicks come fast: each is worked out from the light's last reported brightness, which lags behind, so
/// steps get lost and the light jumps. The dial instead reads the brightness once when turning starts, keeps the
/// running value itself, and sends absolute values. While one is on its way only the newest waits, so the light
/// follows the knob without a queue building up.
/// </summary>
public sealed class BrightnessDial(
    Func<HaTarget, Task<LightLevel>> read,
    Func<HaTarget, int, Task> set,
    Func<TimeSpan> clock)
{
    /// <summary>After this long without a click the next one reads the light again (something else may have changed it).</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(3);

    readonly SemaphoreSlim _gate = new(1, 1);
    HaTarget? _target;
    LightLevel? _level;
    TimeSpan _last;
    (HaTarget Target, int Brightness)? _queued;
    bool _sending;
    readonly object _queue = new();

    /// <summary>Raised with the new level after each click, before Home Assistant has it.</summary>
    public event Action<HaTarget, LightLevel>? Changed;

    /// <summary>Raised when sending fails.</summary>
    public event Action<HaTarget, Exception>? Failed;

    /// <summary>Forget the running value, for when the light changed some other way (a toggle, the slider).</summary>
    public void Forget() => _level = null;

    public async Task TurnAsync(HaTarget target, int delta)
    {
        int brightness;
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
            brightness = Math.Clamp((_level.IsOn ? _level.Brightness : 0) + delta, 0, 255);
            // An absolute brightness turns every light in the target on at it, or all off at 0.
            _level = new LightLevel(brightness > 0 ? _level.Total : 0, _level.Total, brightness);
            Changed?.Invoke(target, _level);
        }
        finally
        {
            _gate.Release();
        }
        await SendAsync(target, brightness);
    }

    async Task SendAsync(HaTarget target, int brightness)
    {
        lock (_queue)
        {
            if (_sending)
            {
                _queued = (target, brightness);
                return;
            }
            _sending = true;
        }
        (HaTarget Target, int Brightness)? next = (target, brightness);
        while (next is { } n)
        {
            try
            {
                await set(n.Target, n.Brightness);
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
