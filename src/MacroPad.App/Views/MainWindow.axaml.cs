using Avalonia.Controls;
using Avalonia.Input;
using MacroPad.App.ViewModels;
using MacroPad.Core;

namespace MacroPad.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    void Shortcut_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        e.Handled = true;
        if (KeyMap.IsModifier(e.PhysicalKey) || !KeyMap.ToHid(e.PhysicalKey, out byte code)) return;
        var m = e.KeyModifiers;
        var parts = new System.Collections.Generic.List<string>();
        if (m.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (m.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(Usages.KeyName(code));
        var shortcut = string.Join("+", parts);
        if (GlobalHotkey.IsValid(shortcut)) vm.NextProfileShortcut = shortcut;
        else vm.ShortcutStatus = $"{shortcut} can't be a shortcut: use Ctrl, Alt, Shift or Win with a letter, digit or F-key (F13–F24 work alone).";
    }

    void Recorder_PointerPressed(object? sender, PointerPressedEventArgs e) => (sender as Control)?.Focus();

    void Recorder_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: SlotViewModel slot }) return;
        e.Handled = true;
        // Modifier keys on their own only start a combination; the next real key completes it.
        if (KeyMap.IsModifier(e.PhysicalKey)) return;
        if (!KeyMap.ToHid(e.PhysicalKey, out byte code)) return;
        var m = e.KeyModifiers;
        var mods = (m.HasFlag(KeyModifiers.Control) ? Modifiers.LeftCtrl : 0)
                   | (m.HasFlag(KeyModifiers.Shift) ? Modifiers.LeftShift : 0)
                   | (m.HasFlag(KeyModifiers.Alt) ? Modifiers.LeftAlt : 0)
                   | (m.HasFlag(KeyModifiers.Meta) ? Modifiers.LeftGui : 0);
        slot.AddChord(new KeyChord(mods, code));
    }
}
