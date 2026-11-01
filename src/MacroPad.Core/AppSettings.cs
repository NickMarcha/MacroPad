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

    /// <summary>MacroPad's profiles. The first is the Default every other profile falls back to.</summary>
    public List<Profile> Profiles { get; set; } = [new Profile()];

    /// <summary>The name of the active profile.</summary>
    public string? ActiveProfile { get; set; }

    /// <summary>Where on the screen the pop-up appears.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PopupPosition>))]
    public PopupPosition PopupPosition { get; set; } = PopupPosition.Bottom;

    /// <summary>Global keyboard shortcut for the next profile (Windows), e.g. "Ctrl+Alt+P"; null for none.</summary>
    public string? NextProfileShortcut { get; set; }

    /// <summary>The room or light the knob controls, as last picked with a Select key.</summary>
    public HaTarget? SelectedTarget { get; set; }

    /// <summary>Show a pop-up with the room's brightness after each Home Assistant action.</summary>
    public bool ShowPopups { get; set; } = true;

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

    public Profile Default => Profiles.Count > 0 ? Profiles[0] : AddDefault();

    public Profile Active => Profiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? Default;

    Profile AddDefault()
    {
        var p = new Profile();
        Profiles.Insert(0, p);
        return p;
    }

    public Profile? Find(string? name) => Profiles.FirstOrDefault(p => p.Name == name);

    /// <summary>What a press does now: the active profile's action, else the Default profile's.</summary>
    public PadAction? ActionFor(ushort code) => ActionFor(Active, code);

    /// <summary>A profile's action for a code, falling back to the Default profile.</summary>
    public PadAction? ActionFor(Profile profile, ushort code) =>
        profile.Actions.GetValueOrDefault(Triggers.Key(code)) ?? Default.Actions.GetValueOrDefault(Triggers.Key(code));

    /// <summary>Sets (or with null removes) a profile's own action for a code.</summary>
    public void SetAction(Profile profile, ushort code, PadAction? action)
    {
        if (action is null) profile.Actions.Remove(Triggers.Key(code));
        else profile.Actions[Triggers.Key(code)] = action;
    }

    public void SetAction(ushort code, PadAction? action) => SetAction(Default, code, action);

    /// <summary>Makes the profile after the active one active (wrapping round) and returns it.</summary>
    public Profile NextProfile()
    {
        var i = Profiles.IndexOf(Active);
        var next = Profiles[(i + 1) % Profiles.Count];
        ActiveProfile = next.Name;
        return next;
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

/// <summary>The nine places a pop-up can go: corners, edge middles and the centre.</summary>
public enum PopupPosition { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJson : JsonSerializerContext;
