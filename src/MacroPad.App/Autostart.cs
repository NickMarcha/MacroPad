using System;
using System.IO;
using MacroPad.Core.Updates;

namespace MacroPad.App;

/// <summary>
/// Starting MacroPad hidden in the tray at login, so pad presses reach Home Assistant without opening the window:
/// the Run key on Windows, an XDG autostart entry on Linux.
/// </summary>
public static class Autostart
{
    public const string BackgroundArgument = "--background";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "MacroPad";

    static readonly InstallInfo Install = InstallInfo.Detect();

    /// <summary>Not for development builds: those run through `dotnet` from a build folder.</summary>
    public static bool IsSupported => Install.Kind != InstallKind.Development && Install.ExecutablePath is not null;

    static string LinuxEntry => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", "macropad.desktop");

    public static bool IsEnabled
    {
        get
        {
            if (!IsSupported) return false;
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(Name) is string;
            }
            return File.Exists(LinuxEntry);
        }
    }

    public static void Set(bool enabled)
    {
        if (!IsSupported) return;
        // The AUR package's command, so autostart keeps working when pacman moves the binary; else this file.
        var exe = Install.Kind == InstallKind.PackageManaged ? "macropad" : Install.ExecutablePath!;
        if (OperatingSystem.IsWindows())
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(Name, $"\"{exe}\" {BackgroundArgument}");
            else key.DeleteValue(Name, throwOnMissingValue: false);
            return;
        }
        if (!enabled)
        {
            File.Delete(LinuxEntry);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(LinuxEntry)!);
        File.WriteAllText(LinuxEntry,
            "[Desktop Entry]\nType=Application\nName=MacroPad\nComment=Sends macropad presses to Home Assistant\n" +
            $"Exec=\"{exe}\" {BackgroundArgument}\nIcon=macropad\nTerminal=false\nX-GNOME-Autostart-enabled=true\n");
    }
}
