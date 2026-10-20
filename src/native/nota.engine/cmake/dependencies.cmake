# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# CMake dependencies via CPM (general infra, not per-library vendoring).
# Nothing here lands in the repo: CPM fetches each package into the build dir
# (_deps) — or CPM_SOURCE_CACHE when set (CI caches it) — from a pinned
# URL + URL_HASH or a commit SHA below (never a movable tag; the release it
# matches is in the comment). To add a dependency, append one CPMAddPackage
# block and its row in LICENSES/third-party.md.

CPMAddPackage(
    NAME nlohmann_json
    VERSION 3.12.0
    URL "https://github.com/nlohmann/json/releases/download/v3.12.0/json.tar.xz"
    URL_HASH "SHA256=42f6e95cad6ec532fd372391373363b62a14af6d771056dbfc86160e6dfff7aa"
    OPTIONS
        "JSON_BuildTests OFF"
        "JSON_Install OFF"
)
CPMAddPackage(
    NAME dr_libs
    URL "https://github.com/mackron/dr_libs/archive/cd99e2cca5ccb48f8d000f1cd844424dcdbbaf59.tar.gz"
    URL_HASH "SHA256=f65ec7dff6e34e74391f12d322b240d7567acb0c4017f1d47502eb053dffe32a"
    DOWNLOAD_ONLY YES
)
CPMAddPackage(
    NAME miniaudio
    URL "https://github.com/mackron/miniaudio/archive/refs/tags/0.11.25.tar.gz"
    URL_HASH "SHA256=b900edcffe979816e2560a0580b9b1216d674b4f17fbadeca8f777a7f8ab0274"
    DOWNLOAD_ONLY YES
)
CPMAddPackage(
    NAME signalsmith_linear
    URL "https://github.com/Signalsmith-Audio/linear/archive/7f53cdd1ccd52b409dacf2af24e7ff838c5580cd.tar.gz"
    URL_HASH "SHA256=578acaef191a3e7aa0330fc699cc0b8f3b56f0316f45bf553f57d7bdd618f12b"
    DOWNLOAD_ONLY YES
)
CPMAddPackage(
    NAME signalsmith_stretch
    URL "https://github.com/Signalsmith-Audio/signalsmith-stretch/archive/57b93f4e9206a089a45387eaa39bdc9f310d3308.tar.gz"
    URL_HASH "SHA256=ad02e24334438b203e81d44f6c9906f3c6773e90a4ea923bb3e73d15697187d6"
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
    URL "https://github.com/xiph/flac/archive/refs/tags/1.5.0.tar.gz"
    URL_HASH "SHA256=aea54ed186ad07a34750399cb27fc216a2b62d0ffcd6dc2e3064a3518c3146f8"
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
    URL "https://github.com/thestk/rtmidi/archive/518f8f1168afe745ea9660e11eeee044e74eeaa1.tar.gz"
    URL_HASH "SHA256=a8379f8fdf8f65407b2b4e17d695192f539fe159681e1bcfadf2dd190e2c353c"
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
    URL "https://github.com/juce-framework/JUCE/archive/refs/tags/8.0.14.tar.gz"
    URL_HASH "SHA256=ceb18e4ac9ab5ea71f3f20240d5852707767a1789ab43d06656a296da9e62f3e"
)
