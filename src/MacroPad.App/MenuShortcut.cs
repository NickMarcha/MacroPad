using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using Avalonia.Platform;
using MacroPad.Core.Updates;

namespace MacroPad.App;

/// <summary>
/// Makes MacroPad findable in the Start menu (Windows: a .lnk under the user's Programs folder) or the applications
/// menu (Linux: ~/.local/share/applications/macropad.desktop with the icon). The AUR package ships its own menu entry,
/// so package-managed installs don't get one. An existing shortcut is rewritten at each start, so it follows the
/// binary if it moves.
/// </summary>
public static class MenuShortcut
{
    static readonly InstallInfo Install = InstallInfo.Detect();

    public static bool IsSupported =>
        Install.Kind is InstallKind.Portable or InstallKind.ReadOnly && Install.ExecutablePath is not null;

    public static string Label => OperatingSystem.IsWindows() ? "Show MacroPad in the Start menu" : "Show MacroPad in the applications menu";

    public static string Note => Install.Kind switch
    {
        InstallKind.PackageManaged => "Your package manager already added MacroPad to the menu.",
        InstallKind.Development => "Not for development builds.",
        _ when OperatingSystem.IsWindows() => "Adds a shortcut to your Start menu, so you can search for MacroPad. It points at this file; move the file and MacroPad updates the shortcut on its next start.",
        _ => "Adds MacroPad to your desktop's applications menu (~/.local/share/applications).",
    };

    static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x
        ? x
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    public static string ShortcutPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MacroPad.lnk")
        : Path.Combine(DataHome, "applications", "macropad.desktop");

    static string LinuxIcon => Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps", "macropad.png");

    public static bool IsEnabled => IsSupported && File.Exists(ShortcutPath);

    public static void Set(bool enabled)
    {
        if (!IsSupported) return;
        if (!enabled)
        {
            File.Delete(ShortcutPath);
            return;
        }
        Write();
    }

    /// <summary>At start: points an existing shortcut at this binary again.</summary>
    public static void Refresh()
    {
        if (!IsEnabled) return;
        try { Write(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException) { }
    }

    static void Write()
    {
        var exe = Install.ExecutablePath!;
        var dir = Path.GetDirectoryName(exe)!;
        if (OperatingSystem.IsWindows())
        {
            WindowsShortcut.Create(ShortcutPath, exe, dir, "Configure your macropad and its Home Assistant actions");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(LinuxIcon)!);
        using (var icon = AssetLoader.Open(new Uri("avares://MacroPad/Assets/macropad.png")))
        using (var file = File.Create(LinuxIcon))
            icon.CopyTo(file);
        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
        File.WriteAllText(ShortcutPath, string.Join('\n',
            "[Desktop Entry]",
            "Type=Application",
            "Version=1.5",
            "Name=MacroPad",
            "Comment=Configure your macropad and its Home Assistant actions",
            $"Exec={Quote(exe)}",
            $"Path={dir}",
            $"Icon={LinuxIcon}",
            "Terminal=false",
            "Categories=Utility;Settings;HardwareSettings;",
            "Keywords=macropad;keyboard;macro;knob;",
            ""));
    }

    /// <summary>Quotes a path for a desktop entry's Exec key, as the Desktop Entry spec asks.</summary>
    static string Quote(string path)
    {
        var escaped = new StringBuilder();
        foreach (var c in path)
        {
            if (c is '"' or '`' or '$' or '\\') escaped.Append('\\');
            escaped.Append(c);
        }
        return "\"" + escaped.ToString().Replace("\\", "\\\\").Replace("%", "%%") + "\"";
    }
}

/// <summary>Windows .lnk files through the shell's ShellLink object (IShellLinkW and IPersistFile), with source-generated COM.</summary>
internal static partial class WindowsShortcut
{
    static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");

    public static void Create(string shortcut, string target, string workingDir, string description)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        CoInitializeEx(IntPtr.Zero, 2); // STA; already initialised on Avalonia's thread is fine
        Directory.CreateDirectory(Path.GetDirectoryName(shortcut)!);
        var hr = CoCreateInstance(ShellLinkClass, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, typeof(IShellLinkW).GUID, out var pointer);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var instance = new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
            var link = (IShellLinkW)instance;
            link.SetPath(target);
            link.SetWorkingDirectory(workingDir);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            ((IPersistFile)instance).Save(shortcut, true);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr instance);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    void GetPath(IntPtr file, int size, IntPtr findData, uint flags);
    void GetIDList(out IntPtr idList);
    void SetIDList(IntPtr idList);
    void GetDescription(IntPtr name, int size);
    void SetDescription(string name);
    void GetWorkingDirectory(IntPtr dir, int size);
    void SetWorkingDirectory(string dir);
    void GetArguments(IntPtr args, int size);
    void SetArguments(string args);
    void GetHotkey(out ushort hotkey);
    void SetHotkey(ushort hotkey);
    void GetShowCmd(out int showCmd);
    void SetShowCmd(int showCmd);
    void GetIconLocation(IntPtr iconPath, int size, out int icon);
    void SetIconLocation(string iconPath, int icon);
    void SetRelativePath(string relativePath, uint reserved);
    void Resolve(IntPtr hwnd, uint flags);
    void SetPath(string file);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("0000010b-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    void GetClassID(out Guid classId);
    [PreserveSig] int IsDirty();
    void Load(string fileName, uint mode);
    void Save(string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
    void SaveCompleted(string fileName);
    void GetCurFile(out IntPtr fileName);
}
