#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
#
# Local Linux build of Nota: native engine (miniaudio PulseAudio/ALSA + RtMidi/ALSA
# + VST3) + managed + smoke test. Mirrors scripts/build-win.ps1.
#
# Build host needs: cmake, ninja, a C++20 compiler, .NET SDK 10, and the dev
# packages JUCE + the engine link against — on Debian/Ubuntu:
#   sudo apt install build-essential cmake ninja-build libasound2-dev \
#     libx11-dev libxext-dev libxrandr-dev libxinerama-dev libxcursor-dev \
#     libfreetype6-dev libfontconfig1-dev
# (miniaudio runtime-links PulseAudio/ALSA via dlopen, so libpulse/libasound are
# not needed at link time — only libasound2-dev for RtMidi.)
#
# Usage: scripts/build-linux.sh [free|pro]   (default free)

set -euo pipefail
cd "$(dirname "$0")/.."

# The native engine (libnota_engine.so) can't be cross-compiled from macOS/Windows;
# refuse early with a clear message + a container command instead of a confusing
# compiler/link failure.
if [ "$(uname -s)" != "Linux" ]; then
  cat >&2 <<'MSG'
error: build-linux.sh builds a native Linux engine and must run on Linux.
       From macOS/Windows, run it inside a Linux container (single line — safe to paste):

  docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -c 'apt-get update -qq && apt-get install -y --no-install-recommends cmake ninja-build build-essential libasound2-dev libx11-dev libxext-dev libxrandr-dev libxinerama-dev libxcursor-dev libfreetype6-dev libfontconfig1-dev && scripts/build-linux.sh'
MSG
  exit 1
fi

EDITION="${1:-free}"
NATIVE="src/native/nota.engine/build"

echo "==> Building native engine (edition=${EDITION}, PulseAudio/ALSA + RtMidi/ALSA)"
cmake -G Ninja -S src/native/nota.engine -B "${NATIVE}" \
  -DCMAKE_BUILD_TYPE=Release "-DNOTA_EDITION=${EDITION}"
cmake --build "${NATIVE}"

echo "==> Building managed (.NET, edition=${EDITION})"
dotnet build Nota.sln -c Release "-p:NotaEdition=${EDITION}" --nologo

echo "==> Smoke test"
dotnet run --project tests/Nota.SmokeTest -c Release "-p:NotaEdition=${EDITION}" --nologo

echo "==> Done. Run the app with: dotnet run --project src/managed/Nota.App"
