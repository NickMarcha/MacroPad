using System;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using MacroPad.Core;
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
    TrayIcon? _tray;
    GlobalHotkey? _hotkey;
    readonly CancellationTokenSource _stop = new();

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
            desktop.Exit += (_, _) =>
            {
                _stop.Cancel();
                _hotkey?.Dispose();
                _vm.Dispose();
            };
            CreateTrayIcon();
            _vm.ProfilesChanged += UpdateTrayMenu;
            _vm.ShowWindowRequested += ShowWindow;
            _vm.ShortcutChanged += RegisterShortcut;
            _vm.ExitRequested += Quit;
            SingleInstance.Listen(args => Dispatcher.UIThread.Post(() => _vm.HandleCommandLine(args)), _stop.Token);
            var toast = new ToastWindow { DataContext = _vm.Toast };
            _vm.Toast.Shown += toast.Present;

            var background = desktop.Args?.Contains(Autostart.BackgroundArgument) == true;
            if (!background) ShowWindow();

            _vm.ConnectCommand.Execute(null);
            _ = StartAsync();
            _ = _vm.CheckForUpdatesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    async System.Threading.Tasks.Task StartAsync()
    {
        MenuShortcut.Refresh();
        await _vm!.StartServicesAsync();
        UpdateTrayMenu();
        RegisterShortcut();
        // A start with --profile (when no MacroPad was running yet) switches once everything is up.
        if (_desktop?.Args is { } args && args.Contains("--profile")) _vm.HandleCommandLine(args);
    }

    void RegisterShortcut()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        var vm = _vm!;
        var shortcut = vm.NextProfileShortcut;
        if (shortcut is null)
        {
            vm.ShortcutStatus = "";
            return;
        }
        _hotkey = GlobalHotkey.Register(shortcut,
            () => Dispatcher.UIThread.Post(() => vm.SwitchProfile("next")),
            ok => Dispatcher.UIThread.Post(() => vm.ShortcutStatus = ok
                ? $"{shortcut} switches to the next profile."
                : $"Windows refused {shortcut}: another app probably uses it."));
    }

    void ShowWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow { DataContext = _vm };
            _window.Closing += (_, e) =>
            {
                // Quitting closes the window itself; let it.
                if (Quitting) return;
                if (!_vm!.KeepRunningInTray)
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
        if (Quitting) return;
        Quitting = true;
        _desktop?.Shutdown();
    }

    void CreateTrayIcon()
    {
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://MacroPad/Assets/macropad.ico"))),
            ToolTipText = "MacroPad",
        };
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [_tray]);
        UpdateTrayMenu();
    }

    /// <summary>Right-click menu: the profiles (the active one ticked), next profile, open, quit.</summary>
    void UpdateTrayMenu()
    {
        if (_tray is null || _vm is null) return;
        var menu = new NativeMenu();
        foreach (var profile in _vm.Settings.Profiles)
        {
            var name = profile.Name;
            var item = new NativeMenuItem(name)
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = name == _vm.Settings.Active.Name,
            };
            item.Click += (_, _) => _vm.SwitchProfile(name);
            menu.Add(item);
        }
        var next = new NativeMenuItem("Next profile") { Gesture = null };
        next.Click += (_, _) => _vm.SwitchProfile("next");
        menu.Add(next);
        menu.Add(new NativeMenuItemSeparator());
        var open = new NativeMenuItem("Open MacroPad");
        open.Click += (_, _) => ShowWindow();
        menu.Add(open);
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();
        menu.Add(quit);
        _tray.Menu = menu;
        _tray.ToolTipText = $"MacroPad · {_vm.Settings.Active.Name}";
    }
}
