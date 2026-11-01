using Avalonia;
using System;
using System.Diagnostics;
using MacroPad.Core.Updates;

namespace MacroPad.App;

sealed class Program
{
    /// <summary>Set by an update: the binary to start once this process has shut down.</summary>
    public static string? RestartWith { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Started by an update: let the old copy finish exiting first, or it would look like a running MacroPad and
        // this start would hand over to it and quit.
        Updater.WaitForPreviousToExit(args, TimeSpan.FromSeconds(15));

        // Already running: hand over the command line (--profile next, or just "show the window") and stop.
        if (SingleInstance.TrySendToRunning(args)) return 0;
        Updater.CleanUp(Environment.ProcessPath);
        var code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // The window and the pad are closed and the instance pipe is gone: now the updated copy can take over.
        if (RestartWith is { } exe)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { Updater.AfterUpdateArgument, Environment.ProcessId.ToString() } });
        return code;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
