#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
#
# Package Nota for Linux as a portable AppImage: build the native engine, publish
# the self-contained app, assemble an AppDir (AppRun + .desktop + icon), then run
# appimagetool. Mirrors scripts/package-win.ps1 (Inno Setup) and package-dmg.sh.
#
# Build host needs the same dev packages as build-linux.sh, plus `appimagetool`
# (https://github.com/AppImage/appimagetool). If appimagetool isn't on PATH the
# script downloads it next to itself. Set APPIMAGE_EXTRACT_AND_RUN=1 (done below)
# so it works in containers without FUSE.
#
# Usage: scripts/package-linux.sh [free|pro] [x64|arm64]   (defaults: free x64)
# Output: dist/Nota-<version>-<arch>.AppImage

set -euo pipefail
cd "$(dirname "$0")/.."

# This builds the native Linux engine (libnota_engine.so) — it can't be
# cross-compiled from macOS/Windows, so refuse early with a clear message and a
# ready-to-run container command instead of failing later on a missing .so.
if [ "$(uname -s)" != "Linux" ]; then
  cat >&2 <<'MSG'
error: package-linux.sh builds a native Linux engine and must run on Linux.
       From macOS/Windows, run it inside a Linux container (single line — safe to paste):

  docker run --rm -e CMAKE_BUILD_PARALLEL_LEVEL=2 -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -c 'apt-get update -qq && apt-get install -y --no-install-recommends cmake ninja-build build-essential curl ca-certificates file squashfs-tools libasound2-dev libx11-dev libxext-dev libxrandr-dev libxinerama-dev libxcursor-dev libfreetype6-dev libfontconfig1-dev && scripts/package-linux.sh'
MSG
  exit 1
fi

EDITION="${1:-free}"
# The native engine is compiled for the build host's arch (no cross-compile), so the
# default target arch follows uname -m; pass an explicit arch only if it matches.
case "$(uname -m)" in
  x86_64)        HOST_ARCH="x64"   ;;
  aarch64|arm64) HOST_ARCH="arm64" ;;
  *)             HOST_ARCH="x64"   ;;
esac
ARCH="${2:-$HOST_ARCH}"
case "${ARCH}" in
  x64)   AI_ARCH="x86_64"  ;;
  arm64) AI_ARCH="aarch64" ;;
  *) echo "Unknown arch '${ARCH}' (expected x64 or arm64)" >&2; exit 1 ;;
esac
if [ "${ARCH}" != "${HOST_ARCH}" ]; then
  echo "error: requested ARCH=${ARCH} but the host is ${HOST_ARCH}; the native engine" >&2
  echo "       is not cross-compiled — run this on a ${ARCH} Linux host/container." >&2
  exit 1
fi

VERSION="$(tr -d '[:space:]' < VERSION)"
RID="linux-${ARCH}"
# Dedicated build dir per arch (mirrors package-win.ps1's build-$Arch) so packaging
# never reuses/clobbers a dev build/ cache configured for another OS.
NATIVE="src/native/nota.engine/build-linux-${ARCH}"
PUB="$(pwd)/dist/publish-${ARCH}"
APPDIR="dist/Nota.AppDir"
OUT="dist/Nota-${VERSION}-${AI_ARCH}.AppImage"

echo "==> Building native engine (${ARCH}, PulseAudio/ALSA + RtMidi/ALSA)…"
# Guard against a build dir left configured for a different arch (e.g. reused across
# an arm64/x86_64 switch, or an emulated container vs a native one): CMake would keep
# the cached cross-arch library paths and fail to link. If the cached host processor
# doesn't match this environment, wipe and reconfigure clean.
CACHE="${NATIVE}/CMakeCache.txt"
if [ -f "${CACHE}" ]; then
  CACHED_ARCH="$(sed -n 's/^CMAKE_HOST_SYSTEM_PROCESSOR:INTERNAL=//p' "${CACHE}")"
  if [ -n "${CACHED_ARCH}" ] && [ "${CACHED_ARCH}" != "$(uname -m)" ]; then
    echo "    build dir was configured for ${CACHED_ARCH}, this is $(uname -m) — reconfiguring clean"
    rm -rf "${NATIVE}"
  fi
fi
cmake -G Ninja -S src/native/nota.engine -B "${NATIVE}" \
  -DCMAKE_BUILD_TYPE=Release "-DNOTA_EDITION=${EDITION}" >/dev/null
cmake --build "${NATIVE}" >/dev/null

echo "==> Publishing managed app (${RID}, self-contained)…"
rm -rf "${PUB}"
dotnet publish src/managed/Nota.App -c Release -r "${RID}" --self-contained true \
  "-p:NotaEdition=${EDITION}" "-p:NotaNativeDir=$(pwd)/${NATIVE}" -o "${PUB}" >/dev/null

# The csproj copies the native artifacts for a linux RID; copy them explicitly too
# as a safety net.
cp -f "${NATIVE}/libnota_engine.so" "${PUB}/"
[ -f "${NATIVE}/nota-scanworker" ] && cp -f "${NATIVE}/nota-scanworker" "${PUB}/"

echo "==> Assembling AppDir…"
rm -rf "${APPDIR}"
mkdir -p "${APPDIR}/usr/bin" \
         "${APPDIR}/usr/share/applications" \
         "${APPDIR}/usr/share/icons/hicolor/256x256/apps"
cp -R "${PUB}/." "${APPDIR}/usr/bin/"
chmod +x "${APPDIR}/usr/bin/Nota.App" "${APPDIR}/usr/bin/nota-scanworker" 2>/dev/null || true

# Icon (top-level for the AppImage thumbnail + the freedesktop hicolor path).
cp assets/icons/linux/nota-256.png "${APPDIR}/usr/share/icons/hicolor/256x256/apps/nota.png"
cp assets/icons/linux/nota-256.png "${APPDIR}/nota.png"

# Desktop entry (top-level copy is what appimagetool reads for metadata).
cat > "${APPDIR}/nota.desktop" <<'DESK'
[Desktop Entry]
Type=Application
Name=Nota
Comment=Nota digital audio workstation
Exec=Nota.App
Icon=nota
Categories=AudioVideo;Audio;
Terminal=false
DESK
cp "${APPDIR}/nota.desktop" "${APPDIR}/usr/share/applications/nota.desktop"

# AppRun launches the self-contained apphost.
cat > "${APPDIR}/AppRun" <<'RUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "${HERE}/usr/bin/Nota.App" "$@"
RUN
chmod +x "${APPDIR}/AppRun"

echo "==> Building AppImage…"
mkdir -p dist

# Preferred path: appimagetool. It's shipped only as an AppImage, and its runtime is
# static-PIE — which QEMU user emulation can't exec (ENOEXEC), so a Linux-x64 build on
# an Apple-Silicon mac can't run it. Try it; on failure fall back to assembling the
# AppImage by hand (squashfs the AppDir + prepend the official type2 runtime — exactly
# what appimagetool does internally, minus executing a static-PIE ELF).
build_with_appimagetool() {
  local tool
  tool="$(command -v appimagetool || true)"
  if [ -z "${tool}" ]; then
    tool="dist/appimagetool-${AI_ARCH}.AppImage"   # cached under dist/ (gitignored)
    if [ ! -f "${tool}" ]; then
      echo "    appimagetool not found — downloading…"
      curl -fsSL -o "${tool}" \
        "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-${AI_ARCH}.AppImage" || return 1
      chmod +x "${tool}"
    fi
  fi
  # extract-and-run avoids needing FUSE; ARCH stamps the target arch.
  APPIMAGE_EXTRACT_AND_RUN=1 ARCH="${AI_ARCH}" "${tool}" "${APPDIR}" "${OUT}"
}

build_manually() {
  echo "    assembling AppImage by hand (mksquashfs + type2 runtime)…"
  if ! command -v mksquashfs >/dev/null; then
    echo "error: need squashfs-tools (mksquashfs) for the manual AppImage path — apt install squashfs-tools" >&2
    exit 1
  fi
  local runtime="dist/runtime-${AI_ARCH}"          # cached under dist/ (gitignored)
  if [ ! -f "${runtime}" ]; then
    curl -fsSL -o "${runtime}" \
      "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-${AI_ARCH}"
  fi
  local sq="dist/nota-${AI_ARCH}.squashfs"
  rm -f "${sq}"
  mksquashfs "${APPDIR}" "${sq}" -root-owned -noappend -quiet -comp zstd
  cat "${runtime}" "${sq}" > "${OUT}"    # type2 runtime finds the appended filesystem itself
  chmod +x "${OUT}"
  rm -f "${sq}"
}

if ! build_with_appimagetool; then
  echo "    appimagetool couldn't run (static-PIE under QEMU?) — falling back to manual build."
  build_manually
fi

echo "==> Done: ${OUT}"
