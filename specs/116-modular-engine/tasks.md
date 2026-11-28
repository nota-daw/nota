---
description: "Dependency-ordered tasks for nota-daw/nota#1116 (specs/116-modular-engine)"
---
<!-- SPDX-License-Identifier: AGPL-3.0-only -->

# Tasks: Modular Native Engine Architecture (`#1116`)

**Input**: Design documents from `/specs/116-modular-engine/`
**Prerequisites**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/` (`module-dag.md`, `cpp-engine-api.md`, `registries-and-io.md`), `quickstart.md`

## Phase 1: Setup, CMake Skeleton, `clangd` Tooling & Pentad VST3 Removal [US1]

- [x] T001 [US1] Add `.cache/` ignore entry to `.gitignore` and create root `.clangd` configured for single-arch `dev` `compile_commands.json` (`UnusedIncludes: Strict`, `MissingIncludes: Strict`, CPM `_deps/` suppression, `ClangTidy`)
- [x] T002 [P] [US1] Create `src/native/nota.engine/CMakePresets.json` (`dev`, `dev-cpp`, `release`) and add `NOTA_UNIVERSAL_BINARY` + `NOTA_BUILD_C_ABI` + `NOTA_BUILD_TESTS` options to `src/native/nota.engine/CMakeLists.txt`
- [x] T003 [P] [US1] Create `src/native/nota.engine/cmake/NotaModuleHelpers.cmake` (`nota_add_shared_module`, hidden default visibility, `*_EXPORTS` macros, `@loader_path` / `$ORIGIN` `RPATH`, `PUBLIC include` vs `PRIVATE src` discipline, and standalone header compile verification helper)
- [x] T004 [US1] Remove test `Pentad` VST3 wrapper (`src/native/nota.engine/plugins/pentad/`, `NOTA_BUILD_PENTAD_VST3` block in `src/native/nota.engine/CMakeLists.txt`, and `Pentad VST3` section in `BUILD.md`) while keeping built-in `Pentad` (`kind = 14`) and `nota-render-probe`

## Phase 2: `modules/math/` (`nota::math` → `nota_math` SHARED DLL) [US1]

- [x] T005 [US1] Create `modules/math/` CMake config and public headers (`Export.h`, `CpuFeatures.h`, `SimdVec.h`, `BufferOps.h`, `StereoOps.h`, `ComplexOps.h`, `FastMath.h`, `Random.h`, `Fft.h`) keeping hot vector kernels header-inline
- [x] T006 [US1] Implement `RealFft` Pimpl (`src/FftAccelerate.cpp`, `src/FftPffft.cpp`), `src/CpuFeatures.cpp`, and `src/Windowing.cpp` (extracting Kaiser sinc / Hann / Blackman from legacy sources)
- [x] T007 [US1] Wire legacy consumers (`dsp/math/*` compatibility shims or direct callers) onto `nota::math` and add `tests/test_math.cpp` verifying `Vec4f`, `BufferOps`, `StereoOps`, `FastMath`, and `RealFft` parity via `ctest`
- [x] T007b [US1] Add hard C++20 code-safety gate: `nota_enable_strict_cxx` (`-Wall -Wextra -Wpedantic -Wconversion -Wsign-conversion -Wold-style-cast -Wshadow -Wnon-virtual-dtor -Wnull-dereference -Werror` / MSVC `/W4 /permissive- /WX`) applied to all `modules/*` and `tests/*`, `NOTA_ENABLE_SANITIZERS` + `dev-asan` preset (ASan + UBSan), expanded `.clangd` `modernize-*`/`cppcoreguidelines-*` checks, `std::bit_cast`/`[[nodiscard]]`/RAII without raw `new`/`delete` in `modules/math/` and `modules/media/`

## Phase 3: `modules/media/` (`nota::media` → `nota_media` SHARED DLL) [US1]

- [x] T008 [US1] Create `modules/media/` public contracts (`Export.h`, `AudioCodecTypes.h`, `IAudioDecoder.h`, `IAudioEncoder.h`, `AudioCodecRegistry.h`, `SampleBuffer.h`, `AudioFile.h`, `AudioImport.h`, `AudioSource.h`, `StreamingAudioSource.h`, `SampleAnalysis.h`, `TempoDetect.h`, `Warp.h`)
- [x] T009 [US1] Implement central codec registry & built-in codecs (`src/AudioCodecRegistry.cpp`, `src/codecs/PathUtils.{h,cpp}`, `WavCodec.cpp`, `FlacCodec.cpp`, `Mp3Codec.cpp`, `PcmDitherConverter.h`, `BuiltinCodecs.cpp`) keeping `dr_libs`, `FLAC`, `lame`, and `signalsmith_*` strictly `PRIVATE`
- [x] T010 [US1] Migrate `AudioFile`, `AudioImport`, `StreamingAudioSource`, `SampleBuffer`, `Warp`, and `nota_encoder` to `AudioCodecRegistry`, and verify `tests/test_media.cpp` in `ctest`

## Phase 4: `modules/dsp/` (`nota::dsp` → `nota_dsp` SHARED DLL) [US1]

- [x] T011 [US1] Create `modules/core/` (`nota_core`: `ConfigFile`, `EditorKeys`) for non-DSP process-wide runtime support and `modules/dsp/` headers (`rt/` types incl. `OutputRouting.h`, `SpscRingBuffer.h`, `SnapshotHistory.h`, `ScratchArena.h`, `AudioSpan.h`, `PlanarStereoBuffer.h`, `TransportInfo.h`; `dsp/` types incl. `Parameter.h`, `SilenceGate.h`, `DeviceTelemetry.h`, `MidiTypes.h`, `AutomationTypes.h`, `primitives/`, `poly/`)
- [x] T012 [US1] Define JUCE-free device contracts, capability interfaces (`ISlotAnnotationDevice`, `ISampleInstrument`, `IMosaicInstrument`, `IWavetableInstrument`, `IRhythmInstrument`, `IRack`), `PluginContracts.h` (`PluginFormatKind`, `PluginFormatDescriptor`, `PluginDescriptor`, `IPluginHost`, `IPluginCatalog`, `IPluginScanner`), and `DeviceRegistry.h`
- [x] T013 [US1] Move all shared global atomics/counters/factories (`SilenceGate`, `Device`, `Instrument`, `DeviceRegistry` in `nota_dsp`; `ConfigFile`, `EditorKeys` in `nota_core`) into exported `.cpp` definitions

## Phase 5: `modules/devices/` (`nota::devices` → `nota_devices` SHARED DLL) [US2]

- [x] T014 [P] [US2] Reorganize all 27 built-in audio effects into dedicated `modules/devices/src/effects/<name>/` directories (splitting large effects like `chamber`, `color`, `lens`, `eq`, etc. into `*Params.h`, `*Dsp.*`, `*Device.*`)
- [x] T015 [P] [US2] Reorganize all 18 built-in instruments into dedicated `modules/devices/src/instruments/<name>/` directories (preserving `Pentad` `kind = 14`; decoupling `RhythmMachine` via `DeviceRegistry::createChainEffect`)
- [x] T016 [P] [US2] Reorganize all 6 MIDI FX (`modules/devices/src/midi_fx/<name>/`) and 4 racks (`modules/devices/src/racks/`) into per-device subdirectories, eliminating concrete instrument `#include` cascades in `RackCore` via `DeviceRegistry` and `IPluginHost`
- [x] T017 [US2] Implement `modules/devices/src/Registration.cpp` (`registerBuiltinDevices()`), remove legacy `DeviceFactory.cpp` / `BuiltinDevices.cpp`, and add `tests/test_devices.cpp` covering creation by `kind` & `slug`, defaults, cloning, and idle/auto-sleep

## Phase 6: `modules/pluginscanner/` (`nota::pluginscanner` + `nota-scanworker` + `nota-scanner-selftest`) [US4]

- [x] T018 [US4] Create `modules/pluginscanner/` public interfaces (`IPluginFormatBackend`, `PluginFormatRegistry`, `PluginScannerModule.h`) and CMake-generated symbol export filters hiding all `juce::*` symbols
- [x] T019 [US4] Implement format-neutral wire codec (`wire/ScanWireCodec.{h,cpp}`), `OutOfProcessScanner.cpp`, `PluginCatalogImpl.cpp` (with lossless legacy `plugins.xml` migration and per-backend existence checks), `PluginScannerImpl.cpp`, `backends/juce/JucePluginFormatBackend.cpp` (VST3 + AU, no GUI), and `nota-scanworker` + `nota-scanner-selftest`
- [x] T020 [US4] Add `tests/test_pluginscanner.cpp` (including JUCE-free mock `IPluginFormatBackend` simulating future CLAP/LV2/VST2 formats) and verify `nota-scanner-selftest` + zero exported `juce::*` symbols

## Phase 7: `modules/pluginhost/` (`nota::pluginhost` + `nota-render-probe`) [US4]

- [x] T021 [US4] Create `modules/pluginhost/` (`PluginHostModule.h`, internal `IPluginFormatHost` dispatch, `PluginHostImpl`, `PluginLoader`, `PluginInstrument`, `PluginEffect`, `NotaPlayHead`, `EditorSnapshot`, `PluginEditorWindow`) injected with `std::shared_ptr<IPluginCatalog>` and zero scanner sources
- [x] T022 [US4] Extract macOS Objective-C++ bridges into `*_mac.mm` files, wire CMake-generated symbol export filters hiding all `juce::*` symbols, preserve `nota-render-probe`, and verify zero dynamic `juce::*` exports

## Phase 8: `modules/io/` (`nota::io` → `nota_io` SHARED DLL) [US5]

- [x] T023 [P] [US5] Implement `modules/io/` `audio/` subsystem (`AudioIoTypes.h`, `IAudioDriver.h`, `IAudioOutputStream.h`, `IAudioInputStream.h`, `AudioDriverRegistry.{h,cpp}`, `OutputRouter.h`, `AudioConfigStore.{h,cpp}`, `CoreAudioDriver` + `*_mac.mm`, `MiniaudioDriver`, `AsioDriver`/`AsioSession` with injected `AsioDriverApi`, and `NullAudioDriver`)
- [x] T024 [P] [US5] Implement `modules/io/` `midi/` (`IMidiDriver`, `IMidiInput`, `IMidiOutput`, `MidiDriverRegistry`, `MidiParser`, `MpeInput`, `CoreMidiDriver` + `*_mac.mm`, `RtMidiDriver`) and `gamepad/` (`IGamepadDriver`, `GamepadInput`, `GameControllerDriver_mac.mm` / stub) subsystems without mutual dependencies
- [x] T025 [US5] Relocate `FakeAsioDriver` / `asio_self_test` out of `src/abi/nota_engine_audio.cpp` into `tests/test_io.cpp` covering `NullAudioDriver`, `FakeAsioDriver`, `MidiParser`/`MpeInput`, and `GamepadInput`

## Phase 9: `modules/engine/` (`nota::engine` → `nota_engine_core` OOP API) & `modules/c_abi/` (`nota_engine`) [US3]

- [x] T026 [US3] Design & create clean self-contained OOP C++20 headers under `modules/engine/include/nota/engine/` (`Engine.h` forward-declaring nested `Engine::*` classes + focused <180-line headers `Transport.h`, `Mixer.h`, `Track.h`, `Clip.h`, `Device.h`, `Rack.h`, `Session.h`, `Recording.h`, `Automation.h`, `Modulation.h`, `Preview.h`, `AudioIo.h`, `MidiAndGamepad.h`, `History.h`, `Diagnostics.h`, `NotaEngine.h`)
- [x] T027 [US3] Decompose `Engine_*.cpp` into internal domain services (`core/`, `model/`, `render/` splitting `Engine_Render.cpp`, `tracks/` + `clips/` splitting `Engine_Tracks.cpp`, `devices/`, `session/`, `recording/`, `automation/`, `io/` with `AudioSessionManager`) and `api/` weak-ID handle implementations
- [x] T028 [US3] Implement native `tests/test_engine_cpp.cpp` (full OOP C++ coverage with `NOTA_BUILD_C_ABI=OFF`) and `tests/test_headers_compile.cpp` checking standalone compile of every public header
- [x] T029 [US3] Extract `modules/c_abi/` (`nota_engine` target & binary, optional via `-DNOTA_BUILD_C_ABI=ON`) delegating strictly through OOP `nota::Engine`, `AudioCodecRegistry`, and `IPluginScanner`, keeping only `nota_*` exports

## Phase 10: Final Cross-Cutting Verification & Nota Closing Gates

- [x] T030 Run full `dev-cpp`, `dev`, `dev-asan` (ASan + UBSan), and `release` CMake preset builds with `NOTA_STRICT_WARNINGS_AS_ERRORS=ON`, `ctest` across all suites, `clangd --check` across modules, `nm` symbol checks, `tests/perf` (`nota_perf`, `nota_idle_cpu`), and `scripts/perf-gate.sh` (verify no regressions)
- [x] T031 Assess PR smoke-test gate per `AGENTS.md`: synchronize minimal multi-DLL wildcard copy (`nota_*.{dll,dylib,so}`) in `.csproj` / packaging scripts and `-DNOTA_UNIVERSAL_BINARY=ON` in `scripts/build*.sh` (zero C# code edits) if needed so `scripts/build.sh` passes end-to-end
- [x] T032 Finalize AGPL-3.0-only SPDX header audit, update `CHANGELOG.md` `[Unreleased]` (and `BUILD.md` / docs if warranted), and update `specs/116-modular-engine/` completion status
- [x] T033 Codify native modular architecture, file readability (`< 180`-line public headers, `> 400`-line device split / no `> 1,000`-line monoliths), strict C++20, and mandatory `clang-format` gates across `ARCHITECTURE.md`, `CONTRIBUTING.md`, `AGENTS.md`, `CLAUDE.md`, `.specify/memory/constitution.md`, `.specify/templates/overrides/`, and `nota-instrument` skill (`scripts/sync-agent-skills.sh --check`)
