using Avalonia.Controls;
using Avalonia.Input;
using MacroPad.App.ViewModels;
using MacroPad.Core;

namespace MacroPad.App.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    void Recorder_PointerPressed(object? sender, PointerPressedEventArgs e) => (sender as Control)?.Focus();

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
}
