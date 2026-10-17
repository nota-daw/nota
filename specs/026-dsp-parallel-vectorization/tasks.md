# Tasks: Multi-Core Parallel Track Rendering & SIMD DSP Acceleration

**Input**: Design artifacts from `specs/026-dsp-parallel-vectorization/` (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`)

---

### Phase 1: Setup & Foundational Primitives

**Purpose**: Core concurrency primitives and SIMD buffer infrastructure

- [x] T001 Implement `AudioThreadPool.h` in `src/native/nota.engine/src/AudioThreadPool.h` with real-time pre-allocated worker threads, atomic task scheduling counter, and non-allocating barrier
- [x] T002 Implement double-buffered `TpState` (32-element array with contiguous 16-element slice) in `src/native/nota.engine/src/Ceiling.h` to eliminate `% 16` circular buffer modulo

**Checkpoint**: Core thread pool and SIMD buffer layout ready and compilable.

---

## Phase 2: User Story 1 - Multi-Core Parallel Track Rendering (Priority: P1) 🎯 MVP

**Goal**: Parallelize Pass 1 leaf track rendering in `Engine_Render.cpp` across real-time worker threads

**Independent Test**: Render a multi-track project with 16 synthesizer tracks; verify concurrent worker thread execution, zero audio dropouts, and identical output to sequential render.

### Implementation for User Story 1

- [x] T003 [US1] Instantiate `AudioThreadPool` on `Engine` in `src/native/nota.engine/src/Engine.h` and configure worker count during `setSampleRate` / engine setup on message thread
- [x] T004 [US1] Refactor Pass 1 leaf track rendering loop in `src/native/nota.engine/src/Engine_Render.cpp` into a parallel task batch dispatched to `AudioThreadPool`
- [x] T005 [US1] Ensure thread-safe scratch buffers (`Track::scratchBuffer` or per-worker buffers) so concurrent track processing never collides on shared scratch memory
- [x] T006 [US1] Implement barrier synchronization before Pass 1.5 group submixes and Pass 2 return tracks

**Checkpoint**: Independent tracks execute concurrently across multiple CPU cores with clean audio rendering.

---

## Phase 3: User Story 2 - True-Peak & Mixer SIMD Vectorization (Priority: P1)

**Goal**: Accelerate True-Peak FIR interpolation in `Ceiling.h` and mixer accumulation in `Engine_Render.cpp` using hardware SIMD

**Independent Test**: Measure CPU time in `Ceiling` with True-Peak enabled; verify $> 40\%$ reduction in processing time with identical inter-sample peak readings.

### Implementation for User Story 2

- [x] T007 [US2] Implement vectorized 16-tap FIR dot-product for `tpPush` in `src/native/nota.engine/src/Ceiling.h` using ARM NEON (`vld1q_f32`, `vmlaq_f32`) and x86 SSE/AVX (`_mm_loadu_ps`, `_mm_fmadd_ps` / `_mm_mul_ps`)
- [x] T008 [US2] Vectorize mixer sample accumulation and fader gain application in `src/native/nota.engine/src/Engine_Render.cpp` using SIMD FMA

**Checkpoint**: True-Peak limiter and mixer accumulation operate with SIMD acceleration and zero modulo operations.

---

## Phase 4: User Story 3 - Inner-Loop FastMath Acceleration (Priority: P2)

**Goal**: Eliminate transcendental standard library functions (`std::log10`, `std::pow`, `std::sin`) in inner audio loops

**Independent Test**: Run `Chamber` with active Ducking and `Reverb` with modulation; verify sonic transparency ($< 0.05\text{ dB}$ deviation) and reduced CPU usage.

### Implementation for User Story 3

- [x] T009 [US3] Replace per-sample `std::log10` and `std::pow` in `Chamber.h` ducking follower (`src/native/nota.engine/src/Chamber.h`) with `fast_math::linToDb` and `fast_math::dbToLin`
- [x] T010 [US3] Replace per-sample `std::sin` in `Reverb.h` modulation LFO (`src/native/nota.engine/src/Reverb.h`) with `fast_math::fastSin`
- [x] T011 [US3] Replace block-rate `std::pow(10.0f, db / 20.0f)` in `Ceiling.h` with `fast_math::dbToLin`

**Checkpoint**: All hot-path audio loops use fast approximations with verified numerical stability.

---

## Phase 5: User Story 4 - UI Telemetry Decimation (Priority: P3)

**Goal**: Eliminate per-sample atomic writes to scope ring buffers in `Compressor.h`

**Independent Test**: Inspect UI scope waveform in `Compressor`; verify smooth visual display while atomic writes are decimated to control rate.

### Implementation for User Story 4

- [x] T012 [US4] Decimate `keyRing_` writing in `Compressor.h` (`src/native/nota.engine/src/Compressor.h`) from per-sample to control rate (every 32 frames)

**Checkpoint**: UI telemetry writes are decoupled from audio-rate memory bus churn.

---

## Phase 6: Verification & Automated Smoke Tests

**Goal**: Comprehensive validation of audio fidelity, multi-core scaling, and real-time safety (AR-6)

- [x] T013 Add automated multi-core parallel rendering and True-Peak SIMD verification tests in `tests/Nota.SmokeTest/`
- [x] T014 Verify Real-Time Audio Rule AR-6 across all touched files (zero heap allocations, zero mutexes, zero blocking I/O)
- [x] T015 Run full clean build and smoke tests via `./scripts/build.sh`

---

## Phase 7: Polish & Documentation

- [x] T016 Ensure two-line AGPL-3.0-only SPDX headers on newly added files
- [x] T017 Update `CHANGELOG.md` under `[Unreleased]` documenting multi-core parallel rendering, SIMD True-Peak, and DSP optimizations
- [x] T018 Run `/speckit-converge` to verify all specification requirements are met

---

## Dependencies & Execution Order

- **Setup (Phase 1, T001–T002)**: Prerequisite for all subsequent phases.
- **Multi-Core Parallelism (Phase 2, T003–T006)**: Depends on T001. Delivers primary scalability for large sessions.
- **SIMD Vectorization (Phase 3, T007–T008)**: Depends on T002. Delivers True-Peak limiter performance.
- **FastMath Acceleration (Phase 4, T009–T011)**: Can proceed in parallel with Phase 2/3.
- **Telemetry Decimation (Phase 5, T012)**: Independent quick-win optimization.
- **Verification & Polish (Phases 6 & 7, T013–T018)**: Final quality gate before merging.
