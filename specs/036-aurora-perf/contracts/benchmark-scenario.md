# Contract: `aurora-supersaw-2x` benchmark scenario

**Feature**: `036-aurora-perf` | **Date**: 2026-10-21

Extends the existing `tests/perf/nota_perf` CLI (see its header comment for the
base contract). No new binary, no new flags — one new scenario id in the same
output schema, so `bench-engine.sh` and `perf-gate.sh` pick it up unchanged.

## Invocation

```text
nota_perf list              # includes: aurora-supersaw-2x
nota_perf run aurora-supersaw-2x [seconds]
```

## Scenario setup (through the public C ABI only, AR-7)

- Engine at 48 kHz; two instrument tracks, built-in kind 5 (Aurora).
- Super Saw factory params applied per track via `nota_plugin_param_set`
  (param lookup by id via `nota_plugin_param_id` — no hardcoded indices).
- One MIDI clip per track: 6-voice chord loop (whole-note chords + off-beat
  stabs, same vocabulary as the existing `fillClip` passage, transposed apart
  so voices don't share pitch).
- Offline render in 512-frame blocks (the harness's global block size; the
  rate-normalized `wall_ms_per_s` keeps the numbers comparable across setups).

## Output schema (identical to existing scenarios)

```text
aurora-supersaw-2x <TAB> Aurora Super Saw x2 <TAB> <wall_ms_per_s> <TAB> <peak>
```

- `wall_ms_per_s`: wall-clock milliseconds of render per second of audio.
- `peak`: peak absolute sample value; runs with peak near zero are invalid
  (broken render must never read as "fast").

## Acceptance mapping

- SC-002: p99 `wall_ms_per_s` over the run `< 500` (1 s of audio in ≤ 0.5 s wall time — equivalently p99 block time < 5.33 ms at 48 kHz / 512 frames).
- SC-004: two consecutive runs on an idle reference machine agree within ±10%.
