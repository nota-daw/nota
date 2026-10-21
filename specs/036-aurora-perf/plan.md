# Implementation Plan: Aurora CPU Optimization

**Branch**: `036-aurora-perf` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/036-aurora-perf/spec.md`

## Summary

Two Aurora instances on the Super Saw factory preset stutter on x86 Linux
while Apple M2 stays clean. Research (`research.md`) attributes the gap to
per-sample transcendentals (glibc `exp2`/`tan`/`sin`), an SSE2-baseline x86
build, cache pressure from 14 wavetable reads/voice/sample, small Linux audio
buffers, and serial-first adaptive track parallelism. Approach, in strict
order: (0) reproduce with a new `aurora-supersaw-2x` benchmark scenario on
the reference machine; (1) bit-identical constant hoisting out of the sample
loop; (2) threshold-gated (`< -50 dB`) fast-math swaps, each revertible;
(3) engine buffering/parallelism fixes. No UI change, no C ABI change, no new
dependencies (Eco switch explicitly out of scope).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time.
- **Build / packaging** → [`BUILD.md`](../../BUILD.md).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): AGPL headers, dependency policy, PR expectations.
- Related: `specs/034-engine-perf-gate` (the regression gate this feature feeds a scenario into, not rebuilds).

## Technical Context

**Language/Version**: C++20 (engine DSP + `tests/perf` harness), C# / .NET 10 (smoke-test null coverage)

**Primary Dependencies**: None new. Uses existing `fast_math` (`src/native/nota.engine/src/dsp/math/FastMath.h`), existing `nota_perf` harness, existing `NullDb` smoke-test pattern.

**Storage**: N/A (no persistent data changes; benchmark/null renders are transient)

**Audio-Thread Impact** *(required — state explicitly)*: DSP edits stay inside existing render functions — no new allocation, locks, or I/O on the audio thread (AR-6); no new cross-thread data (AR-4/AR-5 patterns untouched); benchmark and null tests run off the audio thread.

**Testing**: `tests/perf/nota_perf` (+ `scripts/bench-engine.sh`, `scripts/perf-gate.sh`) for performance; `tests/Nota.SmokeTest` (new Aurora null-test class reusing the `NullDb` pattern) for sound preservation; `scripts/build-linux.sh` as the PR gate.

**Target Platform**: x86_64 Linux (reference machine for all acceptance numbers); must not regress macOS/Windows (034 gate + smoke test cover this).

**Project Type**: Desktop DAW (native real-time engine + managed UI).

**Performance Goals**: Reference scenario p99 block time < 50% of budget (< 2.66 ms/block at 48 kHz / 256 frames; < 500 wall-ms per second of audio); benchmark reproducible within ±10%.

**Constraints**: Tier-1 steps bit-identical (`< -120 dB` residual); Tier-2 steps `< -50 dB` residual or reverted; fully invisible (no UI, no preset changes); one logical change per PR (Constitution V) — each tier ships separately.

**Scale/Scope**: 2 files of DSP (`WavetableSynth.cpp`, possibly `.h`), 1 benchmark scenario, 1 smoke-test class, Linux buffer default + `rtParallel_` engagement tweak. No ABI, no managed changes beyond tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Real-time safety (AR-6)**: PASS — hoisting removes work from the audio thread; approximations are branch-free arithmetic; no alloc/lock/IO added. Benchmark/null-test code never runs on the audio thread.
- **II. Boundary discipline (AR-7, JUCE)**: PASS — no C ABI change (benchmark uses existing `nota_plugin_param_set`); no JUCE touched.
- **III. Licensing**: PASS — no new files without the two-line SPDX header; no new dependencies (Valgrind/perf are local/CI tools only, same as 034).
- **IV. Verification gate**: PASS — extends the smoke test (Aurora null tests); `build-linux.sh` stays the gate; 034 perf gate gains the new scenario.
- **V. One logical change per PR**: PASS by construction — Phase order maps to separate PRs (benchmark → Tier-1 → Tier-2 items → engine tweaks).
- **VI. Docs in sync**: PASS — user-visible effect gets a `CHANGELOG.md [Unreleased]` entry; no FEATURES.md surface (invisible optimization); `nota-docs`/`nota-features` skills at implementation time if anything user-visible emerges.
- **VII. Spec-first**: PASS — this plan + spec + (next) tasks.
- **VIII. No rule-weakening**: PASS — no exceptions requested.

*Post-Phase-1 re-check*: design adds no new surface (benchmark scenario reuses existing CLI schema; null tests reuse existing helper) — all gates still pass, no violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/036-aurora-perf/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── benchmark-scenario.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/native/nota.engine/src/devices/instruments/
├── WavetableSynth.cpp   # Tier-1 hoisting + Tier-2 fast-math (the only DSP file)
└── WavetableSynth.h     # signature changes only if hoisting needs them
src/native/nota.engine/src/engine/
└── Engine_Render.cpp    # rtParallel_ engagement (Tier-3, separate PR)
src/native/nota.engine/src/audio_io/
└── ...                  # Linux default buffer (Tier-3, separate PR)

tests/perf/nota_perf.cpp # + aurora-supersaw-2x scenario (Phase 0 PR)
tests/Nota.SmokeTest/    # + Aurora null-test class (Phase 0 PR)
scripts/                 # bench-engine.sh / perf-gate.sh reused unchanged
```

**Structure Decision**: Engine-only feature. Managed app, UI, pluginhost, and
packaging branches are untouched and omitted. Test code lives with the
existing harnesses it extends.

## Complexity Tracking

> No Constitution Check violations — nothing to justify. This section stays empty.
