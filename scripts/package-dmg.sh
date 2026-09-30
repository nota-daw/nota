#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Build a distributable macOS Nota.app for ONE architecture and wrap it in a
# compressed .dmg with a drag-to-Applications layout. Run it once per arch:
# Apple Silicon and Intel each get their own .dmg (half the size of a universal
# bundle, which had to carry two full self-contained .NET runtimes plus a launcher
# stub — per-arch ReadyToRun framework assemblies can't be lipo-merged).
#
# Both arches build fine on an Apple Silicon host: clang cross-compiles the
# x86_64 engine and dotnet cross-publishes osx-x64.
#
# Usage:  scripts/package-dmg.sh arm64|x86_64
# Output: dist/Nota-<version>-<arch>.dmg
#         Override output dir with NOTA_DMG_DIR=/some/dir

set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="/opt/homebrew/bin:$PATH"

ARCH="${1:-}"
case "${ARCH}" in
  arm64)        RID="osx-arm64" ;;
  x86_64|x64)   ARCH="x86_64"; RID="osx-x64" ;;
  *) echo "usage: $0 arm64|x86_64" >&2; exit 2 ;;
esac

APP_NAME="Nota"
BUNDLE_ID="com.nota.daw"
VERSION="$(tr -d '[:space:]' < VERSION)"                       # single source of truth
BUILD_NUMBER="$(echo "${VERSION}" | awk -F. '{ printf "%d%03d%03d", $1, $2, $3 }')"
NATIVE_BUILD="$(pwd)/src/native/nota.engine/build-mac-${ARCH}"  # thin, per-arch
OUT_DIR="${NOTA_DMG_DIR:-dist}"
DMG="${OUT_DIR}/${APP_NAME}-${VERSION}-${ARCH}.dmg"

WORK="$(pwd)/dist/_mac-${ARCH}"                                # scratch
PUB="${WORK}/publish"
APP="${WORK}/${APP_NAME}.app"
CONTENTS="${APP}/Contents"

echo "==> Nota ${VERSION} — ${ARCH} .dmg"
rm -rf "${WORK}"
mkdir -p "${OUT_DIR}"

# 1) Native engine (thin, this arch only) -------------------------------------
echo "==> Building native engine (${ARCH})…"
cmake -G Ninja -S src/native/nota.engine -B "${NATIVE_BUILD}" \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES="${ARCH}" >/dev/null
cmake --build "${NATIVE_BUILD}" >/dev/null
lipo -info "${NATIVE_BUILD}/libnota_engine.dylib"

# 2) Managed publish, pointed at this arch's engine ---------------------------
echo "==> Publishing managed (${RID}, self-contained)…"
dotnet publish src/managed/Nota.App -c Release -r "${RID}" --self-contained true \
  -p:NotaNativeDir="${NATIVE_BUILD}" -o "${PUB}" >/dev/null

# 3) Assemble the .app --------------------------------------------------------
echo "==> Assembling ${APP_NAME}.app…"
mkdir -p "${CONTENTS}/MacOS" "${CONTENTS}/Resources"
cp -R "${PUB}/." "${CONTENTS}/MacOS/"
chmod +x "${CONTENTS}/MacOS/${APP_NAME}.App" \
         "${CONTENTS}/MacOS/nota-scanworker" 2>/dev/null || true

echo "==> Generating app icon (${APP_NAME}.icns)…"
ICON_SRC="assets/icons/logo.png"
ICONSET="$(mktemp -d)/${APP_NAME}.iconset"
mkdir -p "${ICONSET}"
for size in 16 32 128 256 512; do
  sips -z "${size}" "${size}" "${ICON_SRC}" \
    --out "${ICONSET}/icon_${size}x${size}.png" >/dev/null
  sips -z "$((size * 2))" "$((size * 2))" "${ICON_SRC}" \
    --out "${ICONSET}/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "${ICONSET}" -o "${CONTENTS}/Resources/${APP_NAME}.icns"
rm -rf "${ICONSET}"

cat > "${CONTENTS}/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>            <string>${APP_NAME}</string>
  <key>CFBundleDisplayName</key>     <string>${APP_NAME}</string>
  <key>CFBundleIdentifier</key>      <string>${BUNDLE_ID}</string>
  <key>CFBundleExecutable</key>      <string>${APP_NAME}.App</string>
  <key>CFBundleIconFile</key>        <string>${APP_NAME}</string>
  <key>CFBundlePackageType</key>     <string>APPL</string>
  <key>CFBundleShortVersionString</key> <string>${VERSION}</string>
  <key>CFBundleVersion</key>         <string>${BUILD_NUMBER}</string>
  <key>LSMinimumSystemVersion</key>  <string>13.0</string>
  <key>NSHighResolutionCapable</key> <true/>
  <key>NSMicrophoneUsageDescription</key>
  <string>Nota records audio from your selected input device.</string>
</dict>
</plist>
PLIST

echo "==> Ad-hoc signing…"
codesign --force --deep --sign - "${APP}" >/dev/null 2>&1 || \
  echo "   (codesign warning ignored)"

# 4) Build the .dmg -----------------------------------------------------------
echo "==> Building ${DMG}…"
# Background art is required: assets/macos/dmg-background.png (660x400) and its
# @2x (1320x800) are merged into one HiDPI .tiff so Finder picks the right one.
# Window layout (icon positions, hidden chrome) lives in scripts/dmg-settings.py
# and is written by dmgbuild straight into .DS_Store — no Finder/AppleScript, so
# it works headless in CI.
BG_1X="assets/macos/dmg-background.png"
BG_2X="assets/macos/dmg-background@2x.png"
for f in "${BG_1X}" "${BG_2X}"; do
  [ -f "${f}" ] || { echo "error: missing ${f}" >&2; exit 1; }
done
BG_TMP="$(mktemp -d)"
cp "${BG_1X}" "${BG_TMP}/bg.png"
sips -s dpiWidth 144 -s dpiHeight 144 "${BG_2X}" --out "${BG_TMP}/bg@2x.png" >/dev/null
tiffutil -cathidpicheck "${BG_TMP}/bg.png" "${BG_TMP}/bg@2x.png" \
  -out "${BG_TMP}/background.tiff" >/dev/null 2>&1

# dmgbuild >= 1.6.7 is required: older releases reference the background with a
# legacy alias that Finder on macOS 26 no longer resolves (plain white window).
# 1.6.7 needs Python >= 3.10, and on an older interpreter pip silently falls back
# to 1.6.5 — so pick a new enough Python explicitly (macOS's /usr/bin/python3 is 3.9).
DMGBUILD_VENV="$(pwd)/dist/_dmgbuild-venv"                     # reused across runs
DMGBUILD_REQ="dmgbuild>=1.6.7"
if ! "${DMGBUILD_VENV}/bin/python" -c \
     'import importlib.metadata as m, sys; v = tuple(map(int, m.version("dmgbuild").split(".")[:3])); sys.exit(v < (1, 6, 7))' \
     2>/dev/null; then
  PY=""
  for c in python3.14 python3.13 python3.12 python3.11 python3.10 python3; do
    if command -v "${c}" >/dev/null && "${c}" -c 'import sys; sys.exit(sys.version_info < (3, 10))'; then
      PY="${c}"; break
    fi
  done
  [ -n "${PY}" ] || { echo "error: dmgbuild needs Python >= 3.10 (brew install python)" >&2; exit 1; }
  echo "   (installing ${DMGBUILD_REQ} with ${PY} into ${DMGBUILD_VENV})"
  rm -rf "${DMGBUILD_VENV}"
  "${PY}" -m venv "${DMGBUILD_VENV}"
  "${DMGBUILD_VENV}/bin/pip" install --quiet --upgrade pip "${DMGBUILD_REQ}"
fi

rm -f "${DMG}"
"${DMGBUILD_VENV}/bin/dmgbuild" -s scripts/dmg-settings.py \
  -D app="${APP}" \
  -D background="${BG_TMP}/background.tiff" \
  -D icon="${CONTENTS}/Resources/${APP_NAME}.icns" \
  "${APP_NAME} ${VERSION}" "${DMG}" >/dev/null
rm -rf "${BG_TMP}"

echo "==> Verifying:"
echo -n "   apphost: "; lipo -archs "${CONTENTS}/MacOS/${APP_NAME}.App"
echo -n "   engine:  "; lipo -archs "${CONTENTS}/MacOS/libnota_engine.dylib"
echo -n "   scanner: "; lipo -archs "${CONTENTS}/MacOS/nota-scanworker"
for bin in "${APP_NAME}.App" libnota_engine.dylib nota-scanworker; do
  [ "$(lipo -archs "${CONTENTS}/MacOS/${bin}")" = "${ARCH}" ] || \
    { echo "error: ${bin} is not ${ARCH}-only" >&2; rm -f "${DMG}"; exit 1; }
done

echo "==> Done: ${DMG}"
echo "    (scratch build tree left in ${WORK})"
