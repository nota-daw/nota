# Feature Specification: Multi-Core Parallel Track Rendering & SIMD DSP Acceleration

**Feature Branch**: `026-dsp-parallel-vectorization`

**Created**: 2026-10-17

**Status**: Implemented

**Input**: User request: "Plan and implement multi-core parallel track rendering, SIMD vectorization, and transcendental math optimizations across the DSP engine." — plan and implement multi-threaded track rendering, SIMD vectorization (True-Peak FIR, mixer summing), transcendental math elimination (Chamber ducking, Reverb LFO), and UI telemetry decimation across Nota's real-time C++20 DSP engine.

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind**:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR-4 lock-free queues, AR-5 immutable snapshots, AR-6 audio thread safety: zero allocations, zero mutexes, zero blocking I/O),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, AGPL-3.0-only headers, smoke tests must pass),
[`BUILD.md`](../../BUILD.md),
[`FEATURES.md`](../../FEATURES.md).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Multi-Core Parallel Track Rendering (Priority: P1) 🎯 MVP

As a music producer working on complex, multi-track projects (30+ tracks with heavy software synths and insert chains), I want Nota to distribute independent track processing across multiple CPU cores, so that audio playback remains fluid without dropouts or buffer increases.

**Why this priority**: Single-threaded rendering bottlenecks modern multi-core machines on 1 core. Parallel track rendering scales engine throughput by 300%–600% on Apple Silicon and modern multi-core x86/ARM CPUs.

**Independent Test**:
Render a multi-track project with 16 synthesizer tracks and insert devices; verify that worker threads execute track processing concurrently, audio output is bit-exact to sequential processing, and total wall-clock render time drops significantly.

**Acceptance Scenarios**:

1. **Given** a session with multiple independent leaf tracks, **When** playback rolls, **Then** tracks are processed in parallel across pre-allocated real-time worker threads without audio glitching or dropouts.
2. **Given** tracks with sidechain routings or group buses, **When** parallel render runs, **Then** dependency ordering is strictly respected (sources finish before dependents read their bus).
3. **Given** single-core or disabled multi-threading configuration, **When** playback rolls, **Then** the engine seamlessly falls back to sequential rendering with identical audio output.

---

### User Story 2 - SIMD True-Peak & Mixer Vectorization (Priority: P1)

As a mixing and mastering engineer using built-in dynamics devices (`Ceiling`) and complex summing, I want true-peak limiting and mixer bus math to execute via hardware SIMD instructions (ARM NEON / x86 AVX/SSE), so that master bus limiting consumes minimal CPU headroom.

**Why this priority**: `Ceiling.h` performs 96 scalar modulo operations per sample in its true-peak FIR interpolator. SIMD FMA vectorization with double-buffered history eliminates modulo arithmetic and speeds up the limiter by up to 5x.

**Independent Test**:
Run `Ceiling` in True-Peak mode at 44.1 kHz, 96 kHz, and 192 kHz; verify true-peak detection accuracy matches ITU-R BS.1770-4 inter-sample peak specifications while executing in a fraction of CPU cycles.

**Acceptance Scenarios**:

1. **Given** `Ceiling.h` with True-Peak enabled, **When** processing audio blocks, **Then** 16-tap FIR interpolation is computed using double-buffered history and SIMD vector dot products (`vmlaq_f32` / `_mm_fmadd_ps`) without inner loop modulo `%` operations.
2. **Given** track summing into group and master buses in `Engine_Render.cpp`, **When** mixing, **Then** sample accumulation and gain multiplication utilize SIMD vector operations.

---

### User Story 3 - Inner-Loop Math Acceleration with FastMath (Priority: P2)

As a producer applying dense reverb, ducking, and modulation, I want DSP effect loops to avoid costly standard library transcendental functions (`std::log10`, `std::pow`, `std::sin`), so that CPU cycles are preserved for audio synthesis.

**Why this priority**: Per-sample transcendentals in `Chamber` (ducking follower) and `Reverb` (modulation LFO) degrade cache and execution throughput. Replacing them with bounded polynomial/table approximations maintains perceptual transparency while saving substantial CPU.

**Independent Test**:
Process signals through `Chamber` with active Ducking and `Reverb` with modulation; verify identical sonic character within $< 0.05\text{ dB}$ margin and reduced per-block CPU time.

**Acceptance Scenarios**:

1. **Given** `Chamber.h` with Ducking active, **When** processing audio samples, **Then** envelope-to-dB and dB-to-linear conversions use `fast_math::linToDb` and `fast_math::dbToLin`.
2. **Given** `Reverb.h` with chorus modulation active, **When** modulating delay lines, **Then** LFO computation uses `fast_math::fastSin`.

---

### User Story 4 - UI Telemetry Decimation & Memory Bus Optimization (Priority: P3)

As a user viewing real-time metering and waveforms in the UI, I want audio processing threads to update visual telemetry at control rates rather than per-sample atomic writes, preventing cache line thrashing.

**Why this priority**: Per-sample atomic `load`/`store` in audio loops (e.g. `Compressor.h` writing to `keyRing_` 44,100 times/sec for a 60 Hz UI display) degrades L1 cache locality and slows down the audio thread.

**Independent Test**:
Profile memory bus and cache hit rates in `Compressor`; verify scope waveforms render cleanly in UI while atomic ring writes are decimated to block/control rate.

**Acceptance Scenarios**:

1. **Given** `Compressor.h`, **When** streaming audio, **Then** telemetry ring buffers are updated at decimated control intervals (e.g. every 32 or 64 samples) rather than every sample.

---

## Edge Cases

- **Zero or One Core Available**: On single-core environments or systems with high thread contention, parallel worker pool must gracefully collapse to in-thread execution without deadlocks.
- **Sidechain Cyclic Dependencies**: If tracks configure mutual sidechain taps, DAG dependency resolution must detect or break cycles deterministically without stalling the audio thread.
- **Denormals & Float Bounds**: SIMD vector math must maintain Flush-To-Zero (FTZ) and Denormals-Are-Zero (DAZ) states matching `ScopedNoDenormals`.
- **Dynamic Thread Count Reconfiguration**: Changing buffer size or sample rate must never allocate or deallocate worker threads on the real-time audio thread.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Engine MUST provide an `AudioThreadPool` / worker system pre-allocated on the message thread during engine setup, strictly adhering to AR-6 (zero allocations, zero mutex locks, zero priority inversions on the audio thread).
- **FR-002**: `Engine_Render.cpp` MUST support parallel processing of leaf tracks in Pass 1, distributing tracks across available worker threads using lock-free synchronization.
- **FR-003**: `Ceiling.h` True-Peak detector (`tpPush`) MUST replace scalar modulo circular indexing with double-buffered history arrays and SIMD vector dot products.
- **FR-004**: `Chamber.h` ducking follower MUST replace per-sample `std::log10` and `std::pow` with `fast_math::linToDb` and `fast_math::dbToLin`.
- **FR-005**: `Reverb.h` chorus modulation LFO MUST replace per-sample `std::sin` with `fast_math::fastSin`.
- **FR-006**: Telemetry ring buffers in `Compressor.h` MUST decimate scope writes to control rate without degrading UI visual fidelity.
- **FR-007**: All optimizations MUST yield audio output that is bit-exact or perceptually indistinguishable ($< 0.05\text{ dB}$ deviation) from reference implementations.
- **FR-008**: Build script `./scripts/build.sh` driving `Nota.SmokeTest` MUST pass 100% green on all target platforms (macOS Apple Silicon/Intel, Linux, Windows).

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Multi-track project render performance on 8-core CPUs improves by at least **200%–400%** (2x–4x throughput) when multi-threading is active compared to single-threaded baseline.
- **SC-002**: `Ceiling.h` True-Peak mode CPU usage is reduced by at least **40%**.
- **SC-003**: `Chamber.h` CPU load with active Ducking is reduced by at least **25%**.
- **SC-004**: Zero audio dropouts, zero priority inversions, and zero memory allocations on the real-time audio thread under full load.
- **SC-005**: All smoke tests pass without warnings or failures.

---

## Assumptions

- Hardware architectures support standard IEEE 754 floating-point operations with NEON (ARM64) and SSE/AVX2 (x86_64).
- The operating system grants real-time priority or high-priority audio threads (via CoreAudio on macOS, ALSA/JACK/PipeWire on Linux, WASAPI on Windows).
- Decimation of visual scope rings to ~700–1400 Hz (every 32–64 samples) is more than sufficient for 60–144 Hz display refresh rates.
