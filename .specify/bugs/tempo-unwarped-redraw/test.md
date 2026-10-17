# Bug Verification: un-warped audio clips keep their width when the tempo is changed in the transport bar

- **Slug**: tempo-unwarped-redraw
- **Tested**: 2026-10-18
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

With the fix, dragging the transport bar's BPM field resizes un-warped audio clips live. The
reporter confirmed this in a local Release build on Linux. The smoke test passes. Warped
clips were not checked by hand.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix) | Local build (this branch merged with `fix/mcp-tempo-ui-sync`), project with un-warped audio clips, drag the BPM field | pass | Reporter: "clips resized live" |
| Ruler modes | Same, with the ruler in Bars and in Time | pass | Reporter tried both while reproducing; the fix's refresh ends in `Redraw()`, which repaints the ruler |
| Fast drag | Drag the BPM field quickly back and forth | pass | Reporter: "seemed stable"; no stutter reported |
| Warped clips unchanged | Change tempo with a warped clip on screen | not-run | The test project had no warped clips |
| New / updated tests | none | not-run | No UI test harness, as the assessment says |
| Regression suite | `dotnet run --project tests/Nota.SmokeTest -c Release` | pass | `SMOKE TEST PASSED` |
| Build | `dotnet build Nota.sln -c Release -nodeReuse:false -m:1` | pass | 0 errors, no new warnings |

## Output Excerpts

```
SMOKE TEST PASSED
```

## Residual Risks

- Warped clips weren't checked by hand. Their `LengthBeats` is tempo-independent, so the
  extra refresh should redraw them unchanged.
- Tested on Linux only (Ubuntu 26.04 x64). The change is platform-neutral window wiring.
- Peak refetch cost during a drag on a project with many long un-warped clips wasn't
  measured (see the follow-up in `fix.md`).

## Recommendation

Close the bug. It's verified end-to-end on Linux by the reporter. A maintainer may want to
glance at a warped clip during a tempo drag on macOS or Windows.
