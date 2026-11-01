"""Prints the pad's consumer-control reports (report ID 5) as keys are pressed: media keys and MacroPad's trigger codes.

Usage: python listen.py [seconds] [VID PID]   (default 30 s, 514C 8851). Needs `pip install hidapi`. Read-only.
Close MacroPad first if it should not act on the presses at the same time (both can read).
"""
import sys
import time
import hid

seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 30
vid, pid = (int(sys.argv[2], 16), int(sys.argv[3], 16)) if len(sys.argv) > 3 else (0x514C, 0x8851)
consumer = [d for d in hid.enumerate(vid, pid) if d['usage_page'] == 0x0C]
if not consumer:
    # Linux hidraw has one node per interface; interface 1 carries the consumer reports.
    consumer = [d for d in hid.enumerate(vid, pid) if d['interface_number'] == 1]
c = hid.device()
c.open_path(consumer[0]['path'])
end = time.time() + seconds
while time.time() < end:
    r = c.read(16, 500)
    if r and r[0] == 5:
        usage = r[1] | r[2] << 8
        print(time.strftime('%H:%M:%S'), bytes(r).hex(' '), f'usage 0x{usage:04X}' if usage else 'release', flush=True)
