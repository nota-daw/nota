#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# Regenerates nix/nix-deps.json (CMake/CPM + npm) and nix/nuget-deps.json (.NET)
# from the upstream dependency files so Nix sandboxed builds stay in sync.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

NIX_DEPS_FILE="nix/nix-deps.json"
NUGET_DEPS_FILE="nix/nuget-deps.json"

echo "==> Updating CMake/CPM and npm pins in $NIX_DEPS_FILE..."
NPM_HASH="$(nix run nixpkgs#prefetch-npm-deps -- src/web/remote/package-lock.json 2>/dev/null | tail -n 1)"

python3 - "$NIX_DEPS_FILE" "$NPM_HASH" <<'PY'
import json
import re
import subprocess
import sys
from pathlib import Path

nix_deps_path = Path(sys.argv[1])
npm_hash = sys.argv[2].strip()

existing = json.loads(nix_deps_path.read_text()) if nix_deps_path.exists() else {}
existing_cmake = existing.get("cmakeDeps", {})

cmakelists = Path("src/native/nota.engine/CMakeLists.txt").read_text()
cpm_version = re.search(r'set\(\s*CPM_VERSION\s+"([^"]+)"\s*\)', cmakelists).group(1)
cpm_sha256 = re.search(r'set\(\s*CPM_SHA256\s+"([^"]+)"\s*\)', cmakelists).group(1)

deps_cmake = Path("src/native/nota.engine/cmake/dependencies.cmake").read_text()
# Strip the Windows-only if(WIN32) ... endif() block so Linux Nix pins stay Linux-only.
deps_linux = re.sub(r'if\s*\(\s*WIN32\s*\).*?endif\s*\(\s*\)', '', deps_cmake, flags=re.S)

cmake_deps = {}
for block in re.finditer(r'CPMAddPackage\s*\((.*?)\)', deps_linux, flags=re.S):
    body = block.group(1)
    name_m = re.search(r'\bNAME\s+([A-Za-z0-9_]+)', body)
    url_m = re.search(r'\bURL\s+"([^"]+)"', body)
    if not name_m or not url_m:
        continue
    name, url = name_m.group(1), url_m.group(1)
    prev = existing_cmake.get(name, {})
    if prev.get("url") == url and prev.get("hash"):
        entry = dict(prev)
    else:
        raw_hash = subprocess.check_output(
            ["nix-prefetch-url", "--unpack", url], text=True
        ).strip().splitlines()[-1]
        sri = subprocess.check_output(
            ["nix", "hash", "convert", "--hash-algo", "sha256", "--to", "sri", raw_hash],
            text=True,
        ).strip()
        entry = {"url": url, "hash": sri}
        if prev.get("stripRoot") is False:
            entry["stripRoot"] = False
        if url.endswith("/download") or prev.get("extension"):
            entry["extension"] = prev.get("extension", "tar.gz")
    cmake_deps[name] = entry

out = {
    "cpm": {"version": cpm_version, "sha256": cpm_sha256},
    "cmakeDeps": cmake_deps,
    "npmHash": npm_hash,
}
nix_deps_path.write_text(json.dumps(out, indent=2) + "\n")
PY

echo "==> Updating NuGet pins in $NUGET_DEPS_FILE..."
TMP_NUGET="$(mktemp -d)"
trap 'rm -rf "$TMP_NUGET"' EXIT

nix shell nixpkgs#dotnetCorePackages.sdk_10_0 nixpkgs#nuget-to-json -c bash -c "
  set -euo pipefail
  dotnet restore src/managed/Nota.App/Nota.App.csproj --packages '$TMP_NUGET' -r linux-x64 >/dev/null
  dotnet restore src/managed/Nota.App/Nota.App.csproj --packages '$TMP_NUGET' -r linux-arm64 >/dev/null
  dotnet restore src/managed/Nota.App/Nota.App.csproj --packages '$TMP_NUGET' >/dev/null
  nuget-to-json '$TMP_NUGET' > '$NUGET_DEPS_FILE'
"

echo "==> Done."
