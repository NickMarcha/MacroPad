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

    // Knob turns that arrive while a brightness change is still on its way, summed into the next one.
    int _pendingStep;
    HaTarget? _pendingTarget;
    bool _stepping;
    readonly KnobAcceleration _acceleration = new();
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Runs on the UI thread, so the fields above need no locking.</summary>
    async Task OnPadPressedAsync(ushort code)
    {
        if (!Triggers.IsTrigger(code) || Settings.ActionFor(code) is not { } action) return;

        if (action.Command == HaCommand.Select)
        {
            if (action.Target.Kind == HaTargetKind.Selected) return;
            Settings.SelectedTarget = action.Target;
            SaveSettings();
            Message = $"Knob controls {action.Target.Name}.";
            return;
        }

        var target = action.Target;
        if (target.Kind == HaTargetKind.Selected)
        {
            if (Settings.SelectedTarget is not { } selected)
            {
                Message = "No room selected yet: press a Select key first.";
                return;
            }
            target = selected;
        }
        if (_ha is null)
        {
            Message = $"{action.Describe()}: Home Assistant isn't connected.";
            return;
        }

        if (action.Command is HaCommand.Brighter or HaCommand.Dimmer)
        {
            var direction = action.Command == HaCommand.Brighter ? 1 : -1;
            var delta = direction * _acceleration.Step(action.Step, direction, _clock.Elapsed);
            if (_stepping && _pendingTarget == target)
            {
                _pendingStep += delta;
                return;
            }
            await StepAsync(target, delta);
            return;
        }

        await RunAsync(action with { Target = target });
    }

    async Task StepAsync(HaTarget target, int delta)
    {
        _stepping = true;
        _pendingTarget = target;
        try
        {
            while (delta != 0)
            {
                var step = Math.Clamp(Math.Abs(delta), 1, HaAction.MaxStep);
                if (!await RunAsync(new HaAction(delta > 0 ? HaCommand.Brighter : HaCommand.Dimmer, target, step))) break;
                delta = _pendingStep;
                _pendingStep = 0;
            }
        }
        finally
        {
            _stepping = false;
            _pendingStep = 0;
        }
    }

    async Task<bool> RunAsync(HaAction action)
    {
        try
        {
            await _ha!.RunAsync(action);
            Message = action.Describe();
            return true;
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            Message = $"{action.Describe()} failed: {e.Message}";
            return false;
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
