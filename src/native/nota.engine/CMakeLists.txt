# SPDX-License-Identifier: AGPL-3.0-only
cmake_minimum_required(VERSION 3.24)

# Engine version: single source of truth is the VERSION file next to this one,
# bumped by the release skill (independent of the app's repo-root VERSION).
file(STRINGS "${CMAKE_CURRENT_SOURCE_DIR}/VERSION" NOTA_ENGINE_VERSION LIMIT_COUNT 1)
string(STRIP "${NOTA_ENGINE_VERSION}" NOTA_ENGINE_VERSION)
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/VERSION")

# OBJC/OBJCXX only exist on Apple; enable them conditionally so the Windows
# (MSVC/clang-cl) configure doesn't fail looking for an Objective-C compiler.
project(nota_engine LANGUAGES C CXX VERSION ${NOTA_ENGINE_VERSION})

option(NOTA_UNIVERSAL_BINARY "Build macOS universal arm64;x86_64 binaries" ON)
option(NOTA_BUILD_C_ABI "Build optional nota_engine C ABI wrapper library" ON)
option(NOTA_BUILD_TESTS "Build native C++ unit tests" ON)
option(NOTA_ENABLE_LTO "Enable ThinLTO/LTCG in Release builds" ON)
option(NOTA_STRICT_WARNINGS_AS_ERRORS "Treat strict warnings as errors on modular targets" OFF)
option(NOTA_ENABLE_SANITIZERS "Enable AddressSanitizer and UndefinedBehaviorSanitizer on modular targets" OFF)

# compile_commands.json on every configure: feeds clangd/clang-tidy and the
# include-cleaner runs. Expose a symlink at src/native/nota.engine/compile_commands.json
# pointing to the active build dir (`build/`, `build-dev/`, `build-asan/`, etc.).
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)
if(CMAKE_EXPORT_COMPILE_COMMANDS AND NOT WIN32)
    file(CREATE_LINK
        "${CMAKE_BINARY_DIR}/compile_commands.json"
        "${CMAKE_CURRENT_SOURCE_DIR}/compile_commands.json"
        RESULT _nota_cc_link_res
        SYMBOLIC
    )
endif()

# Reuse already-fetched CPM dependency sources when configuring sibling preset dirs (e.g. build-dev).
foreach(_nota_dep IN ITEMS dr_libs flac hiir juce lame miniaudio nlohmann_json pffft rtmidi signalsmith_linear signalsmith_stretch)
    string(TOUPPER "${_nota_dep}" _nota_dep_upper)
    if(NOT DEFINED FETCHCONTENT_SOURCE_DIR_${_nota_dep_upper} AND EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/build/_deps/${_nota_dep}-src")
        set(FETCHCONTENT_SOURCE_DIR_${_nota_dep_upper} "${CMAKE_CURRENT_SOURCE_DIR}/build/_deps/${_nota_dep}-src" CACHE PATH "" FORCE)
    endif()
endforeach()

# Architecture + deployment-target defaults come FIRST, before the CPM
# include below: every CPM-configured subbuild (flac, JUCE, …) inherits them,
# otherwise their static libs come out single-arch and the universal link fails.
if(APPLE)
    if(NOT CMAKE_OSX_DEPLOYMENT_TARGET)
        set(CMAKE_OSX_DEPLOYMENT_TARGET "12.0" CACHE STRING "" FORCE)
    endif()
    if(NOTA_UNIVERSAL_BINARY AND NOT CMAKE_OSX_ARCHITECTURES)
        set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64" CACHE STRING "" FORCE)
    endif()
endif()

include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/NotaModuleHelpers.cmake")

set(CPM_VERSION "v0.43.2")
set(CPM_SHA256 "49a3bef91ceb65bb66d57255e12d1ffd22abc2f6408fa9fe4534c544a2f232aa")
set(CPM_FILE "${CMAKE_BINARY_DIR}/_cpm/CPM.cmake")
if(NOT EXISTS "${CPM_FILE}" AND EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/build/_cpm/CPM.cmake")
    file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_cpm")
    file(COPY_FILE "${CMAKE_CURRENT_SOURCE_DIR}/build/_cpm/CPM.cmake" "${CPM_FILE}")
endif()
if(NOT EXISTS "${CPM_FILE}")
    file(DOWNLOAD
        "https://github.com/cpm-cmake/CPM.cmake/releases/download/${CPM_VERSION}/CPM.cmake"
        "${CPM_FILE}"
        EXPECTED_HASH SHA256=${CPM_SHA256}
        STATUS _cpm_status)
    list(GET _cpm_status 0 _cpm_code)
    if(NOT _cpm_code EQUAL 0)
        message(FATAL_ERROR
            "CPM download failed (network needed on fresh configure). "
            "Set CPM_SOURCE_CACHE or -DFETCHCONTENT_SOURCE_DIR_<NAME>=<dir>. "
            "Status: ${_cpm_status}")
    endif()
endif()
include("${CPM_FILE}")

# The engine ships as a shared library, and fetched static deps (FLAC, lame)
# plus the static nota_pluginhost (with JUCE compiled in) link into it. Static
# objects aren't PIC by default, so on Linux the .so link fails on non-PIC
# relocations (R_X86_64_PC32 in libFLAC, local-exec TLS in JUCE). Build every
# target position-independent so those objects are .so-safe (no-op on
# macOS/Windows). Must precede the CPM include: subbuilds read this default
# when they configure, not when they build.
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/dependencies.cmake")
if(APPLE)
    enable_language(OBJC)
    enable_language(OBJCXX)
endif()

# --- macOS target (min macOS 12, universal arm64 + x86_64) -------------------
# 12.0 must match Info.plist LSMinimumSystemVersion (scripts/bundle-mac.sh,
# scripts/package-dmg.sh). The bundled .NET 10 runtime and the launcher are built
# for macOS 12, Avalonia / Skia / JUCE for older — so 12 is the app-wide minimum
# (#684; Microsoft lists only 15+ as supported for .NET 10: 12–14 are ours to
# test). Never raise this to the build host's OS: the dylib's minos gates dlopen
# on the user's Mac. Anything newer than 12 goes behind @available.
# (Arch/target defaults themselves are set above the CPM include.)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

# --- Engine sources ----------------------------------------------------------
# Portable core (all platforms): engine, immutable graph, DSP, C ABI, decoders.
# domain subdirectories (abi/, dsp/, devices/, audio_io/, media/).
set(LAME_ROOT "${lame_SOURCE_DIR}")
add_subdirectory(cmake/lame lame-build)

add_subdirectory(modules/core)
add_subdirectory(modules/math)
add_subdirectory(modules/media)
add_subdirectory(modules/dsp)
add_subdirectory(modules/devices)
add_subdirectory(modules/pluginscanner)
add_subdirectory(modules/pluginhost)
add_subdirectory(modules/io)
add_subdirectory(modules/engine)

if(NOTA_BUILD_C_ABI)
    add_subdirectory(modules/c_abi)
endif()

enable_testing()
if(NOTA_BUILD_TESTS)
    add_subdirectory(tests)
endif()
