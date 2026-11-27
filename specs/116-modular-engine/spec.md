<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Feature Specification: Modular Native Engine Architecture, OOP C++20 API, Unified Registries, and Developer Tooling

**Feature Branch**: `feat/1116-modular-engine`

**Created**: 2026-11-27

**Status**: Approved (`nota-daw/nota#1116`)

**Issue**: [`nota-daw/nota#1116`](https://github.com/nota-daw/nota/issues/1116)

**Input**: User description: "Refactor the native C++20 audio engine into clean dynamically linked shared modules (`nota_math`, `nota_media`, `nota_dsp`, `nota_devices`, `nota_pluginhost`, `nota_pluginscanner`, `nota_io`, `nota_engine_core`, and optional C ABI `nota_engine`), with an OOP `Engine::*` C++20 API, universal SIMD math, audio codec registry, device registry with per-device folders, format-agnostic plugin scanner foundations (CLAP/LV2/JUCE-free VST2), unified audio/MIDI/gamepad hardware `io/` module, Pentad VST3 test wrapper removal, and first-class `clangd`/`compile_commands.json` support."

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — zero C# logic changes in this scope (optional packaging wildcard sync at PR readiness only)
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`)
- [x] Plugin hosting (`pluginhost/` and standalone `pluginscanner/`) — the only places JUCE is allowed
- [x] Build / packaging / CI (`src/native/nota.engine/CMakeLists.txt`, `CMakePresets.json`, `.clangd`, `BUILD.md`)
- [ ] Docs only

**Nota docs to keep in mind**:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR expectations),
[`BUILD.md`](../../BUILD.md),
[`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Modular Dynamic Libraries, Developer Tooling & Core Math/Media/DSP Foundations (Priority: P1)

Engineers working on the native engine configure and build clean, dynamically linked C++20 modules with strict visibility boundaries, working `clangd` navigation/diagnostics, unified vector audio-math utilities, centralized audio codec registration, and real-time DSP contracts whose global state is safely owned in a single shared library — without carrying the unused test `NotaPentad` VST3 build target.

**Why this priority**: Every higher-level module (`devices/`, `pluginhost/`, `pluginscanner/`, `io/`, `engine/`) depends on clean CMake boundaries, `clangd` compatibility, core math, media codecs, and single-image RT globals before any safe split can occur.

**Independent Test**: Configure and build the native `dev` and `dev-cpp` CMake presets, verify `test_math` and `test_media` pass via `ctest`, verify `clangd --check` succeeds cleanly against the generated single-arch `compile_commands.json`, and verify `plugins/pentad/` / `NOTA_BUILD_PENTAD_VST3` are removed while `Nota Render Probe` still builds.

**Acceptance Scenarios**:

1. **Given** a developer opens native C++ files in an editor or runs `clangd --check` using the `dev` CMake preset, **When** `compile_commands.json` is queried, **Then** single-architecture compilation commands and clean `PUBLIC`/`PRIVATE` include boundaries allow `clangd` to analyze headers, C++ sources, and `*_mac.mm` files with zero driver errors or include-order hacks.
2. **Given** the `math/` shared module is built dynamically, **When** `test_math` exercises vector operations, stereo deinterleave/interleave, fast math approximations, and FFT transforms, **Then** numerical outputs match reference behavior without introducing cross-DLL call overhead in tight sample loops.
3. **Given** the `media/` shared module registers decoders and encoders via `AudioCodecRegistry`, **When** callers inspect, stream, import, or encode WAV, FLAC, or MP3 files, **Then** all media paths go through the registry rather than calling format-specific libraries directly, and existing audio decoding/encoding parity is preserved.
4. **Given** multiple dynamically linked engine libraries execute concurrently in one process, **When** real-time counters, editor-open flags, silence gates, and device UIDs are mutated or queried, **Then** every shared library resolves the single authoritative state exported by `dsp/` rather than private header copies.

---

### User Story 2 - Extensible Device Registry & Per-Device Directory Layout (Priority: P1)

Engine contributors can inspect, maintain, and add built-in audio effects, instruments, MIDI effects, and racks cleanly: every built-in device lives in its own directory (with complex effects/instruments separated into parameter definitions, DSP engines, and device glue), and registers through one central `DeviceRegistry` without editing monolithic switch statements across multiple files.

**Why this priority**: Currently built-in effects sit flat in one large folder with >1,000-line source files, and adding a device requires updating several switch-based factory functions and `Engine` helpers.

**Independent Test**: Build `devices/` dynamically against `dsp/`, `media/`, and `math/`, execute `test_devices` in `ctest` to instantiate every registered built-in effect (27), instrument (18), MIDI effect (6), and rack (4) by both legacy integer `kind` and human slug, verify default parameters, cloning, auto-sleep behavior, and `Pentad` (`kind = 14`) availability.

**Acceptance Scenarios**:

1. **Given** all built-in devices register with `DeviceRegistry`, **When** a caller requests creation or cloning by numeric `kind` or readable string slug, **Then** the matching device or instrument is returned with identical parameter layouts and defaults to legacy project saves.
2. **Given** built-in audio effects (27), instruments (18), MIDI effects (6), and racks (4) reside in dedicated per-device directories under `devices/`, **When** a developer navigates any complex effect or instrument, **Then** parameters, DSP algorithms, and device lifecycle bindings live in clean, human-readable files without pulling sibling device headers into `engine/`.

---

### User Story 3 - Object-Oriented C++20 Engine API & Optional Preserved C ABI (Priority: P1)

Native C++ consumers interact with a clean, fully object-oriented `nota::Engine` API built around lightweight domain handles (`Engine::Transport`, `Engine::Mixer`, `Engine::Track`, `Engine::Clip`, `Engine::Device`, `Engine::Parameter`, `Engine::Rack`, `Engine::Session`, `Engine::Recorder`, `Engine::AudioIo`, etc.), while managed `.NET` and performance harnesses continue using the optional C ABI shared library (`nota_engine`) without changing P/Invoke library names or breaking behavior.

**Why this priority**: Decomposing `Engine.h` (~3,235 lines) and `Engine_*.cpp` (~19,000 lines) while cleanly decoupling the OOP C++ engine from the optional C ABI is the core goal of this refactoring.

**Independent Test**: Build both `-DNOTA_BUILD_C_ABI=OFF` (running `test_engine_cpp` and `test_headers_compile` directly against OOP `Engine::*` C++ API without building C ABI) and `-DNOTA_BUILD_C_ABI=ON` (running `test_c_abi` and `tests/perf` against `libnota_engine.*`).

**Acceptance Scenarios**:

1. **Given** `-DNOTA_BUILD_C_ABI=OFF`, **When** a pure C++ application or `test_engine_cpp` creates a `nota::Engine`, creates tracks, clips, devices, racks, and renders offline or live blocks through `Engine::Track`, `Engine::Clip`, `Engine::Device`, and `Engine::Parameter`, **Then** everything compiles and passes natively using only concise, self-contained C++20 public headers.
2. **Given** `-DNOTA_BUILD_C_ABI=ON`, **When** `nota_engine` (`c_abi/`) is loaded by C callers or benchmark harnesses, **Then** every `nota_*` C ABI symbol delegates cleanly through the public OOP C++ engine, codec registry, and scanner interfaces with no regressions in output or real-time safety.
3. **Given** any handle returned by `Engine` (`Engine::Track`, `Engine::Clip`, `Engine::Device`, `Engine::Parameter`), **When** the underlying entity is removed or undone, **Then** queries via the handle report invalid/empty safely without dangling pointers or undefined behavior.

---

### User Story 4 - Independent Plug-in Scanner (Format-Extensible) & Isolated Plug-in Host (Priority: P2)

Plug-in scanning (`pluginscanner/` + `nota-scanworker` + `nota-scanner-selftest`) runs and builds independently of plug-in hosting (`pluginhost/` + `nota-render-probe`), hides all internal JUCE symbols dynamically, and introduces a format-backend registry ready for future CLAP, LV2, and JUCE-independent VST2 support without modifying engine or catalog core logic.

**Why this priority**: Today the scanner is fused into the static host lib solely due to old static JUCE duplication bugs, and scanner/catalog internals hardcode VST3/AU JUCE assumptions.

**Independent Test**: Verify `nota-scanner-selftest` and `test_pluginscanner` pass cleanly (including a JUCE-free mock backend testing custom format registration, wire serialization, and missing-bundle cleanup), verify `Nota Render Probe` loads cleanly through `nota_pluginhost`, and verify `nm -gU` / symbol inspection finds zero exported `juce::*` symbols outside either library.

**Acceptance Scenarios**:

1. **Given** `pluginscanner/` defines `IPluginFormatBackend` and `PluginFormatRegistry`, **When** a new format backend (real or mock, e.g., simulating CLAP, LV2, or JUCE-free VST2) is registered alongside JUCE's VST3/AU backend, **Then** `OutOfProcessScanner`, `PluginCatalogImpl`, and `nota-scanworker` discover candidates, probe via the format-neutral wire codec, and clean up missing entries without format-specific conditionals.
2. **Given** a user already has an existing `plugins.xml` catalog created before this refactor, **When** `PluginCatalogImpl` loads at startup, **Then** entries migrate losslessly into `PluginDescriptor` records and remain loadable by `pluginhost/`.
3. **Given** both `pluginhost/` and `pluginscanner/` link third-party hosting code dynamically or privately, **When** inspected at link/load time on macOS/Linux/Windows, **Then** zero `juce::*` symbols leak from either shared library and neither module includes headers from the other.

---

### User Story 5 - Unified Hardware I/O Module (`io/`) for Audio, MIDI & Gamepad (Priority: P2)

Audio output/input streams, MIDI ports/MPE parsing, and gamepad/HID controller polling live in a dedicated `io/` shared module organized cleanly into independent `audio/`, `midi/`, and `gamepad/` subsystems, removing OS `#ifdef`s and global ASIO singletons from `Engine` and the C ABI.

**Why this priority**: Previously `src/audio_io/` combined unrelated MIDI and gamepad code with disconnected audio output/input classes, leaking `sharedAsioSession()` and OS conditionals directly into engine and C ABI sources.

**Independent Test**: Run `test_io` in `ctest` covering `AudioDriverRegistry` device enumeration, `NullAudioDriver` output/input lifecycle, `FakeAsioDriver` duplex session event/failure handling (migrated out of C ABI), `MidiParser` / `MpeInput` routing, and gamepad ring/axis polling without requiring physical audio hardware.

**Acceptance Scenarios**:

1. **Given** `io/audio/` exposes `IAudioDriver`, `IAudioOutputStream`, `IAudioInputStream`, and `AudioDriverRegistry`, **When** `Engine::AudioIo` switches drivers/devices, configures sample rate or buffer size, handles exclusive-mode fallbacks, or attaches duplex capture streams, **Then** it operates solely through the unified driver/stream abstractions with zero platform `#ifdef`s in `engine/` or `c_abi/`.
2. **Given** MIDI drivers (`io/midi/`) and gamepad inputs (`io/gamepad/`) are cleanly separated from `io/audio/`, **When** MIDI/MPE or controller changes occur, **Then** audio headers and audio stream drivers remain untouched and free of controller/HID dependencies.

---

### Edge Cases

- What happens if an OOP handle (`Engine::Track`, `Engine::Clip`, `Engine::Device`, `Engine::Parameter`) is used after the track/device is deleted or an undo snapshot restores an earlier state? Methods must check handle validity cleanly (`isValid() == false`) and return default/no-op failure rather than dereferencing stale pointers.
- What happens when `compile_commands.json` is generated on macOS while switching between `dev` (single host architecture) and `release` (universal `arm64;x86_64` + LTO)? `.clangd` strips multi-`-arch` / `-flto*` flags safely if pointed to a release DB, while `dev` presets omit them natively.
- What happens if all physical audio hardware fails or is absent (headless CI)? `AudioSessionManager` cleanly falls back through policy to `NullAudioDriver` without crashing or blocking RT callbacks.
- What happens if a hosted plugin in `nota-scanworker` hangs, crashes, or prints garbage to stdout? `OutOfProcessScanner` enforces the deadline, terminates the worker cleanly, slices the sentinel block, and reports a structured outcome.
- What happens when running downstream verification scripts expecting `nota_engine` alongside multiple sibling shared libraries? All shared libraries share a clean `nota_*` prefix and `@loader_path` / `$ORIGIN` `RPATH`, and `-DNOTA_BUILD_C_ABI=ON` produces `nota_engine` cleanly.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST decompose the native engine into 9 core dynamically linked CMake shared modules (`modules/core` -> `nota_core`, `modules/math` -> `nota_math`, `modules/media` -> `nota_media`, `modules/dsp` -> `nota_dsp`, `modules/devices` -> `nota_devices`, `modules/pluginhost` -> `nota_pluginhost`, `modules/pluginscanner` -> `nota_pluginscanner`, `modules/io` -> `nota_io`, `modules/engine` -> `nota_engine_core`) plus an optional C ABI wrapper (`modules/c_abi` -> `nota_engine` controlled by `NOTA_BUILD_C_ABI`) with an acyclic dependency graph and strict `PUBLIC` vs `PRIVATE` include separation.
- **FR-002**: System MUST provide an object-oriented C++20 API centered on `nota::Engine` and nested domain classes (`Engine::Transport`, `Engine::Mixer`, `Engine::Track`, `Engine::TrackCollection`, `Engine::Clip`, `Engine::Device`, `Engine::DeviceChain`, `Engine::Parameter`, `Engine::Rack`, `Engine::RackChain`, `Engine::Macro`, `Engine::Session`, `Engine::Scene`, `Engine::Slot`, `Engine::Recorder`, `Engine::AutomationLane`, `Engine::Modulator`, `Engine::Preview`, `Engine::AudioIo`, `Engine::MidiIo`, `Engine::GamepadIo`, `Engine::History`, `Engine::Diagnostics`) in clean, self-contained headers without third-party SDK types leaking publicly.
- **FR-003**: Optional C ABI target `nota_engine` MUST delegate through public C++ engine, codec registry, and plugin scanner interfaces while preserving existing C symbol semantics and library output name (`libnota_engine.*` / `nota_engine.dll`).
- **FR-004**: System MUST remove the test `Pentad` VST3 wrapper (`plugins/pentad/`, `NOTA_BUILD_PENTAD_VST3` in `CMakeLists.txt`, and its `BUILD.md` section) while retaining built-in `Pentad` (`kind = 14`) and `nota-render-probe`.
- **FR-005**: Module `math/` MUST provide a unified SIMD vector register abstraction (`Vec<T, Lanes>`), unified buffer/stereo/complex kernels, scalar+vector fast math, CPU feature detection, and Pimpl `RealFft` hiding platform/vendor FFT libraries (`Accelerate`/`pffft`) while keeping hot kernels inline.
- **FR-006**: Module `media/` MUST provide `IAudioDecoder`, `IAudioEncoder`, and a central `AudioCodecRegistry` so all audio decoding, probing, streaming, import, and export use registered codecs without duplicating `dr_wav`/`dr_flac`/`dr_mp3`/`libFLAC`/`LAME` integration.
- **FR-007**: Module `dsp/` MUST host core RT primitives, device/capability contracts (`Device`, `Instrument`, `MidiDevice`, `capabilities/*`), JUCE-free plug-in contracts (`PluginContracts.h`), and a central `DeviceRegistry`, owning all former header global atomics/counters/factories inside `.cpp` translations.
- **FR-008**: Module `devices/` MUST place every built-in audio effect (27), instrument (18), MIDI effect (6), and rack (4) in its own directory, split large devices into focused parameter/DSP/device files, and register them with `DeviceRegistry` without monolithic `switch` factories.
- **FR-009**: Module `pluginscanner/` MUST build independently from `pluginhost/` (including `nota_pluginscanner` DLL, `nota-scanworker`, and `nota-scanner-selftest`), define `IPluginFormatBackend` and `PluginFormatRegistry`, use a format-neutral worker protocol (`ScanWireCodec` returning `PluginDescriptor`), maintain `plugins.xml` compatibility, encapsulate JUCE VST3/AU scanning inside `backends/juce/` with hidden exports, and leave clean extension hooks for CLAP, LV2, and JUCE-independent VST2.
- **FR-010**: Module `pluginhost/` MUST build independently with hidden JUCE symbol exports, consume `IPluginCatalog` through `nota_dsp` contracts, maintain an internal format-host dispatch (`IPluginFormatHost`), isolate macOS Objective-C++ bridges in `*_mac.mm`, and expose zero dynamic `juce::*` symbols.
- **FR-011**: Module `io/` MUST organize independent `audio/`, `midi/`, and `gamepad/` subsystems, unify audio output/input/duplex behind `IAudioDriver`, `IAudioOutputStream`, `IAudioInputStream`, and `AudioDriverRegistry` (with `CoreAudio`, `Miniaudio`, `Asio`, and `NullAudioDriver`), decouple `MidiDriverRegistry` / `MidiParser` / `MpeInput` from audio streams, and isolate gamepad/HID polling.
- **FR-012**: Repository MUST provide `.clangd`, `src/native/nota.engine/CMakePresets.json` (`dev`, `dev-cpp`, `dev-asan`, `release` with `NOTA_UNIVERSAL_BINARY`), `.cache/` gitignore entry, standalone header compilation tests (`test_headers_compile`), and native `.mm` Objective-C++ files instead of overriding `.cpp` language properties.
- **FR-013**: All modular native libraries (`modules/*`) and native C++ test suites (`tests/*`) MUST enforce strict modern C++20 code-safety standards (`nota_enable_strict_cxx` with `-Wall -Wextra -Wpedantic -Wshadow -Wnon-virtual-dtor -Wold-style-cast -Woverloaded-virtual -Wnull-dereference -Wconversion -Wsign-conversion` + `-Werror`, MSVC `/W4 /permissive- /WX`), RAII ownership without raw `new`/`delete` in domain/API code, `std::span`/`std::string_view`/`std::bit_cast`/`[[nodiscard]]`/`explicit`, `dev-asan` ASan+UBSan verification, and unconditional `clang-format` adherence before concluding native edits.
- **FR-014**: The modular readability, architecture isolation, strict C++20, and mandatory `clang-format` gates MUST be codified across contributor and agent guidance files (`ARCHITECTURE.md`, `CONTRIBUTING.md`, `AGENTS.md`, `CLAUDE.md`, `.specify/memory/constitution.md`, `.specify/templates/overrides/`, and `nota-instrument` skill).

### Key Entities

- **`nota::Engine` & `Engine::*` Handles**: Root engine Pimpl owning decomposed domain services (`render/`, `tracks/`, `clips/`, `devices/`, `session/`, `recording/`, `automation/`, `io/`), accessed through safe copyable reference-handle objects validated via weak ownership + entity ID.
- **`nota::math::Vec<T, Lanes>`**: Portable SIMD register wrapper with unified buffer, stereo, complex, and vectorized fast-math operations.
- **`nota::media::AudioCodecRegistry`**: Central registry mapping file extensions / format IDs to `IAudioDecoder` and `IAudioEncoder` factories plus header probing.
- **`nota::dsp::DeviceRegistry`**: Central catalog and factory registry for built-in effects, instruments, MIDI FX, racks, defaults, cloning, and injected `IPluginHost`.
- **`nota::scanner::PluginFormatRegistry` / `IPluginFormatBackend`**: Extensible format backend abstraction decoupling scan paths, discovery, existence checks, and out-of-process probing across VST3/AU and future CLAP/LV2/VST2.
- **`nota::io::audio::AudioDriverRegistry` / `IAudioDriver`**: Central driver registry unifying output streams, capture streams, duplex clock sharing, channel names, and device events across CoreAudio, Miniaudio, ASIO, and test drivers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Native engine compiles cleanly as separate shared modules in both `-DNOTA_BUILD_C_ABI=OFF` and `-DNOTA_BUILD_C_ABI=ON` configurations, and every native `ctest` suite (`test_core`, `test_math`, `test_media`, `test_dsp`, `test_devices`, `test_pluginscanner`, `test_pluginhost`, `test_io`, `test_engine_cpp`, `test_c_abi`, `test_headers_compile`, plus `nota-scanner-selftest`) passes 100%.
- **SC-002**: Symbol inspection confirms 0 dynamic `juce::*` exports from `libnota_pluginhost.*` and `libnota_pluginscanner.*`, and all public headers under `modules/*/include/nota/**` compile standalone with zero leaked third-party SDK headers (`dr_*`, `FLAC`, `lame`, `juce`, `miniaudio`, `rtmidi`, `signalsmith`, OS audio/MIDI/HID headers).
- **SC-003**: `clangd --check` against the `dev` compilation database reports zero analysis/include errors on engine, scanner, and I/O translation units.
- **SC-004**: `tests/perf` (`nota_perf`, `nota_idle_cpu`) and `scripts/perf-gate.sh` remain within budget (<= +1% instructions, <= +15% wall time), preserving output parity across modularized DSP/render stages.
- **SC-005**: Every built-in device (27 effects, 18 instruments, 6 MIDI FX, 4 racks) resides in its own subdirectory, `Engine.h` public entry headers remain concise (<180 lines each for public API views), and no monolith >1,000-line effect/engine TU blocks comprehension.
- **SC-006**: Hard C++20 code-safety gate passes with zero warnings under strict compiler flags (`NOTA_STRICT_WARNINGS_AS_ERRORS=ON`, including `-Wconversion`, `-Wsign-conversion`, `-Wold-style-cast`, `-Wshadow`, `-Wnon-virtual-dtor`, `-Werror`), zero raw `new`/`delete` in modular libraries (`modules/*` except optional `c_abi` handle boundaries), and 100% green `ctest` execution under the `dev-asan` ASan+UBSan preset.

## Assumptions

- Refactoring scope stays strictly in `src/native/nota.engine/` (plus developer lint config `.clangd`/`.gitignore`, native CTest suites, and `BUILD.md` Pentad VST3 removal), without rewriting managed `.NET` application code.
- Multi-DLL `.csproj` copy wildcards / `-DNOTA_UNIVERSAL_BINARY=ON` in `scripts/build*.sh` may be added purely as minimal packaging integration at the final PR verification gate so `scripts/build.sh` passes end-to-end.
- No new third-party dependencies (`clap`, `lilv`, VST2 SDK, etc.) are added in this task; only abstractions enabling them later are introduced.

## Nota Compliance Checklist

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — existing CPM deps only, `LICENSES/third-party.md` unaffected
- [x] Audio-thread impact considered: hot math kernels stay inline in headers; no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); JUCE contained strictly to `pluginhost/` and `pluginscanner/` (`backends/juce/`) with hidden exports
- [x] Smoke-test impact checked: `tests/Nota.SmokeTest` preserved and `nota_engine` output name unchanged; `tests/Nota.SmokeTest/HostedPluginTests.cs` opt-in left untouched
- [x] User-visible change: internal refactoring only (unless packaging changes warrant note; `BUILD.md` Pentad VST3 section updated)
- [x] UI change: N/A (engine-only scope)
