using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroPad.Core;
using MacroPad.Core.HomeAssistant;

namespace MacroPad.App.ViewModels;

/// <summary>Home Assistant: the connection settings, and running actions when the pad sends a trigger code.</summary>
public partial class MainViewModel
{
    readonly PadListener _listener = new();
    HomeAssistantClient? _ha;

    public AppSettings Settings { get; } = AppSettings.Load();

    [ObservableProperty] public partial string HaUrl { get; set; } = "";
    [ObservableProperty] public partial string HaToken { get; set; } = "";
    [ObservableProperty] public partial string HaStatus { get; set; } = "Not connected.";
    [ObservableProperty] public partial bool IsHaConnected { get; set; }

    ToastViewModel? _toast;

    /// <summary>The pop-up after Home Assistant actions and profile switches; its slider sets the brightness.</summary>
    public ToastViewModel Toast => _toast ??= new ToastViewModel(SetBrightnessFromPopupAsync) { Position = Settings.PopupPosition };

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

    /// <summary>Starts listening to the pad and, if Home Assistant was set up before, connects to it.</summary>
    public async Task StartHomeAssistantAsync()
    {
        InitProfiles();
        _listener.Pressed += code => Dispatcher.UIThread.Post(() => _ = OnPadPressedAsync(code));
        _listener.Start();
        HaUrl = Settings.HomeAssistantUrl ?? "";
        if (Settings.HomeAssistantUrl is { } url && Settings.Token is { } token)
            await ConnectHomeAssistantAsync(url, token, save: false);
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
            var client = new HomeAssistantClient(Http, HomeAssistantClient.NormalizeUrl(url), token);
            var targets = await client.GetTargetsAsync();
            _ha = client;
            foreach (var t in targets.Where(t => !SlotViewModel.HaTargets.Contains(t)))
                SlotViewModel.HaTargets.Add(t);
            HaUrl = client.BaseUrl.ToString();
            IsHaConnected = true;
            HaStatus = $"Connected: {targets.Count(t => t.Kind == HaTargetKind.Area)} areas, {targets.Count(t => t.Kind == HaTargetKind.Light)} lights.";
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
