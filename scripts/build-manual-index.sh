#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# Rebuilds the Assistant panel's manual index (specs/098) from a nota-docs checkout:
#   scripts/build-manual-index.sh [path/to/nota-docs]     (default ../nota-docs)
# Writes assets/manual/manual-index.json.gz, which is committed and embedded in the app. Run it
# when the manual changes for a release; the smoke test (--assistant) then checks every Show me
# anchor in it against this build. Fails, writing nothing, on any page the builder can't read.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
docs="$(cd "${1:-$repo/../nota-docs}" && pwd)"
out="$repo/assets/manual/manual-index.json.gz"

dotnet build "$repo/tests/Nota.SmokeTest" -c Release --nologo -v q
dotnet "$repo/tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll" --manual-index-build "$docs" "$out"
