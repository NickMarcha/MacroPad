using MacroPad.Core.HomeAssistant;

namespace MacroPad.Core.Tests;

public class BrightnessDialTests
{
    static readonly HaTarget Panel = new(HaTargetKind.Light, "light.living_panel", "Living Panel");

    sealed class FakeLight
    {
        public int Reads;
        public List<int> Sent = [];
        public TimeSpan Now;
        public TaskCompletionSource? Hold;
        public LightLevel Level = new(1, 1, 100);

        public BrightnessDial Dial() => new(
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
        var seen = new List<LightLevel>();
        dial.Changed += (_, level) => seen.Add(level);
        await dial.TurnAsync(Panel, -250);
        await dial.TurnAsync(Panel, 300);
        Assert.Equal([0, 255], light.Sent);
        Assert.False(seen[0].IsOn);
        Assert.Equal(100, seen[1].Percent);
    }

    [Fact]
    public async Task Reads_again_after_a_pause_or_when_forgotten()
    {
        var light = new FakeLight();
        var dial = light.Dial();
        await dial.TurnAsync(Panel, 5);
        light.Now += BrightnessDial.Fresh + TimeSpan.FromSeconds(1);
        light.Level = new LightLevel(1, 1, 50);   // someone dimmed it in the meantime
        await dial.TurnAsync(Panel, 5);
        dial.Forget();
        await dial.TurnAsync(Panel, 5);
        Assert.Equal(3, light.Reads);
        Assert.Equal([105, 55, 55], light.Sent);
    }
}
