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

/// <summary>A pad in the sidebar: one plugged in by USB, one on Bluetooth, or a named one that is neither.</summary>
public sealed partial class PadChoice(ConnectedPad? pad, PadRecord record, bool onBluetooth) : ObservableObject
{
    public ConnectedPad? Pad { get; } = pad;
    public PadRecord Record { get; } = record;
    public bool IsConnected => Pad is not null;
    public bool OnBluetooth { get; } = onBluetooth;
    public string Label => Record.Label;
    public string Detail => (Pad, OnBluetooth) switch
    {
        (not null, true) => "USB and Bluetooth",
        (not null, false) => "USB",
        (null, true) => "Bluetooth",
        _ => "Not connected",
    };

    /// <summary>After a rename.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Label));
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

    // The same key or knob action on the new layer, so switching layers compares like with like.
    partial void OnSelectedLayerChanged(LayerViewModel? value)
    {
        SelectedSlot = value?.All.FirstOrDefault(s => s.Slot == SelectedSlot?.Slot) ?? value?.Buttons.FirstOrDefault();
        ShowLightsOf(value);
    }

    partial void OnSelectedSlotChanged(SlotViewModel? oldValue, SlotViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = !IsLightsMode;
    }

    /// <summary>A click on a key or knob: what it does, or in the Lights view, its colour (knobs have no lights).</summary>
    [RelayCommand]
    void SelectSlot(SlotViewModel slot)
    {
        if (!IsLightsMode) SelectedSlot = slot;
        else if (SelectedLayer?.KeyLights.FirstOrDefault(k => k.Slot == slot.Slot && slot.Slot <= Protocol.MaxButtons) is { } light) SelectedLight = light;
    }

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
        var (connected, addresses) = await Task.Run(() => (Pads.Connected(), PadIds.BluetoothAddresses()));
        var known = Settings.Pads.Count;
        var usb = connected.Select(c => (Pad: c, Record: Settings.PadFor(c.Key))).ToList();
        var onBluetooth = Pads.LinkBluetooth(Settings, addresses, out var linked).Values.ToHashSet();
        if (Settings.Pads.Count != known || linked) SaveSettings();
        var choices = usb.Select(u => new PadChoice(u.Pad, u.Record, onBluetooth.Contains(u.Record))).ToList();
        // Pads on Bluetooth only, and named or Bluetooth pads that are off or unplugged, so they don't vanish from the list.
        var plugged = usb.Select(u => u.Record).ToHashSet();
        choices.AddRange(Settings.Pads.Where(r => !plugged.Contains(r) && (onBluetooth.Contains(r) || r.Name is not null || r.Bluetooth is not null))
            .Select(r => new PadChoice(null, r, onBluetooth.Contains(r))));
        choices = choices.OrderBy(c => c.Record.Number).ToList();

        var keep = SelectedPad?.Record.Key;
        _choosingPad = true;
        PadChoices.Clear();
        foreach (var c in choices) PadChoices.Add(c);
        var choice = choices.FirstOrDefault(c => c.Record.Key == keep && c.IsConnected) ?? choices.FirstOrDefault(c => c.IsConnected)
                     ?? choices.FirstOrDefault(c => c.Record.Key == keep) ?? choices.FirstOrDefault();
        SelectedPad = choice;
        _choosingPad = false;

        if (choice?.Pad is null)
        {
            ShowNotConnected(choice);
            return;
        }
        if (reopen || _device is null || _openKey != choice.Pad.Key) await OpenPadAsync(choice);
    }

    void ShowNotConnected(PadChoice? choice)
    {
        _device?.Dispose();
        _device = null;
        _openKey = null;
        IsConnected = false;
        Layers = [];
        SelectedSlot = null;
        DeviceStatus = choice is { OnBluetooth: true }
            ? "Connected by Bluetooth, so its keys work. The pad only lets its keys be read and changed over USB: plug it in to edit them."
            : choice is { Record.Bluetooth: not null }
            ? "Not connected. Switch it on to use it over Bluetooth, or plug it in by USB to see and change its keys."
            : choice is not null
            ? "Not connected. Plug it in by USB to see and change its keys."
            : OperatingSystem.IsLinux()
                ? "No macropad found. Plugged in? On Linux, install the udev rule from the README."
                : "No macropad found. Is it plugged in by USB?";
    }

    async Task OpenPadAsync(PadChoice choice)
    {
        var pad = choice.Pad!;
        await Run("Connecting…", () =>
        {
            _device?.Dispose();
            _device = MacroPadDevice.Open(pad.Device);
        });
        _openKey = _device is null ? null : pad.Key;
        if (_device is null)
        {
            IsConnected = false;
            DeviceStatus = $"Couldn't open {choice.Label}.";
            return;
        }
        var (buttons, knobs) = (_device.Buttons, _device.Knobs);
        DeviceStatus = $"{_device.Name} · USB · {buttons} keys, {knobs} knob{(knobs == 1 ? "" : "s")}";
        IsConnected = true;
        HasLayerLights = _device.Model.Lights == LightKind.PerLayer;
        HasKeyLights = _device.Model.Lights == LightKind.PerKey;
        if (!HasKeyLights) IsLightsMode = false;
        _layout = PadLayout.For(buttons, knobs);
        BuildLayers(choice.Record.Number, _device.Model, buttons, knobs);
        await ReadAsync();
    }

    // The pads in the sidebar; the one shown.
    public System.Collections.ObjectModel.ObservableCollection<PadChoice> PadChoices { get; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PadName), nameof(PadNamePlaceholder))]
    public partial PadChoice? SelectedPad { get; set; }
    bool _choosingPad;
    string? _openKey;
    PadLayout _layout = PadLayout.For(0, 0);

    /// <summary>Settings is shown instead of a pad.</summary>
    [ObservableProperty] public partial bool IsSettingsOpen { get; set; }

    [RelayCommand]
    void OpenSettings()
    {
        IsSettingsOpen = true;
        _choosingPad = true;
        SelectedPad = null;
        _choosingPad = false;
    }

    partial void OnSelectedPadChanged(PadChoice? oldValue, PadChoice? newValue)
    {
        if (_choosingPad || newValue is null) return;
        IsSettingsOpen = false;
        var openNow = PadChoices.FirstOrDefault(c => c.Pad?.Key == _openKey);
        if (newValue.Pad is not null && newValue.Pad.Key == _openKey) return;
        if (Layers.SelectMany(l => l.All).Any(s => s.IsDirty))
        {
            Message = "Write or undo the changes on this pad before switching to another.";
            Dispatcher.UIThread.Post(() => { _choosingPad = true; SelectedPad = openNow ?? oldValue; _choosingPad = false; });
            return;
        }
        if (newValue.Pad is null) ShowNotConnected(newValue);
        else _ = OpenPadAsync(newValue);
    }

    /// <summary>The shown pad's name; empty goes back to "Pad N".</summary>
    public string PadName
    {
        get => SelectedPad?.Record.Name ?? "";
        set
        {
            if (SelectedPad is not { } pad) return;
            pad.Record.Name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            SaveSettings();
            pad.Refresh();
            OnPropertyChanged();
        }
    }

    public string PadNamePlaceholder => SelectedPad is { } p ? $"Pad {p.Record.Number + 1}" : "";

    /// <summary>
    /// Turns the drawn pad a quarter turn anticlockwise, and remembers it for this pad. Anticlockwise because one turn
    /// takes the vendor's drawing (knobs on the right) to the pad as it usually sits (knobs at the top).
    /// </summary>
    [RelayCommand]
    void Rotate()
    {
        if (SelectedPad is not { } pad) return;
        pad.Record.Rotation = (pad.Record.Rotation + 3) % 4;
        SaveSettings();
        foreach (var layer in Layers) layer.Arrange(_layout.Rotated(pad.Record.Rotation));
    }

    bool CanConnect() => !IsBusy;

    void BuildLayers(int pad, PadModel model, int buttons, int knobs)
    {
        string[] actions = ["turn left", "press", "turn right"];
        var number = SelectedLayer?.Number ?? 1;
        var layout = _layout.Rotated(SelectedPad?.Record.Rotation ?? 0);
        Layers = Enumerable.Range(1, Protocol.LayerCount).Select(layer =>
        {
            var l = new LayerViewModel(
                layer,
                Enumerable.Range(0, buttons).Select(b => new SlotViewModel(pad, layer, Protocol.ButtonSlot(b), $"Key {b + 1}", model.HasDelay)).ToList(),
                Enumerable.Range(0, knobs).SelectMany(k => Enumerable.Range(0, 3).Select(a =>
                    new SlotViewModel(pad, layer, Protocol.KnobSlot(k, a), $"Knob {k + 1}, {actions[a]}", model.HasDelay))).ToList());
            l.Arrange(layout);
            return l;
        }).ToList();
        SelectedLayer = Layers[number - 1];
    }

    [RelayCommand(CanExecute = nameof(CanUseDevice))]
    async Task ReadAsync()
    {
        var device = _device!;
        var read = new Dictionary<int, Dictionary<int, Binding>>();
        var lights = new Dictionary<int, KeyLights>();
        if (!await Run("Reading from the pad…", () =>
            {
                for (int layer = 1; layer <= Protocol.LayerCount; layer++)
                {
                    read[layer] = device.ReadLayer(layer);
                    if (device.Model.Lights == LightKind.PerKey) lights[layer] = device.ReadKeyLights(layer);
                }
            }))
            return;
        foreach (var layer in Layers)
            foreach (var slot in layer.All)
                LoadSlot(slot, read[layer.Number].GetValueOrDefault(slot.Slot, new EmptyBinding()));
        LoadLights(lights);
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
