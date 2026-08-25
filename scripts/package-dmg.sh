#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
#
# Build a distributable macOS **universal** (arm64 + x86_64) Nota.app and wrap it
# in a compressed .dmg with a drag-to-Applications layout.
#
# Why per-arch subtrees + a launcher (NOT lipo-merge everything): .NET
# self-contained publishes are per-RID, and it is NOT true that "the managed
# assemblies are identical across RIDs" — some framework assemblies ship from the
# runtime pack as per-arch ReadyToRun PE images (System.Private.CoreLib.dll above
# all). lipo only fattens Mach-O, so a flat merged tree keeps the arm64 CoreLib and
# CoreCLR aborts with BADIMAGEFORMAT (0x8007000B) on Intel — the app just dock-
# bounces and quits. A single flat folder physically cannot hold both per-arch
# CoreLibs. So we keep BOTH complete self-contained trees under MacOS/{arm64,x64}
# and make the bundle's main executable a tiny universal C stub that execs the
# apphost matching the CPU we're natively running on.
#
# Usage:  scripts/package-dmg.sh [free|pro]        (default free)
# Output: dist/Nota-<version>-universal.dmg
#         Override output dir with NOTA_DMG_DIR=/some/dir

set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="/opt/homebrew/bin:$PATH"

EDITION="${1:-free}"
APP_NAME="Nota"
BUNDLE_ID="com.nota.daw"
VERSION="$(tr -d '[:space:]' < VERSION)"                       # single source of truth
BUILD_NUMBER="$(echo "${VERSION}" | awk -F. '{ printf "%d%03d%03d", $1, $2, $3 }')"
NATIVE_BUILD="src/native/nota.engine/build"
OUT_DIR="${NOTA_DMG_DIR:-dist}"
DMG="${OUT_DIR}/${APP_NAME}-${VERSION}-universal.dmg"

WORK="$(pwd)/dist/_universal"                                  # scratch: per-RID publishes
ARM="${WORK}/arm64"
X64="${WORK}/x64"
APP="${WORK}/${APP_NAME}.app"
CONTENTS="${APP}/Contents"

echo "==> Nota ${VERSION} (${EDITION}) — universal .dmg"
rm -rf "${WORK}"
mkdir -p "${OUT_DIR}"

# 1) Native engine (fat) ------------------------------------------------------
echo "==> Building native engine (universal arm64+x86_64)…"
cmake -G Ninja -S src/native/nota.engine -B "${NATIVE_BUILD}" \
  -DCMAKE_BUILD_TYPE=Release -DNOTA_EDITION="${EDITION}" >/dev/null
cmake --build "${NATIVE_BUILD}" >/dev/null
lipo -info "${NATIVE_BUILD}/libnota_engine.dylib"

# 2) Managed publishes, one per arch ------------------------------------------
publish() {
  local rid="$1" out="$2"
  echo "==> Publishing managed (${rid}, self-contained)…"
  dotnet publish src/managed/Nota.App -c Release -r "${rid}" --self-contained true \
    -p:NotaEdition="${EDITION}" -o "${out}" >/dev/null
}
publish osx-arm64 "${ARM}"
publish osx-x64   "${X64}"

# 3) Assemble the .app: both runtimes side by side + a universal launcher ------
# MacOS/arm64 and MacOS/x64 each hold a complete self-contained publish; the
# bundle's CFBundleExecutable is the "Nota" stub below, which execs the right one.
echo "==> Assembling ${APP_NAME}.app (per-arch runtimes + launcher)…"
rm -rf "${APP}"
mkdir -p "${CONTENTS}/MacOS/arm64" "${CONTENTS}/MacOS/x64" "${CONTENTS}/Resources"
cp -R "${ARM}/." "${CONTENTS}/MacOS/arm64/"
cp -R "${X64}/." "${CONTENTS}/MacOS/x64/"
for a in arm64 x64; do
  chmod +x "${CONTENTS}/MacOS/${a}/${APP_NAME}.App" \
           "${CONTENTS}/MacOS/${a}/nota-scanworker" 2>/dev/null || true
done

echo "==> Compiling universal launcher (${APP_NAME})…"
LAUNCHER_SRC="$(mktemp -d)/launcher.c"
cat > "${LAUNCHER_SRC}" <<'CSRC'
// Universal trampoline: exec the runtime matching the CPU we natively run on.
// macOS loads this binary's native slice, so uname() reports the real arch
// (arm64 on Apple Silicon, x86_64 on Intel) — even though the child apphost and
// its ReadyToRun framework assemblies are per-arch/thin.
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <limits.h>
#include <unistd.h>
#include <sys/utsname.h>
#include <mach-o/dyld.h>

int main(int argc, char** argv) {
    char exe[PATH_MAX];
    uint32_t sz = (uint32_t)sizeof(exe);
    if (_NSGetExecutablePath(exe, &sz) != 0) return 127;
    char dir[PATH_MAX];
    if (!realpath(exe, dir)) return 127;
    char* slash = strrchr(dir, '/');
    if (slash) *slash = '\0';                       // dir = …/Contents/MacOS

    struct utsname u;
    const char* arch = "x64";                        // default to Intel
    if (uname(&u) == 0 && strcmp(u.machine, "arm64") == 0) arch = "arm64";

    char target[PATH_MAX];
    snprintf(target, sizeof(target), "%s/%s/Nota.App", dir, arch);

    char** nargv = (char**)malloc(sizeof(char*) * (size_t)(argc + 1));
    if (!nargv) return 127;
    nargv[0] = target;
    for (int i = 1; i < argc; i++) nargv[i] = argv[i];
    nargv[argc] = NULL;
    execv(target, nargv);
    perror("nota launcher: execv");                  // only reached on failure
    return 127;
}
CSRC
# -mmacosx-version-min gates on which macOS the OS lets this run; keep it in sync
# with Info.plist LSMinimumSystemVersion and the engine's CMAKE_OSX_DEPLOYMENT_TARGET.
# Without it clang stamps the build host's OS (e.g. 26.0) and older Macs refuse it.
clang -arch arm64 -arch x86_64 -mmacosx-version-min=13.0 \
  -O2 -o "${CONTENTS}/MacOS/${APP_NAME}" "${LAUNCHER_SRC}"
chmod +x "${CONTENTS}/MacOS/${APP_NAME}"

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
  <key>CFBundleExecutable</key>      <string>${APP_NAME}</string>
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

echo "==> Ad-hoc signing (deep — lipo invalidated the per-slice signatures)…"
codesign --force --deep --sign - "${APP}" >/dev/null 2>&1 || \
  echo "   (codesign warning ignored)"

# 5) Build the .dmg -----------------------------------------------------------
echo "==> Building ${DMG}…"
STAGE="$(mktemp -d)/dmg"
mkdir -p "${STAGE}"
cp -R "${APP}" "${STAGE}/"
ln -s /Applications "${STAGE}/Applications"                   # drag-to-install target
rm -f "${DMG}"
hdiutil create -volname "${APP_NAME} ${VERSION}" -srcfolder "${STAGE}" \
  -fs HFS+ -format UDZO -ov "${DMG}" >/dev/null
rm -rf "${STAGE}"

echo "==> Verifying:"
echo -n "   launcher:      "; lipo -archs "${CONTENTS}/MacOS/${APP_NAME}"
echo -n "   arm64 apphost: "; lipo -archs "${CONTENTS}/MacOS/arm64/${APP_NAME}.App"
echo -n "   x64 apphost:   "; lipo -archs "${CONTENTS}/MacOS/x64/${APP_NAME}.App"
echo -n "   arm64 CoreLib: "; file -b "${CONTENTS}/MacOS/arm64/System.Private.CoreLib.dll" | grep -qi 'PE32' && echo "present (per-arch)"
echo -n "   x64 CoreLib:   "; file -b "${CONTENTS}/MacOS/x64/System.Private.CoreLib.dll" | grep -qi 'PE32' && echo "present (per-arch)"

echo "==> Done: ${DMG}"
echo "    (scratch build tree left in ${WORK})"
