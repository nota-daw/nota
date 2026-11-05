# Bug Verification: playback stutters while an audio clip is open in the Clip panel

- **Slug**: audio-editor-playhead-redraw
- **Tested**: 2026-10-30
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified on Linux; not tested on Windows

## Summary

With the fix, playing through a clip that's open in the Clip panel no longer repaints the
audio editor's waveform view: full waveform repaints drop from about 29 per second to none,
and the playhead line still moves across the editor. The smoke test passes. The reporter's
platform (Windows) wasn't available, so the fix targets the likely cause rather than a
reproduced stutter.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Repaint count, before | `develop` (db9990a) with a temporary counter in `WaveformView.Render`; 60 s audio clip open in the Clip panel; 8 s samples stopped, playing outside the clip, playing inside it | 0 / 0 / 29.9 and 0 / 0 / 28.0 per s (2 runs) | The counter isn't committed |
| Repaint count, after | Same on this branch | 0 / 0 / 0 per s (2 runs) | |
| Playhead still drawn | Seek into the clip, play, screenshot the editor | pass | Line moves across the waveform in step with the arrangement |
| Editor looks the same | Screenshot with the clip open and stopped, before vs after | pass | Identical |
| New / updated tests | none | not-run | No UI test harness; the engine is untouched, so no `RenderPoolTests` guard |
| Build + regression suite | `scripts/build-linux.sh` (`CC=gcc-13 CXX=g++-13`) | pass | `SMOKE TEST PASSED` |

## Output Excerpts

```
SMOKE TEST PASSED
BUILD_DONE=0
```

An earlier run on the same change failed two `overloaded: ... blocks late with render
threads` timing checks. Rerunning `--perf` alone on the same binary passed once and failed
once, so the timing guards are flaky on this 4-core VM. This change doesn't touch the
engine, and the final full run passed.

## Residual Risks

- Windows untested. The change is platform-neutral Avalonia drawing, but the reporter's
  stutter wasn't reproduced, and their "loop on the clip" doesn't exist as a feature. Most
  likely they had the clip open in the Clip panel.
- Process CPU under Xvfb was too noisy to measure (Avalonia's X11 loop busy-polls), so the
  benchmark counts repaints instead of measuring time.

## Recommendation

Merge, and ask the reporter to confirm on Windows. Reopen if the stutter persists with the
Clip panel closed. That would point at the follow-ups in `fix.md`.
