# Bug Fix: render threads stay parked while a mixed project overruns the buffer

- **Slug**: consort-arp-dropout-storm
- **Fixed**: 2026-11-03
- **Assessment**: ./assessment.md
- **Status**: applied (engine change + regression guard). The live demo21 storm from the
  report no longer reproduces on either build, so the live before/after is inconclusive. See
  Local Verification.

## Summary

The live-device pool decision now looks at the whole block, not only the poolable built-in
leaves. `render()` measures the serial share (block time minus time inside the pool's
`dispatch`) and a decaying score of near-late blocks. The pool joins when built-ins plus the
serial share don't fit `kRtSizeLoad` of the period, or when near-late blocks pile up, and sizes
its helpers to the budget the serial share leaves. Before, it joined only when the built-ins
alone averaged a whole period, so a block that overran because of plug-ins (always serial)
plus built-ins kept all render threads parked.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Engine.h` | modified | `serialFrac_`, `nearLate_`, `dispatchNs_`; `kRtNearLate` 0.9, `kRtLateJoin` 1.8, `kRtMinBudget` 0.2, documented next to `kRtSizeLoad` |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | `render()`: per-block serial fraction (1/16 smoothed) and near-late score (×0.9 decay). `mixGraph`: dispatch wall time; join on `probeMissed`, or when alone and `leaf > period` or (`leaf > 0.2·period` and (`leaf + serial > 0.85·period` or `nearLate ≥ 1.8`)); helpers from `max(0.2·period, 0.85·period − serial)`; leave only when `parWork + serial ≤ period` and `nearLate < 1`; the probe counts the serial share |
| `tests/Nota.SmokeTest/RenderPoolTests.cs` | added test | `MixedSession` + check 3c |
| `CHANGELOG.md` | modified | `Fixed` entry (#450) |

## Tests Added or Updated

- `RenderPoolTests` 3c, "serial share + built-ins". Armed tracks playing another track's MIDI
  are the serial leaves; in the field these are plug-ins. Noise-clip audio tracks with Reverb
  are the poolable ones. Both are calibrated to about 0.6 of a 64-frame period, so one thread
  needs about 1.2 periods.
  - The pool must join: render threads > 50 ms CPU.
  - At most 10 % of blocks late after warm-up.

  With the old decision (reproduced by the first build of this fix, where the serial
  measurement read 0) the threads used **0 ms** and **2625 of 2625** blocks were late. With the
  fix: threads join (2.4–2.6 s CPU), 30–130 of 2625 late depending on machine noise.

## Local Verification

- `scripts/build.sh` passes, except the two pre-existing `RenderPoolTests` "overloaded" checks.
  They are noisy on this Mac: in one `--perf` run they passed with this change, 7 of 4125
  late; in the full run they failed, 99 of 4125, as they do on `develop`.
- Live, `demo21.nota`, bundles of `fix/plugin-block-midi` (before) and this branch (after),
  driven over MCP, loop 0–32:

  | Build | 44.1 kHz / 64, 90 s | 48 kHz / 64, 120–150 s |
  |---|---|---|
  | before (#438 head) | 0 dropouts, `dspLoad` 0.23 | 0 dropouts, 0.24 |
  | after (this fix) | 0 dropouts, 0.23 | 4 (one burst at 07:46:12), 0.25 |

  The 2026-11-02 storm with Splice and Maschine handled (about 500 per 30 s, mostly gone when
  Consort was muted) did not come back on 2026-11-03 with either build. Those measurements
  were taken while builds, `sample` runs and the LaunchServices throttling from Splice's
  per-block lookups were still in play on the machine. So the live case for this change is
  unproven. The engine gap it closes is shown only by the synthetic guard.

## Deviations from Assessment

- The assessment's root-cause hypothesis is only half confirmed. The scheduling blind spot is
  real and reproducible (guard 3c). Whether it caused the demo21 storm is not: demo21 is clean
  today without the fix.
- The late-block bound of the new guard is 10 %, not the 1–2 % sketched. This Mac's noise
  alone reaches 5 % in a full smoke run, and the old behaviour is 100 %.

## Follow-ups

- Reproduce the demo21 storm again before claiming this fixes #450 in the PR. If it never
  returns, ship this as "render threads join on whole-block overruns" and close #450 as
  not reproducible, or keep it open with the guard as the evidence.
- Watch for the 4-dropout burst: if live runs show bursts at join time, consider a lower join
  cost (pre-waking workers one block ahead) or a higher `kRtLateJoin`.
- Plug-in leaves on the pool (follow-up of #436) remains the larger lever for plug-in-heavy
  projects (`kontakt_problem.nota`).
