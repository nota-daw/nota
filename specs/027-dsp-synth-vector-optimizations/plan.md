# Implementation Plan: Polyphonic Synth FastMath & SIMD Buffer Inspection Acceleration

**Branch**: `027-dsp-synth-vector-optimizations` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/027-dsp-synth-vector-optimizations/spec.md`

## Summary

Optimize polyphonic synthesizer voice generation across `OperatorSynth`, `WavetableSynth`, `PendulumSynth`, `VoltSynth`, `FluxSynth`, and `BassSynth` using FastMath approximations and loop-invariant hoisting. Accelerate audio buffer silence inspection and energy tracking in `AudioBufferUtils.h` and `Engine_Render.cpp` using vectorized SIMD primitives (`simd::isSilent`, `simd::peakAndSumSquares`, `simd::clip`). Accelerate mixer summing and routing across `Engine_Render.cpp` and `AudioBufferUtils.h`: vectorize leaf and group post-fader gain with sidechain route tap accumulation (`addStereoScaled`), pre-FX route taps (`simd::add`), and master bus safety net (`processMasterBus`) with branchless NaN/Inf suppression and SIMD clipping. Decimate UI spectrum telemetry atomic updates to eliminate audio-thread cache line invalidation.

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions: zero allocations, zero mutexes, zero blocking I/O, AR-7 pure C ABI).
- **A built-in instrument / audio or MIDI effect** → the `nota-instrument` skill.
- **Build / packaging** → [`BUILD.md`](../../BUILD.md).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing (AGPL-3.0-only headers), smoke tests must pass.

## Technical Context

**Language/Version**: C++20 (native engine, `src/native/nota.engine`), C# / .NET 10 (`tests/Nota.SmokeTest`)

**Primary Dependencies**: None (pure C++20 / standard library; ARM NEON and x86 SSE/AVX hardware intrinsics)

**Storage**: N/A (DSP algorithmic and vectorization improvements only)

**Audio-Thread Impact**: Direct execution on the audio rendering callback thread. Zero dynamic memory allocations, zero locks, zero blocking calls (AR-6). Replaces CPU-heavy transcendental instructions and scalar loops with single-cycle SIMD vector instructions and register-based polynomial evaluations.

**Testing**: `./scripts/build.sh` driving `tests/Nota.SmokeTest` end-to-end verification.

**Target Platform**: macOS (Apple Silicon ARM64, Intel x86_64), Linux (x86_64), Windows (x86_64).

**Project Type**: Real-time DAW audio engine (`nota.engine`).

**Performance Goals**:
- 30%–50% CPU reduction on 8-voice polyphonic synth playback.
- 50%–75% CPU reduction on buffer silence inspection and `SilenceDetector::process()`.
- Zero audio dropouts or xruns under high polyphony.

**Constraints**:
- Must adhere strictly to AR-6 (no allocation on the audio thread).
- Bit-exact or perceptually indistinguishable audio quality ($< 0.05\text{ dB}$ difference).
- No C ABI changes required (`nota_engine.h` remains stable).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Principle I: Real-Time Audio Safety**: No allocations, no mutexes, no blocking calls on audio thread. All SIMD algorithms operate in-place or over pre-allocated stack/buffer memory.
- [x] **Principle II: Boundary Discipline**: Internal engine implementation details; no changes to the pure C ABI (`nota_engine.h`).
- [x] **Principle III: Licensing Hygiene**: All files remain AGPL-3.0-only with SPDX headers. No new external dependencies introduced.
- [x] **Principle IV: Verification Gate**: `./scripts/build.sh` smoke test must pass 100% green.
- [x] **Principle V: One Logical Change per PR**: Focused specifically on polyphonic synth FastMath and SIMD buffer inspection.

## Project Structure

### Documentation (this feature)

```text
specs/027-dsp-synth-vector-optimizations/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 research & technical decisions
├── data-model.md        # Phase 1 data structures & SIMD signatures
├── quickstart.md        # Phase 1 verification & test guide
├── checklists/
│   └── requirements.md  # Quality validation checklist
└── tasks.md             # Phase 2 implementation task list
```

### Source Code (repository root)

```text
src/
└── native/nota.engine/src/
    ├── Simd.h                 # Generic SIMD primitives (add, addScaled, scale, mul, clip, maxAbs)
    ├── AudioBufferUtils.h     # Vectorize isBufferSilent, peakAndSumSquares, mixer meters, addStereoScaled, processMasterBus
    ├── OperatorSynth.h        # fastSin FM waveform, decimate ringW_ atomic writes
    ├── WavetableSynth.h       # Invariant hoisting + fastTanh drive, fastSin Fold/LFO
    ├── PendulumSynth.h        # fastSin additive harmonic oscillator and LFO
    ├── VoltSynth.h            # fastSin oscBlep, vibrato, and LFO
    ├── FluxSynth.h            # fastSin drift/sub, fastTanh saturation
    ├── BassSynth.h            # fastSin morph/sub/LFO, fastTanh drive
    └── Engine_Render.cpp      # Eliminate redundant buffer silence scans, vectorize route taps & master bus safety net
```

## Complexity Tracking

No constitution violations. All changes are internal engine DSP performance accelerations adhering to established project patterns.
