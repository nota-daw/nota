# Bug Verification: Milestone 0.60.2 bugs, second batch

- **Slug**: 0602-milestone-2
- **Tested**: 2026-11-16
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: pass for the 8 fixed issues; 5 not changed (see the assessment)

## Summary

Each engine fix has a smoke check that fails on the unchanged engine and passes with the fix
(checked by building both). The UI fixes were driven on the real `MainWindow` in a headless
Avalonia harness. The full `scripts/build.sh` smoke run passes.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| #645, #649, #650, #644, #643 | `dotnet run --project tests/Nota.SmokeTest -c Release -- --m0602` | pass | 6 of the new checks fail on the old engine: Monolith repeats (×2), Volt mono stuck chord, clip deleted under a sounding note, open + closed hat in one choke group with humanize, stray hit on unmute |
| #647 | `--moves` | pass | `AfterRemoval` maps a delete and its undo |
| #646, #647, #648 | headless harness on `MainWindow` (`/private/tmp/n0602harness`) | pass | deleting the open clip closes the roll; an editor on a later clip follows it and goes back on undo; a click in the Arrangement takes the keyboard from the roll's grid and Delete takes the clip; a resize under a tool preview drops it, restores the clip and later previews don't throw |
| Sampler keytrack check | existing smoke check | pass after a test fix | it compared two renders whose voices carried over (Stop was never rendered before the next Play); with #649 the removed note is released, so each render now lets the Stop land first |
| Full build | `scripts/build.sh`, then the full smoke run | pass | |

## Not exercised

- The fixes in the running app with a mouse (headless only), Windows and Linux builds.
- #450, #544, #552, #300, #596: no change.
