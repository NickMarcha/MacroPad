using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroPad.Core;
using MacroPad.Core.HomeAssistant;

namespace MacroPad.App.ViewModels;

public enum BindingKind { None, Keyboard, Media, Mouse, HomeAssistant }

public sealed record Choice<T>(T Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One key or knob action on the current layer, and the editor state for it.</summary>
public partial class SlotViewModel : ViewModelBase
{
    public static IReadOnlyList<BindingKind> Kinds { get; } =
        [BindingKind.None, BindingKind.Keyboard, BindingKind.Media, BindingKind.Mouse, BindingKind.HomeAssistant];

    public static IReadOnlyList<HaCommand> HaCommands { get; } = Enum.GetValues<HaCommand>();

    /// <summary>Areas and lights from Home Assistant, filled in by the main view model when it connects.</summary>
    public static ObservableCollection<HaTarget> HaTargets { get; } = [];

    public static IReadOnlyList<Choice<ushort>> MediaChoices { get; } =
        Usages.Media.Select(m => new Choice<ushort>(m.Key, m.Value)).ToList();

    public static IReadOnlyList<Choice<byte>> KeyChoices { get; } =
        Usages.Keys.Select(k => new Choice<byte>(k.Key, k.Value)).ToList();

    bool _loading;

    public SlotViewModel(int layer, int slot, string label)
    {
        Layer = layer;
        Slot = slot;
        Label = label;
        Chords.CollectionChanged += (_, _) => Changed();
    }

    public int Layer { get; }
    public int Slot { get; }

    /// <summary>The key code the pad sends for this slot when it is a Home Assistant action.</summary>
    public ushort TriggerCode => Triggers.Code(Layer, Slot);
    public string Label { get; }

    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial string Summary { get; set; } = "—";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsKeyboard), nameof(IsMedia), nameof(IsMouse), nameof(IsHomeAssistant))]
    public partial BindingKind Kind { get; set; }

    public bool IsKeyboard => Kind == BindingKind.Keyboard;
    public bool IsMedia => Kind == BindingKind.Media;
    public bool IsMouse => Kind == BindingKind.Mouse;
    public bool IsHomeAssistant => Kind == BindingKind.HomeAssistant;

    // Home Assistant
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsStepped))] public partial HaCommand HaCommand { get; set; }
    [ObservableProperty] public partial HaTarget? HaTarget { get; set; }
    [ObservableProperty] public partial decimal HaStep { get; set; } = 10;
    public bool IsStepped => HaCommand is HaCommand.Brighter or HaCommand.Dimmer;

    public HaAction? HaAction => Kind == BindingKind.HomeAssistant && HaTarget is not null
        ? new HaAction(HaCommand, HaTarget, (int)HaStep)
        : null;

    // Keyboard
    public ObservableCollection<KeyChord> Chords { get; } = [];
    [ObservableProperty] public partial decimal DelayMs { get; set; }
    [ObservableProperty] public partial Choice<byte>? PickedKey { get; set; }
    [ObservableProperty] public partial bool PickCtrl { get; set; }
    [ObservableProperty] public partial bool PickShift { get; set; }
    [ObservableProperty] public partial bool PickAlt { get; set; }
    [ObservableProperty] public partial bool PickWin { get; set; }

    // Media
    [ObservableProperty] public partial Choice<ushort>? Media { get; set; }

    // Mouse
    [ObservableProperty] public partial bool MouseLeft { get; set; }
    [ObservableProperty] public partial bool MouseRight { get; set; }
    [ObservableProperty] public partial bool MouseMiddle { get; set; }
    [ObservableProperty] public partial bool MouseCtrl { get; set; }
    [ObservableProperty] public partial bool MouseShift { get; set; }
    [ObservableProperty] public partial bool MouseAlt { get; set; }
    [ObservableProperty] public partial bool MouseWin { get; set; }
    [ObservableProperty] public partial decimal Wheel { get; set; }
    [ObservableProperty] public partial decimal MoveX { get; set; }
    [ObservableProperty] public partial decimal MoveY { get; set; }

    Binding? _unknown;

    public Binding Binding => Kind switch
    {
        BindingKind.Keyboard when Chords.Count > 0 => new KeyboardBinding(Chords.ToList(), (ushort)DelayMs),
        BindingKind.Media when Media is not null => new MediaBinding(Media.Value),
        BindingKind.Mouse => new MouseBinding(
            Mods(MouseCtrl, MouseShift, MouseAlt, MouseWin),
            (MouseLeft ? MouseButtons.Left : 0) | (MouseRight ? MouseButtons.Right : 0) | (MouseMiddle ? MouseButtons.Middle : 0),
            (sbyte)MoveX, (sbyte)MoveY, (sbyte)Wheel),
        BindingKind.HomeAssistant => new MediaBinding(TriggerCode),
        BindingKind.None when _unknown is not null => _unknown,
        _ => new EmptyBinding(),
    };

    /// <summary>Shows a binding read from the device; clears the dirty flag.</summary>
    public void Load(Binding binding, HaAction? action = null)
    {
        _loading = true;
        _unknown = binding as UnknownBinding;
        Chords.Clear();
        DelayMs = 0;
        Media = null;
        MouseLeft = MouseRight = MouseMiddle = MouseCtrl = MouseShift = MouseAlt = MouseWin = false;
        Wheel = MoveX = MoveY = 0;
        switch (binding)
        {
            case KeyboardBinding k:
                Kind = BindingKind.Keyboard;
                foreach (var c in k.Chords) Chords.Add(c);
                DelayMs = k.DelayMs;
                break;
            case MediaBinding m when Triggers.IsTrigger(m.Usage):
                Kind = BindingKind.HomeAssistant;
                HaTarget = null;
                if (action is not null)
                {
                    if (!HaTargets.Contains(action.Target)) HaTargets.Add(action.Target);
                    HaCommand = action.Command;
                    HaTarget = action.Target;
                    HaStep = action.StepPercent;
                }
                break;
            case MediaBinding m:
                Kind = BindingKind.Media;
                Media = MediaChoices.FirstOrDefault(c => c.Value == m.Usage) ?? new Choice<ushort>(m.Usage, Usages.MediaName(m.Usage));
                break;
            case MouseBinding m:
                Kind = BindingKind.Mouse;
                MouseLeft = m.Buttons.HasFlag(MouseButtons.Left);
                MouseRight = m.Buttons.HasFlag(MouseButtons.Right);
                MouseMiddle = m.Buttons.HasFlag(MouseButtons.Middle);
                MouseCtrl = m.Modifiers.HasFlag(Modifiers.LeftCtrl);
                MouseShift = m.Modifiers.HasFlag(Modifiers.LeftShift);
                MouseAlt = m.Modifiers.HasFlag(Modifiers.LeftAlt);
                MouseWin = m.Modifiers.HasFlag(Modifiers.LeftGui);
                Wheel = m.Wheel;
                MoveX = m.DeltaX;
                MoveY = m.DeltaY;
                break;
            default:
                Kind = BindingKind.None;
                break;
        }
        _loading = false;
        Summary = Describe();
        IsDirty = false;
    }

    public void MarkSaved() => IsDirty = false;

    string Describe() => Kind == BindingKind.HomeAssistant
        ? HaAction?.Describe() ?? "Home Assistant (choose what it does)"
        : Usages.Format(Binding);

    /// <summary>Adds a chord captured from a real key press.</summary>
    public void AddChord(KeyChord chord)
    {
        if (Chords.Count >= Protocol.MaxChords) return;
        Kind = BindingKind.Keyboard;
        Chords.Add(chord);
    }

    [RelayCommand]
    void AddPickedKey()
    {
        if (PickedKey is null && !(PickCtrl || PickShift || PickAlt || PickWin)) return;
        AddChord(new KeyChord(Mods(PickCtrl, PickShift, PickAlt, PickWin), PickedKey?.Value ?? 0));
    }

    [RelayCommand]
    void RemoveChord(KeyChord chord) => Chords.Remove(chord);

    [RelayCommand]
    void ClearChords() => Chords.Clear();

    static Modifiers Mods(bool ctrl, bool shift, bool alt, bool win) =>
        (ctrl ? Modifiers.LeftCtrl : 0) | (shift ? Modifiers.LeftShift : 0) | (alt ? Modifiers.LeftAlt : 0) | (win ? Modifiers.LeftGui : 0);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsDirty) or nameof(IsSelected) or nameof(Summary) or nameof(IsKeyboard) or nameof(IsMedia) or nameof(IsMouse) or nameof(IsHomeAssistant) or nameof(IsStepped)
            or nameof(PickedKey) or nameof(PickCtrl) or nameof(PickShift) or nameof(PickAlt) or nameof(PickWin))
            return;
        if (e.PropertyName == nameof(Kind) && !_loading) _unknown = null;
        Changed();
    }

    void Changed()
    {
        if (_loading) return;
        Summary = Describe();
        IsDirty = true;
    }
}
