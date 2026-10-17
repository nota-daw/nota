# Implementation Plan: Multi-Core Parallel Track Rendering & SIMD DSP Acceleration

**Branch**: `026-dsp-parallel-vectorization` | **Date**: 2026-10-17 | **Spec**: [specs/026-dsp-parallel-vectorization/spec.md](spec.md)

**Input**: Feature specification from `specs/026-dsp-parallel-vectorization/spec.md`

## Summary

Implement multi-core parallel track rendering, hardware SIMD vectorization, and inner-loop math optimizations across Nota's C++20 real-time audio engine. Independent leaf tracks in `Engine_Render.cpp` are scheduled concurrently across pre-allocated worker threads using lock-free task distribution. The True-Peak limiter in `Ceiling.h` is accelerated by replacing scalar circular buffer indexing with double-buffered history arrays and SIMD dot products (ARM NEON / AVX). Inner-loop transcendental math in `Chamber.h` and `Reverb.h` is replaced with `FastMath.h` approximations, and UI telemetry writes in `Compressor.h` are decimated to control rate.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions: no allocations, no locks, no blocking calls, AR-7 pure C ABI).
- **Contributing** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, zero new dependencies, smoke tests must pass.

## Technical Context

**Language/Version**: C++20 (`src/native/nota.engine/`).

**Primary Dependencies**: None (internal engine C++20, standard compiler SIMD intrinsics `<arm_neon.h>` and `<immintrin.h>`).

**Storage**: N/A.

**Audio-Thread Impact**: Direct and high. Enables multi-core utilization while strictly preserving AR-6: all thread resources are pre-allocated on the message thread; task distribution uses atomic counters and non-blocking barriers.

**Testing**: `tests/Nota.SmokeTest` via `scripts/build.sh`.

**Target Platform**: Universal macOS (Apple Silicon ARM64, Intel x86_64), Linux (x86_64, aarch64), Windows (x86_64, aarch64).

**Project Type**: Real-time audio engine shared library (`libnota_engine`).

**Performance Goals**:
- 200%–400% higher rendering throughput on 8+ core machines for large multi-track sessions.
- 40%+ reduction in True-Peak limiter CPU time in `Ceiling.h`.
- 25%+ reduction in Ducking follower CPU time in `Chamber.h`.

**Constraints**:
- Strict real-time safety (AR-6).
- Zero audio dropouts or priority inversions.
- Bit-exact or psychoacoustically transparent audio ($< 0.05\text{ dB}$ deviation).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Real-Time Audio Safety | Pass | Threads pre-allocated on message thread; atomic synchronization with zero mutexes or heap allocations. |
| II. Boundary Discipline | Pass | Engine internal C++20 optimization; C ABI remains pure C. |
| III. Licensing Hygiene | Pass | AGPL-3.0-only; zero new third-party dependencies. |
| IV. Verification Gate | Pass | Verified via `Nota.SmokeTest` and `scripts/build.sh`. |
| V. Single Logical Scope | Pass | Focused entirely on core DSP execution efficiency and parallelism. |
| VI. Documentation Sync | Pass | `CHANGELOG.md` updated under `[Unreleased]`. |
| VII. Spec-First | Pass | Specification, research, data model, and plan fully generated in `specs/026-dsp-parallel-vectorization/`. |
| VIII. No Rule Weakening | Pass | AR-6 constraints fully honored. |

## Project Structure

### Documentation (this feature)

```text
specs/026-dsp-parallel-vectorization/
├── spec.md              # Feature specification
├── plan.md              # Implementation plan (this file)
├── research.md          # Concurrency & SIMD research
├── data-model.md        # Concurrency & SIMD structures
├── quickstart.md        # Build & test instructions
├── contracts/           # SIMD & thread pool interfaces
└── checklists/          # Requirements quality checklist
```

### Source Code Touched

```text
src/native/nota.engine/src/
├── AudioThreadPool.h    # Pre-allocated lock-free worker thread pool for parallel track rendering
├── Engine_Render.cpp    # Multi-threaded Pass 1 leaf track execution & SIMD mixer summing
├── Ceiling.h            # Double-buffered True-Peak state & SIMD FIR dot product
├── Chamber.h            # FastMath ducking follower
├── Reverb.h             # FastMath LFO modulation
└── Compressor.h         # Control-rate telemetry decimation
```
