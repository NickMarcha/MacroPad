using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MacroPad.App.Views;

/// <summary>
/// A pop-up near the bottom of the screen that never takes focus. It hides itself a moment after the last update,
/// but not while the pointer is over it, so the slider can be dragged.
/// </summary>
public partial class ToastWindow : Window
{
    static readonly TimeSpan Visible = TimeSpan.FromSeconds(2.5);
    readonly DispatcherTimer _hide = new() { Interval = Visible };
    bool _pointerOver;

    public ToastWindow()
    {
        InitializeComponent();
        // On X11 (Linux): a notification window stays on top and out of the taskbar. Before the first Show.
        Trace.Write("pop-up: " + X11Notification.Apply(this));
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            if (!_pointerOver) Hide();
        };
        PointerEntered += (_, _) => _pointerOver = true;
        PointerExited += (_, _) =>
        {
            _pointerOver = false;
            _hide.Stop();
            _hide.Start();
        };
    }

    /// <summary>Shows the pop-up (or keeps it up) and restarts the hide timer.</summary>
    public void Present()
    {
        if (!IsVisible) Show();
        Place();
        _hide.Stop();
        _hide.Start();
    }

    void Place()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)(Bounds.Width * scale);
        var height = (int)(Bounds.Height * scale);
        var margin = (int)(24 * scale);
        var where = (DataContext as ViewModels.ToastViewModel)?.Position ?? Core.PopupPosition.Bottom;
        var column = (int)where % 3;
        var row = (int)where / 3;
        var x = column switch { 0 => area.X + margin, 1 => area.X + (area.Width - width) / 2, _ => area.Right - width - margin };
        var y = row switch { 0 => area.Y + margin, 1 => area.Y + (area.Height - height) / 2, _ => area.Bottom - height - margin * 2 };
        Position = new PixelPoint(x, y);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Place();
    }
}
