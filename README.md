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
  - a mouse click, wheel or move, with modifiers held.
- Sets the lights per layer (mode and color).

Tested with USB ID `514C:8851`. The app also looks for the other IDs the vendor app supports
(`1189`/`514C` with PIDs `8830`–`8833`, `8840`, `8842`, `8850`), but those have not been tested.
`docs/protocol.md` describes how the protocol was worked out.

## Install

- **Arch / CachyOS:** `yay -S macropad-bin` (or `paru`). It installs the udev rule too, so the pad works without
  root. Updates come through pacman like any other package.
- **Windows:** download `MacroPad-win-x64.exe` from the
  [latest release](https://github.com/NickMarcha/MacroPad/releases/latest) and run it from any folder.
- **Other Linux:** download `MacroPad-linux-x64`, `chmod +x` it, and install the udev rule (below).

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

## Layout

- `src/MacroPad.Core`: protocol encoding (`Protocol.cs`), binding types, HID access through
  HidSharp (`MacroPadDevice.cs`), key and media names, and install detection and self-update (`Updates/`).
- `src/MacroPad.App`: the Avalonia UI.
- `tests/MacroPad.Core.Tests`: protocol tests built from real reports the pad sent, and update tests.
- `packaging/`: the Linux desktop entry, icon and udev rule, and the AUR package.

## Known limitations

- The pad cannot report its current light setting, so the lights panel shows what you are about to
  set, not what is on the pad now.
- The Windows build is not code-signed, so SmartScreen warns the first time it runs.
- Modifiers you record are always the left-hand ones.
- Keys the OS takes before the app sees them (Win, Alt+Tab, Print Screen on some desktops) need the
  "add a key by name" picker instead of recording.

## License

[PolyForm Noncommercial 1.0.0](LICENSE.md).
