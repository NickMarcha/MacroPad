using System.Reflection;

namespace MacroPad.Core.Updates;

public enum InstallKind
{
    /// <summary>Single-file copy in a folder the user can write: the app updates itself.</summary>
    Portable,
    /// <summary>Installed by a package manager (the AUR package writes a <c>package-managed</c> file beside the binary).</summary>
    PackageManaged,
    /// <summary>A copy in a folder the user cannot write: the app only says when a new version is out.</summary>
    ReadOnly,
    /// <summary>Built from source (<c>dotnet run</c>): never updates.</summary>
    Development,
}

/// <summary>How this copy of the app is installed, and so where its updates come from.</summary>
public sealed record InstallInfo(InstallKind Kind, string? ExecutablePath, AppVersion Version, string? PackageManager)
{
    public const string PackageManagedFile = "package-managed";

    public bool CanSelfUpdate => Kind == InstallKind.Portable;

    /// <summary>One line for the window, so a pacman install and a portable copy can be told apart.</summary>
    public string Describe() => Kind switch
    {
        InstallKind.PackageManaged => $"Installed by {PackageManager}: updates come from your package manager.",
        InstallKind.ReadOnly => $"Copy in a read-only folder ({Path.GetDirectoryName(ExecutablePath)}): it can't update itself.",
        InstallKind.Development => "Development build: no updates.",
        _ => "Portable copy: it updates itself.",
    };

    public static InstallInfo Detect() => Detect(Environment.ProcessPath, AppVersion.Current);

    public static InstallInfo Detect(string? executable, AppVersion version)
    {
        // `dotnet run` and `dotnet MacroPad.dll` run through the dotnet host, not a published single file.
        if (executable is null || version.IsDevelopment ||
            Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return new InstallInfo(InstallKind.Development, executable, version, null);

        var dir = Path.GetDirectoryName(executable)!;
        var marker = Path.Combine(dir, PackageManagedFile);
        if (File.Exists(marker))
        {
            var manager = File.ReadAllText(marker).Trim();
            return new InstallInfo(InstallKind.PackageManaged, executable, version, manager.Length > 0 ? manager : "your package manager");
        }
        return new InstallInfo(IsWritable(dir) ? InstallKind.Portable : InstallKind.ReadOnly, executable, version, null);
    }

    static bool IsWritable(string dir)
    {
        try
        {
            using (File.Create(Path.Combine(dir, ".macropad-write-test-" + Guid.NewGuid().ToString("N")), 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>A release version, X.Y.Z. Local builds are 0.0.0 (the release workflow stamps the real one).</summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    public bool IsDevelopment => this == default;

    public static AppVersion Current
    {
        get
        {
            var info = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return TryParse(info, out var v) ? v : default;
        }
    }

    /// <summary>Reads "1.2.3", "v1.2.3" or "1.2.3+commit"; anything else fails.</summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('v', 'V');
        s = s.Split('+', '-')[0];
        var parts = s.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var a) || !int.TryParse(parts[1], out var b) || !int.TryParse(parts[2], out var c))
            return false;
        version = new AppVersion(a, b, c);
        return true;
    }

    public int CompareTo(AppVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) : Minor != other.Minor ? Minor.CompareTo(other.Minor) : Patch.CompareTo(other.Patch);

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
