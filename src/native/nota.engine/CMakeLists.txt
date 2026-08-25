# SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
cmake_minimum_required(VERSION 3.24)

# OBJC/OBJCXX only exist on Apple; enable them conditionally so the Windows
# (MSVC/clang-cl) configure doesn't fail looking for an Objective-C compiler.
project(nota_engine LANGUAGES C CXX VERSION 0.1.0)
if(APPLE)
    enable_language(OBJC)
    enable_language(OBJCXX)
endif()

# --- Edition (AR-10) ---------------------------------------------------------
# free -> AGPLv3 open build; pro -> commercial closed build.
set(NOTA_EDITION "free" CACHE STRING "Build edition: free or pro")
set_property(CACHE NOTA_EDITION PROPERTY STRINGS free pro)
if(NOT NOTA_EDITION STREQUAL "free" AND NOT NOTA_EDITION STREQUAL "pro")
    message(FATAL_ERROR "NOTA_EDITION must be 'free' or 'pro' (got '${NOTA_EDITION}')")
endif()
message(STATUS "Nota edition: ${NOTA_EDITION}")

# --- macOS target (min macOS 13, universal arm64 + x86_64) -------------------
# 13.0 must match Info.plist LSMinimumSystemVersion and the launcher's
# -mmacosx-version-min in scripts/package-dmg.sh. The .NET 10 runtime floor is
# macOS 12, JUCE supports far older — so 13 is the app-wide minimum. Never raise
# this to the build host's OS: the dylib's minos gates dlopen on the user's Mac.
if(APPLE)
    if(NOT CMAKE_OSX_DEPLOYMENT_TARGET)
        set(CMAKE_OSX_DEPLOYMENT_TARGET "13.0" CACHE STRING "" FORCE)
    endif()
    if(NOT CMAKE_OSX_ARCHITECTURES)
        set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64" CACHE STRING "" FORCE)
    endif()
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
# The engine ships as a shared library, and the static nota_pluginhost (with JUCE
# compiled in) is linked whole-archive into it. Static objects aren't PIC by default,
# so on x86_64 a thread-local in JUCE's juce_graphics emits a local-exec TLS
# relocation (R_X86_64_TPOFF32) that can't live in a .so — the link fails. Build every
# target position-independent so those objects are .so-safe. (No-op on macOS/Windows.)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# --- Engine sources ----------------------------------------------------------
# Portable core (all platforms): engine, immutable graph, DSP, C ABI, decoders.
set(NOTA_ENGINE_SOURCES
    src/nota_engine_core.cpp
    src/nota_engine_track.cpp
    src/nota_engine_session.cpp
    src/nota_engine_device.cpp
    src/nota_engine_automation.cpp
    src/nota_engine_modulation.cpp
    src/nota_engine_audio.cpp
    src/nota_engine_rack.cpp
    src/Engine.cpp
    src/Engine_Automation.cpp
    src/Engine_Modulation.cpp
    src/Engine_Tracks.cpp
    src/Engine_Session.cpp
    src/Engine_Devices.cpp
    src/Engine_Rack.cpp
    src/Engine_Recording.cpp
    src/Engine_Render.cpp
    src/WarpStream.cpp
    src/TempoDetect.cpp
    src/AudioFile.cpp
    src/AudioBackendFactory.cpp   # platform-selected backend construction
)

# Platform device layer: macOS (CoreAudio/CoreMIDI) vs the shared non-Apple layer
# (miniaudio for audio + RtMidi for MIDI) used by both Windows and Linux.
if(APPLE)
    list(APPEND NOTA_ENGINE_SOURCES
        src/CoreAudioBackend.mm
        src/CoreAudioInput.mm
        src/AudioConfig.mm
        src/MidiInput.mm
        src/MidiConfig.mm
        src/GamepadInput.mm
    )
elseif(WIN32 OR (UNIX AND NOT APPLE))
    list(APPEND NOTA_ENGINE_SOURCES
        src/miniaudio_impl.cpp        # the single MINIAUDIO_IMPLEMENTATION TU
        src/MiniaudioBackend.cpp
        src/MiniaudioInput.cpp
        src/AudioConfig_miniaudio.cpp
        src/MidiInput_rtmidi.cpp
        src/MidiConfig_rtmidi.cpp
        src/NotaConfigFile.cpp
        src/GamepadInput_stub.cpp     # v1: macOS-only; reports zero pads
        vendor/rtmidi/RtMidi.cpp
    )
else()
    message(FATAL_ERROR "No Nota audio/MIDI device backend for this platform yet.")
endif()

add_library(nota_engine SHARED ${NOTA_ENGINE_SOURCES})

target_include_directories(nota_engine
    PUBLIC  ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/vendor/dr_libs
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/vendor           # Signalsmith Stretch, miniaudio, RtMidi
)

target_compile_definitions(nota_engine PRIVATE
    NOTA_EDITION_NAME="${NOTA_EDITION}"
)

# Output a stable name: libnota_engine.dylib / nota_engine.dll (consumed by P/Invoke).
set_target_properties(nota_engine PROPERTIES
    OUTPUT_NAME "nota_engine"
    MACOSX_RPATH ON
)

if(APPLE)
    target_link_libraries(nota_engine PRIVATE
        "-framework AudioToolbox"
        "-framework CoreAudio"
        "-framework CoreMIDI"
        "-framework AudioUnit"
        "-framework Foundation"
        "-framework GameController"
        "-framework CoreFoundation"
    )
elseif(WIN32)
    # RtMidi -> WinMM; miniaudio WASAPI -> ole32 (COM). __WINDOWS_MM__ selects
    # RtMidi's WinMM backend. NOMINMAX/WIN32_LEAN_AND_MEAN keep <windows.h> from
    # clobbering std::min/max and bloating the DSP TUs; _USE_MATH_DEFINES gives M_PI.
    target_compile_definitions(nota_engine PRIVATE
        __WINDOWS_MM__ NOMINMAX WIN32_LEAN_AND_MEAN _USE_MATH_DEFINES)
    target_link_libraries(nota_engine PRIVATE winmm ole32)
elseif(UNIX AND NOT APPLE)
    # RtMidi -> ALSA sequencer (__LINUX_ALSA__), which links libasound directly.
    # miniaudio runtime-links PulseAudio/ALSA via dlopen (see nota_miniaudio.h), so
    # only libasound (for RtMidi) + dl/pthread/m are needed at link time — the build
    # host needs libasound2-dev; libpulse is optional at runtime.
    target_compile_definitions(nota_engine PRIVATE __LINUX_ALSA__)
    target_link_libraries(nota_engine PRIVATE asound pthread dl m)
endif()

# --- Plugin hosting (M3) via JUCE -------------------------------------------
# JUCE lives ONLY in the pluginhost module; the core engine above stays
# JUCE-free (ARCHITECTURE.md § Plugin hosting). Free -> JUCE AGPLv3; Pro -> JUCE
# Commercial (licence flags TBD per NOTA_EDITION — see open questions).
add_subdirectory(vendor/JUCE)

# One source, two languages: ObjC++ on macOS (AU hosting + NSEvent key monitor,
# guarded by #if JUCE_MAC), plain C++ on Windows (VST3-only).
add_library(nota_pluginhost STATIC
    pluginhost/PluginHost.cpp
)
if(APPLE)
    set_source_files_properties(pluginhost/PluginHost.cpp PROPERTIES LANGUAGE OBJCXX)
endif()

target_include_directories(nota_pluginhost PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${CMAKE_CURRENT_SOURCE_DIR}/src   # core interfaces: Instrument.h, Device.h, PluginHostBridge.h
)

target_link_libraries(nota_pluginhost
    PRIVATE juce::juce_audio_processors juce::juce_gui_basics
    PUBLIC  juce::juce_recommended_config_flags
)

# Shared JUCE config: hosting-only, trimmed subsystems. VST3 everywhere; AU only
# on macOS (the format doesn't exist elsewhere).
set(NOTA_JUCE_DEFS
    JUCE_STANDALONE_APPLICATION=0
    JUCE_USE_CURL=0
    JUCE_WEB_BROWSER=0
    JUCE_MODAL_LOOPS_PERMITTED=0
    JUCE_PLUGINHOST_VST3=1
)
if(APPLE)
    list(APPEND NOTA_JUCE_DEFS JUCE_PLUGINHOST_AU=1)
endif()
target_compile_definitions(nota_pluginhost PRIVATE ${NOTA_JUCE_DEFS})

# AU hosting UI (AUGenericView) lives in CoreAudioKit. JUCE doesn't auto-link it
# here because JUCE_PLUGINHOST_* are set as compile defs rather than via its
# CMake module options.
if(APPLE)
    target_link_libraries(nota_pluginhost PRIVATE "-framework CoreAudioKit")
endif()

# Whole-archive so the C ABI symbols (referenced only by P/Invoke, not by the
# engine) survive into the shared library. Requires CMake 3.24+.
target_link_libraries(nota_engine PRIVATE
    $<LINK_LIBRARY:WHOLE_ARCHIVE,nota_pluginhost>
)

# Export ONLY the C ABI. On macOS the engine statically links JUCE and a hosted
# plugin usually links its own; with JUCE's weak symbols exported, dyld coalesces
# them across images and the plugin calls OUR JUCE with objects allocated by ITS
# JUCE — a heap double-free that crashes when a plugin builds its editor. An
# exported-symbols list hides every non-nota symbol. On Windows this hazard does
# not exist (each DLL keeps its own JUCE; nothing is exported unless marked
# __declspec(dllexport), which only the nota_* C ABI is). (ARCHITECTURE.md § Plugin hosting)
if(APPLE)
    target_link_options(nota_engine PRIVATE
        "LINKER:-exported_symbols_list,${CMAKE_CURRENT_SOURCE_DIR}/nota_exports.txt")
elseif(UNIX AND NOT APPLE)
    # Same hazard on ELF: with default-visibility JUCE symbols the dynamic loader can
    # interpose ours against a hosted plugin's own JUCE, corrupting the heap. A version
    # script exports ONLY the nota_* C ABI and forces everything else local — the exact
    # analogue of the macOS -exported_symbols_list.
    target_link_options(nota_engine PRIVATE
        "LINKER:--version-script=${CMAKE_CURRENT_SOURCE_DIR}/nota_exports.map")
endif()

# --- Out-of-process scan worker (M3-1) --------------------------------------
# Isolated child process: a crashing/hanging plugin can't take down the DAW.
add_executable(nota-scanworker pluginhost/scanworker/main.cpp)
target_link_libraries(nota-scanworker
    PRIVATE juce::juce_audio_processors
    PUBLIC  juce::juce_recommended_config_flags
)
target_compile_definitions(nota-scanworker PRIVATE ${NOTA_JUCE_DEFS})
if(APPLE)
    target_link_libraries(nota-scanworker PRIVATE "-framework CoreAudioKit")
endif()
