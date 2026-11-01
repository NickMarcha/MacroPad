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

## Not checked yet

- The meaning of the info bytes past the first four.
- Whether the knob's "turn left" is slot 16 or 18 on every pad (factory defaults are 16 = `3`,
  17 = `2`, 18 = `1`).
