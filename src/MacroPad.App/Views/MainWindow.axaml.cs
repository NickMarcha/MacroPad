using System;
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

    static readonly Avalonia.Platform.Storage.FilePickerFileType ProfileFiles = new("MacroPad profiles")
    {
        Patterns = ["*" + ProfileFile.Extension, "*.json"],
    };

    async void ExportProfile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await ExportAsync(all: false);

    async void ExportAllProfiles_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await ExportAsync(all: true);

    async System.Threading.Tasks.Task ExportAsync(bool all)
    {
        if (DataContext is not MainViewModel vm) return;
        var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = all ? "Export all profiles" : "Export profile",
            SuggestedFileName = vm.ExportFileName(all),
            FileTypeChoices = [ProfileFiles],
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(vm.ExportProfiles(all));
            vm.Message = $"Saved {file.Name}.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            vm.Message = $"Couldn't save {file.Name}: {ex.Message}";
        }
    }

    async void ImportProfiles_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Import profiles",
            AllowMultiple = false,
            FileTypeFilter = [ProfileFiles],
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new System.IO.StreamReader(stream);
            vm.ImportProfiles(await reader.ReadToEndAsync());
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            vm.Message = $"Couldn't read {files[0].Name}: {ex.Message}";
        }
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
