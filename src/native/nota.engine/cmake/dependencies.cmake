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

# Header-only audio dependencies (fetch-only wiring; no built artifacts).
# Pins below are byte-verified against the pre-CPM snapshots.
CPMAddPackage(
    NAME dr_libs
    GIT_TAG cd99e2cca5ccb48f8d000f1cd844424dcdbbaf59
    GITHUB_REPOSITORY mackron/dr_libs
    DOWNLOAD_ONLY YES
)
CPMAddPackage(
    NAME miniaudio
    GIT_TAG 0.11.25
    GITHUB_REPOSITORY mackron/miniaudio
    DOWNLOAD_ONLY YES
)
CPMAddPackage(
    NAME signalsmith_linear
    GIT_TAG 7f53cdd1ccd52b409dacf2af24e7ff838c5580cd
    GITHUB_REPOSITORY Signalsmith-Audio/linear
    DOWNLOAD_ONLY YES
)
CPMAddPackage(
    NAME signalsmith_stretch
    GIT_TAG 57b93f4e9206a089a45387eaa39bdc9f310d3308
    GITHUB_REPOSITORY Signalsmith-Audio/signalsmith-stretch
    DOWNLOAD_ONLY YES
)
CPMAddPackage(
    NAME hiir
    URL "https://ldesoras.fr/src/hiir-1.50.zip"
    URL_HASH "SHA256=8a13724e8d698a40f53687d40466443879f6250abd2229097f3990b76e6855a6"
    DOWNLOAD_ONLY YES
)
CPMAddPackage(
    NAME flac
    GIT_TAG 1.5.0
    GITHUB_REPOSITORY xiph/flac
    OPTIONS
        "WITH_OGG OFF"
        "BUILD_PROGRAMS OFF"
        "BUILD_TESTING OFF"
        "BUILD_EXAMPLES OFF"
        "BUILD_DOCS OFF"
        "BUILD_CXXLIBS OFF"
        "INSTALL_MANPAGES OFF"
)
CPMAddPackage(
    NAME rtmidi
    GIT_TAG 518f8f1168afe745ea9660e11eeee044e74eeaa1
    GITHUB_REPOSITORY thestk/rtmidi
    DOWNLOAD_ONLY YES
)
CPMAddPackage(
    NAME lame
    URL "https://sourceforge.net/projects/lame/files/lame/3.100/lame-3.100.tar.gz/download"
    URL_HASH "SHA256=ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e"
    DOWNLOAD_ONLY YES
)
CPMAddPackage(
    NAME JUCE
    GIT_TAG 8.0.14
    GITHUB_REPOSITORY juce-framework/JUCE
    GIT_SHALLOW TRUE
)
