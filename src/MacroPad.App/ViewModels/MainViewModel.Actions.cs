using System;
using System.Threading.Tasks;
using MacroPad.Core;
using MacroPad.Core.HomeAssistant;

namespace MacroPad.App.ViewModels;

/// <summary>Running the action for a trigger code the pad sent: Home Assistant calls and profile switches.</summary>
public partial class MainViewModel
{
    readonly KnobAcceleration _acceleration = new();
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    BrightnessDial? _dial;

    /// <summary>Knob turns: absolute brightness that MacroPad keeps itself, so fast turns can't make the light jump.</summary>
    BrightnessDial Dial
    {
        get
        {
            if (_dial is not null) return _dial;
            _dial = new BrightnessDial(t => _ha!.GetLevelAsync(t), (t, b) => _ha!.SetBrightnessAsync(t, b, transition: 0), () => _clock.Elapsed);
            _dial.Changed += (target, level) =>
            {
                Trace.Write($"dial {target.Name} -> {level.Brightness}");
                Message = $"{target.Name}: {(level.IsOn ? $"{level.Percent}%" : "off")}";
                if (Settings.ShowPopups) Toast.Show(target, "Brightness", level);
            };
            _dial.Failed += (target, e) => Message = $"{target.Name}: {e.Message}";
            return _dial;
        }
    }

    /// <summary>Runs on the UI thread, so the fields above need no locking.</summary>
    async Task OnPadPressedAsync(ushort code)
    {
        if (!Triggers.IsTrigger(code) || Settings.ActionFor(code) is not { } action) return;

        switch (action.Command)
        {
            case PadCommand.NextProfile:
                SwitchProfile(Settings.NextProfile().Name);
                return;
            case PadCommand.Profile:
                SwitchProfile(action.ProfileName);
                return;
            case PadCommand.Select:
                if (action.Target is null || action.Target.Kind == HaTargetKind.Selected) return;
                Settings.SelectedTarget = action.Target;
                SaveSettings();
                Message = $"Knob controls {action.Target.Name}.";
                await ShowPopupAsync(action.Target, "Selected for the knob");
                return;
        }

        if (action.Target is not { } target) return;
        if (target.Kind == HaTargetKind.Selected)
        {
            if (Settings.SelectedTarget is not { } selected)
            {
                Message = "No room selected yet: press a Select key first.";
                return;
            }
            target = selected;
        }
        if (_ha is null)
        {
            Message = $"{action.Describe()}: Home Assistant isn't connected.";
            return;
        }

        if (action.Command is PadCommand.Brighter or PadCommand.Dimmer)
        {
            var direction = action.Command == PadCommand.Brighter ? 1 : -1;
            var delta = direction * _acceleration.Step(action.Step, direction, _clock.Elapsed);
            Trace.Write($"click {code:X4} delta {delta}");
            try
            {
                await Dial.TurnAsync(target, delta);
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                Message = $"{target.Name}: {e.Message}";
            }
            return;
        }

        _dial?.Forget();
        if (await RunAsync(action with { Target = target }))
            await ShowPopupAsync(target, action.Command switch
            {
                PadCommand.TurnOn => "Turned on",
                PadCommand.TurnOff => "Turned off",
                _ => "Toggled",
            });
    }

    /// <summary>Reads how lit the target is now and shows the pop-up, when pop-ups are on.</summary>
    async Task ShowPopupAsync(HaTarget target, string caption)
    {
        if (!Settings.ShowPopups || _ha is null) return;
        LightLevel? level = null;
        try
        {
            level = await _ha.GetLevelAsync(target);
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
        }
        Toast.Show(target, caption, level);
    }

    async Task SetBrightnessFromPopupAsync(HaTarget target, int brightness)
    {
        if (_ha is null) return;
        try
        {
            _dial?.Forget();
            await _ha.SetBrightnessAsync(target, brightness);
            Message = $"{target.Name}: {(brightness == 0 ? "off" : $"{(int)Math.Round(brightness * 100 / 255.0)}%")}";
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            Message = $"Setting {target.Name} failed: {e.Message}";
        }
    }

    async Task<bool> RunAsync(PadAction action)
    {
        try
        {
            await _ha!.RunAsync(action);
            Message = action.Describe();
            return true;
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            Message = $"{action.Describe()} failed: {e.Message}";
            return false;
        }
    }
}
