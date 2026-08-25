#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
#
# Full local Nota build: native engine (universal) + managed.
# Usage: scripts/build.sh [free|pro]   (default: free)

set -euo pipefail
cd "$(dirname "$0")/.."

EDITION="${1:-free}"
export PATH="/opt/homebrew/bin:$PATH"

echo "==> Building native engine (edition=$EDITION, universal arm64+x86_64)"
cmake -G Ninja -S src/native/nota.engine -B src/native/nota.engine/build \
  -DCMAKE_BUILD_TYPE=Release -DNOTA_EDITION="$EDITION"
cmake --build src/native/nota.engine/build
lipo -info src/native/nota.engine/build/libnota_engine.dylib

echo "==> Building managed (.NET, edition=$EDITION)"
dotnet build Nota.sln -c Release -p:NotaEdition="$EDITION" --nologo

echo "==> Smoke test"
dotnet run --project tests/Nota.SmokeTest -c Release -p:NotaEdition="$EDITION" --nologo

echo "==> Done. Run the app with: dotnet run --project src/managed/Nota.App"
