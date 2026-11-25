using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MacroPad.Core;

namespace MacroPad.App.ViewModels;

/// <summary>
/// Editing the pad as a whole: undo and redo, copy, cut, paste and clear, dragging one key onto another, and moving
/// keys and layers between layers. In the Lights view the same commands work on the keys' colours.
/// </summary>
public partial class MainViewModel
{
    /// <summary>One undoable change: what it was called, and how to go back and forth.</summary>
    sealed class HistoryEntry(string name, object? mergeKey)
    {
        public string Name { get; } = name;
        public object? MergeKey { get; } = mergeKey;
        public DateTime At { get; set; } = DateTime.UtcNow;
        public List<(Action Undo, Action Redo)> Steps { get; } = [];
    }

    readonly List<HistoryEntry> _undo = [], _redo = [];
    readonly List<SlotViewModel> _editedSlots = [];
    readonly List<LayerViewModel> _editedLights = [];
    bool _flushPosted, _restoring;

    // Edits of one key or one layer's lights within this long are one step (a drag across the colour picker).
    static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(1);

    SlotContent? _slotClip;
    Color? _colorClip;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoTip => CanUndo ? $"Undo {_undo[^1].Name} (Ctrl+Z)" : "Nothing to undo";
    public string RedoTip => CanRedo ? $"Redo {_redo[^1].Name} (Ctrl+Y)" : "Nothing to redo";

    void Track(IEnumerable<LayerViewModel> layers)
    {
        foreach (var slot in layers.SelectMany(l => l.All)) slot.Edited += OnSlotEdited;
        ResetHistory();
    }

    /// <summary>After reading the pad or switching pad or profile: what is shown is the starting point.</summary>
    void ResetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _editedSlots.Clear();
        _editedLights.Clear();
        foreach (var layer in Layers)
        {
            foreach (var slot in layer.All) slot.Known = slot.Content;
            layer.KnownLights = layer.Lights;
        }
        HistoryChanged();
    }

    void OnSlotEdited(SlotViewModel slot)
    {
        if (_restoring) return;
        if (!_editedSlots.Contains(slot)) _editedSlots.Add(slot);
        PostFlush();
    }

    void LightsEdited(LayerViewModel layer)
    {
        if (_restoring) return;
        if (!_editedLights.Contains(layer)) _editedLights.Add(layer);
        PostFlush();
    }

    // One click can change several properties; they become one step once the UI has handled the click.
    void PostFlush()
    {
        if (_flushPosted) return;
        _flushPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _flushPosted = false;
            Flush();
        }, DispatcherPriority.Background);
    }

    /// <summary>Records the edits made since the last step as one step, called <paramref name="name"/> if given.</summary>
    void Flush(string? name = null)
    {
        var entry = new HistoryEntry(name ?? "", null);
        object? only = null;
        foreach (var slot in _editedSlots)
        {
            var (before, after) = (slot.Known, slot.Content);
            if (before.SameAs(after)) continue;
            slot.Known = after;
            entry.Steps.Add((() => Restore(slot, before), () => Restore(slot, after)));
            only = slot;
        }
        foreach (var layer in _editedLights)
        {
            var (before, after) = (layer.KnownLights, layer.Lights);
            if (before is null || before.Equals(after)) continue;
            layer.KnownLights = after;
            entry.Steps.Add((() => RestoreLights(layer, before), () => RestoreLights(layer, after)));
            only = layer;
        }
        _editedSlots.Clear();
        _editedLights.Clear();
        if (entry.Steps.Count == 0) return;

        if (name is null)
        {
            var key = entry.Steps.Count == 1 ? only : null;
            name = key switch
            {
                SlotViewModel s => $"the change to {s.Label}",
                LayerViewModel l => $"the lights change on {l.Title}",
                _ => "the change",
            };
            // Another small edit of the same thing straight after: keep the first step's way back, take the new way forward.
            if (key is not null && _undo.Count > 0 && _undo[^1] is { } last && last.MergeKey == key && DateTime.UtcNow - last.At < MergeWindow && _redo.Count == 0)
            {
                last.Steps[0] = (last.Steps[0].Undo, entry.Steps[0].Redo);
                last.At = DateTime.UtcNow;
                return;
            }
            entry = Copy(entry, name, key);
        }
        else entry = Copy(entry, name, null);
        _undo.Add(entry);
        _redo.Clear();
        HistoryChanged();

        static HistoryEntry Copy(HistoryEntry e, string name, object? key)
        {
            var c = new HistoryEntry(name, key);
            c.Steps.AddRange(e.Steps);
            return c;
        }
    }

    void Restore(SlotViewModel slot, SlotContent content)
    {
        _restoring = true;
        slot.Show(content);
        slot.Known = slot.Content;
        _restoring = false;
    }

    void RestoreLights(LayerViewModel layer, KeyLights lights)
    {
        _restoring = true;
        SetLights(layer, lights);
        layer.KnownLights = layer.Lights;
        QueueLightWrite(layer);
        _restoring = false;
    }

    void SetLights(LayerViewModel layer, KeyLights lights)
    {
        layer.LightMode = lights.Mode;
        for (int i = 0; i < layer.KeyLights.Count && i < lights.Colors.Count; i++)
            layer.KeyLights[i].Color = KeyLightViewModel.ToColor(lights.Colors[i]);
        if (layer == SelectedLayer)
        {
            OnPropertyChanged(nameof(SelectedLightMode));
            OnPropertyChanged(nameof(LightColor));
        }
    }

    void HistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoTip));
        OnPropertyChanged(nameof(RedoTip));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    void Undo()
    {
        Flush();
        if (_undo.Count == 0) return;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        for (int i = entry.Steps.Count - 1; i >= 0; i--) entry.Steps[i].Undo();
        _redo.Add(entry);
        HistoryChanged();
        Message = $"Undid {entry.Name}.";
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    void Redo()
    {
        Flush();
        if (_redo.Count == 0) return;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        foreach (var step in entry.Steps) step.Redo();
        _undo.Add(entry);
        HistoryChanged();
        Message = $"Redid {entry.Name}.";
    }

    /// <summary>
    /// Makes the changes <paramref name="change"/> does into one undo step called <paramref name="name"/> ("pasting onto
    /// Key 5", for "Undid …"), and says <paramref name="done"/> in the status bar. False when it changed nothing.
    /// </summary>
    bool Edit(string name, string done, Action change)
    {
        Flush();
        var steps = _undo.Count;
        change();
        Flush(name);
        var changed = _undo.Count > steps;
        Message = changed ? $"{done} Ctrl+Z undoes it." : "Nothing to change: they're the same already.";
        return changed;
    }

    void Set(SlotViewModel slot, SlotContent content)
    {
        slot.Show(content with { Inherited = false });
        OnSlotEdited(slot);
    }

    void SetColor(KeyLightViewModel light, Color color)
    {
        if (LayerOf(light) is not { } layer || light.Color == color) return;
        light.Color = color;
        if (light == SelectedLight) OnPropertyChanged(nameof(LightColor));
        QueueLightWrite(layer);
    }

    LayerViewModel? LayerOf(KeyLightViewModel light) => Layers.FirstOrDefault(l => l.KeyLights.Contains(light));
    LayerViewModel LayerOf(SlotViewModel slot) => Layers[slot.Layer - 1];

    /// <summary>The key's light on its layer; null for knobs.</summary>
    KeyLightViewModel? LightOf(SlotViewModel slot) =>
        slot.Slot <= Protocol.MaxButtons ? LayerOf(slot).KeyLights.ElementAtOrDefault(slot.Slot - 1) : null;

    // Copy, cut, paste and clear work on the selected key: what it does, or in the Lights view its colour.

    [RelayCommand]
    void Copy()
    {
        if (IsLightsMode)
        {
            if (SelectedLight is not { } light) return;
            _colorClip = light.Color;
            Message = $"Copied the colour of {light.Label}.";
        }
        else
        {
            if (SelectedSlot is not { } slot) return;
            _slotClip = slot.Content;
            Message = $"Copied {slot.Label}.";
        }
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    void Cut()
    {
        Copy();
        if (IsLightsMode && SelectedLight is { } light) Edit($"cutting {light.Label}'s colour", $"Cut {light.Label}'s colour.", () => SetColor(light, Colors.Black));
        else if (!IsLightsMode && SelectedSlot is { } slot) Edit($"cutting {slot.Label}", $"Cut {slot.Label}.", () => Set(slot, SlotContent.Empty));
    }

    bool CanPaste() => IsLightsMode ? _colorClip is not null : _slotClip is not null;

    [RelayCommand(CanExecute = nameof(CanPaste))]
    void Paste()
    {
        if (IsLightsMode)
        {
            if (SelectedLight is not { } light || _colorClip is not { } color) return;
            Edit($"pasting onto {light.Label}", $"Pasted onto {light.Label}.", () => SetColor(light, color));
        }
        else
        {
            if (SelectedSlot is not { } slot || _slotClip is not { } clip) return;
            Edit($"pasting onto {slot.Label}", $"Pasted onto {slot.Label}.", () => Set(slot, clip));
        }
    }

    /// <summary>Makes the selected key do nothing, or in the Lights view turns its light off (black).</summary>
    [RelayCommand]
    void Clear()
    {
        if (IsLightsMode && SelectedLight is { } light) Edit($"clearing {light.Label}'s colour", $"Turned off {light.Label}'s light.", () => SetColor(light, Colors.Black));
        else if (!IsLightsMode && SelectedSlot is { } slot) Edit($"clearing {slot.Label}", $"Cleared {slot.Label}.", () => Set(slot, SlotContent.Empty));
    }

    /// <summary>
    /// A key dropped on another: swaps the two, or with <paramref name="copy"/> copies the first onto the second. In the
    /// Lights view it is their colours.
    /// </summary>
    public void Drop(SlotViewModel from, SlotViewModel to, bool copy)
    {
        if (from == to) return;
        var where = from.Layer == to.Layer ? to.Label : $"{to.Label} on Layer {to.Layer}";
        if (IsLightsMode)
        {
            if (LightOf(from) is not { } a || LightOf(to) is not { } b) return;
            var (ca, cb) = (a.Color, b.Color);
            Edit(copy ? $"copying {from.Label}'s colour to {where}" : $"swapping colours of {from.Label} and {where}",
                copy ? $"Copied {from.Label}'s colour to {where}." : $"Swapped the colours of {from.Label} and {where}.", () =>
            {
                SetColor(b, ca);
                if (!copy) SetColor(a, cb);
            });
        }
        else
        {
            var (ca, cb) = (from.Content, to.Content);
            Edit(copy ? $"copying {from.Label} to {where}" : $"swapping {from.Label} and {where}",
                copy ? $"Copied {from.Label} to {where}." : $"Swapped {from.Label} and {where}.", () =>
            {
                Set(to, ca);
                if (!copy) Set(from, cb);
            });
        }
    }

    /// <summary>The key in the same place on another layer: swaps with it, or with <paramref name="copy"/> copies onto it.</summary>
    public void DropOnLayer(SlotViewModel from, int layer, bool copy)
    {
        if (layer == from.Layer || Layers.ElementAtOrDefault(layer - 1)?.All.FirstOrDefault(s => s.Slot == from.Slot) is not { } to) return;
        Drop(from, to, copy);
    }

    /// <summary>Copies everything on the shown layer (or in the Lights view, its lights) onto <paramref name="layer"/>.</summary>
    public void CopyLayerTo(int layer) => LayerOperation(layer, swap: false);

    /// <summary>Swaps the shown layer with <paramref name="layer"/>.</summary>
    public void SwapLayerWith(int layer) => LayerOperation(layer, swap: true);

    void LayerOperation(int number, bool swap)
    {
        if (SelectedLayer is not { } from || Layers.ElementAtOrDefault(number - 1) is not { } to || from == to) return;
        var what = IsLightsMode ? "lights" : "keys";
        Edit(swap ? $"swapping the {what} of {from.Title} and {to.Title}" : $"copying {from.Title}'s {what} to {to.Title}",
            swap ? $"Swapped the {what} of {from.Title} and {to.Title}." : $"Copied {from.Title}'s {what} to {to.Title}.", () =>
        {
            if (IsLightsMode)
            {
                var (a, b) = (from.Lights, to.Lights);
                SetLights(to, a);
                QueueLightWrite(to);
                if (!swap) return;
                SetLights(from, b);
                QueueLightWrite(from);
            }
            else
                foreach (var a in from.All)
                {
                    if (to.All.FirstOrDefault(s => s.Slot == a.Slot) is not { } b) continue;
                    var (ca, cb) = (a.Content, b.Content);
                    Set(b, ca);
                    if (swap) Set(a, cb);
                }
        });
    }

    /// <summary>Clears every key on the shown layer, or in the Lights view turns all its lights off.</summary>
    public void ClearLayer()
    {
        if (SelectedLayer is not { } layer) return;
        Edit(IsLightsMode ? $"clearing {layer.Title}'s lights" : $"clearing {layer.Title}",
            IsLightsMode ? $"Turned off every light on {layer.Title}." : $"Cleared every key on {layer.Title}.", () =>
        {
            if (IsLightsMode)
            {
                SetLights(layer, new KeyLights(KeyLightMode.Off, Enumerable.Repeat(new Rgb(0, 0, 0), KeyLights.Count).ToList()));
                QueueLightWrite(layer);
            }
            else
                foreach (var slot in layer.All) Set(slot, SlotContent.Empty);
        });
    }
}
