# MacroPad

A cross-platform configurator (Windows and Linux) for the cheap "MINI_KEYBOARD" macropads sold on
AliExpress, for example the 3-key-plus-knob "Macropad Macro Programming Keyboard RGB". It replaces
the vendor's Windows-only `MINI_KEYBOARD.exe`. Built with Avalonia and .NET 10.

Bindings are stored on the pad itself. After you write them, the pad works on any computer without
this app running.

## What it does

- Finds the pad and reads all three layers from it.
- Each key and knob action (turn left, press, turn right) can be set to:
  - a keyboard shortcut or a macro of up to 18 steps, with an optional delay between steps. Record
    the steps by pressing keys, or pick keys by name (F13–F24, or keys the OS grabs);
  - a media key (volume, play/pause, next track, …);
  - a mouse click, wheel or move, with modifiers held;
  - a **MacroPad action**: toggle, turn on or off, or turn up and down a Home Assistant area or light, or a
    [Bragi](#bragi-audio) audio control (mute, volume); *select* a target for the knob to control; or switch
    MacroPad's profile.
- Sets the lights per layer (mode and color).
- **Profiles**: MacroPad's own layers of actions. Switch them from the tray menu, a pad key, a keyboard shortcut or
  `macropad --profile <name|next>`.
- A **pop-up** for each Home Assistant change and profile switch, with a brightness slider you can drag, in any of
  nine places on the screen.
- Runs in the tray (optionally from login) so pad presses reach Home Assistant without the window open. Closing
  the window keeps it running there (an option).

Tested with USB ID `514C:8851`. The app also looks for the other IDs the vendor app supports
(`1189`/`514C` with PIDs `8830`–`8833`, `8840`, `8842`, `8850`), but those have not been tested.
`docs/protocol.md` describes how the protocol was worked out.

## Install

- **Arch / CachyOS:** `yay -S macropad-bin` (or `paru`). It installs the udev rule too, so the pad works without
  root. Updates come through pacman like any other package.
- **Windows:** download `MacroPad-win-x64.exe` from the
  [latest release](https://github.com/NickMarcha/MacroPad/releases/latest), put it in a folder of its own and run
  it. Under Settings, tick *Show MacroPad in the Start menu* to find it by search, and *Start at login* to keep it
  in the tray.
- **Other Linux:** download `MacroPad-linux-x64`, `chmod +x` it, and install the udev rule (below). Settings →
  *Show MacroPad in the applications menu* adds a menu entry.

## The window

- **Pad**: the pad's layers, the MacroPad profile, each key and knob action, and the lights.
- **Settings**: Home Assistant, running in the background, the next-profile shortcut, pop-ups, the Start menu
  shortcut, and the version with *Check for updates*.

## Updates

The app checks GitHub for a newer release when it starts. The bottom bar always says how this copy is installed:

- **Portable copy** (the `.exe`, or the Linux binary in a folder you can write): an *Update now* button downloads
  the new release, checks its SHA-256, swaps itself and restarts.
- **Installed by pacman** (the AUR package writes `/opt/macropad/package-managed`): it only tells you a new version
  is out. Update it with `yay -Syu`.
- **Read-only folder**: it tells you a new version is out and leaves the file alone.

Local builds report version 0.0.0 and never update.

## Linux without the AUR package

Normal users cannot open the pad's raw HID interface until a udev rule allows it:

```sh
sudo cp packaging/linux/70-macropad.rules /etc/udev/rules.d/
sudo udevadm control --reload && sudo udevadm trigger
```

Then replug the pad. The rule tags the device `uaccess`, which gives the logged-in desktop user access on systemd
distros.

## Build and run from source

```sh
dotnet run --project src/MacroPad.App
dotnet test
dotnet publish src/MacroPad.App -c Release -r linux-x64 -o out   # or -r win-x64; one self-contained file
```

## Releasing

Push a tag: `git tag v0.2.0 && git push origin v0.2.0`. `.github/workflows/release.yml` tests, builds Windows
and Linux, publishes the GitHub release and updates the AUR package (`packaging/aur/README.md` covers the one-time
AUR setup).

## Home Assistant

1. In Home Assistant, open your profile → Security → *Long-lived access tokens* and create one.
2. In MacroPad, under **Home Assistant**, paste the address (a dashboard link works too) and the token, and press
   Connect. Your areas and lights appear in the editor.
3. Set a key or knob action to *Home Assistant*, pick what it does and on what, and press *Write to pad*.

How it works: the pad can only send key codes, so a Home Assistant slot is written as a consumer-control code from
an unused range (`0x0F00`–`0x0F7F`) that Windows and Linux ignore. MacroPad reads the pad's reports in the
background (without taking them from the OS, and on Wayland too) and calls Home Assistant's REST API. The action
for each code lives in MacroPad's settings, so Home Assistant actions only work while MacroPad runs: leave it in the
tray, and tick *Start at login*.

*Toggle* on an area works like a wall switch: if any light in it is on, all go off; otherwise all go on. *Brighter*
and *Dimmer* step the brightness in Home Assistant's units (1 = 1/255 of full, the finest a light takes; 5 by
default). Knob turns accelerate like a mouse pointer: a single click moves the set step, and quick clicks in the
same direction move up to 8 times as much. Dimming to zero turns the light off.

While you turn, MacroPad keeps the brightness itself and sends exact values without a fade. (Relative steps went
wrong when clicks came fast: Home Assistant works each out from the light's last report, which lags, so the light
jumped.) It reads the light again after three seconds without a click. On an area, turning sets all its lights to
the same brightness.

A room picker setup, for example: keys 1–3 *Select* Office, Living Room and Bedroom; the knob's press is *Toggle*
and its turns *Down* and *Up*, all on the target *Selected target*. The keys then only choose which room the knob
works on (MacroPad remembers the choice across restarts).

Settings are in `%APPDATA%\MacroPad\settings.json` (Windows) or `~/.config/macropad/settings.json` (Linux). The
Home Assistant token and the Bragi key are encrypted for your Windows user; on Linux the file is readable only by
you.

## Bragi (audio)

[Bragi](https://bragi.example.ts.net/api/v1/help) is a PipeWire audio mixer with a REST API. Under Settings →
**Bragi (audio)**, enter its address and an API key (Bragi's dashboard: the lock button, Security, *API
connections*) and press Connect. Every control becomes a target: a headset's speakers and mic, and each network
peer's outgoing and incoming audio.

- *Toggle* mutes and unmutes; *Turn on* unmutes; *Turn off* mutes.
- *Up* and *Down* turn the volume, 0–150%, with the same acceleration as for lights. Turning doesn't unmute.
- *Select* works for audio too, so one knob can control a room or a headset, whichever was picked last.
- The pop-up shows the volume (and *Muted*), with a slider up to 150%.

A control that isn't streaming (Bragi's 409) says so in the pop-up instead of changing.

## Profiles

The pad's own layers can't be switched from the computer (the vendor app has no command for it either), so
MacroPad has profiles instead. A profile says what each action key does; anything a profile doesn't set comes
from **Default**, so a key set to *Next profile* in Default works in every profile. Keyboard, media and mouse
bindings are stored on the pad and are the same in every profile.

The profile picked in the window is the active one and the one you edit. To switch otherwise:

- the tray icon's right-click menu (the active profile is ticked);
- a pad key set to the MacroPad action *Next profile* or *Profile*;
- a keyboard shortcut: on Windows, set one under *Next profile shortcut*; anywhere, have a shortcut run
  `macropad --profile next` or `macropad --profile Kitchen` (on Linux, in your desktop's keyboard settings);
- the command line: a second `macropad` hands its arguments to the running one, so only one ever runs.

Switching shows a pop-up with the profile's name. Per-application profiles would build on this: a rule that picks
the profile for the app in front.

## Layout

- `src/MacroPad.Core`: protocol encoding (`Protocol.cs`), binding types, HID access through
  HidSharp (`MacroPadDevice.cs`), the background listener (`PadListener.cs`), settings (`AppSettings.cs`),
  targets and the service interface (`Target.cs`), the knob's dial (`LevelDial.cs`), the Home Assistant client
  (`HomeAssistant/`), the Bragi client (`Bragi/`), key and media names, and install detection and self-update (`Updates/`).
- `src/MacroPad.App`: the Avalonia UI.
- `tests/MacroPad.Core.Tests`: protocol tests built from real reports the pad sent, and update tests.
- `packaging/`: the Linux desktop entry, icon and udev rule, and the AUR package.

## Known limitations

- The pad cannot report its current light setting, so the lights panel shows what you are about to
  set, not what is on the pad now.
- Tested on Windows. The Linux build compiles and packages but has not been run against the pad on Linux yet.
- The Windows build is not code-signed, so SmartScreen warns the first time it runs.
- Modifiers you record are always the left-hand ones.
- Keys the OS takes before the app sees them (Win, Alt+Tab, Print Screen on some desktops) need the
  "add a key by name" picker instead of recording.

## License

[PolyForm Noncommercial 1.0.0](LICENSE.md).
