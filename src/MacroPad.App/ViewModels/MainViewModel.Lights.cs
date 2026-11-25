using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroPad.Core;

namespace MacroPad.App.ViewModels;

/// <summary>Lights on pads with a colour per key: shown on the drawn pad, and written as you change them.</summary>
public partial class MainViewModel
{
    public static IReadOnlyList<KeyLightModeChoice> KeyLightModes { get; } =
    [
        new(KeyLightMode.Off, "Off", "No lights."),
        new(KeyLightMode.On, "On", "Every key lit in its own colour."),
        new(KeyLightMode.Press, "When pressed", "A key lights in its colour while you press it."),
        new(KeyLightMode.Spread, "Spread", "A press lights the key, then spreads along its row and column."),
        new(KeyLightMode.Rainbow, "Rainbow", "An animated rainbow across the pad. Ignores the key colours."),
        new(KeyLightMode.Wave, "Wave", "A slow colour wave. Ignores the key colours."),
    ];

    /// <summary>The factory colours, offered as swatches.</summary>
    public static IReadOnlyList<Color> Swatches { get; } =
        KeyLights.Factory.Colors.Select(KeyLightViewModel.ToColor).Distinct().Append(Colors.White).ToList();

    /// <summary>The open pad sets a colour per key (and the window can show a Lights view).</summary>
    [ObservableProperty] public partial bool HasKeyLights { get; set; }

    /// <summary>The open pad takes one mode and colour per layer.</summary>
    [ObservableProperty] public partial bool HasLayerLights { get; set; }

    /// <summary>The pad page shows lights instead of what keys do.</summary>
    [ObservableProperty] public partial bool IsLightsMode { get; set; }

    [ObservableProperty] public partial KeyLightViewModel? SelectedLight { get; set; }

    public KeyLightModeChoice? SelectedLightMode
    {
        get => KeyLightModes.FirstOrDefault(m => m.Mode == SelectedLayer?.LightMode);
        set
        {
            if (value is null || SelectedLayer is not { } layer || layer.LightMode == value.Mode) return;
            layer.LightMode = value.Mode;
            OnPropertyChanged();
            QueueLightWrite(layer);
        }
    }

    /// <summary>The selected key's colour; changing it writes the layer's lights.</summary>
    public Color LightColor
    {
        get => SelectedLight?.Color ?? Colors.Black;
        set
        {
            if (SelectedLight is not { } light || SelectedLayer is not { } layer || light.Color == value) return;
            light.Color = value;
            OnPropertyChanged();
            QueueLightWrite(layer);
        }
    }

    // Only one view shows a selection: the key whose action you edit, or the key whose colour you edit.
    partial void OnSelectedLightChanged(KeyLightViewModel? oldValue, KeyLightViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = IsLightsMode;
        OnPropertyChanged(nameof(LightColor));
    }

    partial void OnIsLightsModeChanged(bool value)
    {
        if (SelectedLight is not null) SelectedLight.IsSelected = value;
        if (SelectedSlot is not null) SelectedSlot.IsSelected = !value;
    }

    void ShowLightsOf(LayerViewModel? layer)
    {
        SelectedLight = layer?.KeyLights.FirstOrDefault(k => k.Slot == SelectedLight?.Slot) ?? layer?.KeyLights.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedLightMode));
    }

    [RelayCommand]
    void UseSwatch(Color color) => LightColor = color;

    /// <summary>Gives every key on this layer the selected key's colour.</summary>
    [RelayCommand]
    void ColorAllKeys()
    {
        if (SelectedLayer is not { } layer || SelectedLight is not { } light) return;
        foreach (var k in layer.KeyLights) k.Color = light.Color;
        QueueLightWrite(layer);
    }

    void LoadLights(IReadOnlyDictionary<int, KeyLights> lights)
    {
        foreach (var layer in Layers)
        {
            if (!lights.TryGetValue(layer.Number, out var l)) continue;
            layer.LightMode = l.Mode;
            for (int i = 0; i < layer.KeyLights.Count; i++) layer.KeyLights[i].Color = KeyLightViewModel.ToColor(l.Colors[i]);
        }
        ShowLightsOf(SelectedLayer);
        OnPropertyChanged(nameof(LightColor));
    }

    // Dragging across the colour picker changes the colour many times a second; the pad gets the latest after a pause.
    CancellationTokenSource? _lightWrite;

    void QueueLightWrite(LayerViewModel layer)
    {
        _lightWrite?.Cancel();
        var cancel = _lightWrite = new CancellationTokenSource();
        var device = _device;
        _ = Task.Delay(200, cancel.Token).ContinueWith(async t =>
        {
            if (t.IsCanceled || device is null) return;
            var lights = await Dispatcher.UIThread.InvokeAsync(() => layer.Lights);
            await Run($"Setting the lights on layer {layer.Number}…", () => device.WriteKeyLights(layer.Number, lights));
            Message = $"Lights on layer {layer.Number}: {KeyLightModes.First(m => m.Mode == lights.Mode).Name}.";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
