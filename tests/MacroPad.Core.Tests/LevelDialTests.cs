namespace MacroPad.Core.Tests;

public class LevelDialTests
{
    static readonly Target Panel = new(TargetKind.Light, "light.living_panel", "Living Panel");

    sealed class FakeLight
    {
        public int Reads;
        public List<int> Sent = [];
        public TimeSpan Now;
        public TaskCompletionSource? Hold;
        public Level Level = Level.Lights(1, 1, 100);

        public LevelDial Dial() => new(
            _ => { Reads++; return Task.FromResult(Level); },
            async (_, b) => { Sent.Add(b); if (Hold is not null) await Hold.Task; },
            () => Now);
    }

    [Fact]
    public async Task Reads_once_then_sends_absolute_values()
    {
        var light = new FakeLight();
        var dial = light.Dial();
        await dial.TurnAsync(Panel, 40);
        await dial.TurnAsync(Panel, 40);
        await dial.TurnAsync(Panel, -10);
        Assert.Equal(1, light.Reads);
        Assert.Equal([140, 180, 170], light.Sent);
    }

    [Fact]
    public async Task While_a_send_is_slow_only_the_newest_value_waits()
    {
        var light = new FakeLight { Hold = new TaskCompletionSource() };
        var dial = light.Dial();
        var first = dial.TurnAsync(Panel, 10);           // sends 110 and waits
        await dial.TurnAsync(Panel, 10);                  // 120, queued
        await dial.TurnAsync(Panel, 10);                  // 130 replaces 120
        light.Hold.SetResult();
        await first;
        Assert.Equal([110, 130], light.Sent);
    }

    [Fact]
    public async Task Clamps_turns_off_at_zero_and_reports_each_click()
    {
        var light = new FakeLight();
        var dial = light.Dial();
        var seen = new List<Level>();
        dial.Changed += (_, level) => seen.Add(level);
        await dial.TurnAsync(Panel, -250);
        await dial.TurnAsync(Panel, 300);
        Assert.Equal([0, 255], light.Sent);
        Assert.False(seen[0].IsOn);
        Assert.Equal(100, seen[1].Percent);
    }

    [Fact]
    public async Task Audio_uses_percent_and_keeps_mute()
    {
        var light = new FakeLight { Level = new Level(false, 60, 100, 150) };   // muted at 60%
        var dial = light.Dial();
        Level? seen = null;
        dial.Changed += (_, level) => seen = level;
        await dial.TurnAsync(Panel, 5);            // 5/255 of full: 2%
        await dial.TurnAsync(Panel, 40 * 8);       // fast: clamps at 150%
        Assert.Equal([62, 150], light.Sent);
        Assert.False(seen!.IsOn);                  // turning doesn't unmute
        Assert.Equal(150, seen.MaxPercent);
    }

    [Fact]
    public async Task Reads_again_after_a_pause_or_when_forgotten()
    {
        var light = new FakeLight();
        var dial = light.Dial();
        await dial.TurnAsync(Panel, 5);
        light.Now += LevelDial.Fresh + TimeSpan.FromSeconds(1);
        light.Level = Level.Lights(1, 1, 50);   // someone dimmed it in the meantime
        await dial.TurnAsync(Panel, 5);
        dial.Forget();
        await dial.TurnAsync(Panel, 5);
        Assert.Equal(3, light.Reads);
        Assert.Equal([105, 55, 55], light.Sent);
    }
}
