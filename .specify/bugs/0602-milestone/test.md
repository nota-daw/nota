# Bug Verification: Milestone 0.60.2 bugs

- **Slug**: 0602-milestone
- **Tested**: 2026-11-15
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Summary

The engine-level fixes are covered by new smoke checks that pass; #573's cause and fix were
confirmed in a headless Avalonia render. The full `scripts/build.sh` run passes apart from the
render-pool timing guards, which fail the same way on the unchanged develop engine on this
loaded machine. The UI fixes (#529, #604, #565, #538, #548, #445, #221, #164) are built and
reviewed but were not driven in the running app here; #221 on Windows / Linux, #358 and #544
are not exercised on their platforms.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| #467, #537, #530 | `dotnet run --project tests/Nota.SmokeTest -- --m0602` | pass | deactivated split, MIDI and audio envelope re-base, 200 warp markers read, emptied `ClipSet` |
| #573 | headless Avalonia: 1×/1.25×/2× `RenderTargetBitmap` drawn back with a dest rect vs. a pixel source rect | pass | dest-only cuts the bottom-right at 1.25× and 2×; the pixel source rect shows the whole frame at every scale |
| #559 | Valve checks in the smoke test | pass | Output / Mix now glide over ≤ 256 frames; the Auto Comp and Mix 0 checks measure the block after the glide |
| #450 | native harness: Consort + Arp, loop 0–16, 64-frame blocks, 90 s | not reproduced | voices stay at 1, block time steady (~10 µs avg) |
| Full build | `scripts/build.sh` | pass except render-pool timing | `overloaded` / `four indivisible VST3 jobs` also fail on the unchanged develop engine (`--perf`) |

## Not exercised

- The UI fixes in the running app (no GUI session here).
- #221 on Windows / Linux, #358 on Linux, #544 with KONTRA, #573 on a Windows 125 % display.
- #450, #552, #300, #596: no change; see the assessment.
