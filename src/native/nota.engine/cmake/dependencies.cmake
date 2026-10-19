# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# CMake dependencies via CPM (general infra, not per-library vendoring).
# Nothing here lands in the repo: CPM fetches each package into the build dir
# (_deps) from a pinned URL + URL_HASH below. To add a dependency, append one
# CPMAddPackage block and its row in LICENSES/third-party.md.

CPMAddPackage(
    NAME nlohmann_json
    VERSION 3.12.0
    URL "https://github.com/nlohmann/json/releases/download/v3.12.0/json.tar.xz"
    URL_HASH "SHA256=42f6e95cad6ec532fd372391373363b62a14af6d771056dbfc86160e6dfff7aa"
    OPTIONS
        "JSON_BuildTests OFF"
        "JSON_Install OFF"
)
