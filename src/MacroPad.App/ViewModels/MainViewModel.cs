using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HidSharp;
using MacroPad.Core;
using MacroPad.Core.Updates;

namespace MacroPad.App.ViewModels;

public partial class LayerViewModel(int number, IReadOnlyList<SlotViewModel> buttons, IReadOnlyList<SlotViewModel> knobs) : ViewModelBase
{
    public int Number { get; } = number;
    public string Title => $"Layer {Number}";
    public IReadOnlyList<SlotViewModel> Buttons { get; } = buttons;
    public IReadOnlyList<SlotViewModel> Knobs { get; } = knobs;
    public IEnumerable<SlotViewModel> All => Buttons.Concat(Knobs);
}

/// <summary>A pad in the header's list.</summary>
public sealed record PadChoice(ConnectedPad Pad, PadRecord Record)
{
    public string Label => $"{Record.Label} · {Pad.Model}";
    public override string ToString() => Label;
}

public partial class MainViewModel : ViewModelBase, IDisposable
{
    public static IReadOnlyList<LedMode> LedModes { get; } = Enum.GetValues<LedMode>();
    public static IReadOnlyList<LedColor> LedColors { get; } = Enum.GetValues<LedColor>();

    MacroPadDevice? _device;
    readonly object _io = new();
    static readonly System.Net.Http.HttpClient Http = new();
    ReleaseInfo? _release;

    public InstallInfo Install { get; } = InstallInfo.Detect();
    public string VersionText => Install.Version.IsDevelopment ? "dev build" : $"v{Install.Version}";
    public string InstallText => Install.Describe();

    [ObservableProperty] public partial string UpdateText { get; set; } = "";

    /// <summary>Raised when the app should quit (after an update), so it shuts down the normal way.</summary>
    public event Action? ExitRequested;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    public partial bool CanUpdate { get; set; }

    [RelayCommand]
    async Task CheckForUpdatesNowAsync()
    {
        if (Install.Kind == InstallKind.Development)
        {
            UpdateText = "Development builds don't update.";
            return;
        }
        UpdateText = "Checking…";
        if (!await CheckForUpdatesAsync()) UpdateText = "Couldn't reach GitHub.";
        else if (!CanUpdate && (_release is null || !(_release.Version > Install.Version))) UpdateText = $"Up to date ({VersionText}).";
    }

    /// <summary>Asks GitHub for the newest release. False when it couldn't be reached.</summary>
    public async Task<bool> CheckForUpdatesAsync()
    {
        if (Install.Kind == InstallKind.Development) return true;
        try
        {
            _release = await new Updater(Http).GetLatestAsync();
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            return false;
        }
        if (_release is null || !(_release.Version > Install.Version)) return true;
        UpdateText = Install.Kind switch
        {
            InstallKind.Portable => $"Version {_release.Version} is out.",
            InstallKind.PackageManaged => $"Version {_release.Version} is out: update it with your package manager (yay -Syu, paru).",
            _ => $"Version {_release.Version} is out: download it from GitHub.",
        };
        CanUpdate = Install.CanSelfUpdate && _release.DownloadUrl is not null;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    async Task UpdateAsync()
    {
        if (_release is null || Install.ExecutablePath is not { } exe) return;
        CanUpdate = false;
        var version = _release.Version;
        UpdateText = $"Downloading {version}…";
        // Progress reports arrive later on the UI thread; once the download is over they must not cover the outcome.
        var downloading = true;
        try
        {
            var progress = new Progress<double>(f => { if (downloading) UpdateText = $"Downloading {version}… {f:P0}"; });
            await new Updater(Http).InstallAsync(_release, exe, progress);
            downloading = false;
            UpdateText = $"Restarting into {version}…";
            // Shut down normally; Program.Main starts the new copy once this one is gone.
            Program.RestartWith = exe;
            ExitRequested?.Invoke();
        }
        catch (Exception e)
        {
            downloading = false;
            UpdateText = $"Update failed: {e.Message}";
            CanUpdate = true;
        }
    }

    public MainViewModel()
    {
        // Before any window binds to them: the profile box only picks its selection out of a list that is already full.
        InitProfiles();
        // Plugging a pad in or out: update the list, and connect again if the open pad went away.
        DeviceList.Local.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!IsBusy) _ = RefreshPadsAsync();
        });
    }

    [ObservableProperty] public partial string DeviceStatus { get; set; } = "Looking for the macropad…";
    [ObservableProperty] public partial string Message { get; set; } = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ReadCommand), nameof(WriteCommand), nameof(ApplyLedCommand))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ReadCommand), nameof(WriteCommand), nameof(ApplyLedCommand), nameof(ConnectCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial IReadOnlyList<LayerViewModel> Layers { get; set; } = [];
    [ObservableProperty] public partial LayerViewModel? SelectedLayer { get; set; }
    [ObservableProperty] public partial SlotViewModel? SelectedSlot { get; set; }

    [ObservableProperty] public partial LedMode LedMode { get; set; } = LedMode.Backlight;
    [ObservableProperty] public partial LedColor LedColor { get; set; } = LedColor.Blue;

    bool CanUseDevice => IsConnected && !IsBusy;

    partial void OnSelectedLayerChanged(LayerViewModel? value) => SelectedSlot = value?.Buttons.FirstOrDefault();

    partial void OnSelectedSlotChanged(SlotViewModel? oldValue, SlotViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    [RelayCommand]
    void SelectSlot(SlotViewModel slot) => SelectedSlot = slot;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    public async Task ConnectAsync()
    {
        _device?.Dispose();
        _device = null;
        await RefreshPadsAsync(reopen: true);
    }

    /// <summary>Lists the pads plugged in now, and opens the chosen one if it isn't open (or <paramref name="reopen"/>).</summary>
    async Task RefreshPadsAsync(bool reopen = false)
    {
        var connected = await Task.Run(Pads.Connected);
        var known = Settings.Pads.Count;
        var choices = connected.Select(c => new PadChoice(c, Settings.PadFor(c.Key))).OrderBy(c => c.Record.Number).ToList();
        if (Settings.Pads.Count != known) SaveSettings();

        var keep = SelectedPad?.Pad.Key;
        _choosingPad = true;
        PadChoices.Clear();
        foreach (var c in choices) PadChoices.Add(c);
        var choice = choices.FirstOrDefault(c => c.Pad.Key == keep) ?? choices.FirstOrDefault();
        SelectedPad = choice;
        _choosingPad = false;
        HasSeveralPads = choices.Count > 1;

        if (choice is null)
        {
            _device?.Dispose();
            _device = null;
            IsConnected = false;
            DeviceStatus = OperatingSystem.IsLinux()
                ? "No macropad found. Plugged in? On Linux, install the udev rule from the README."
                : "No macropad found. Is it plugged in?";
            return;
        }
        if (reopen || _device is null || _openKey != choice.Pad.Key) await OpenPadAsync(choice);
    }

    async Task OpenPadAsync(PadChoice choice)
    {
        await Run("Connecting…", () =>
        {
            _device?.Dispose();
            _device = MacroPadDevice.Open(choice.Pad.Device);
        });
        _openKey = _device is null ? null : choice.Pad.Key;
        if (_device is null)
        {
            IsConnected = false;
            DeviceStatus = $"Couldn't open {choice.Label}.";
            return;
        }
        // The info reply carries the number of buttons and knobs (3 and 1 on the 3-key knob pad).
        int buttons = _device.Info[2] is > 0 and <= 15 ? _device.Info[2] : 3;
        int knobs = _device.Info[3] is <= 3 ? _device.Info[3] : 1;
        var who = HasSeveralPads ? $"{choice.Record.Label} ({_device.Name})" : _device.Name;
        DeviceStatus = $"Connected: {who} — {buttons} keys, {knobs} knob{(knobs == 1 ? "" : "s")}";
        IsConnected = true;
        BuildLayers(choice.Record.Number, buttons, knobs);
        await ReadAsync();
    }

    // The pads plugged in now, for the header's list; the one being edited.
    public System.Collections.ObjectModel.ObservableCollection<PadChoice> PadChoices { get; } = [];
    [ObservableProperty] public partial PadChoice? SelectedPad { get; set; }
    [ObservableProperty] public partial bool HasSeveralPads { get; set; }
    bool _choosingPad;
    string? _openKey;

    partial void OnSelectedPadChanged(PadChoice? oldValue, PadChoice? newValue)
    {
        if (_choosingPad || newValue is null || newValue.Pad.Key == _openKey) return;
        if (Layers.SelectMany(l => l.All).Any(s => s.IsDirty))
        {
            Message = "Write or undo the changes on this pad before switching to another.";
            Dispatcher.UIThread.Post(() => { _choosingPad = true; SelectedPad = oldValue; _choosingPad = false; });
            return;
        }
        _ = OpenPadAsync(newValue);
    }

    bool CanConnect() => !IsBusy;

    void BuildLayers(int pad, int buttons, int knobs)
    {
        string[] actions = ["⟲ Turn left", "● Press", "⟳ Turn right"];
        var number = SelectedLayer?.Number ?? 1;
        Layers = Enumerable.Range(1, Protocol.LayerCount).Select(layer => new LayerViewModel(
            layer,
            Enumerable.Range(0, buttons).Select(b => new SlotViewModel(pad, layer, Protocol.ButtonSlot(b), $"Key {b + 1}")).ToList(),
            Enumerable.Range(0, knobs).SelectMany(k => Enumerable.Range(0, 3).Select(a =>
                new SlotViewModel(pad, layer, Protocol.KnobSlot(k, a), knobs == 1 ? actions[a] : $"Knob {k + 1} {actions[a]}"))).ToList()
        )).ToList();
        SelectedLayer = Layers[number - 1];
    }

    [RelayCommand(CanExecute = nameof(CanUseDevice))]
    async Task ReadAsync()
    {
        var device = _device!;
        var read = new Dictionary<int, Dictionary<int, Binding>>();
        if (!await Run("Reading from the pad…", () =>
            {
                for (int layer = 1; layer <= Protocol.LayerCount; layer++)
                    read[layer] = device.ReadLayer(layer);
            }))
            return;
        foreach (var layer in Layers)
            foreach (var slot in layer.All)
                LoadSlot(slot, read[layer.Number].GetValueOrDefault(slot.Slot, new EmptyBinding()));
        Message = "Read all layers from the pad.";
    }

    [RelayCommand(CanExecute = nameof(CanUseDevice))]
    async Task WriteAsync()
    {
        var device = _device!;
        var changes = Layers.Select(l => (l.Number, Slots: l.All.Where(s => s.IsDirty).ToList()))
            .Where(c => c.Slots.Count > 0).ToList();
        if (changes.Count == 0)
        {
            Message = "Nothing changed.";
            return;
        }
        if (!await Run("Writing to the pad…", () =>
            {
                foreach (var (layer, slots) in changes)
                    device.WriteBindings(layer, slots.Select(s => KeyValuePair.Create(s.Slot, s.Binding)).ToList());
            }))
            return;
        // The pad only knows a Home Assistant slot by its trigger code; what the code does is kept in the settings.
        foreach (var (_, slots) in changes)
            foreach (var s in slots)
            {
                SaveAction(s);
                s.MarkSaved();
            }
        SaveSettings();
        Message = $"Saved {changes.Sum(c => c.Slots.Count)} change(s) to the pad.";
    }

    [RelayCommand(CanExecute = nameof(CanUseDevice))]
    async Task ApplyLedAsync()
    {
        var (device, layer, mode, color) = (_device!, SelectedLayer?.Number ?? 1, LedMode, LedColor);
        if (await Run("Setting lights…", () => device.WriteLed(layer, mode, color)))
            Message = $"Lights on layer {layer}: {mode}{(color == LedColor.None ? "" : $", {color}")}.";
    }

    /// <summary>Runs device I/O off the UI thread; reports failures instead of throwing.</summary>
    async Task<bool> Run(string busyText, Action action)
    {
        IsBusy = true;
        Message = busyText;
        try
        {
            await Task.Run(() => { lock (_io) action(); });
            return true;
        }
        catch (Exception e)
        {
            Message = $"Failed: {e.Message}";
            if (e is System.IO.IOException or TimeoutException)
            {
                _device?.Dispose();
                _device = null;
                IsConnected = false;
                DeviceStatus = "Lost the macropad. Plug it back in or press Connect.";
            }
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        _device?.Dispose();
    }
}
