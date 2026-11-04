# Bug Verification: Take-lane review follow-ups (#187)

- **Slug**: take-lane-review
- **Tested**: 2026-11-04
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The join no longer dips, a comp cut before the next audio block uses the new tempo, and a moved clip no longer highlights the wrong part of its take. The full Linux smoke suite passed, including the render-pool guards.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix) | `Nota.SmokeTest.dll --takes` | pass | Join level, pre-block comp cut, pre-block record cut, reversed flatten, cross-track fade |
| New / updated tests | same run, `TAKES PASSED` | pass | Flatten of a fully merged comp still matches; a reversed neighbour does too |
| Regression suite | `Nota.SmokeTest.dll` (full), rerun after the review fixes | pass | `SMOKE TEST PASSED`, exit 0 |
| Highlight | develop app vs this branch, same project | pass | Moved section stays on the track; its lane range is dim after the fix |
| Lint / type-check | `dotnet build Nota.sln -c Release` | pass | Existing warnings only |
| macOS / Windows | — | not-run | Linux only in this environment |

## Output Excerpts

```
[PASS] a butt-joined take section does not dip (head 0, join 0.2192/0.1414, scale 0.7071)
[PASS] comping after a tempo change, before the next block, cuts neighbours on the new tempo
[PASS] stopping a recording after a tempo change, before the next block, cuts one beat
[PASS] a reversed take section stays full-level at the join (head 0, join 0.2121/0.1414)
[PASS] flatten keeps the full-level join (first mismatch -1)
[PASS] moving one half of a join onto another track fades the edge that was shared (0)
TAKES PASSED
SMOKE TEST PASSED
EXIT:0
```

The mixer applies a constant pan-law gain (~0.707), so the join is compared with a full-level frame of the same clip rather than the raw sample. Frame 0 is still silence.

Second review round: `TakeLaneTests` adds the reopen of a flattened join, a deactivated half, and undo/redo across comp + flatten (macOS, `scripts/build.sh`).

## Residual Risks

- The highlight check is a screenshot, not a smoke assertion: `Nota.SmokeTest` does not reference `Nota.App`.
- Scanner self-test and `--scan-scale` were not re-run.

## Recommendation

The three review comments on #403, and the four on this follow-up, are fixed on this branch. Reference #187; do not reopen it. The pair-scan note is addressed by looking up each section's group once and walking boundaries in start order.
