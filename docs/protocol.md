# MINI_KEYBOARD macropad protocol

Notes on how the vendor app (`MINI_KEYBOARD.exe`, from the archive
`New_English_software_is_set_in_the_upgrade_model-20250318.7z`) talks to the pad. Everything below
was checked against a 3-key, 1-knob pad with USB ID `514C:8851`.

## How it was worked out

1. Strings in the exe show a Qt 5 app that uses `hidapi.dll`.
2. The exe was loaded into Ghidra 12.1.4 through [REA](https://github.com/morluto/rea)
   (`rea search`, `rea xrefs`, `rea decompile`). The callers of `hid_write`, `hid_read` and
   `hid_enumerate` show the packets and the ID table.
3. Read-only queries were sent to the pad to confirm the layout. Then one write was made to layer 3
   key 1, read back and restored.
4. The packet format turned out to match the "884x" family in
   [ch57x-keyboard-tool](https://github.com/kriomant/ch57x-keyboard-tool). That tool does not list
   this USB ID. The LED codes come from that project and agree with the table in the vendor exe.

## Device

- The vendor app looks for VID `0x1189` with PIDs `8842 8840 8830 8831 8832 8833 8850`, then for
  VID `0x514C` with those PIDs plus `8851`.
- The configuration interface is USB interface 0 (vendor usage page `0xFF00`). It takes 65-byte
  output reports with report ID 3 and answers with 64-byte input reports that start with `03`.
- Interface 1 is the normal keyboard/mouse/consumer HID the pad types with.

## Packets (byte 0 is always the report ID `03`)

| Purpose | Bytes |
|---|---|
| Info | `03 FB FB FB` → `03 FB <buttons> <knobs> …` (this pad: `03 FB 03 01`) |
| Read layer | `03 FA <count> 00 <layer 1-3>` → `<count>` reports in the write layout below, with `FA` in byte 1 |
| Write slot | `03 FE <slot> <layer> <type> <delay lo> <delay hi> 00 00 00 <n> <payload…>` |
| Write lights | `03 FE B0 <layer> 08 00 00 00 00 00 01 00 <color<<4 \| mode>` |
| Save | `03 FD FE FF` (the vendor app then waits 200 ms) |

Slots: buttons are `1…15`. Knob *k* uses `16+3k` (turn left), `17+3k` (press) and `18+3k`
(turn right). Reading 18 slots per layer covers 15 buttons and one knob.

Payloads by type:

- `1` keyboard: `<n>` = number of steps, then `<modifiers> <HID usage>` pairs. A lone modifier is
  sent with `n = 0`. Modifier bits: `01` LCtrl, `02` LShift, `04` LAlt, `08` LGui, `10`–`80` the
  right-hand versions.
- `2` media: `n = 2`, then the consumer usage, low byte first (`E9 00` = Volume Up).
- `3` mouse: `n = 1` (the pad reports `4` back), then `<modifiers> <buttons> <dx> <dy> <wheel>`.
  Buttons: `1` left, `2` right, `4` middle.

LED modes: `0` off, `1` backlight, `2` shock, `3` shock2, `4` light on press, `5` white
backlight. Colors `1`–`7`: red, orange, yellow, green, cyan, blue, purple.

The pad gives no answer to the vendor app's light-read command (`03 FA B0 <layer>`), so the current
light setting cannot be read back. Light writes are confirmed: layer 1 set to green backlight turned the pad green.

Key presses can be read in the background: the consumer-control collection (interface 1, report ID 5) can be
opened on Windows while the OS also acts on it. Volume Up arrived as `05 E9 00` and then `05 00 00` on release.

## Commands not to send

The vendor app has two more commands. MacroPad never sends them, and nothing should without knowing exactly what
it's doing:

| Bytes | What the vendor app uses it for |
|---|---|
| `03 EF EF` | `KB_UpData_SoftWare`: puts the pad into **firmware-update mode** (its bootloader). |
| `03 FC FC <n> <m> …` | `Set_Keyboard_Ver_SLOT`: switches the pad to another keyboard model/layout (15 variants). |

There is **no command to switch the pad's active layer**. The vendor app's `SendLayer(int)` only changes which
layer the app is editing (it reads that layer's lights). That's why MacroPad has its own profiles instead.

The pad's keys never send anything on the configuration interface; presses come on interface 1 (keyboard, mouse
and consumer-control reports). MacroPad's actions use consumer usages `0x0F00`–`0x17FF`, which no OS acts on:
each pad gets a block of `0x60` (three layers of `0x20` slots), so the code says which pad a press came from.

## Reproducing the analysis

The vendor exe isn't in this repository (it's the vendor's). With a copy of it:

- **Strings:** a Qt 5 app, the UI object names (`LED_Mode_*`, `delay_spinBox_*`, …), and the hidapi imports.
- **Decompiling:** [REA](https://github.com/morluto/rea) over Ghidra 12.1.x, from the command line, without
  installing REA into an agent:

  ```sh
  export GHIDRA_INSTALL_DIR=/path/to/ghidra_12.1.4_PUBLIC
  export JAVA_HOME=/path/to/jdk-21-or-newer     # REA needs JAVA_HOME; a java on PATH alone isn't enough on Windows
  rea() { npx -p rea-agents rea "$@"; }
  rea search  MINI_KEYBOARD.exe hid_ --kind procedures --snapshot mk.snap --provider ghidra --format json
  rea xrefs   MINI_KEYBOARD.exe 0x47af48 --snapshot mk.snap --provider ghidra --format json   # callers of hid_write
  rea decompile MINI_KEYBOARD.exe 0x40bd20 --snapshot mk.snap --provider ghidra --format json
  ```

  Each call re-imports into Ghidra (about a minute); `--snapshot` keeps the analysis between calls. The binary has
  no symbols: find functions through strings (`rea search … "Modify key value Flag"`, then `rea xrefs` on its
  address) or through the Qt meta-call switch (`moc_widget.cpp` from the vendor archive lists the slots in order).

Addresses in the 2025-03-18 build (SHA-256 `334ae835…0f717b6e`):

| Address | What |
|---|---|
| `0x47af48` | `hid_write` import |
| `0x4726e0` | finding the pad: VID/PID table at `0x4be19c`, opens interface 0 |
| `0x472240` | the info command (`03 FB`) and reading the reply |
| `0x40bd20` | writing keys and lights, then saving (`03 FE …`, `03 FD FE FF`) |
| `0x414b70` | reading the configuration (`03 FA …`) |
| `0x411170`, `0x410f40`, `0x411610`, `0x411c50`, `0x412110` | building keyboard, media, mouse and delay payloads |
| `0x411860` | building the light payload; the value table starts at file offset `0xbca20` |
| `0x479610` | the widget's Qt meta-call switch (case 5 `SendLayer`, case 16 firmware update) |
| `0x415280` | `SendLayer`: UI only |

For trying things on the pad, `tools/hid/probe.py` and `tools/hid/listen.py` are read-only. Test a write on a slot
nobody uses (the original check used layer 3 key 1), read it back, and restore it.

## Not checked yet

- The meaning of the info bytes past the first four.
- Whether the knob's "turn left" is slot 16 or 18 on every pad (factory defaults are 16 = `3`,
  17 = `2`, 18 = `1`).
- Pads other than `514C:8851`, and more than one pad at once.
