# Feature Specification: Professional C++20 DSP Architecture Overhaul, `.h`/`.cpp` Modularization & Plugin Scanner Separation

**Feature Branch**: `030-dsp-architecture-overhaul`

**Created**: 2026-10-19

**Status**: Implemented

**Input**: User description: "In this folder you have the Nota DAW code and my DSP optimization patches — plan a rebuild of the entire DSP code into professional C++; it would be good to split the plugin scanner out from the engine itself; we want to keep the whole C ABI of the engine; you may take inspiration from well-written DSP elsewhere; we also want to split the code into sensible folders. Read all Nota project rules and plan this carefully. Try to split things into more .cpp and .h files the way C++ is normally written, and write an implementation spec from it."

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [x] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [x] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind**:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR-4 lock-free queues, AR-5 immutable snapshots, AR-6 audio-thread safety, AR-7 pure C ABI, AR-8 sample-accurate MIDI, AR-9 `Transport` single source of time),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (AGPL-3.0-only SPDX headers, dependency policy, PR expectations),
[`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md), [`FEATURES.md`](../../FEATURES.md).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Idiomatic `.h`/`.cpp` Modular Architecture & Domain Folder Organization (Priority: P1) 🎯 MVP

As a C++ audio developer working on Nota's native engine, I want the flat 138-file `src/` directory reorganized into domain-driven subdirectories (`abi/`, `dsp/`, `devices/`, `engine/`, `audio_io/`, `media/`) and monolithic header-only classes split into clean declaration headers (`.h`) and implementation translation units (`.cpp`), so that compilation parallelizes across CPU cores, include dependencies are minimized, and the codebase follows professional C++20/C++23 engineering standards.

**Why this priority**:
Currently, all 138 engine files sit in a single flat folder (`src/native/nota.engine/src/`), and all 25 audio effects, 16 instruments, 6 MIDI effects, and rack containers are written as 500–1,800 line header-only files. Including `BuiltinDevices.h` or compiling `Engine_Devices.cpp` forces the compiler to re-parse ~35,000 lines of inline DSP implementation. Splitting declarations (`.h`) from implementations (`.cpp`) and organizing files into logical folders is the structural foundation for all subsequent DSP and scanner improvements.

**Independent Test**:
Build `nota_engine` and `nota-scanworker` via `./scripts/build.sh`. Verify that incremental edits to a single device `.cpp` file recompile only that translation unit and re-link in under 2 seconds, all exported symbols match `nota_exports.txt` / `nota_exports.map`, and `tests/Nota.SmokeTest` passes 100%.

**Acceptance Scenarios**:

1. **Given** `src/native/nota.engine/`, **When** inspecting the directory layout, **Then** source files are cleanly separated into `src/abi/`, `src/dsp/{core,math,primitives,poly}/`, `src/devices/{racks,effects,instruments,midi_fx}/`, `src/engine/`, `src/audio_io/{macos,desktop}/`, `src/media/`, and `pluginhost/{bridge,hosting,scanner}/`.
2. **Given** any non-template DSP device, instrument, MIDI effect, rack, or engine subsystem, **When** inspecting its source files, **Then** the `.h` file contains only the class interface, public types, and member declarations, while method bodies and internal helper tables reside in the corresponding `.cpp` file.
3. **Given** the public C ABI header `include/nota/nota_engine.h`, **When** built on macOS, Linux, and Windows, **Then** 100% of existing C ABI functions, structs, and enums remain intact with zero breaking changes to `Nota.Infrastructure` P/Invoke bindings.

---

### User Story 2 - Standalone Plugin Scanner & Thread-Safe Plugin Catalog Separation (Priority: P1)

As a DAW user scanning VST3 and AudioUnit plugins in the background while browsing or editing a project, I want plugin discovery and catalog management (`nota_pluginscanner`) separated from the audio engine and in-process plugin hosting (`nota_pluginhost`), so that background plugin scans are completely thread-safe, resilient to crashing or noisy plugins, and decoupled from real-time engine internals.

**Why this priority**:
`pluginhost/PluginHost.cpp` currently mixes out-of-process scanning, XML catalog persistence, scan-path management, JUCE GUI window hosting, and real-time `PluginInstrument`/`PluginEffect` audio adapters in one 1,030-line file. Furthermore, `catalog()` and `extraScanPaths()` are unsynchronized function-local statics accessed concurrently when C# runs `Task.Run(() => catalog.Scan(worker))`. Separating the scanner into its own module with thread-safe catalog access and Zrythm-inspired coordinator/worker management eliminates data races and decouples scanning from the engine.

**Independent Test**:
Run plugin discovery and catalog queries concurrently from multiple threads, test with crashing/hanging/stdout-polluting plugins, and run `Nota.SmokeTest` (including `PluginStoreTests`). Verify thread-safe catalog updates, clean separation of CMake targets (`nota_pluginscanner`, `nota_pluginhost`, `nota-scanworker`), and 100% C ABI compatibility for all `nota_pluginhost_*` functions.

**Acceptance Scenarios**:

1. **Given** `pluginhost/scanner/`, **When** compiled via CMake, **Then** `nota_pluginscanner` builds as an independent static library with zero dependencies on `Engine`, `Track`, `Graph`, or `Device`.
2. **Given** a background scan running via `nota_pluginhost_scan()`, **When** the UI thread concurrently queries `nota_pluginhost_plugin_count()`, `nota_pluginhost_plugin_desc()`, or `nota_pluginhost_index_of_id()`, **Then** access is synchronized safely without data races or iterator invalidation.
3. **Given** `PluginInstrument` and `PluginEffect` in `pluginhost/hosting/`, **When** processing audio blocks on the real-time thread, **Then** stereo deinterleaving/interleaving and sidechain gain scaling use vectorized SIMD and `FastMath` with zero heap allocations.

---

### User Story 3 - Shared C++20 DSP Primitives, FastMath & Hardware SIMD Acceleration (Priority: P1)

As a music producer running CPU-heavy mixing and synthesis sessions, I want all built-in instruments and audio effects to share a unified, industrial-grade C++20 DSP library (`dsp/core`, `dsp/math`, `dsp/primitives`, `dsp/poly`) featuring `FastMath` approximations, ARM NEON / x86 SSE/AVX/FMA vectorization, and deduplicated filter/delay/oscillator/voice building blocks, so that CPU load is minimized while maintaining studio-grade audio transparency ($< 0.05\text{ dB}$ error).

**Why this priority**:
Eliminating duplicated filter/delay/oscillator code across 40+ devices and replacing per-sample standard library transcendentals (`std::sin`, `std::cos`, `std::tanh`, `std::log10`, `std::pow`, `std::exp2`) and scalar buffer loops with unified `FastMath`, `Simd`, and `SimdAudio` routines delivers 25%–55% CPU savings across effects and polyphonic synths.

**Independent Test**:
Execute `DspOptimizationTests` in `tests/Nota.SmokeTest`, verifying numerical accuracy, frequency response stability, True-Peak SIMD FIR accuracy, absence of NaNs/denormals, and measurable CPU reduction across all built-in effects and instruments.

**Acceptance Scenarios**:

1. **Given** `src/dsp/math/`, **When** any device or synth evaluates pitch, dB/linear conversion, saturation, or LFOs, **Then** it uses `FastMath.h` (`exp2f`, `log2f`, `dbToLin`, `linToDb`, `tanh`, `tanhPrecise`, `tanhRatio`, `sin`, `cos`, `tanPrewarp`).
2. **Given** `src/dsp/math/Simd.h` and `SimdAudio.h`, **When** performing buffer summing, scaling, clamping, NaN/Inf suppression, 16-tap FIR dot products, deinterleaving/interleaving, or peak/RMS/correlation metering, **Then** hardware SIMD instructions (ARM NEON / x86 SSE/AVX/FMA) execute in 16-sample vector chunks with clean scalar tails.
3. **Given** `src/dsp/primitives/` and `src/dsp/poly/`, **When** building filters (`SvfFilter`, `BiquadCascade`, `LadderFilter`), delay lines (`DelayLine`), convolutions (`Convolution`), true-peak meters (`TruePeakMeter`), or polyphonic instruments (`PolyVoiceManager`, `SynthVoice`), **Then** devices reuse these shared `.h`/`.cpp` classes instead of duplicating local implementations.
4. **Given** real-time audio methods across `dsp/`, `devices/`, and `engine/`, **When** compiled with Clang, **Then** methods are annotated with `NOTA_RT_SAFE` (`noexcept [[clang::nonblocking]]`) and `NOTA_HOT`, and `Engine::computeBlockMidi()` performs zero runtime vector resizing on the audio thread (fixing AR-6).

---

### User Story 4 - Engine-Wide Hybrid Silence Detection & Auto-Sleep Bypass (Priority: P2)

As a producer working on large arrangements (30–80 tracks) where many tracks are silent at any given bar, I want stateful hysteresis silence detection (`SilenceDetector`) and automatic device/instrument sleep (`isIdle()`, `hasTail()`, `wake()`), so that silent tracks and decayed time-based effects consume near-zero CPU while waking up instantaneously on sample 0 of any incoming transient.

**Why this priority**:
In typical DAW arrangements, 70%–85% of tracks are silent at any given moment. Skipping silent generators and sleeping effect chains reduces idle and sparse-arrangement CPU usage by 40%–60%.

**Independent Test**:
Run `DspSilenceBypassTests` in `tests/Nota.SmokeTest`, verifying that delay/reverb/chamber/compressor/limiter tails decay naturally below $-100\text{ dBFS}$ before sleeping, memoryless devices bypass zero-blocks immediately, and transient attacks wake devices on sample 0 with zero clicks or truncation.

**Acceptance Scenarios**:

1. **Given** `SilenceDetector` in `src/dsp/core/SilenceDetector.h`/`.cpp`, **When** monitoring track, return, or master buffers, **Then** it uses single-pass SIMD `peakAndSumSquares`, dual-threshold hysteresis ($-80\text{ dBFS}$ attack / $-90.5\text{ dBFS}$ release), and a sample-accurate hold window to prevent chattering.
2. **Given** any built-in `Device` or `Instrument`, **When** input is silent and internal tails/voices have decayed below $-100\text{ dBFS}$ for the hangover period, **Then** `isIdle()` returns `true` and `Engine_Render.cpp` skips redundant processing, PDC, and send accumulation while propagating `currentSilent` down the chain.

---

### User Story 5 - Multi-Core Parallel Track Rendering & Vectorized Mixer Summing (Priority: P2)

As a user running heavy multi-track sessions on modern multi-core CPUs, I want independent tracks rendered in parallel across a lock-free real-time worker pool (`AudioThreadPool`) with cache-aligned job slots and per-worker scratch buffers, followed by vectorized SIMD bus summing and master safety processing, so that multi-core headroom scales linearly without priority inversion or audio dropouts.

**Why this priority**:
Single-threaded track rendering bottlenecks on a single CPU core. Lock-free parallel rendering with per-worker scratch buffers and SIMD mixer summing unlocks 2x–4x higher track/plugin counts.

**Independent Test**:
Render a 16-track project with active instruments, insert chains, group submixes, sidechain route taps, and return sends via `Nota.SmokeTest`; verify deterministic output matching sequential rendering and zero real-time safety violations.

**Acceptance Scenarios**:

1. **Given** `AudioThreadPool` in `src/engine/AudioThreadPool.h`/`.cpp`, **When** `Engine::mixGraph()` executes Pass 1 leaf tracks, **Then** independent tracks are dispatched across pre-allocated real-time worker threads using atomic ticket counters, `alignas(64)` cache-aligned job slots, and per-worker scratch buffers (`workerScratch_[workerId]`).
2. **Given** leaf tracks, group submixes, return buses, and the master bus in `Engine_Render.cpp`, **When** summing audio and computing peak/RMS/stereo map telemetry, **Then** all gain scaling, route taps (`routeBus_`, `routeBusPre_`), and master NaN/Inf suppression + clipping execute via single-pass SIMD routines.

---

### Edge Cases

- **Audio-Thread Allocation Hazard in `computeBlockMidi`**: Track count increases must allocate MIDI event buffers on the message thread during `Graph` construction/publication, never via `std::vector::resize()` on the audio thread.
- **Concurrent Plugin Scan & UI Browsing**: Calling `nota_pluginhost_scan()` on a background thread while the UI thread reads `nota_pluginhost_plugin_desc()` or instantiates a plugin must be protected by thread-safe synchronization in `PluginCatalog`.
- **Noisy or Crashing Third-Party Plugins**: A plugin that prints arbitrary text to `stdout` during scanning or crashes/hangs in `findAllTypesForFile` must not corrupt the XML catalog or hang `nota_pluginhost_scan()`.
- **Non-Finite Floats (NaN / $\pm\infty$)**: Unstable plugin or feedback signals must be branchlessly squashed to `0.0f` via `simd::squashNonFinite` on the master bus before clamping.
- **Denormals in Filter & Reverb Tails**: `ScopedNoDenormals` (FTZ/DAZ) must be active on both the main audio callback thread and all `AudioThreadPool` worker threads!

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The native engine directory `src/native/nota.engine/` MUST be organized into domain subdirectories: `src/abi/`, `src/dsp/{core,math,primitives,poly}/`, `src/devices/{racks,effects,instruments,midi_fx}/`, `src/engine/`, `src/audio_io/{macos,desktop}/`, `src/media/`, and `pluginhost/{bridge,hosting,scanner}/`.
- **FR-002**: Every non-template class across `dsp/`, `devices/`, `engine/`, `audio_io/`, `media/`, and `pluginhost/` MUST be split into a declaration header (`.h`) and an implementation file (`.cpp`), leaving only small inline performance-critical primitives (`FastMath.h`, `Simd.h`, `SimdAudio.h`, `AudioSpan.h`, `RtAttributes.h`, `ScopedNoDenormals.h`, `Hadamard.h`, `Random.h`) and templates (`SpscRingBuffer`, `SnapshotHistory`, `PolyVoiceManager`) in headers.
- **FR-003**: The public C ABI in `include/nota/nota_engine.h` and symbol export maps (`nota_exports.txt`, `nota_exports.map`) MUST remain 100% backward-compatible.
- **FR-004**: Plugin scanning and catalog management MUST be extracted into a dedicated `nota_pluginscanner` module (`pluginhost/scanner/`) with thread-safe `PluginCatalog` synchronization and zero dependencies on `Engine` or `Track`.
- **FR-005**: Real-time audio functions MUST be annotated with `NOTA_RT_SAFE` (`noexcept [[clang::nonblocking]]`) and uphold AR-6 (zero heap allocations, zero locks, zero blocking I/O on the audio thread and worker threads).
- **FR-006**: `ScopedNoDenormals` MUST be applied on the main audio thread and every `AudioThreadPool` worker thread.
- **FR-007**: All built-in effects and instruments MUST use centralized `FastMath.h`, `Simd.h`, `SimdAudio.h`, and shared `dsp/primitives/` classes, eliminating duplicated filter/delay/math implementations and decimating UI telemetry ring-buffer atomic writes to block/control rate.
- **FR-008**: All built-in effects and instruments MUST implement the `isIdle()` / `hasTail()` / `wake()` sleep contract and integrate with `SilenceDetector` in `Engine_Render.cpp`.
- **FR-009**: `Engine_Render.cpp` MUST execute independent leaf tracks in parallel via `AudioThreadPool` using `alignas(64)` job structures and per-worker scratch buffers, and vectorize all mixer bus summing, route taps, and master bus processing.

### Key Entities

- **`PluginCatalog` & `OutOfProcessScanner` (`pluginhost/scanner/`)**: Thread-safe plugin registry and child-process scan coordinator decoupled from `Engine`.
- **`FastMath`, `Simd`, `SimdAudio` (`src/dsp/math/`)**: Vectorized and polynomial math primitives for ARM NEON and x86 SSE/AVX/FMA.
- **`SvfFilter`, `BiquadCascade`, `LadderFilter`, `DelayLine`, `Oscillator`, `Envelope`, `Oversampler`, `Convolution`, `TruePeakMeter` (`src/dsp/primitives/`)**: Shared C++20 DSP building blocks (`.h` + `.cpp`).
- **`SynthVoice` & `PolyVoiceManager` (`src/dsp/poly/`)**: Reusable polyphonic voice allocation, sample-accurate event rendering, sustain pedal, and MPE management.
- **`SilenceDetector` (`src/dsp/core/`)**: Dual-threshold hysteresis audio activity detector.
- **`AudioThreadPool` (`src/engine/`)**: Lock-free multi-core real-time fork-join task executor with per-worker scratch buffers.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of existing C ABI functions in `include/nota/nota_engine.h` and all tests in `tests/Nota.SmokeTest` (plus new DSP optimization, silence bypass, and scanner tests) pass on macOS, Linux, and Windows.
- **SC-002**: Incremental compilation of a modified device `.cpp` file recompiles only 1 translation unit instead of rebuilding all devices.
- **SC-003**: Polyphonic synthesizer and native effect CPU load is reduced by 25%–50%, and idle/sparse project CPU load is reduced by 40%–60%.
- **SC-004**: Multi-track rendering throughput on 8+ core CPUs scales by 200%–400% with zero audio-thread heap allocations or mutex locks.

---

## Assumptions

- Target compilers support C++20 (`AppleClang 17+`, `GCC 13+`, `MSVC 19.38+`).
- IEEE-754 32-bit `float` representation is standard across all supported x86_64 and ARM64 targets.
- Built-in device parameter order and serialization formats remain 100% unchanged so existing `.nota` projects and factory presets load identically.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned
- [x] UI change → N/A (native engine & pluginhost architecture only)
