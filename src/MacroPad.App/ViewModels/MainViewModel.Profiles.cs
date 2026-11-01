using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroPad.Core;

namespace MacroPad.App.ViewModels;

/// <summary>
/// MacroPad's profiles: its own layers of actions on top of the pad. One is active (what presses do now); one is being
/// edited in the window. Keyboard, media and mouse bindings live on the pad and are the same in every profile.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Raised when profiles are added, renamed, removed or switched, for the tray menu.</summary>
    public event Action? ProfilesChanged;

    [ObservableProperty] public partial string ActiveProfileName { get; set; } = "Default";
    [ObservableProperty] public partial string? EditedProfileName { get; set; }
    [ObservableProperty] public partial string ProfileNameDraft { get; set; } = "";

    Profile EditedProfile => Settings.Find(EditedProfileName) ?? Settings.Default;
    public bool IsEditingDefault => EditedProfile == Settings.Default;

    void InitProfiles()
    {
        SlotViewModel.ProfileNames.Clear();
        foreach (var p in Settings.Profiles) SlotViewModel.ProfileNames.Add(p.Name);
        ActiveProfileName = Settings.Active.Name;
        EditedProfileName = Settings.Active.Name;
    }

    /// <summary>Makes a profile active, from the tray, a pad key, a shortcut or the command line.</summary>
    public void SwitchProfile(string? name)
    {
        var profile = name == "next" ? Settings.NextProfile() : Settings.Find(name);
        if (profile is null)
        {
            Message = $"There is no profile called {name}.";
            return;
        }
        Settings.ActiveProfile = profile.Name;
        SaveSettings();
        ActiveProfileName = profile.Name;
        Message = $"Profile: {profile.Name}";
        if (Settings.ShowPopups) Toast.ShowMessage(profile.Name, "Profile");
        ProfilesChanged?.Invoke();
    }

    [RelayCommand]
    void NextProfile() => SwitchProfile("next");

    /// <summary>Raised when the next-profile shortcut changes, so the app registers the new one.</summary>
    public event Action? ShortcutChanged;

    /// <summary>Raised when a second start (or the tray) asks for the window.</summary>
    public event Action? ShowWindowRequested;

    public bool IsWindows => OperatingSystem.IsWindows();

    [ObservableProperty] public partial string ShortcutStatus { get; set; } = "";

    /// <summary>System-wide shortcut for the next profile (Windows), e.g. "Ctrl+Alt+P".</summary>
    public string? NextProfileShortcut
    {
        get => Settings.NextProfileShortcut;
        set
        {
            Settings.NextProfileShortcut = string.IsNullOrWhiteSpace(value) ? null : value;
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShortcutText));
            ShortcutChanged?.Invoke();
        }
    }

    public string ShortcutText => NextProfileShortcut ?? "Click here, then press a shortcut";

    [RelayCommand]
    void ClearShortcut() => NextProfileShortcut = null;

    /// <summary>Arguments from a later start: <c>--profile &lt;name|next&gt;</c>, else show the window.</summary>
    public void HandleCommandLine(string[] args)
    {
        var i = Array.IndexOf(args, "--profile");
        if (i >= 0 && i + 1 < args.Length)
        {
            SwitchProfile(args[i + 1]);
            return;
        }
        if (Array.IndexOf(args, Autostart.BackgroundArgument) < 0) ShowWindowRequested?.Invoke();
    }

    // The window edits the active profile: picking one in the list switches to it.
    partial void OnActiveProfileNameChanged(string value)
    {
        if (value is null) return;
        if (value != EditedProfileName && Layers.SelectMany(l => l.All).Any(s => s.IsDirty))
        {
            Message = "Write or undo the changes on this profile before switching.";
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ActiveProfileName = EditedProfileName ?? Settings.Default.Name);
            return;
        }
        if (Settings.ActiveProfile != value) SwitchProfile(value);
        EditedProfileName = value;
    }

    partial void OnEditedProfileNameChanged(string? value)
    {
        if (value is null) return;
        ProfileNameDraft = value;
        OnPropertyChanged(nameof(IsEditingDefault));
        // Show the edited profile's actions on the slots that are actions.
        foreach (var slot in Layers.SelectMany(l => l.All))
            if (!slot.IsDirty) LoadSlot(slot, slot.Binding);
    }

    void LoadSlot(SlotViewModel slot, Binding binding)
    {
        var profile = EditedProfile;
        var own = profile.Actions.ContainsKey(Triggers.Key(slot.TriggerCode));
        slot.Load(binding, Settings.ActionFor(profile, slot.TriggerCode), inherited: !own && profile != Settings.Default);
    }

    /// <summary>After a write: keeps what an action slot does in the edited profile; forgets codes the pad no longer sends.</summary>
    void SaveAction(SlotViewModel slot)
    {
        if (slot.Kind != BindingKind.Action)
        {
            foreach (var p in Settings.Profiles) Settings.SetAction(p, slot.TriggerCode, null);
            return;
        }
        if (slot.IsInherited) return;
        Settings.SetAction(EditedProfile, slot.TriggerCode, slot.PadAction);
    }

    [RelayCommand]
    void UseDefaultAction()
    {
        if (SelectedSlot is not { } slot || IsEditingDefault) return;
        Settings.SetAction(EditedProfile, slot.TriggerCode, null);
        SaveSettings();
        LoadSlot(slot, slot.Binding);
        Message = $"{slot.Label} now does the same as in Default.";
    }

    /// <summary>The profile being edited, or all of them, as a profile file's text.</summary>
    public string ExportProfiles(bool all) => ProfileFile.Write(all ? Settings.Profiles : [EditedProfile]);

    public string ExportFileName(bool all) => (all ? "MacroPad profiles" : EditedProfile.Name) + ProfileFile.Extension;

    /// <summary>
    /// Adds the profiles in a profile file and switches to the first. Keys the profile uses that aren't MacroPad
    /// actions on this pad yet become actions, marked as changes, so Write to pad makes them send their codes.
    /// </summary>
    public void ImportProfiles(string json)
    {
        if (Layers.SelectMany(l => l.All).Any(s => s.IsDirty))
        {
            Message = "Write or undo the changes on this profile before importing.";
            return;
        }
        System.Collections.Generic.List<Profile> added;
        try
        {
            added = ProfileFile.AddTo(Settings, ProfileFile.Read(json));
        }
        catch (FormatException e)
        {
            Message = e.Message;
            return;
        }
        SaveSettings();
        foreach (var p in added) SlotViewModel.ProfileNames.Add(p.Name);
        ProfilesChanged?.Invoke();
        ActiveProfileName = added[0].Name;

        var marked = 0;
        foreach (var slot in Layers.SelectMany(l => l.All))
        {
            if (slot.Kind == BindingKind.Action || Settings.ActionFor(added[0], slot.TriggerCode) is not { } action) continue;
            slot.UseAction(action);
            marked++;
        }
        var names = string.Join(", ", added.Select(p => p.Name));
        Message = marked == 0
            ? $"Imported {names}."
            : $"Imported {names}. {marked} key(s) on this pad became actions: press Write to pad so they send their codes.";
    }

    [RelayCommand]
    void AddProfile()
    {
        var n = Settings.Profiles.Count + 1;
        string name;
        do name = $"Profile {n++}"; while (Settings.Find(name) is not null);
        Settings.Profiles.Add(new Profile { Name = name });
        SaveSettings();
        SlotViewModel.ProfileNames.Add(name);
        EditedProfileName = name;
        ProfilesChanged?.Invoke();
    }

    [RelayCommand]
    void RenameProfile()
    {
        var name = ProfileNameDraft.Trim();
        var profile = EditedProfile;
        if (name.Length == 0 || name == profile.Name) return;
        if (name.Equals("next", StringComparison.OrdinalIgnoreCase) || Settings.Find(name) is not null)
        {
            Message = $"Pick another name than {name}.";
            return;
        }
        var old = profile.Name;
        profile.Name = name;
        // Profile actions that pointed at the old name follow it.
        foreach (var p in Settings.Profiles)
            foreach (var (key, action) in p.Actions.ToList())
                if (action.ProfileName == old) p.Actions[key] = action with { ProfileName = name };
        if (Settings.ActiveProfile == old) Settings.ActiveProfile = name;
        SaveSettings();
        SlotViewModel.ProfileNames[SlotViewModel.ProfileNames.IndexOf(old)] = name;
        if (ActiveProfileName == old) ActiveProfileName = name;
        EditedProfileName = name;
        ProfilesChanged?.Invoke();
    }

    [RelayCommand]
    void DeleteProfile()
    {
        var profile = EditedProfile;
        if (profile == Settings.Default) return;
        Settings.Profiles.Remove(profile);
        if (Settings.ActiveProfile == profile.Name) Settings.ActiveProfile = Settings.Default.Name;
        SaveSettings();
        SlotViewModel.ProfileNames.Remove(profile.Name);
        ActiveProfileName = Settings.Active.Name;
        EditedProfileName = Settings.Default.Name;
        ProfilesChanged?.Invoke();
    }
}
