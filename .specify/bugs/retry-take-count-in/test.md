# Bug Verification: Discard and retry ignores count-in

- **Slug**: retry-take-count-in
- **Tested**: 2026-11-10
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Summary

The automated reproduction fails without the fix and passes with it. Three real-time
render-pool timing checks fail in the full smoke run, on a machine under heavy unrelated load
(load average ~9, Rider.Backend at ~420 % CPU). These code paths are not touched by the fix,
but they have not been checked on an idle machine. The bug has not yet been re-checked in the
app on a real audio interface.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (pre-fix) | `Nota.SmokeTest --retry-take`, engine change reverted | fail (as expected) | retried clip 6,01 beats = count-in + take |
| Reproduction (post-fix) | `Nota.SmokeTest --retry-take` | pass | retried clip at 8,00, 2,02 beats |
| New / updated tests | `RetryTakeTests.AudioFromStopWithCountIn` | pass | input fed through the real `onInputBlock` gate every block |
| Retry suite | `Nota.SmokeTest --retry-take` | pass | `RETRY TAKE PASSED` (MIDI count-in, punch-in, Session count-in included) |
| Full build + smoke | `scripts/build.sh` | fail (3) | render-pool deadline checks only; same 3 on a rerun |
| Full smoke, 3rd run | `Nota.SmokeTest` | fail (2) | only the 2 "overloaded" checks; load average 24, the VST3 deadline check passed this time |
| Manual, real interface | app, armed audio track, count-in, ⌘. | not-run | no hands-on run in this session |

## Output Excerpts

```
[FAIL] four indivisible VST3 jobs: 12/100 device deadlines missed after warm-up (<= 1)
[FAIL] isolated real VST3 render-pool regressions
[FAIL] overloaded: 261 of 4125 blocks late with render threads after warm-up (<= 1 %)
[FAIL] overloaded: at most 12 late blocks within 50 ms (<= 4: …)
SMOKE TEST FAILED (3)
```

Every other check passes.

The failing checks (`RenderPoolTests` 3b, `HostedRenderPoolTests`) are wall-clock deadline
budgets. They never start a count-in, so the `TransportPlay` count-in branch the fix touches
does not run in them. They go through `TimingBudget.Check`, which is not enforced under CI
because it is load-sensitive. How many fail tracks the machine load (3 → 2 as load went
9 → 24 with Rider indexing), not the change.

## Residual Risks

- The render-pool timing checks have not had a clean local pass on an idle machine. CI does
  not enforce them (`TimingBudget`).
- The test uses the pretend input device, not a real driver callback thread. The race it pins
  (the Stop command reopening the gate) is in engine command order and does not depend on the
  driver.

## Recommendation

The fix holds in the automated reproduction. Every non-timing check passes. Before closing,
hand-check ⌘. on an audio take with a count-in in the app on a real interface. Then open the
PR with `Closes #521`.
