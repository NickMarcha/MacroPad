using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using MacroPad.App.ViewModels;
using MacroPad.App.Views;

namespace MacroPad.App;

public partial class App : Application
{
    MainViewModel? _vm;
    MainWindow? _window;
    IClassicDesktopStyleApplicationLifetime? _desktop;

    /// <summary>Set when the user picks Quit, so closing the window really exits.</summary>
    public static bool Quitting { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            _vm = new MainViewModel();
            // The window can close while MacroPad stays in the tray listening to the pad.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => _vm.Dispose();
            CreateTrayIcon();

            var background = desktop.Args?.Contains(Autostart.BackgroundArgument) == true;
            if (!background) ShowWindow();

            _vm.ConnectCommand.Execute(null);
            _ = _vm.StartHomeAssistantAsync();
            _ = _vm.CheckForUpdatesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    void ShowWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow { DataContext = _vm };
            _window.Closing += (_, e) =>
            {
                if (Quitting || !_vm!.KeepRunningInTray)
                {
                    Quit();
                    return;
                }
                e.Cancel = true;
                _window.Hide();
            };
            _desktop!.MainWindow = _window;
        }
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    void Quit()
    {
        Quitting = true;
        _desktop?.Shutdown();
    }

    void CreateTrayIcon()
    {
        var open = new NativeMenuItem("Open MacroPad");
        open.Click += (_, _) => ShowWindow();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();
        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://MacroPad/Assets/macropad.ico"))),
            ToolTipText = "MacroPad",
            Menu = [open, new NativeMenuItemSeparator(), quit],
        };
        tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [tray]);
    }
}
