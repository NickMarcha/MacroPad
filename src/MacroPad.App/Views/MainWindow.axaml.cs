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
