# Implementation Plan: DSP Core Performance & Optimization

**Branch**: `024-dsp-optimizations` | **Date**: 2026-10-17 | **Spec**: [specs/024-dsp-optimizations/spec.md](spec.md)

**Input**: Feature specification from `specs/024-dsp-optimizations/spec.md`

## Summary

Optimize real-time audio processing performance in Nota's C++20 audio engine across core native effects (`Delay`, `Compressor`, `Eq`, `Chamber`) and synthesis helpers. Implement a unified `FastMath.h` header for mathematical approximations (logarithmic, exponential, hyperbolic, and trigonometric operations), eliminate redundant loop recalculations via invariant hoisting, restructure `Eq.h` into chunked block-oriented biquad execution, and unroll the 8-point Fast Walsh-Hadamard Transform in `Chamber.h`, strictly preserving Real-Time Audio Safety (AR-6).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **Contributing** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing (AGPL-3.0-only headers), zero new dependencies without approval, smoke tests must pass.

## Technical Context

**Language/Version**: C++20 (native engine in `src/native/nota.engine/`).

**Primary Dependencies**: None (pure C++20 standard library + existing vendor libraries `hiir`, `dr_libs`).

**Storage**: N/A (DSP algorithmic optimization only).

**Audio-Thread Impact**: Direct and beneficial. Optimizes CPU time in audio callbacks while strictly adhering to AR-6: zero heap allocations, no mutexes or locks, no I/O, no exception throwing.

**Testing**: `tests/Nota.SmokeTest` driven by `scripts/build.sh` (or `dotnet test`).

**Target Platform**: Universal macOS (ARM64 Apple Silicon + x86_64 Intel), Linux (x86_64, aarch64), Windows (x86_64, aarch64).

**Project Type**: Real-time audio engine shared library (`libnota_engine.dylib` / `.so` / `.dll`).

**Performance Goals**:
- 15–25% reduction in CPU time across target DSP devices (`Delay`, `Compressor`, `Eq`).
- Zero audible degradation (< 0.05 dB relative error across audio bands).

**Constraints**:
- Strict real-time safety (AR-6).
- Deterministic, denormal-safe arithmetic (FTZ/DAZ preserved).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
| :--- | :--- | :--- |
| **I. Real-Time Audio Safety** | PASS | Zero allocations, zero locks, zero file I/O in all audio paths. |
| **II. Boundary Discipline** | PASS | Pure internal engine changes; C ABI in `nota_engine.h` is untouched. JUCE untouched. |
| **III. Licensing Hygiene** | PASS | All files AGPL-3.0-only; no new external dependencies. |
| **IV. Verification Gate** | PASS | `scripts/build.sh` and `Nota.SmokeTest` will validate end-to-end functionality. |
| **V. One Logical Change** | PASS | Single cohesive PR focused on core DSP performance optimizations. |
| **VI. Documentation Sync** | PASS | Architectural notes documented in feature spec and research artifacts. |
| **VII. Spec-First** | PASS | Follows full spec-kit workflow: `spec.md` → `research.md` → `data-model.md` → `plan.md` → `tasks.md`. |
| **VIII. Safety Rules** | PASS | No rules bent or bypassed. |

## Project Structure

### Documentation (this feature)

```text
specs/024-dsp-optimizations/
├── spec.md              # Feature specification
├── checklists/
│   └── requirements.md  # Spec quality checklist
├── research.md          # Phase 0: mathematical formulations & research decisions
├── data-model.md        # Phase 1: data structures & memory invariants
├── contracts/
│   └── dsp-interfaces.md # Phase 1: internal API contracts
├── quickstart.md        # Phase 1: validation & verification steps
├── plan.md              # This implementation plan
└── tasks.md             # Phase 2: implementation tasks (speckit-tasks output)
```

### Source Code Touched

```text
src/native/nota.engine/src/
├── FastMath.h           # NEW: centralized fast math approximations
├── Delay.h              # MODIFIED: invariant hoisting & fast LFO
├── Compressor.h         # MODIFIED: fast log2/exp2 in detector & gain stages
├── Eq.h                 # MODIFIED: block-based biquad processing
├── Chamber.h            # MODIFIED: unrolled hadamard8, fastTanh in loopLimit/softClip, branch hoisting, unrolled diffusion
├── Pentad.h             # MODIFIED: include FastMath.h to reuse unified implementations
├── Vintage.h            # MODIFIED: fastTanh in saturation curve & precomputed constants, fastSin in wow/flutter
├── AutoFilter.h         # MODIFIED: fastTanh in drive & analog saturation, fastSin in LFO
├── AutoPan.h            # MODIFIED: invariant hoisting for invTanhKk, fastSin in LFO
├── BassSynth.h          # MODIFIED: invariant hoisting for preDrive makeup outside sample loop
├── DynamicEq.h          # MODIFIED: fast_math::linToDb in 4-band envelope detector, exp2f in modulation
└── Ceiling.h            # MODIFIED: invariant hoisting for invCeil, fastTanh in softClip
```

**Structure Decision**: Keep all optimizations within `src/native/nota.engine/src/`, creating `FastMath.h` as an internal header directly included by DSP device headers.

## Complexity Tracking

*No constitutional violations. Table left empty.*
