<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Implementation Plan: Modular Native Engine Architecture (`nota.engine`)

**Branch**: `feat/1116-modular-engine` | **Date**: 2026-11-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/116-modular-engine/spec.md`

## Summary

Decompose `src/native/nota.engine/` from a single monolithic `nota_engine` target (3,235-line `Engine.h`, 19,000 lines of `Engine_*.cpp`, flat device directories, duplicated codec/factory/backend switches, coupled `INTERFACE` scanner inside `pluginhost/`, and mixed `src/audio_io/` holding audio, MIDI, and gamepad code) into 8 dynamically linked core shared libraries (`math/`, `media/`, `dsp/`, `devices/`, `pluginhost/`, `pluginscanner/`, `io/`, `engine/`) plus an optional `c_abi/` shared wrapper preserving the `nota_engine` target and binary name. Introduce universal abstractions (`nota::math::Vec`, `AudioCodecRegistry`, `DeviceRegistry`, `IPluginFormatBackend`/`PluginFormatRegistry` ready for CLAP/LV2/JUCE-free VST2, `io/{audio,midi,gamepad}` drivers/registries), an object-oriented C++20 `nota::Engine` (`Engine::*`) handle API, `Pentad` VST3 wrapper removal, and first-class `clangd`/`compile_commands.json` developer tooling.

## Nota: Read Before Planning

- [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-4..AR-9 (command queue, immutable graph snapshots, audio-thread safety, C ABI containment, JUCE symbol hiding, `Transport` time authority)
- [`CONTRIBUTING.md`](../../CONTRIBUTING.md): AGPL-3.0-only headers, dependency policy (`LICENSES/third-party.md`), PR verification gate
- [`BUILD.md`](../../BUILD.md): CMake dependencies, `NOTA_BUILD_PENTAD_VST3` removal

## Technical Context

**Language/Version**: C++20 (native engine, modules, C++ tests), Objective-C++ isolated strictly in `*_mac.mm` files on macOS, pure C11 header (`nota_engine.h`) for optional `c_abi/`. Zero C# logic edits.

**Primary Dependencies**: Existing CPM dependencies only (`nlohmann_json`, `dr_libs`, `miniaudio`, `signalsmith_linear`, `pffft`, `signalsmith_stretch`, `hiir`, `flac`, `rtmidi`, `lame`, `JUCE`, `asiosdk`), isolated per-module as `PRIVATE` dependencies. Zero new external dependencies added in this task.

**Storage**: Unchanged project/preset/catalog paths (`NOTA_DATA_DIR`, `plugins.xml`, `scanpaths.txt`, `scan-failed.txt`, `audio.json`, `midi.json`) with lossless migration in `PluginCatalogImpl`.

**Audio-Thread Impact**: Hot math kernels (`SimdVec`, `BufferOps`, `StereoOps`, `ComplexOps`, `FastMath`) and lightweight DSP primitives remain header-inline to prevent PLT/IAT call overhead inside per-sample loops (`scripts/perf-gate.sh` within +1% instructions / +15% wall time). All former header `static inline` atomics/counters (`SilenceGate`, `Device`, `Instrument`, `RackCore`) move into `nota_dsp` `.cpp` exports (and process-wide `ConfigFile` / `EditorKeys` singletons move into `nota_core`) so multiple DLLs share one state. All RT paths obey AR-4..AR-6.

**Testing**: Native C++ CTest executables (`test_math`, `test_media`, `test_devices`, `test_pluginscanner`, `test_io`, `test_engine_cpp`, `test_c_abi`, `test_headers_compile`) + `nota-scanner-selftest` + `tests/perf` (`nota_perf`, `nota_idle_cpu`) + `clangd --check`.

**Target Platform**: macOS (`arm64` / `x86_64`, Cocoa/`CoreAudio`/`CoreMIDI`/`GameController`/`Accelerate`), Linux (`x86_64`/`arm64`, ALSA/PulseAudio/`pffft`/`RtMidi`), Windows (`x86_64`/`arm64`, WASAPI/ASIO/`WinMM`/`pffft`/`RtMidi`).

**Project Type**: Native multi-DLL CMake workspace inside `src/native/nota.engine/`.

**Performance Goals**: Match or improve `tests/perf` and `scripts/perf-gate.sh` baseline (no regression beyond >1% instructions or >15% wall time).

**Constraints**: Balanced granularity (8 core DLLs + 1 optional C ABI DLL), acyclic module DAG, zero `juce::*` dynamic symbol exports from `nota_pluginhost` and `nota_pluginscanner`, self-contained public headers under `modules/*/include/nota/**`, `@loader_path` / `$ORIGIN` `RPATH`, clean `clangd` indexing via `CMakePresets.json` (`dev` single-arch host preset) + root `.clangd`.

**Scale/Scope**: Full reorganization of `src/native/nota.engine/` (~55 built-in devices, ~19k LOC engine breakdown, CMake modularization, tooling, native tests) executed incrementally so every phase builds and passes CTest cleanly.

## Constitution Check

- **0. Approved Issue First**: PASS — [`nota-daw/nota#1116`](https://github.com/nota-daw/nota/issues/1116) is open and carries `approved`.
- **I. Real-Time Audio Safety**: PASS — inline SIMD hot kernels, single-DLL exported RT atomics in `nota_dsp`, SPSC queues/snapshots preserved (AR-4..AR-6).
- **II. Clean Module & Interface Boundaries**: PASS — strict `PUBLIC include` / `PRIVATE src` CMake isolation; JUCE confined exclusively to `pluginhost/` and `pluginscanner/backends/juce/` with hidden symbol exports (AR-7).
- **III. Universal Engine & Modular Clarity**: PASS — C++20 OOP `Engine::*` primary API, optional C ABI (`nota_engine`), OOP registries (`AudioCodecRegistry`, `DeviceRegistry`, `PluginFormatRegistry`, `AudioDriverRegistry`, `MidiDriverRegistry`), 1-folder-per-device layout.
- **IV. Licensing & Dependency Hygiene**: PASS — AGPL-3.0-only SPDX two-line header on every new source file; zero unapproved third-party dependencies added.
- **V. Verification Gate**: PASS — CTest per module + header standalone build + `clangd --check` + `nm` export verification + `tests/perf` (`nota_perf` / `perf-gate.sh`).
- **VI. Spec-First & Scoped PR**: PASS — spec artifacts in `specs/116-modular-engine/` followed by phased tasks & incremental verification.

## Project Structure

### Documentation (this feature)

```text
specs/116-modular-engine/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/
│   └── requirements.md
├── contracts/
│   ├── module-dag.md
│   ├── cpp-engine-api.md
│   └── registries-and-io.md
└── tasks.md
```

### Source Code (repository root)

```text
.clangd                                      # Root clangd configuration
.gitignore                                   # Add .cache/
BUILD.md                                     # Remove Pentad VST3 section
src/native/nota.engine/
├── CMakeLists.txt                           # Top-level modular orchestrator & options
├── CMakePresets.json                        # dev / dev-cpp / release presets
├── cmake/
│   ├── NotaModuleHelpers.cmake              # nota_add_shared_module(), RPATH, visibility, header tests
│   └── dependencies.cmake                   # Existing CPM third-party packages
├── modules/
│   ├── math/                                # nota_math (nota::math)
│   ├── media/                               # nota_media (nota::media)
│   ├── dsp/                                 # nota_dsp (nota::dsp)
│   ├── devices/                             # nota_devices (nota::devices) — 1 folder per device
│   ├── pluginhost/                          # nota_pluginhost (nota::pluginhost) + nota-render-probe
│   ├── pluginscanner/                       # nota_pluginscanner (nota::pluginscanner) + worker + selftest
│   ├── io/                                  # nota_io (nota::io) -> audio/, midi/, gamepad/
│   ├── engine/                              # nota_engine_core (nota::engine) -> OOP Engine::* API
│   └── c_abi/                               # nota_engine (nota::c_abi, optional) -> extern "C"
└── tests/                                   # CTest suites per module + test_headers_compile
```

**Structure Decision**: 8 core shared modules (`math/`, `media/`, `dsp/`, `devices/`, `pluginhost/`, `pluginscanner/`, `io/`, `engine/`) plus 1 optional `c_abi/` target (`nota_engine`). Both `pluginhost/` and `pluginscanner/` stay strictly isolated (`PRIVATE juce_audio_processors` with symbol export masks under Option A). `io/` separates `audio/`, `midi/`, and `gamepad/` internally while avoiding an extra tiny DLL just for gamepad.

## Complexity Tracking

| Architectural Choice | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| 8+1 SHARED libraries instead of 1 monolithic target | Enforces compile-time dependency barriers, allows optional `NOTA_BUILD_C_ABI=OFF`, decouples scanner & hosting, isolates third-party SDKs cleanly | Keeping monolith preserves ~3.2k-line `Engine.h` coupling and leaks third-party headers/macros across subsystems |
| Moving header `static inline` atomics (`SilenceGate`, `Device`, `EditorKeys`, `RackCore`) to `nota_dsp` `.cpp` | Prevents duplicated per-DLL state across dynamically linked modules on macOS/Linux/Windows | Leaving globals `inline` in headers causes silent cross-DLL state divergence |
| Preserving JUCE inside `pluginscanner/backends/juce/` (`nota_audio_processors` PRIVATE + export filter) | Preserves 100% lossless `plugins.xml` and AU Component Manager compatibility in Phase 5 without writing an XML/AU parser from scratch; isolated behind `IPluginFormatBackend` for easy future removal | Rewriting XML + AU discovery in STL right away adds parsing risk to the structural migration |
| Packaging wildcard note for `.csproj` / `scripts/build*.sh` at PR gate | Multi-DLL runtime loading next to `.NET` binaries requires sibling `nota_*.{dll,dylib,so}` on Windows and packaged bundles | Leaving single-file `libnota_engine` copy alone would break `scripts/build.sh` smoke test on Windows/bundle builds |
