using System;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MacroPad.App.ViewModels;

namespace MacroPad.App.Views;

/// <summary>The pad page's editing by mouse and keyboard: dragging keys, their right-click menu, the layer menu and shortcuts.</summary>
public partial class PadPage : UserControl
{
    const double DragDistance = 6;

    SlotViewModel? _pressed, _overSlot;
    LayerViewModel? _overLayer;
    Point _start;
    bool _dragging;

    public PadPage()
    {
        InitializeComponent();
        // Tunnelling, and handled ones too: the keycaps are buttons, which take the pointer for themselves.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>The key or knob action a keycap on the drawn pad stands for.</summary>
    SlotViewModel? SlotAt(object? source) =>
        source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is { CommandParameter: SlotViewModel slot } button
        && Pad.IsVisualAncestorOf(button) ? slot : null;

    static LayerViewModel? LayerAt(object? source) =>
        source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: LayerViewModel layer } ? layer : null;

    void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = SlotAt(e.Source);
        _start = e.GetPosition(this);
        _dragging = false;
    }

    void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null) return;
        var at = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(at.X - _start.X) < DragDistance && Math.Abs(at.Y - _start.Y) < DragDistance) return;
            _dragging = true;
        }
        var hit = this.InputHitTest(at);
        var slot = SlotAt(hit);
        var layer = slot is null ? LayerAt(hit) : null;
        if (layer?.Number == _pressed.Layer) layer = null;
        // In the Lights view only keys have something to drop: knobs have no lights.
        if (Vm?.IsLightsMode == true && slot is not null && slot.Slot > MacroPad.Core.Protocol.MaxButtons) slot = null;
        Highlight(slot == _pressed ? null : slot, layer);
        Cursor = new Cursor(e.KeyModifiers.HasFlag(KeyModifiers.Control) ? StandardCursorType.DragCopy : StandardCursorType.DragMove);
    }

    void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressed is { } from && _dragging && Vm is { } vm)
        {
            var copy = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (_overSlot is { } to) vm.Drop(from, to, copy);
            else if (_overLayer is { } layer) vm.DropOnLayer(from, layer.Number, copy);
            e.Handled = true;
        }
        EndDrag();
    }

    void Highlight(SlotViewModel? slot, LayerViewModel? layer)
    {
        if (_overSlot != slot)
        {
            if (_overSlot is not null) _overSlot.IsDropTarget = false;
            if (slot is not null) slot.IsDropTarget = true;
            _overSlot = slot;
        }
        if (_overLayer != layer)
        {
            if (_overLayer is not null) _overLayer.IsDropTarget = false;
            if (layer is not null) layer.IsDropTarget = true;
            _overLayer = layer;
        }
    }

    void EndDrag()
    {
        Highlight(null, null);
        _pressed = null;
        _dragging = false;
        Cursor = null;
    }

    void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (SlotAt(e.Source) is not { } slot || Vm is not { } vm || e.Source is not Control target) return;
        if (vm.IsLightsMode && slot.Slot > MacroPad.Core.Protocol.MaxButtons) return;
        vm.SelectSlotCommand.Execute(slot);
        var others = vm.Layers.Where(l => l.Number != slot.Layer).ToList();
        var menu = new ContextMenu
        {
            ItemsSource = new Control[]
            {
                Item("Copy", "Ctrl+C", vm.CopyCommand),
                Item("Cut", "Ctrl+X", vm.CutCommand),
                Item("Paste", "Ctrl+V", vm.PasteCommand),
                Item("Clear", "Delete", vm.ClearCommand),
                new Separator(),
                Submenu("Copy to", others.Select(l => Action(l.Title, () => vm.DropOnLayer(slot, l.Number, copy: true)))),
                Submenu("Swap with", others.Select(l => Action($"{slot.Label} on {l.Title}", () => vm.DropOnLayer(slot, l.Number, copy: false)))),
            },
        };
        menu.Open(target);
        e.Handled = true;
    }

    void LayerMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || vm.SelectedLayer is not { } shown || sender is not Control button) return;
        var others = vm.Layers.Where(l => l != shown).ToList();
        var what = vm.IsLightsMode ? "lights" : "keys";
        var flyout = new MenuFlyout
        {
            ItemsSource = new Control[]
            {
                Submenu($"Copy {shown.Title}'s {what} to", others.Select(l => Action(l.Title, () => vm.CopyLayerTo(l.Number)))),
                Submenu($"Swap {shown.Title}'s {what} with", others.Select(l => Action(l.Title, () => vm.SwapLayerWith(l.Number)))),
                new Separator(),
                Action(vm.IsLightsMode ? $"Turn off every light on {shown.Title}" : $"Clear every key on {shown.Title}", vm.ClearLayer),
            },
        };
        flyout.ShowAt(button);
    }

    static MenuItem Item(string header, string gesture, ICommand command) =>
        new() { Header = header, InputGesture = KeyGesture.Parse(gesture), Command = command };

    static MenuItem Action(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    static MenuItem Submenu(string header, System.Collections.Generic.IEnumerable<MenuItem> items) =>
        new() { Header = header, ItemsSource = items.ToList() };

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Vm is not { } vm || !vm.IsConnected) return;
        var source = e.Source as Visual;
        // Text boxes keep their own undo, copy and delete; so do drop-downs and number boxes for the keys they use.
        if (source?.FindAncestorOfType<TextBox>(includeSelf: true) is not null) return;
        var inField = source?.FindAncestorOfType<ComboBox>(includeSelf: true) is not null
                      || source?.FindAncestorOfType<NumericUpDown>(includeSelf: true) is not null;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        ICommand? command = (ctrl, shift, e.Key) switch
        {
            (true, false, Key.Z) => vm.UndoCommand,
            (true, true, Key.Z) or (true, false, Key.Y) => vm.RedoCommand,
            _ when inField => null,
            (true, false, Key.C) => vm.CopyCommand,
            (true, false, Key.X) => vm.CutCommand,
            (true, false, Key.V) => vm.PasteCommand,
            (false, false, Key.Delete or Key.Back) => vm.ClearCommand,
            _ => null,
        };
        if (command is null) return;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }
}
