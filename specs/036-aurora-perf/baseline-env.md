# Baseline environment: 036-aurora-perf

Reference machine for all acceptance numbers in this feature (spec FR-001,
SC-004). M2 numbers are NOT acceptance evidence.

## Machine (recorded 2026-10-21, T001)

- CPU: 12th Gen Intel Core i7-12700 (12 cores / 20 threads, up to 4.9 GHz)
- RAM: 31 GB
- OS: Linux (see `uname -srm` at run time)
- NOTE: CPU frequency scaling observed at 54% during recording — for
  reproducible benchmark runs (SC-004, ±10%) prefer an idle machine and,
  if needed, the performance governor.

## Audio config (`audio.json` at recording time)

- `bufferFrames`: 0 (= device default)
- `sampleRate`: 0.0 (= device default)
- Backend: miniaudio (PulseAudio/ALSA runtime)

## Baseline runs

| Date | Commit | Scenario | wall_ms_per_s (run 1 / run 2) | Peak | Notes |
|------|--------|----------|-------------------------------|------|-------|
| 2026-10-21 | 036-aurora-perf:T031 (pitch early-out + chorus hoist) | `aurora-supersaw-2x` | 26.63 / 28.45 (Δ 6.6%) | 0.79 | combined Tier-1 ≈ 2.6× vs baseline; null −240 dBFS PASSED |
| 2026-10-21 | T032 verdict | — | 28 ≪ 500 ms/s budget | — | budget CLOSED → Phase 6 (Tier-2) SKIPPED, no sound-risking changes |
| 2026-10-21 | 036-aurora-perf:T050+T051 (engine) | `aurora-supersaw-2x` | 28.76 | 0.79 | offline path intentionally unchanged; null-vs-base PASSED (engine edits don't alter renders) |
| 2026-10-21 | T052 note | live cold-start | — | — | not runnable headless: offline + determinism verified; live-device playback check is manual (user) |
| 2026-10-21 | 036-aurora-perf:T030 cross-build null | 10 presets base-vs-feature | −240 dBFS all | — | bit-identical, gate −120 dB PASSED |
| _TBD (T032)_ | _TBD_ | `aurora-supersaw-2x` | _TBD_ | _TBD_ | post Tier-1 |
| _TBD (T044)_ | _TBD_ | `aurora-supersaw-2x` | _TBD_ | _TBD_ | post Tier-2 (if run) |
| _TBD (T052)_ | _TBD_ | `aurora-supersaw-2x` | _TBD_ | _TBD_ | post engine tweaks |

## Profiler snapshot (T012)

Method: gdb sampling (no `perf`/Valgrind on machine), 100 live samples × all
threads on `aurora-supersaw-2x`. Pool workers parked in 100% of samples —
offline 2-track render runs fully on the main thread (pool never engages).

| Rank | Frame | Hits/100 | Reading |
|------|-------|----------|---------|
| 1 | `WavetableSynth::oscStack` | ~145 frame hits | dominant DSP cost — Tier-1 target confirmed |
| 2 | `exp2f64` (libm) | 10 | per-unison/per-voice `exp2` caught red-handed |
| 3 | `WavetableSynth::chorus` | 10 | global FX, secondary |
| 4 | `__sin_fma` (libm) | 3 | LFO/chorus `sin` present |
| 5 | `filterPair` / `warpRead` | 3 each | filters cheaper than oscillators here |

Verdict: R3 hotspot ranking CONFIRMED — Phase 5 (Tier-1 hoisting) aims at the
right code. Pool idleness additionally justifies Phase 7 engine work.
