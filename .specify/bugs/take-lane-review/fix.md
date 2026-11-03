# Bug Fix: Take-lane review follow-ups (#187)

- **Slug**: take-lane-review
- **Fixed**: 2026-11-04
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Butt-joined take sections of one recording no longer fade at the shared edge, a comp cut uses the tempo `setBpm` already stored, and a moved clip is not painted as the take's audio at its new timeline range.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Track.h` | modified | `fadeHead` / `fadeTail` on `AudioClip`; `audioClipEdgeGain` honours them |
| `src/native/nota.engine/src/engine/Engine.h` | modified | `authoringSamplesPerBeat`, `settleTakeJoins` |
| `src/native/nota.engine/src/engine/Engine.cpp` | modified | settle the clone at the start of `republishWithTrackRaw` |
| `src/native/nota.engine/src/engine/Engine_Takes.cpp` | modified | settle implementation; call it from `placeTakesForTempo`; flatten bakes the flags |
| `src/native/nota.engine/src/engine/Engine_Tracks.cpp` | modified | `overwriteAudioClipsInRange` uses authoring samples-per-beat |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | unwarped mix passes the clip's fade flags |
| `src/managed/Nota.App/ArrangementView.Takes.cs` | modified | highlight only a section still at the take position |
| `src/managed/Nota.App/ArrangementView.Vms.cs` | modified | `ClipVM.SourceOffsetFrames` |
| `src/managed/Nota.App/ArrangementView.cs` | modified | copy `SourceOffsetFrames` on refresh |
| `tests/Nota.SmokeTest/TakeLaneTests.cs` | modified | join level and pre-block tempo cut |
| `CHANGELOG.md` | modified | Unreleased Fixed entry |

## Tests Added or Updated

- `TakeLaneTests.ThreePasses` — rendered audio at the default-comp join (frame 30000) matches a full-level frame of the same clip; frame 0 still fades out to silence.
- `TakeLaneTests.CompBeforeBlock` — `SetBpm(60)` then `CompTake` of take 1 over beats 0.5–1.0, with no render in between, leaves source frames 24000–48000 on the new section, 0–24000 on the partial last pass, and 48000 onward on the full pass.

## Local Verification

- Commands run: native Release build (`CC=gcc CXX=g++`), `dotnet build Nota.sln -c Release`, `Nota.SmokeTest.dll --takes` → TAKES PASSED, full `Nota.SmokeTest.dll` → SMOKE TEST PASSED (includes the render-pool guards).
- Manual checks: take lanes of a moved section, develop build versus this branch. The moved clip stays on the track; the lane no longer lights the part of the take under the clip's new range.

## Deviations from Assessment

None. Flags are derived on the clone inside `republishWithTrackRaw` and on the clones `placeTakesForTempo` publishes, not inside `publishRaw`.

## Follow-ups

- Snapshots published before this change still default the new fade fields to on, so undoing back to one of those can dip. New publishes do not.
