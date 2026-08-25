#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Full local Nota build: native engine (universal) + managed.
# Usage: scripts/build.sh

set -euo pipefail
cd "$(dirname "$0")/.."

export PATH="/opt/homebrew/bin:$PATH"

echo "==> Building native engine (universal arm64+x86_64)"
cmake -G Ninja -S src/native/nota.engine -B src/native/nota.engine/build \
  -DCMAKE_BUILD_TYPE=Release
cmake --build src/native/nota.engine/build
lipo -info src/native/nota.engine/build/libnota_engine.dylib

echo "==> Building managed (.NET)"
dotnet build Nota.sln -c Release --nologo

echo "==> Smoke test"
dotnet run --project tests/Nota.SmokeTest -c Release --nologo

echo "==> Done. Run the app with: dotnet run --project src/managed/Nota.App"
