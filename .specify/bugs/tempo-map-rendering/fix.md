<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Fix: Mapped rendering and split performance

- **Slug**: tempo-map-rendering
- **Fixed**: 2026-11-12
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Reverse the source/beat relation before mapped stretching, render consolidation on an
independent clock, and use mapped natural-rate endpoints for ADSR. Compare borrowed
warp-cache identity before allocating marker copies in split/undo.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/media/WarpStream.h`, `WarpStream.cpp` | modified | Optional virtual reversed source reader; monotonically increasing virtual coordinates preserve positive-ratio streaming and RePitch interpolation. |
| `src/native/nota.engine/src/engine/Engine_Tracks.cpp` | modified | Reverse musical positions before stretching; cache identity/build validation includes direction for synchronous, incremental and background builds. Consolidation uses elapsed endpoint duration and an independent Transport; privately builds deferred source caches and preserves beat/time markers in warped output. Borrowed cache comparisons avoid temporary marker vectors. |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | Explicit clock for clip mixing and offline consolidation; mapped caches already contain played direction; mapped physical endpoints set natural-rate ADSR length once per clip. |
| `src/native/nota.engine/src/engine/Engine.h`, `Track.h`, `Engine.cpp` | modified | Private clock-aware render entry point, immutable direction metadata, builder direction stamps and publication validation. |
| `tests/Nota.SmokeTest/TempoMapRenderingTests.cs`, `Program.cs` | added/modified | 38 checks in focused tempo mode and the full suite. |
| `CHANGELOG.md` | modified | Record the rendering corrections and cheaper warped split/undo. |

## Tests Added or Updated

- RePitch and Complex reversed impulse playback at mapped beat 6 (second 4), rather
  than second 5; all three cache-building paths, direction toggles, stale forward
  background work invalidation, and trimmed windows.
- Natural-rate release with forward/reversed PCM and clip starts 0 and 2: sustain
  remains audible until the mapped release, then fades over the final musical beat.
- Consolidation at starts 0 and 4, natural-rate/warped clips, and deferred/eager
  caches: mapped PCM duration, late audio, and unchanged transport position/state.

## Local Verification

- Before production fixes, the initial focused reproductions fail reverse, release
  and consolidation assertions (`build/tempo-rendering-before.log`).
- After fixes and expanded edge coverage, `dotnet run --project tests/Nota.SmokeTest
  -c Release -- --tempo` passes all 101 checks (38 added here).
- Full Windows build/UI and Linux performance results are recorded in `test.md`.
- No dependency, user label, workflow, binding or documented capability is added.
  The docs/features/shortcuts audit requires no separate edits for restored behavior.

## Deviations from Assessment

- Deferred consolidation source preparation and stale-direction worker tests are
  included because both were identified as risks in the assessment.
- Develop advanced to 39c7857c during verification. Its Arrangement group-tray changes
  are merged, retaining both branches' changelog entries. The merged Windows build,
  smoke suite and 40 tempo UI checks pass. Native/performance sources in develop
  39c7857c are identical to e191a57b; the native test snapshot is unchanged by this merge.

## Follow-ups

- Require the unchanged hosted performance gate to pass on the published source;
  the preceding local pass did not predict the hosted +2.15% split regression.
- No extension hooks are registered (`hooks: {}`).
