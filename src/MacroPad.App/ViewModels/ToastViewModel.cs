using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MacroPad.Core.HomeAssistant;

namespace MacroPad.App.ViewModels;

/// <summary>The pop-up after a Home Assistant action: the room, what happened, and a brightness slider you can drag.</summary>
public partial class ToastViewModel : ViewModelBase
{
    readonly Func<HaTarget, int, Task> _setBrightness;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    bool _updating;
    LightLevel? _level;

    public ToastViewModel(Func<HaTarget, int, Task> setBrightness)
    {
        _setBrightness = setBrightness;
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            if (Target is { } target)
                await _setBrightness(target, (int)Math.Round(Percent * 255 / 100));
        };
    }

    /// <summary>Raised when the pop-up should appear (or stay up, with new content).</summary>
    public event Action? Shown;

    public HaTarget? Target { get; private set; }

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Caption { get; set; } = "";
    [ObservableProperty] public partial string Detail { get; set; } = "";
    [ObservableProperty] public partial double Percent { get; set; }
    [ObservableProperty] public partial bool HasLevel { get; set; }
    [ObservableProperty] public partial MacroPad.Core.PopupPosition Position { get; set; }

    /// <summary>A pop-up with just text, for profile switches.</summary>
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
    public void Show(HaTarget target, string caption, LightLevel? level)
    {
        if (target != Target) _level = null;
        Target = target;
        Title = target.Name;
        Caption = caption;
        if (level is not null) _level = level;
        _updating = true;
        HasLevel = _level is not null;
        if (_level is not null)
        {
            Percent = _level.IsOn ? _level.Percent : 0;
            Detail = Describe(_level.IsOn, _level.On, _level.Total, _level.Percent);
        }
        _updating = false;
        Shown?.Invoke();
    }

    partial void OnPercentChanged(double value)
    {
        if (_updating || _level is null) return;
        // Dragged by the user: show it at once, send it when the slider rests for a moment.
        Detail = Describe(value > 0, value > 0 ? _level.Total : 0, _level.Total, (int)Math.Round(value));
        Caption = "Brightness";
        _debounce.Stop();
        _debounce.Start();
        Shown?.Invoke();
    }

    static string Describe(bool on, int lightsOn, int total, int percent)
    {
        if (!on) return "Off";
        var lights = total > 1 ? $"{lightsOn} of {total} lights on · " : "";
        return $"{lights}{percent}%";
    }
}
