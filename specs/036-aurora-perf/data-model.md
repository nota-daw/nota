# Data Model: Aurora CPU Optimization

**Feature**: `036-aurora-perf` | **Date**: 2026-10-21

No persistent data changes. The entities below are test/benchmark artifacts
(from spec Key Entities), defined so plan, tasks, and tests use one vocabulary.

## Reference scenario

The fixed yardstick for every measurement in this feature.

- **Fields**: two Aurora instances (built-in kind 5) · factory Super Saw preset params (table 0, both oscillators on, full unison, spread, drive/chorus/reverb amounts per `FactoryPresetCatalog`) · fixed polyphonic passage (6-voice chords, ≥5 min or looped equivalent) · 48 kHz · 256-frame buffer · x86 Linux reference machine (model + backend + settings recorded with results).
- **Validation**: identical setup twice on an idle machine reproduces p50/p99 within ±10%.
- **Relationships**: consumed by Benchmark report; presets rendered by Null-test comparison.

## Null-test comparison

One before/after render pair per factory Aurora preset.

- **Fields**: preset id · pre-change render (offline, fixed seed/clip) · post-change render · residual level in dBFS.
- **Validation rules**: Tier-1 step passes iff residual `< -120 dB` (bit-identical); Tier-2 step passes iff residual `< -50 dB`, else the step is reverted.
- **State transitions**: pending → pass → (absorbed into suite) | pending → fail → (change reverted, no retry in the same PR).

## Benchmark report

One line per run of the reference scenario.

- **Fields**: scenario id (`aurora-supersaw-2x`) · label · wall ms per second of audio · peak level (non-silence proof) · machine id · engine commit.
- **Validation rules**: peak must show signal (a "faster" silent render is invalid — same principle as 034 FR-012); machine + commit must be recorded or the number is meaningless.
- **Relationships**: produced by the benchmark scenario contract; consumed by SC-002/SC-004 verdicts.
