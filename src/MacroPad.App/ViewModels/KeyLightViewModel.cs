using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MacroPad.Core;

namespace MacroPad.App.ViewModels;

/// <summary>One key's light colour on one layer (pads with a colour per key).</summary>
public partial class KeyLightViewModel(int slot) : ViewModelBase
{
    public int Slot { get; } = slot;
    public string Label => $"Key {Slot}";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Brush), nameof(Hex))]
    public partial Color Color { get; set; } = Colors.Black;

    [ObservableProperty] public partial bool IsSelected { get; set; }

    public IBrush Brush => new SolidColorBrush(Color);
    public string Hex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";

    public Rgb Rgb => new(Color.R, Color.G, Color.B);
    public static Color ToColor(Rgb rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);
}

/// <summary>A light mode with what it looks like on the pad.</summary>
public sealed record KeyLightModeChoice(KeyLightMode Mode, string Name, string Description);
