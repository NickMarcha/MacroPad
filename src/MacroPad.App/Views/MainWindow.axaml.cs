using System;
using Avalonia.Controls;
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
}
