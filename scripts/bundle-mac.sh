#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Assemble a macOS .app bundle for Nota with a proper Info.plist (so the OS treats
# it as a real app — needed for the microphone TCC permission prompt / M4-3).
#
# Usage: scripts/bundle-mac.sh
# Output: /Applications/Nota.app  (ad-hoc signed, self-contained arm64)
#         Override the destination with NOTA_APP_DIR=/some/dir

set -euo pipefail
cd "$(dirname "$0")/.."

RID="osx-arm64"
APP_NAME="Nota"
BUNDLE_ID="com.nota.daw"
# App version: single source of truth is the repo-root VERSION file.
# CFBundleVersion must monotonically increase per build;
# derive it from the semver so QA can tell builds apart.
VERSION="$(tr -d '[:space:]' < VERSION)"
BUILD_NUMBER="$(echo "${VERSION}" | awk -F. '{ printf "%d%03d%03d", $1, $2, $3 }')"
NATIVE_BUILD="src/native/nota.engine/build"
APP_DIR="${NOTA_APP_DIR:-/Applications}"
APP="${APP_DIR}/${APP_NAME}.app"
CONTENTS="${APP}/Contents"

echo "==> Building native engine (universal)…"
cmake -G Ninja -S src/native/nota.engine -B "${NATIVE_BUILD}" \
  -DCMAKE_BUILD_TYPE=Release >/dev/null
cmake --build "${NATIVE_BUILD}" >/dev/null

echo "==> Publishing managed app (${RID}, self-contained)…"
PUBDIR="$(pwd)/dist/publish"
rm -rf "${PUBDIR}"
dotnet publish src/managed/Nota.App -c Release -r "${RID}" --self-contained true \
  -o "${PUBDIR}" >/dev/null

echo "==> Assembling ${APP}…"
mkdir -p "${APP_DIR}"
rm -rf "${APP}"
mkdir -p "${CONTENTS}/MacOS" "${CONTENTS}/Resources"
cp -R "${PUBDIR}/." "${CONTENTS}/MacOS/"
chmod +x "${CONTENTS}/MacOS/${APP_NAME}.App" "${CONTENTS}/MacOS/nota-scanworker" 2>/dev/null || true

echo "==> Generating app icon (${APP_NAME}.icns)…"
ICON_SRC="assets/icons/logo.png"
ICONSET="$(mktemp -d)/${APP_NAME}.iconset"
mkdir -p "${ICONSET}"
# Canonical macOS iconset: each base size plus its @2x Retina variant.
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

echo "==> Done: ${APP}"
echo "    Launch: open ${APP}"
