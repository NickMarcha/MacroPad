using System;
using System.Threading.Tasks;
using MacroPad.Core;

namespace MacroPad.App.ViewModels;

/// <summary>
/// Running the action for a trigger code the pad sent: lights through Home Assistant, audio through Bragi, and
/// profile switches.
/// </summary>
public partial class MainViewModel
{
    readonly KnobAcceleration _acceleration = new();
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    LevelDial? _dial;

    /// <summary>Knob turns: an absolute level MacroPad keeps itself, so fast turns can't make the target jump.</summary>
    LevelDial Dial
    {
        get
        {
            if (_dial is not null) return _dial;
            _dial = new LevelDial(t => Service(t).GetLevelAsync(t), (t, v) => Service(t).SetValueAsync(t, v), () => _clock.Elapsed);
            _dial.Changed += (target, level) =>
            {
                Trace.Write($"dial {target.Name} -> {level.Value}");
                Message = $"{target.Name}: {Describe(target, level)}";
                if (Settings.ShowPopups) Toast.Show(target, target.Kind == TargetKind.Audio ? "Volume" : "Brightness", level);
            };
            _dial.Failed += (target, e) => Message = $"{target.Name}: {e.Message}";
            return _dial;
        }
    }

    /// <summary>The connected service for a target. Throws a readable error when it isn't connected.</summary>
    ITargetService Service(Target target)
    {
        ITargetService? service = target.Kind == TargetKind.Audio ? _bragi : _ha;
        return service ?? throw new System.Net.Http.HttpRequestException(
            target.Kind == TargetKind.Audio ? "Bragi isn't connected (Settings)." : "Home Assistant isn't connected (Settings).");
    }

    static string Describe(Target target, Level level) => target.Kind == TargetKind.Audio
        ? (level.IsOn ? $"{level.Percent}%" : $"muted · {level.Percent}%")
        : (level.IsOn ? $"{level.Percent}%" : "off");

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
                if (action.Target is null || action.Target.Kind == TargetKind.Selected) return;
                Settings.Active.SelectedTarget = action.Target;
                SaveSettings();
                Message = $"Knob controls {action.Target.Name}.";
                await ShowPopupAsync(action.Target, "Selected for the knob");
                return;
        }

        if (action.Target is not { } target) return;
        if (target.Kind == TargetKind.Selected)
        {
            if (Settings.Active.SelectedTarget is not { } selected)
            {
                Message = "Nothing selected yet: press a Select key first.";
                return;
            }
            target = selected;
        }

        try
        {
            if (action.Command is PadCommand.Up or PadCommand.Down)
            {
                var direction = action.Command == PadCommand.Up ? 1 : -1;
                var delta = direction * _acceleration.Step(action.Step, direction, _clock.Elapsed);
                Trace.Write($"click {code:X4} delta {delta}");
                await Dial.TurnAsync(target, delta);
                return;
            }

            _dial?.Forget();
            await Service(target).RunAsync(action.Command, target);
            var done = action with { Target = target };
            Message = done.Describe();
            await ShowPopupAsync(target, done.Describe());
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Message = $"{(action with { Target = target }).Describe()}: {e.Message}";
            if (Settings.ShowPopups) Toast.ShowMessage(target.Name, e.Message);
        }
    }

    /// <summary>Reads the target's level now and shows the pop-up, when pop-ups are on.</summary>
    async Task ShowPopupAsync(Target target, string caption)
    {
        if (!Settings.ShowPopups) return;
        Level? level = null;
        try
        {
            level = await Service(target).GetLevelAsync(target);
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
        }
        Toast.Show(target, caption, level);
    }

    /// <summary>The pop-up's slider: an absolute value in the target's units.</summary>
    async Task SetFromPopupAsync(Target target, int value)
    {
        try
        {
            _dial?.Forget();
            await Service(target).SetValueAsync(target, value);
            Message = $"{target.Name}: {value}";
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            Message = $"Setting {target.Name} failed: {e.Message}";
        }
    }
}
