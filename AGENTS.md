# Working on MacroPad

Read this before changing anything. The README says what MacroPad does for users; this file says how it is built,
how to test it, and what was learned the hard way. `docs/protocol.md` covers the pad's USB protocol and how it was
reverse engineered.

## Principles

- No backward compatibility layers. When a format changes (settings, profile files), migrate the owner's real data
  once instead of keeping old code paths.
- The simplest implementation that fully works; established libraries over hand-rolled code.
- Grow in layers: every release works end to end.
- **This repository is public.** No tokens, keys, real hostnames (tailnet names), home or device names, or personal
  configuration in code, tests, docs, screenshots or commit messages. Use generic examples (Office, Living Room, Desk
  Headset, studio, `ha.example.ts.net`). The history was scrubbed once already; don't make that necessary again.
- Ask before writing to the pad anywhere but a slot nobody uses, and never send the commands listed under "Commands
  not to send" in `docs/protocol.md` (one puts the pad into firmware-update mode).

## Build, test, run

```sh
dotnet build
dotnet test                                   # tests/MacroPad.Core.Tests: protocol, services, profiles, dial, updates
dotnet run --project src/MacroPad.App         # a development build: version 0.0.0, never updates itself
dotnet publish src/MacroPad.App -c Release -r win-x64 -o out    # or linux-x64: one self-contained file
```

- .NET 10, Avalonia 12 (compiled bindings), CommunityToolkit.Mvvm (partial-property `[ObservableProperty]`), HidSharp.
- MacroPad is single-instance (a named pipe / Unix socket per user, `SingleInstance.cs`). A second start hands its
  command line to the running one and exits. **Stop the running copy before starting a test build**, or the test
  build will just hand over to it. The owner usually has one running in the tray (`MacroPad-win-x64.exe`).
- `MACROPAD_TRACE=1` logs knob clicks, dial values and the pop-up's X11 setup to `macropad-trace.log` in the temp
  folder (`Trace.cs`).
- Quick experiments against Core: a .NET file-based app (`dotnet run x.cs`) with
  `#:project <path>/src/MacroPad.Core/MacroPad.Core.csproj` at the top. Reflection-based JSON is off there (as it
  would be trimmed), which is why Core only uses source-generated JSON and `JsonValue`/`JsonNode`.

## Layout

- `src/MacroPad.Core` (no UI):
  - `Protocol.cs`, `Bindings.cs`, `Usages.cs`, `MacroPadDevice.cs`: the pad's protocol and HID access.
  - `Pads.cs`: telling pads apart (USB serial, else device path); `AppSettings.PadFor` numbers them.
  - `PadListener.cs`: reads every pad's consumer-control reports in the background (report ID 5: a 16-bit usage).
  - `PadAction.cs`: actions, profiles, and `Triggers` (the codes action keys send, a block of `0x60` per pad).
  - `Target.cs`: targets, levels and `ITargetService`; `HomeAssistant/` and `Bragi/` implement it.
  - `LevelDial.cs`, `KnobAcceleration.cs`: the knob (see below).
  - `AppSettings.cs` (settings.json, secrets protected with DPAPI on Windows), `ProfileFile.cs` (export/import).
  - `Updates/`: install detection (portable, package-managed, read-only, development) and self-update.
- `src/MacroPad.App`: Avalonia UI. `MainViewModel` is split by concern: `.cs` (pad, layers, read/write, updates),
  `.Services.cs` (connections, settings toggles), `.Actions.cs` (what a press does), `.Profiles.cs`, `.GitHub.cs`.
  Platform bits: `Autostart.cs`, `MenuShortcut.cs` (Start menu .lnk / desktop entry), `GlobalHotkey.cs` (Windows),
  `X11Notification.cs` (Linux pop-up), `SingleInstance.cs`, `Program.cs` (update hand-over).
- `tests/MacroPad.Core.Tests`: fakes for Home Assistant and Bragi are `HttpMessageHandler`s; no network.
- `tools/hid`: read-only Python probes for the pad. `tools/screenshots`: README screenshots against fake services.
  `tools/linux`: Docker checks (AUR package build, pop-up window type under Xvfb).
- `packaging/`: Linux desktop entry, icon, udev rule; `packaging/aur` the AUR package.

## How the pieces fit

- **Two halves.** Keyboard, media and mouse bindings are written to the pad and work without MacroPad. *MacroPad
  actions* are written to the pad as a consumer usage from `0x0F00–0x17FF` (no OS acts on these). MacroPad, in the
  tray, reads the press and looks the code up in the active profile, then the default profile (`Profiles[0]`).
- **Profiles** are MacroPad's own layers: the pad's layer can't be switched from the computer. Each profile keeps its
  own *selected target* for the knob. The window edits the active profile.
- **Services**: a target's kind picks the service (lights → Home Assistant, audio → Bragi). Adding a connection means
  implementing `ITargetService`, adding a `TargetKind`, a connection block in Settings (`MainWindow.axaml`,
  Expander under Connections) and a client in Core with tests against a fake handler.
- **The knob** (`LevelDial`): reads the level once when turning starts, keeps the running value, sends absolute
  values (lights with `transition: 0`), and while a request is out keeps only the newest value. Relative steps
  (`brightness_step`) made lights jump: Home Assistant computes each step from the light's last report, which lags
  behind fast clicks. Steps are 1/255 of full scale on any target, multiplied up to 8× by `KnobAcceleration`.
- **Pop-up** (`ToastWindow`): never takes focus, hides 2.5 s after the last update unless the pointer is over it.
  On X11 it is typed `_NET_WM_WINDOW_TYPE_NOTIFICATION` through Xlib before its first show; KWin ignored
  Avalonia's own skip-taskbar/topmost hints.
- **Updates**: a portable copy downloads the release asset beside itself, checks GitHub's SHA-256 digest, renames
  the running file to `.old`, moves the new one in, then **shuts down normally** and `Program.Main` starts the new
  one with `--after-update <pid>`, which waits for the old process before the single-instance check. (Starting the
  new copy first made it hand over to the old one.) The AUR package writes `/opt/macropad/package-managed`, so that
  copy never updates itself.

## Gotchas met so far

- An Avalonia ComboBox bound to `SelectedItem` only selects from items already in its list: fill lists (profile
  names) before the window is created.
- Quitting: closing the window during `Shutdown()` must not call `Quit()` again (that was a stack overflow).
- Settings written by a newer version can't always be read by an older one (an unknown enum value makes it treat
  the file as broken and keep a `.broken` copy). Keep the owner's installed copy current when changing formats.
- Windows HID: keyboard and mouse collections can't be opened; the consumer-control collection can, and reading it
  doesn't take presses from the OS. On Linux one hidraw node per interface carries all of interface 1's reports.
- `PrintWindow` for screenshots: `CopyFromScreen` grabs whatever is in front when the app can't take the
  foreground.

## Testing on real hardware

The owner has one `514C:8851` pad (3 keys, 1 knob), used on Windows 11 and CachyOS (KDE). Home Assistant and
Bragi run on their tailnet; their addresses and tokens live only in the owner's settings, never here.

- Reading the pad is always safe (`tools/hid/probe.py`, or Read from pad). A test write goes to a slot the owner
  doesn't use, is read back, and is restored.
- A UI check without clicking: run a published build, drive it with Windows UI Automation (tab `Settings`, button
  `Update now`, …) and capture with `PrintWindow` (`tools/screenshots/capture.ps1` shows how).
- Linux: `tools/linux/popup-check.sh` (Xvfb + xprop) and `tools/linux/aur-check.sh` (makepkg + namcap) in an
  `archlinux` container. Real KDE behaviour needs the owner's machine.

## Releasing

- Push a tag `vX.Y.Z`. `.github/workflows/release.yml` tests, builds Windows (on Windows, for the icon) and Linux,
  publishes the release (`MacroPad-win-x64.exe`, `MacroPad-linux-x64`, `MacroPad-X.Y.Z-linux-x64.tar.gz`,
  `SHA256SUMS.txt`) and pushes `macropad-bin` to the AUR with the secret `AUR_SSH_PRIVATE_KEY` (a key used only for
  this package; `packaging/aur/README.md` says how to rotate it).
- GitHub creates no tag events when more than three tags are pushed at once.
- README-only changes don't need a release.
- After a release the owner updates with Settings → About → Check for updates (Windows) or `yay -Syu` (CachyOS).
