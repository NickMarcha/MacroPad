"""Stand-in Home Assistant (port 8123) and Bragi (port 8765) with generic rooms and audio, for README screenshots."""
import json, re, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

AREAS = {"office": ("Office", ["light.desk_lamp", "light.ceiling"]),
         "living_room": ("Living Room", ["light.floor_lamp"]),
         "bedroom": ("Bedroom", ["light.bedside"])}
NAMES = {"light.desk_lamp": "Desk Lamp", "light.ceiling": "Office Ceiling", "light.floor_lamp": "Floor Lamp", "light.bedside": "Bedside Lamp"}
LIGHTS = {"light.desk_lamp": 160, "light.ceiling": 0, "light.floor_lamp": 120, "light.bedside": 0}

DEVICES = [
    {"id": "headset:desk-headset", "kind": "headset", "key": "desk-headset", "name": "Desk Headset", "enabled": True,
     "controls": {"playback": {"direction": "playback", "label": "Speakers", "volume": 0.64, "muted": False, "connected": True, "pannable": False},
                  "capture": {"direction": "capture", "label": "Mic", "volume": 1.0, "muted": False, "connected": True, "pannable": False}}},
    {"id": "peer:studio", "kind": "peer", "key": "studio", "name": "studio", "protocol": "vban", "client_connected": True,
     "controls": {"outgoing": {"direction": "outgoing", "label": "Mic to studio", "volume": 1.0, "muted": False, "connected": True, "pannable": True, "balance": 0.0},
                  "incoming": {"direction": "incoming", "label": "studio to headset", "volume": 0.8, "muted": False, "connected": True, "pannable": True, "balance": 0.0}}},
    {"id": "peer:laptop", "kind": "peer", "key": "laptop", "name": "laptop", "protocol": "vban", "client_connected": False,
     "controls": {"outgoing": {"direction": "outgoing", "label": "Mic to laptop", "volume": 1.0, "muted": False, "connected": True, "pannable": True, "balance": 0.0},
                  "incoming": {"direction": "incoming", "label": "laptop to headset", "volume": 0.9, "muted": True, "connected": True, "pannable": True, "balance": 0.0}}},
]
lock = threading.Lock()


def level(lights):
    on = [l for l in lights if LIGHTS[l] > 0]
    avg = sum(LIGHTS[l] for l in on) / len(on) if on else 0
    return {"on": len(on), "total": len(lights), "brightness": avg}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def reply(self, body, status=200):
        data = body if isinstance(body, bytes) else json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def body(self):
        n = int(self.headers.get("Content-Length") or 0)
        return json.loads(self.rfile.read(n) or b"{}")

    def targets(self, data):
        if "area_id" in data:
            return AREAS[data["area_id"]][1]
        return [data["entity_id"]]

    def do_GET(self):
        p = self.path.split("?")[0]
        with lock:
            if p == "/api/states":
                return self.reply([{"entity_id": e, "state": "on" if b else "off", "attributes": {"friendly_name": NAMES[e], "brightness": b or None}} for e, b in LIGHTS.items()])
            if p == "/api/":
                return self.reply({"message": "API running."})
            if p == "/api/v1/devices":
                return self.reply({"devices": DEVICES})
            m = re.match(r"/api/v1/devices/(\w+)/([\w-]+)$", p)
            if m:
                return self.reply(next(d for d in DEVICES if d["key"] == m.group(2)))
        self.reply({"error": "no"}, 404)

    def do_POST(self):
        p = self.path
        data = self.body()
        with lock:
            if p == "/api/template":
                t = data["template"]
                if "areas()" in t:
                    return self.reply([{"id": k, "name": v[0], "lights": v[1]} for k, v in AREAS.items()])
                m = re.search(r'area_entities\("([^"]+)"\)', t)
                lights = AREAS[m.group(1)][1] if m else [re.search(r'\["([^"]+)"\]', t).group(1)]
                if "|count }}" in t:
                    return self.reply(str(sum(1 for l in lights if LIGHTS[l] > 0)).encode())
                return self.reply(level(lights))
            m = re.match(r"/api/services/light/(\w+)", p)
            if m:
                for l in self.targets(data):
                    if m.group(1) == "turn_off":
                        LIGHTS[l] = 0
                    elif m.group(1) == "toggle":
                        LIGHTS[l] = 0 if LIGHTS[l] else 200
                    else:
                        LIGHTS[l] = data.get("brightness", LIGHTS[l] or 200)
                return self.reply([])
        self.reply({"error": "no"}, 404)

    def do_PATCH(self):
        m = re.match(r"/api/v1/devices/(\w+)/([\w-]+)/controls/(\w+)", self.path)
        data = self.body()
        with lock:
            control = next(d for d in DEVICES if d["key"] == m.group(2))["controls"][m.group(3)]
            control.update({k: v for k, v in data.items() if k in ("volume", "muted")})
            self.reply({"control": control})


if __name__ == "__main__":
    for port in (8123, 8765):
        threading.Thread(target=ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever, daemon=True).start()
    print("fake services up", flush=True)
    threading.Event().wait()
