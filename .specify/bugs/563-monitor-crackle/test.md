# Bug Verification: Monitoring an instrument track through an audio track crackles

- **Slug**: 563-monitor-crackle
- **Tested**: 2026-11-11
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Summary

The mechanism is reproduced and fixed in the engine: with blocks of varying length the monitored
signal had jumps of 0.389 before the fix and none after it (0.00044, the sine's own curvature),
at a fixed lag. The full smoke run passes apart from the render-pool timing checks, which fail
the same way on the unchanged engine on this loaded machine. Not exercised: the reporter's steps
in the app on a Bluetooth output — hence `partial`, not `verified`.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction, pre-fix (automated equivalent) | engine without the fix, dylib copied into `bin/`; `Nota.SmokeTest.dll --track-monitor` | fail (as expected) | varying blocks: worst jump 0.389, correlation 0.445 |
| Reproduction, post-fix (automated equivalent) | same, with the fix | pass | 5/5 |
| Reproduction in the app (instrument → audio track, Bluetooth output) | — | not-run | needs the real app + device by hand |
| New tests | `--track-monitor` | pass | fixed / varying / long-then-short blocks |
| Regression suite | `scripts/build.sh` (engine, solution, full smoke) | fail (3, unrelated) | all 4 FAIL lines are in `-- render pool --` (deadline / late-block timing) |
| Render-pool timing, fixed engine | `--perf` ×2 | fail | 2–3/100 deadlines, 200–243 late blocks |
| Render-pool timing, base engine | `--perf` ×2, fix stashed | fail | 41/100 deadlines, 163–210 late blocks: same failures without the fix |
| Lint / format | clang-format | skipped | not installed on this machine |

## Output Excerpts

```
== BEFORE FIX
  [FAIL] …and with blocks of varying length, as a rate-converting device asks (rms 0,129, worst jump 0,389)
  [FAIL] …at a fixed lag, the longest block (correlation 0,445)
== AFTER FIX
  [PASS] …and with blocks of varying length, as a rate-converting device asks (rms 0,141, worst jump 0,000442)
  [PASS] …at a fixed lag, the longest block (correlation 1,000)
  [PASS] …and the lag shrinks back once long chunks give way to short blocks (correlation 1,000, worst jump 0,000442)
```

`uptime` during the perf comparison: load averages 10.27 15.35 11.64.

## Residual Risks

- The reporter's trigger (Bluetooth output → varying callback sizes) is inferred from their
  `audio.json`, not observed in a live callback trace.
- The render-pool timing checks need a re-run on an idle machine (or CI) to confirm they pass.
- Sidechain keys and the armed input meter keep the per-segment route-bus read (follow-up).

## Recommendation

Hold for a by-hand check in the app (instrument track monitored through an audio track, on the
Bluetooth output, then on a loop), and re-run `scripts/build.sh` on an idle machine; then close
#563 with the PR.
