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

# compile_commands.json on every configure: feeds clangd/clang-tidy and the
# include-cleaner runs. Harmless where nothing reads it.
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Architecture + deployment-target defaults come FIRST, before the CPM
# include below: every CPM-configured subbuild (flac, JUCE, …) inherits them,
# otherwise their static libs come out single-arch and the universal link fails.
if(APPLE)
    if(NOT CMAKE_OSX_DEPLOYMENT_TARGET)
        set(CMAKE_OSX_DEPLOYMENT_TARGET "13.0" CACHE STRING "" FORCE)
    endif()
    if(NOT CMAKE_OSX_ARCHITECTURES)
        set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64" CACHE STRING "" FORCE)
    endif()
endif()

set(CPM_VERSION "v0.43.2")
set(CPM_SHA256 "49a3bef91ceb65bb66d57255e12d1ffd22abc2f6408fa9fe4534c544a2f232aa")
set(CPM_FILE "${CMAKE_BINARY_DIR}/_cpm/CPM.cmake")
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

# --- macOS target (min macOS 13, universal arm64 + x86_64) -------------------
# 13.0 must match Info.plist LSMinimumSystemVersion and the launcher's
# -mmacosx-version-min in scripts/package-dmg.sh. The .NET 10 runtime floor is
# macOS 12, JUCE supports far older — so 13 is the app-wide minimum. Never raise
# this to the build host's OS: the dylib's minos gates dlopen on the user's Mac.
# (Arch/target defaults themselves are set above the CPM include.)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

# --- Engine sources ----------------------------------------------------------
# Portable core (all platforms): engine, immutable graph, DSP, C ABI, decoders.
# domain subdirectories (abi/, dsp/, devices/, audio_io/, media/).
set(NOTA_ENGINE_SOURCES
    src/abi/nota_engine_core.cpp
    src/abi/nota_engine_track.cpp
    src/abi/nota_engine_session.cpp
    src/abi/nota_engine_device.cpp
    src/abi/nota_engine_automation.cpp
    src/abi/nota_engine_modulation.cpp
    src/abi/nota_engine_audio.cpp
    src/abi/nota_engine_rack.cpp
    src/abi/nota_engine_import.cpp
    src/abi/nota_engine_audition.cpp
    src/abi/nota_encoder.cpp
    src/engine/Engine.cpp
    src/engine/Engine_Automation.cpp
    src/engine/Engine_Modulation.cpp
    src/engine/Engine_Tracks.cpp
    src/engine/Engine_Session.cpp
    src/engine/Engine_Devices.cpp
    src/engine/Engine_Rack.cpp
    src/engine/Engine_Recording.cpp
    src/engine/Engine_Render.cpp
    src/engine/Audition.cpp
    src/engine/AudioThreadPool.cpp  # fork-join pool
    src/media/WarpStream.cpp
    src/media/TempoDetect.cpp
    src/media/SampleAnalysis.cpp
    src/media/AudioFile.cpp
    src/media/AudioImport.cpp
    src/media/SampleBuffer.cpp  # peak-table math (.h/.cpp)
    src/audio_io/AudioBackendFactory.cpp   # platform-selected backend construction
    src/audio_io/NotaConfigFile.cpp        # per-user data dir + atomic file IO (all platforms)
    src/audio_io/ConfigJson.cpp  # audio.json / midi.json via CPM nlohmann/json
    src/audio_io/MpeInput.cpp  # MPE routing (.h/.cpp)
    src/devices/Device.cpp  # interface TU (vtable anchor)
    src/devices/Instrument.cpp  # ctor + controller routing
    src/devices/MidiDevice.cpp  # interface TU (vtable anchor)
    src/devices/DeviceFactory.cpp  # built-in factories
    src/devices/BuiltinDevices.cpp  # chain-device factory
    src/devices/racks/RackCore.cpp  # rack core
    src/devices/racks/RackDevice.cpp  # effect rack
    src/devices/racks/RackInstrument.cpp  # instrument rack
    src/devices/racks/DrumRack.cpp  # drum rack
    src/devices/midi_fx/Arpeggiator.cpp
    src/devices/midi_fx/MidiChord.cpp
    src/devices/midi_fx/MidiScale.cpp
    src/devices/midi_fx/MidiNoteLength.cpp
    src/devices/midi_fx/MidiVelocity.cpp
    src/devices/midi_fx/MidiRandom.cpp
    src/engine/Graph.cpp
    src/engine/Track.cpp
    src/engine/Transport.cpp
    src/engine/SessionPlayer.cpp
    src/engine/Automation.cpp
    src/engine/Modulation.cpp
    src/engine/MidiClip.cpp
    src/engine/MidiController.cpp
    src/engine/NoteExpression.cpp
    src/devices/effects/Eq.cpp
    src/devices/effects/Eq3.cpp
    src/devices/effects/DynamicEq.cpp
    src/devices/effects/AutoFilter.cpp
    src/devices/effects/Compressor.cpp
    src/devices/effects/Ceiling.cpp
    src/devices/effects/Prism.cpp
    src/devices/effects/AutoGain.cpp
    src/devices/effects/Delay.cpp
    src/devices/effects/Reverb.cpp
    src/devices/effects/ChamberIrSynth.cpp
    src/devices/effects/Chamber.cpp
    src/devices/effects/BeatRepeat.cpp
    src/devices/effects/Strata.cpp
    src/devices/effects/Chorus.cpp
    src/devices/effects/Flanger.cpp
    src/devices/effects/Phaser.cpp
    src/devices/effects/AutoPan.cpp
    src/devices/effects/Shutter.cpp
    src/devices/effects/Utility.cpp
    src/devices/effects/Lens.cpp
    src/devices/effects/Vintage.cpp
    src/devices/effects/Amp.cpp
    src/devices/effects/Crush.cpp
    src/devices/effects/Forge.cpp
    src/devices/effects/AutoShift.cpp
    src/devices/effects/Vocoder.cpp
    src/devices/instruments/Synth.cpp
    src/devices/instruments/VoltSynth.cpp
    src/devices/instruments/BassSynth.cpp
    src/devices/instruments/Monolith.cpp
    src/devices/instruments/Pentad.cpp
    src/devices/instruments/Consort.cpp
    src/devices/instruments/WavetableSynth.cpp
    src/devices/instruments/OperatorSynth.cpp
    src/devices/instruments/FluxSynth.cpp
    src/devices/instruments/PendulumSynth.cpp
    src/devices/instruments/PhysicalSynth.cpp
    src/devices/instruments/Keys.cpp
    src/devices/instruments/Sampler.cpp
    src/devices/instruments/GrainSynth.cpp
    src/devices/instruments/RhythmVoiceDsp.cpp  # voice engine
    src/devices/instruments/RhythmMachine.cpp
    src/devices/instruments/MosaicProgram.cpp  # program model
    src/devices/instruments/Mosaic.cpp
    src/dsp/core/SilenceDetector.cpp  # hysteresis silence detection
    src/dsp/core/Parameter.cpp  # param mappings + smoothing
    src/dsp/primitives/SvfFilter.cpp  # TPT SVF + LR4
    src/dsp/primitives/BiquadCascade.cpp  # DF2T cascade + RBJ
    src/dsp/primitives/LadderFilter.cpp  # Moog ladder
    src/dsp/primitives/DelayLine.cpp  # power-of-two delay
    src/dsp/primitives/Oscillator.cpp  # polyBLEP + LFO
    src/dsp/primitives/Envelope.cpp  # ADSR/AR/follower
    src/dsp/primitives/Oversampler.cpp  # HIIR 2x/4x/8x
    src/dsp/primitives/Convolution.cpp  # partitioned FFT conv
    src/dsp/primitives/TruePeakMeter.cpp  # 4x SIMD true-peak
    src/dsp/primitives/CompensationDelay.cpp  # PDC + isIdle
    src/dsp/poly/SynthVoice.cpp  # voice state
    src/dsp/poly/PolyVoiceManager.cpp  # voice allocation
)

# Platform device layer: macOS (CoreAudio/CoreMIDI) vs the shared non-Apple layer
# (miniaudio for audio + RtMidi for MIDI) used by both Windows and Linux.
if(APPLE)
    list(APPEND NOTA_ENGINE_SOURCES
        src/audio_io/macos/CoreAudioBackend.mm
        src/audio_io/macos/CoreAudioInput.mm
        src/audio_io/macos/AudioConfig.mm
        src/audio_io/macos/MidiInput.mm
        src/audio_io/macos/MidiConfig.mm
        src/audio_io/macos/GamepadInput.mm
    )
elseif(WIN32 OR (UNIX AND NOT APPLE))
    list(APPEND NOTA_ENGINE_SOURCES
        src/audio_io/desktop/miniaudio_impl.cpp        # the single MINIAUDIO_IMPLEMENTATION TU
        src/audio_io/desktop/MiniaudioBackend.cpp
        src/audio_io/desktop/MiniaudioInput.cpp
        src/audio_io/desktop/AudioConfig_miniaudio.cpp
        src/audio_io/desktop/MidiInput_rtmidi.cpp
        src/audio_io/desktop/MidiConfig_rtmidi.cpp
        src/audio_io/desktop/GamepadInput_stub.cpp     # v1: macOS-only; reports zero pads
        ${rtmidi_SOURCE_DIR}/RtMidi.cpp
    )
else()
    message(FATAL_ERROR "No Nota audio/MIDI device backend for this platform yet.")
endif()

add_library(nota_engine SHARED ${NOTA_ENGINE_SOURCES})

target_include_directories(nota_engine
    PUBLIC  ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/abi  # C ABI TUs
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/audio_io  # backends/config
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/audio_io/macos
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/audio_io/desktop
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/media  # offline media
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/devices  # device interfaces
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/devices/racks
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/devices/effects
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/devices/instruments
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/devices/midi_fx
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src/engine  # engine core
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/pluginhost/bridge
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/pluginhost       # bridge/ relative includes (mirrors pluginhost target)
)

# Third-party headers as system includes: no warnings from foreign code,
# and fetched copies shadow the snapshots they replace.
target_include_directories(nota_engine SYSTEM PRIVATE
    ${dr_libs_SOURCE_DIR}
    ${miniaudio_SOURCE_DIR}
    ${signalsmith_linear_SOURCE_DIR}/include
    ${signalsmith_stretch_SOURCE_DIR}/include
    ${hiir_SOURCE_DIR}
    ${rtmidi_SOURCE_DIR}
)

target_compile_definitions(nota_engine PRIVATE
    NOTA_VERSION_STRING="${PROJECT_VERSION}")

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

# --- Audio codecs for export (FLAC encode, MP3 encode) ----------------------
# libFLAC 1.5 (BSD-3-Clause): fetched xiph/flac 1.5.0, built by its own upstream
# CMake as a static library — CMake is FLAC's preferred MSVC path since 1.5.0.
# Programs, tests, examples, docs, C++ bindings and Ogg container support are
# all off (OPTIONS in cmake/dependencies.cmake); we write native FLAC only.
# See LICENSES/third-party.md.
# (Build toggles live as OPTIONS on the CPM block in cmake/dependencies.cmake.)

# LAME 3.100 (LGPL-2.0-or-later, note 7): fetched tarball + our thin wrapper
# in cmake/lame/ (upstream ships no CMakeLists). Pure C, no nasm/SSE.
set(LAME_ROOT "${lame_SOURCE_DIR}")
add_subdirectory(cmake/lame lame-build)

target_link_libraries(nota_engine PRIVATE FLAC lame nlohmann_json::nlohmann_json)

# --- Plugin hosting (M3) via JUCE -------------------------------------------
# JUCE lives ONLY in the pluginhost module; the core engine above stays
# JUCE-free (ARCHITECTURE.md § Plugin hosting). JUCE is used here under the AGPLv3.

# One source, two languages: ObjC++ on macOS (AU hosting + NSEvent key monitor,
# guarded by #if JUCE_MAC), plain C++ elsewhere (VST3-only).
# hosting split (PluginHost/PluginCommon/NotaPlayHead/
# PluginEditorWindow/PluginInstrument/PluginEffect) + scanner ABI
# (abi_pluginscanner) and coordinator (PluginCatalog/OutOfProcessScanner).
add_library(nota_pluginhost STATIC
    pluginhost/hosting/PluginHost.cpp
    pluginhost/hosting/PluginCommon.cpp
    pluginhost/hosting/NotaPlayHead.cpp
    pluginhost/hosting/PluginEditorWindow.cpp
    pluginhost/hosting/PluginInstrument.cpp
    pluginhost/hosting/PluginEffect.cpp
    pluginhost/scanner/abi_pluginscanner.cpp
    pluginhost/scanner/PluginCatalog.cpp  # thread-safe catalog
    pluginhost/scanner/OutOfProcessScanner.cpp  # scan coordinator
)
if(APPLE)
    set_source_files_properties(
        pluginhost/hosting/PluginEditorWindow.cpp
        pluginhost/hosting/PluginHost.cpp
        PROPERTIES LANGUAGE OBJCXX)
endif()

target_include_directories(nota_pluginhost PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${CMAKE_CURRENT_SOURCE_DIR}/src   # core interfaces: Instrument.h, Device.h, PluginHostBridge.h
    ${CMAKE_CURRENT_SOURCE_DIR}/src/audio_io  # NotaConfigFile.h
    ${CMAKE_CURRENT_SOURCE_DIR}/src/devices  # Device.h/Instrument.h
    ${CMAKE_CURRENT_SOURCE_DIR}/src/engine  # TransportInfo.h/CommandQueue.h
    ${CMAKE_CURRENT_SOURCE_DIR}/pluginhost  # bridge/ + scanner/ relative includes
)

target_link_libraries(nota_pluginhost
    PRIVATE juce::juce_audio_processors juce::juce_gui_basics
    PUBLIC  juce::juce_recommended_config_flags
)

# Plugin scanner separation
# Source-level separation with single JUCE compilation: JUCE module targets
# propagate SOURCES/OBJECTS into every STATIC dependent, so a second STATIC lib
# linking juce_audio_processors would compile a second copy of JUCE (~11k
# duplicate symbols at the nota_engine link). The scanner therefore builds its
# two translation units as part of nota_pluginhost — the single JUCE owner —
# while keeping its own INTERFACE target and directory.
# Enforcement is by construction, verified by grep (see below):
#   pluginhost/scanner/* includes only NotaConfigFile.h and JUCE —
#   never Engine.h, Track.h, Graph.h, Device.h or Instrument.h.
# Full CMake STATIC split either keeps this
# model or makes the scanner JUCE-free (stdlib XML + fork/exec).
add_library(nota_pluginscanner INTERFACE)
target_include_directories(nota_pluginscanner INTERFACE
    ${CMAKE_CURRENT_SOURCE_DIR}/pluginhost
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
# (nota_pluginscanner is INTERFACE in this step — its TUs build inside
# nota_pluginhost — so only nota_pluginhost is archived.)
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

# --- Out-of-process scan worker (027: pluginhost/scanner/worker/) ------------
# Isolated child process: a crashing/hanging plugin can't take down the DAW.
add_executable(nota-scanworker
    pluginhost/scanner/worker/PluginScannerWorker.cpp
    pluginhost/scanner/worker/main.cpp
)
target_link_libraries(nota-scanworker
    PRIVATE juce::juce_audio_processors
    PUBLIC  juce::juce_recommended_config_flags
)
target_compile_definitions(nota-scanworker PRIVATE ${NOTA_JUCE_DEFS})
if(APPLE)
    target_link_libraries(nota-scanworker PRIVATE "-framework CoreAudioKit")
endif()

# --- Nota Pentad as a standalone VST3 (debugging in third-party hosts) -------
# Off by default: the app runs Pentad in-process; this wraps the same DSP
# (devices/instruments/Pentad.h/.cpp) in a JUCE plugin with the generic editor. Configure with
# -DNOTA_BUILD_PENTAD_VST3=ON and build the `NotaPentad_VST3` target; the bundle lands
# in <build>/NotaPentad_artefacts/<config>/VST3/.
option(NOTA_BUILD_PENTAD_VST3 "Build Nota Pentad as a standalone VST3 plugin" OFF)
if(NOTA_BUILD_PENTAD_VST3)
    juce_add_plugin(NotaPentad
        COMPANY_NAME "Nota"
        PRODUCT_NAME "Nota Pentad"
        BUNDLE_ID "app.nota.pentad"
        PLUGIN_MANUFACTURER_CODE Nota
        PLUGIN_CODE Pntd
        FORMATS VST3
        IS_SYNTH TRUE
        NEEDS_MIDI_INPUT TRUE
        NEEDS_MIDI_OUTPUT FALSE
        VST3_CATEGORIES Instrument Synth
        COPY_PLUGIN_AFTER_BUILD FALSE)
    target_sources(NotaPentad PRIVATE plugins/pentad/PentadPlugin.cpp)
    # Pentad DSP implementation + its TU-level dependencies (the plugin links
    # no engine library; keep this list in sync with what Pentad.cpp uses).
    target_sources(NotaPentad PRIVATE
        src/devices/instruments/Pentad.cpp
        src/devices/Instrument.cpp
        src/engine/NoteExpression.cpp)
    target_include_directories(NotaPentad PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
        ${CMAKE_CURRENT_SOURCE_DIR}/src/devices      # Instrument.h
        ${CMAKE_CURRENT_SOURCE_DIR}/src/engine)      # NoteExpression.h
    target_include_directories(NotaPentad SYSTEM PRIVATE
        ${hiir_SOURCE_DIR})                          # Downsampler2xFpu.h (Pentad oversampling)
    target_compile_definitions(NotaPentad PUBLIC
        JUCE_WEB_BROWSER=0 JUCE_USE_CURL=0 JUCE_VST3_CAN_REPLACE_VST2=0 JUCE_DISPLAY_SPLASH_SCREEN=0)
    target_link_libraries(NotaPentad
        PRIVATE juce::juce_audio_utils
        PUBLIC  juce::juce_recommended_config_flags juce::juce_recommended_warning_flags)
endif()
