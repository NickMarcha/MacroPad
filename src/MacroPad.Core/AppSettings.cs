using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroPad.Core.HomeAssistant;

namespace MacroPad.Core;

/// <summary>
/// The app's settings, in settings.json under %APPDATA%\MacroPad (Windows) or ~/.config/macropad (Linux). The Home
/// Assistant token is encrypted for the current Windows user (DPAPI); on Linux the file is readable only by its owner.
/// </summary>
public sealed class AppSettings
{
    public string? HomeAssistantUrl { get; set; }
    public string? HomeAssistantToken { get; set; }

    /// <summary>Home Assistant actions by trigger code (hex, e.g. "0F23").</summary>
    public Dictionary<string, HaAction> Actions { get; set; } = [];

    /// <summary>The room or light the knob controls, as last picked with a Select key.</summary>
    public HaTarget? SelectedTarget { get; set; }

    /// <summary>Closing the window keeps MacroPad in the tray so pad presses still reach Home Assistant.</summary>
    public bool KeepRunningInTray { get; set; } = true;

    [JsonIgnore]
    public string? Token
    {
        get => Unprotect(HomeAssistantToken);
        set => HomeAssistantToken = string.IsNullOrWhiteSpace(value) ? null : Protect(value.Trim());
    }

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        OperatingSystem.IsWindows() ? "MacroPad" : "macropad");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public HaAction? ActionFor(ushort code) => Actions.GetValueOrDefault(code.ToString("X4"));

    public void SetAction(ushort code, HaAction? action)
    {
        if (action is null) Actions.Remove(code.ToString("X4"));
        else Actions[code.ToString("X4")] = action;
    }

    public static AppSettings Load(string? path = null)
    {
        path ??= FilePath;
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.AppSettings) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            // A broken file is kept beside the fresh one rather than lost.
            File.Copy(path, path + ".broken", overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, overwrite: true);
    }

    const string DpapiPrefix = "dpapi:";

    static string Protect(string token) => OperatingSystem.IsWindows()
        ? DpapiPrefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser))
        : token;

    static string? Unprotect(string? stored)
    {
        if (stored is null || !stored.StartsWith(DpapiPrefix, StringComparison.Ordinal)) return stored;
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored[DpapiPrefix.Length..]), null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJson : JsonSerializerContext;
