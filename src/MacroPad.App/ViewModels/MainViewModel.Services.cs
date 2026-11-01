using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroPad.Core;
using MacroPad.Core.Bragi;
using MacroPad.Core.HomeAssistant;

namespace MacroPad.App.ViewModels;

/// <summary>The services MacroPad drives (Home Assistant for lights, Bragi for audio) and the app settings around them.</summary>
public partial class MainViewModel
{
    readonly PadListener _listener = new();
    HomeAssistantClient? _ha;
    BragiClient? _bragi;

    public AppSettings Settings { get; } = AppSettings.Load();

    [ObservableProperty] public partial string HaUrl { get; set; } = "";
    [ObservableProperty] public partial string HaToken { get; set; } = "";
    [ObservableProperty] public partial string HaStatus { get; set; } = "Not connected.";
    [ObservableProperty] public partial bool IsHaConnected { get; set; }

    [ObservableProperty] public partial string BragiUrl { get; set; } = "";
    [ObservableProperty] public partial string BragiKeyInput { get; set; } = "";
    [ObservableProperty] public partial string BragiStatus { get; set; } = "Not connected.";

    ToastViewModel? _toast;

    /// <summary>The pop-up after Home Assistant actions and profile switches; its slider sets the brightness.</summary>
    public ToastViewModel Toast => _toast ??= new ToastViewModel(SetFromPopupAsync) { Position = Settings.PopupPosition };

    public static System.Collections.Generic.IReadOnlyList<PopupPosition> PopupPositions { get; } = Enum.GetValues<PopupPosition>();

    public PopupPosition PopupPosition
    {
        get => Settings.PopupPosition;
        set
        {
            Settings.PopupPosition = value;
            SaveSettings();
            Toast.Position = value;
            OnPropertyChanged();
            // A preview, so you see where it goes.
            Toast.ShowMessage("Pop-ups appear here", "MacroPad");
        }
    }

    public bool ShowPopups
    {
        get => Settings.ShowPopups;
        set
        {
            Settings.ShowPopups = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool KeepRunningInTray
    {
        get => Settings.KeepRunningInTray;
        set
        {
            Settings.KeepRunningInTray = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool CanStartAtLogin => Autostart.IsSupported;

    public bool CanShowInMenu => MenuShortcut.IsSupported;
    public string MenuShortcutLabel => MenuShortcut.Label;
    public string MenuShortcutNote => MenuShortcut.Note;
    public string SettingsPath => AppSettings.FilePath;

    public bool ShowInMenu
    {
        get => MenuShortcut.IsEnabled;
        set
        {
            try
            {
                MenuShortcut.Set(value);
                Message = value ? "Added MacroPad to the menu." : "Removed MacroPad from the menu.";
            }
            catch (Exception e)
            {
                Message = $"Couldn't change the menu shortcut: {e.Message}";
            }
            OnPropertyChanged();
        }
    }

    public bool StartAtLogin
    {
        get => Autostart.IsEnabled;
        set
        {
            try
            {
                Autostart.Set(value);
            }
            catch (Exception e)
            {
                Message = $"Couldn't change start at login: {e.Message}";
            }
            OnPropertyChanged();
        }
    }

    /// <summary>Starts listening to the pad and connects to the services set up before.</summary>
    public async Task StartServicesAsync()
    {
        InitProfiles();
        _listener.Pressed += code => Dispatcher.UIThread.Post(() => _ = OnPadPressedAsync(code));
        _listener.Start();
        HaUrl = Settings.HomeAssistantUrl ?? "";
        BragiUrl = Settings.BragiUrl ?? "";
        var ha = Settings.HomeAssistantUrl is { } url && Settings.Token is { } token
            ? ConnectHomeAssistantAsync(url, token, save: false) : Task.CompletedTask;
        var bragi = Settings.BragiUrl is { } bUrl && Settings.BragiKey is { } key
            ? ConnectBragiAsync(bUrl, key, save: false) : Task.CompletedTask;
        await Task.WhenAll(ha, bragi);
    }

    /// <summary>Adds a service's targets to the editor's list, keeping its order: lights, then audio.</summary>
    static void AddTargets(System.Collections.Generic.IEnumerable<Target> targets)
    {
        foreach (var t in targets.Where(t => !SlotViewModel.Targets.Contains(t)))
            SlotViewModel.Targets.Add(t);
    }

    [RelayCommand]
    async Task ConnectBragiAsync()
    {
        // An empty key box keeps the key saved before.
        var key = string.IsNullOrWhiteSpace(BragiKeyInput) ? Settings.BragiKey : BragiKeyInput.Trim();
        if (string.IsNullOrWhiteSpace(BragiUrl) || key is null)
        {
            BragiStatus = "Enter Bragi's address and an API key.";
            return;
        }
        await ConnectBragiAsync(BragiUrl, key, save: true);
    }

    async Task ConnectBragiAsync(string url, string key, bool save)
    {
        BragiStatus = "Connecting…";
        try
        {
            var client = new BragiClient(Http, ServiceAddress.Normalize(url), key);
            var targets = await client.GetTargetsAsync();
            _bragi = client;
            AddTargets(targets);
            BragiUrl = client.BaseUrl.ToString();
            BragiStatus = $"Connected: {targets.Count} audio controls.";
            if (save)
            {
                Settings.BragiUrl = BragiUrl;
                Settings.BragiKey = key;
                SaveSettings();
                BragiKeyInput = "";
            }
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or UriFormatException or System.Text.Json.JsonException)
        {
            BragiStatus = e switch
            {
                TaskCanceledException => "Bragi didn't answer within 10 seconds.",
                System.Text.Json.JsonException => "That address answered, but not like Bragi's API.",
                _ => e.Message,
            };
        }
    }

    [RelayCommand]
    async Task ConnectHaAsync()
    {
        // An empty token box keeps the token saved before.
        var token = string.IsNullOrWhiteSpace(HaToken) ? Settings.Token : HaToken.Trim();
        if (string.IsNullOrWhiteSpace(HaUrl) || token is null)
        {
            HaStatus = "Enter the Home Assistant address and a long-lived access token.";
            return;
        }
        await ConnectHomeAssistantAsync(HaUrl, token, save: true);
    }

    async Task ConnectHomeAssistantAsync(string url, string token, bool save)
    {
        HaStatus = "Connecting…";
        try
        {
            var client = new HomeAssistantClient(Http, ServiceAddress.Normalize(url), token);
            var targets = await client.GetTargetsAsync();
            _ha = client;
            AddTargets(targets);
            HaUrl = client.BaseUrl.ToString();
            IsHaConnected = true;
            HaStatus = $"Connected: {targets.Count(t => t.Kind == TargetKind.Area)} areas, {targets.Count(t => t.Kind == TargetKind.Light)} lights.";
            if (save)
            {
                Settings.HomeAssistantUrl = HaUrl;
                Settings.Token = token;
                SaveSettings();
                HaToken = "";
            }
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or UriFormatException or System.Text.Json.JsonException)
        {
            IsHaConnected = false;
            HaStatus = e is TaskCanceledException ? "Home Assistant didn't answer within 10 seconds." : e.Message;
        }
    }

    void SaveSettings()
    {
        try
        {
            Settings.Save();
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Message = $"Couldn't save settings: {e.Message}";
        }
    }
}
