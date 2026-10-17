# Implementation Plan: DSP Silence Detection & Dynamic Sleep Bypass

**Branch**: `025-dsp-silence-bypass` | **Date**: 2026-10-17 | **Spec**: [specs/025-dsp-silence-bypass/spec.md](spec.md)

**Input**: Feature specification from `specs/025-dsp-silence-bypass/spec.md`

## Summary

Implement dynamic silence detection and auto-sleep bypass across Nota's C++20 audio engine. The engine skips processing on idle audio/instrument tracks and sleeping effect devices when input and internal states/tails are digital silence (< -100 dBFS). Time-based effects (`Delay`, `Chamber`, `Reverb`, `Compressor`, `Ceiling`) maintain a 50 ms hangover window to allow acoustic tails and release envelopes to naturally decay before sleeping, while memoryless devices (`Eq`, `AutoPan`, `Utility`) bypass immediately. Waking from sleep occurs synchronously on sample 0 of the first non-silent block, strictly adhering to Real-Time Audio Rule AR-6.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **Contributing** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, zero new dependencies, smoke tests must pass.

## Technical Context

**Language/Version**: C++20 (`src/native/nota.engine/`).

**Primary Dependencies**: None (internal engine C++20).

**Storage**: N/A.

**Audio-Thread Impact**: Direct and beneficial. Eliminates redundant CPU cycles on silent blocks while strictly upholding AR-6: zero heap allocations, zero mutexes, zero blocking calls.

**Testing**: `tests/Nota.SmokeTest` via `scripts/build.sh`.

**Target Platform**: Universal macOS (Apple Silicon ARM64, Intel x86_64), Linux (x86_64, aarch64), Windows (x86_64, aarch64).

**Project Type**: Real-time audio engine shared library (`libnota_engine`).

**Performance Goals**:
- 60% reduction in idle baseline CPU usage during empty playback.
- 30–50% reduction in CPU load on medium-to-large multi-track projects where only a subset of tracks sound simultaneously.

**Constraints**:
- Strict real-time safety (AR-6).
- Zero audible clicks, pops, or truncated reverb tails (< -100 dBFS cutoff).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
| :--- | :--- | :--- |
| **I. Real-Time Audio Safety** | PASS | Zero allocations, zero locks, zero file I/O in all audio paths. State variables are inline scalar primitives. |
| **II. Boundary Discipline** | PASS | Internal engine changes; pure C ABI in `nota_engine.h` is untouched. JUCE untouched. |
| **III. Licensing Hygiene** | PASS | All files AGPL-3.0-only; no new external dependencies. |
| **IV. Verification Gate** | PASS | `scripts/build.sh` and `Nota.SmokeTest` validate end-to-end functionality. |
| **V. One Logical Change** | PASS | Single cohesive PR focused on DSP silence detection and sleep bypass. |
| **VI. Documentation Sync** | PASS | Documented in feature spec and research artifacts. |
| **VII. Spec-First** | PASS | Follows full spec-kit workflow: `spec.md` → `research.md` → `data-model.md` → `plan.md` → `tasks.md`. |
| **VIII. Safety Rules** | PASS | No rules bent or bypassed. |

## Project Structure

### Documentation (this feature)

```text
specs/025-dsp-silence-bypass/
├── spec.md              # Feature specification
├── checklists/
│   └── requirements.md  # Spec quality checklist
├── research.md          # Phase 0: silence algorithms & sleep state machines
├── data-model.md        # Phase 1: data structures & state primitives
├── contracts/
│   └── dsp-interfaces.md # Phase 1: internal API contracts
├── quickstart.md        # Phase 1: validation & verification steps
├── plan.md              # This implementation plan
└── tasks.md             # Phase 2: implementation tasks (speckit-tasks output)
```

### Source Code Touched

```text
src/native/nota.engine/src/
├── AudioBufferUtils.h   # Utility: isBufferSilent()
├── Device.h             # Device base class: isIdle(), hasTail(), wake()
├── Delay.h              # Tail countdown & sleep lifecycle
├── Chamber.h            # Reverb tank tail countdown & sleep lifecycle
├── Compressor.h         # Envelope release tail & sleep lifecycle
├── Ceiling.h            # Lookahead drain & limiter sleep lifecycle
├── Eq.h                 # Biquad state drain & instant silence bypass
├── AutoPan.h            # Stateless instant silence bypass
└── Engine_Render.cpp    # Track-level silence evaluation in mixGraph()
```

**Structure Decision**: Keep all changes within `src/native/nota.engine/src/`. Primitives are added directly to `Device` and evaluated in `Engine::mixGraph()`.

## Complexity Tracking

*No constitutional violations. Table left empty.*
