#!/bin/bash
# Runs a published linux-x64 MacroPad under Xvfb in an Arch container, triggers a pop-up and prints its X11 window
# type and state. Expect _NET_WM_WINDOW_TYPE_NOTIFICATION and SKIP_TASKBAR/ABOVE (src/MacroPad.App/X11Notification.cs).
#
#   dotnet publish src/MacroPad.App -c Release -r linux-x64 -o out
#   docker run --rm -v "$PWD/out:/app" -v "$PWD/tools/linux:/tools" archlinux:latest bash /tools/popup-check.sh
set -e
pacman -Syu --noconfirm --needed xorg-server-xvfb xorg-xprop xorg-xwininfo libx11 libice libsm libxext libxrandr \
  libxcursor libxi fontconfig freetype2 mesa ttf-dejavu >/dev/null 2>&1
export DISPLAY=:99 MACROPAD_TRACE=1 HOME=/root
Xvfb :99 -screen 0 1600x900x24 >/dev/null 2>&1 &
sleep 2
cp /app/MacroPad /tmp/MacroPad && chmod +x /tmp/MacroPad
/tmp/MacroPad --background > /tmp/app.log 2>&1 &
sleep 8
/tmp/MacroPad --profile Default   # a second start hands this to the first: a profile pop-up
sleep 1
echo "== trace"; grep -i "pop-up" /tmp/macropad-trace.log || tail -5 /tmp/app.log
for w in $(xwininfo -root -tree | awk '/"MacroPad"/ {print $1}'); do
  echo "-- $w $(xwininfo -id "$w" | grep -E 'Map State|Width' | tr -s ' ' | tr '\n' ' ')"
  xprop -id "$w" _NET_WM_WINDOW_TYPE _NET_WM_STATE
done
