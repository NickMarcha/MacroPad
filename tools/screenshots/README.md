# README screenshots

`docs/images/` is made against stand-ins, so no real rooms, devices or addresses show:

- `fake_services.py`: a fake Home Assistant (port 8123) and Bragi (port 8765) with generic rooms (Office, Living Room,
  Bedroom) and audio (Desk Headset, studio, laptop). Keeps state, so toggles and the knob work.
- `demo-settings.cs`: writes settings pointing at them, with a demo Default and Music profile.
- `capture.ps1`: swaps the demo settings in (backing yours up), runs a published build, captures the Pad and Settings
  tabs with PrintWindow, collects pop-up frames while you press pad keys, and puts your settings back. The steps are
  at the top of the script. Output goes to `out/` (ignored by git).

The pop-up's corners: mask them round (radius 12) before saving, for example with Pillow:

```python
from PIL import Image, ImageDraw
im = Image.open("out/popup-00.png").convert("RGBA")
mask = Image.new("L", im.size, 0); ImageDraw.Draw(mask).rounded_rectangle((0, 0, im.width - 1, im.height - 1), 12, fill=255)
im.putalpha(mask); im.save("docs/images/popup.png")
```

Python's simple HTTP server sometimes trips .NET's connection reuse ("An error occurred while sending the request"
in a pop-up); that is the fake server, not MacroPad. Pick a frame without it.
