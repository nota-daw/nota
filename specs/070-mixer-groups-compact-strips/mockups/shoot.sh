#!/bin/sh
# Renders every scene of mixer.html in Studio Dark and Studio Light (headless Chrome).
cd "$(dirname "$0")" && python3 build.py >/dev/null || exit 1
C="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
for v in dark light; do
  for s in regular folded compact compact-folded; do
    "$C" --headless=new --disable-gpu --hide-scrollbars --allow-file-access-from-files \
      --force-device-scale-factor=2 --window-size=1700,${H:-720} \
      --screenshot="$PWD/$s-$v.png" "file://$PWD/mixer.html?variant=$v&scene=$s" >/dev/null 2>&1
  done
done
