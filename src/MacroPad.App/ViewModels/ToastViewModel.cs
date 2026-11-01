using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MacroPad.Core;

namespace MacroPad.App.ViewModels;

/// <summary>
/// The pop-up after an action: the target, what happened, and a slider for its brightness or volume that you can drag.
/// Lights go to 100%, audio to 150%.
/// </summary>
public partial class ToastViewModel : ViewModelBase
{
    readonly Func<Target, int, Task> _setValue;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    bool _updating;
    Level? _level;

    public ToastViewModel(Func<Target, int, Task> setValue)
    {
        _setValue = setValue;
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            if (Target is { } target && _level is { } level)
                await _setValue(target, (int)Math.Round(Percent * level.Full / 100));
        };
    }

    /// <summary>Raised when the pop-up should appear (or stay up, with new content).</summary>
    public event Action? Shown;

    public Target? Target { get; private set; }

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Caption { get; set; } = "";
    [ObservableProperty] public partial string Detail { get; set; } = "";
    [ObservableProperty] public partial double Percent { get; set; }
    [ObservableProperty] public partial double MaxPercent { get; set; } = 100;
    [ObservableProperty] public partial bool HasLevel { get; set; }
    [ObservableProperty] public partial PopupPosition Position { get; set; }

    /// <summary>A pop-up with just text, for profile switches and errors.</summary>
    public void ShowMessage(string title, string caption)
    {
        Target = null;
        _level = null;
        Title = title;
        Caption = caption;
        Detail = "";
        HasLevel = false;
        Shown?.Invoke();
    }

    /// <summary>Shows a target with what just happened. <paramref name="level"/> null keeps the last reading for it.</summary>
    public void Show(Target target, string caption, Level? level)
    {
        if (target != Target) _level = null;
        Target = target;
        Title = target.Name;
        Caption = caption;
        if (level is not null) _level = level;
        _updating = true;
        HasLevel = _level is not null;
        if (_level is null)
        {
            // No reading (the service didn't answer): don't leave the previous target's level on show.
            Detail = "";
        }
        else
        {
            MaxPercent = _level.MaxPercent;
            // An off light shows at 0; muted audio keeps showing its volume.
            Percent = _level.Total > 0 && !_level.IsOn ? 0 : _level.Percent;
            Detail = Describe(_level, _level.Percent);
        }
        _updating = false;
        Shown?.Invoke();
    }

    partial void OnPercentChanged(double value)
    {
        if (_updating || _level is null) return;
        // Dragged by the user: show it at once, send it when the slider rests for a moment.
        var percent = (int)Math.Round(value);
        var dragged = _level.Total > 0
            ? Level.Lights(percent > 0 ? _level.Total : 0, _level.Total, (int)Math.Round(value * _level.Full / 100))
            : _level with { Value = (int)Math.Round(value * _level.Full / 100) };
        Detail = Describe(dragged, percent);
        Caption = Target?.Kind == TargetKind.Audio ? "Volume" : "Brightness";
        _debounce.Stop();
        _debounce.Start();
        Shown?.Invoke();
    }

    static string Describe(Level level, int percent)
    {
        if (level.Total == 0) return level.IsOn ? $"{percent}%" : $"Muted · {percent}%";
        if (!level.IsOn) return "Off";
        var lights = level.Total > 1 ? $"{level.On} of {level.Total} lights on · " : "";
        return $"{lights}{percent}%";
    }
}
