"""Read-only look at a MINI_KEYBOARD pad: the info reply and every slot of every layer, as raw reports.

Usage: python probe.py [VID PID]   (default 514C 8851). Needs `pip install hidapi`.
Sends only the info (FB) and read (FA) commands; nothing is written. See docs/protocol.md.
"""
import sys
import hid

vid, pid = (int(sys.argv[1], 16), int(sys.argv[2], 16)) if len(sys.argv) > 2 else (0x514C, 0x8851)
devs = hid.enumerate(vid, pid)
for d in devs:
    print(d['interface_number'], hex(d['usage_page']), hex(d['usage']), d['path'])
config = [d for d in devs if d['interface_number'] == 0]
if not config:
    sys.exit("No configuration interface (interface 0) found. Plugged in? On Linux, is the udev rule installed?")
h = hid.device()
h.open_path(config[0]['path'])


def xfer(payload, reads=1, timeout=300):
    h.write(bytes(payload) + bytes(65 - len(payload)))
    return [bytes(h.read(64, timeout)) for _ in range(reads)]


print('info:', xfer([3, 0xFB, 0xFB, 0xFB])[0][:8].hex(' '))
for layer in (1, 2, 3):
    print(f'-- layer {layer}')
    for r in sorted(xfer([3, 0xFA, 18, 0, layer], reads=18), key=lambda r: r[2] if len(r) > 2 else 0):
        print('  ', r[:20].hex(' '))
