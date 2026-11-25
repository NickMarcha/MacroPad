using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MacroPad.Core;

namespace MacroPad.App.ViewModels;

/// <summary>One of the pad's three layers: its slots, and where they are drawn.</summary>
public partial class LayerViewModel(int number, IReadOnlyList<SlotViewModel> buttons, IReadOnlyList<SlotViewModel> knobs) : ViewModelBase
{
    /// <summary>Pixels per key on the drawn pad.</summary>
    public const double Unit = 76;

    public int Number { get; } = number;
    public string Title => $"Layer {Number}";
    public IReadOnlyList<SlotViewModel> Buttons { get; } = buttons;
    public IReadOnlyList<SlotViewModel> Knobs { get; } = knobs;
    public IEnumerable<SlotViewModel> All => Buttons.Concat(Knobs);

    /// <summary>The lights of each key slot 1–16, on pads with a colour per key.</summary>
    public IReadOnlyList<KeyLightViewModel> KeyLights { get; } =
        Enumerable.Range(1, MacroPad.Core.KeyLights.Count).Select(s => new KeyLightViewModel(s)).ToList();

    [ObservableProperty] public partial KeyLightMode LightMode { get; set; }

    /// <summary>What the undo history last saw of this layer's lights.</summary>
    internal MacroPad.Core.KeyLights? KnownLights { get; set; }

    /// <summary>A key is being dragged over this layer's tab.</summary>
    [ObservableProperty] public partial bool IsDropTarget { get; set; }

    public MacroPad.Core.KeyLights Lights => new(LightMode, KeyLights.Select(k => k.Rgb).ToList());

    [ObservableProperty] public partial IReadOnlyList<PlaceViewModel> Places { get; set; } = [];
    [ObservableProperty] public partial double Width { get; set; }
    [ObservableProperty] public partial double Height { get; set; }

    /// <summary>Places the keys and knobs as <paramref name="layout"/> says (already turned the way the pad is drawn).</summary>
    public void Arrange(PadLayout layout)
    {
        var bySlot = All.ToDictionary(s => s.Slot);
        var places = new List<PlaceViewModel>();
        foreach (var k in layout.Keys)
            if (bySlot.TryGetValue(k.Slot, out var slot))
                places.Add(new KeyPlaceViewModel(slot, KeyLights.ElementAtOrDefault(k.Slot - 1), k.X * Unit, k.Y * Unit));
        foreach (var n in layout.Knobs)
        {
            var actions = Enumerable.Range(0, 3).Select(a => bySlot.GetValueOrDefault(Protocol.KnobSlot(n.Index, a))).ToList();
            if (actions.Any(a => a is null)) continue;
            places.Add(new KnobPlaceViewModel(n.Index, actions[0]!, actions[1]!, actions[2]!, n.X * Unit, n.Y * Unit));
        }
        Places = places;
        Width = layout.Width * Unit;
        Height = layout.Height * Unit;
    }
}

/// <summary>Something drawn on the pad, at a position in pixels.</summary>
public abstract class PlaceViewModel(double left, double top, double size)
{
    public double Left { get; } = left;
    public double Top { get; } = top;
    public double Size { get; } = size;
}

public sealed class KeyPlaceViewModel(SlotViewModel slot, KeyLightViewModel? light, double left, double top) : PlaceViewModel(left, top, LayerViewModel.Unit)
{
    public SlotViewModel Slot { get; } = slot;

    /// <summary>The key's light on this layer; null on pads without a colour per key.</summary>
    public KeyLightViewModel? Light { get; } = light;
    public string Number => Slot.Slot.ToString();
}

/// <summary>A knob: turning it left, pressing it and turning it right are three slots.</summary>
public sealed class KnobPlaceViewModel(int index, SlotViewModel turnLeft, SlotViewModel press, SlotViewModel turnRight, double left, double top)
    : PlaceViewModel(left, top, PadLayout.KnobSize * LayerViewModel.Unit)
{
    public string Label { get; } = $"Knob {index + 1}";
    public SlotViewModel TurnLeft { get; } = turnLeft;
    public SlotViewModel Press { get; } = press;
    public SlotViewModel TurnRight { get; } = turnRight;
}
