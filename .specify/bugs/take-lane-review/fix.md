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
| `src/native/nota.engine/src/engine/Engine.cpp` | modified | `publishRaw` settles tracks the new graph does not share |
| `src/native/nota.engine/src/engine/Engine_Takes.cpp` | modified | settle implementation; flatten keeps a full-level join against a section that stays separate |
| `src/native/nota.engine/src/engine/Engine_Tracks.cpp` | modified | `overwriteAudioClipsInRange` takes the caller's samples-per-beat |
| `src/native/nota.engine/src/engine/Engine_Recording.cpp` | modified | a stopped recording is placed and carved at the authoring tempo |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | unwarped mix passes the clip's fade flags |
| `src/managed/Nota.App/ArrangementView.Takes.cs` | modified | highlight only a section still at the take position |
| `src/managed/Nota.App/ArrangementView.Vms.cs` | modified | `ClipVM.SourceOffsetFrames` |
| `src/managed/Nota.App/ArrangementView.cs` | modified | copy `SourceOffsetFrames` on refresh |
| `src/native/nota.engine/include/nota/nota_engine.h` | modified | `nota_clip_joined_edges`, `nota_track_restore_joined_edges` |
| `src/native/nota.engine/src/abi/nota_engine_track.cpp` | modified | the two exports |
| `src/native/nota.engine/src/engine/Engine_TakeRetry.cpp` | modified | a take retry publishes its restored graph as restored |
| `src/managed/Nota.Application/Engine/IAudioEngine.cs` | modified | `ClipJoinedEdges`, `RestoreJoinedEdges` |
| `src/managed/Nota.Infrastructure/Interop/*.Takes.cs` | modified | P/Invoke and wrapper |
| `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs` | modified | optional `AudioClipDto.JoinedEdges` |
| `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs` | modified | save it; restore it once a track's clips are in |
| `tests/Nota.SmokeTest/TakeLaneTests.cs` | modified | join level, pre-block comp and record cuts, reversed flatten (and its reopen), cross-track fade, deactivated half, undo/redo |
| `CHANGELOG.md` | modified | Unreleased Fixed entry |

## Tests Added or Updated

- `TakeLaneTests.ThreePasses` — rendered audio at the default-comp join (frame 30000) matches a full-level frame of the same clip; frame 0 still fades out to silence.
- `TakeLaneTests.CompBeforeBlock` — `SetBpm(60)` then `CompTake` of take 1 over beats 0.5–1.0, with no render in between, leaves source frames 24000–48000 on the new section, 0–24000 on the partial last pass, and 48000 onward on the full pass.
- `TakeLaneTests.RecordBeforeBlock` — with the loop off, a 96000-frame clip is on the track, `SetBpm(60)`, then a 48000-frame recording stops before the next block. The new clip occupies beat 0 and the neighbour starts at beat 1, source frame 48000.
- `TakeLaneTests.FlattenKeepsSeparateJoin` — reversing the partial last pass keeps the join at full level, and flatten renders the same.
- `TakeLaneTests.MoveToTrackFadesExposedEdge` — moving the left section to another track fades the edge that used to meet it. The frame just past that fade is unchanged.
- `TakeLaneTests.FlattenKeepsSeparateJoin` (reopen) — the flattened project with a reversed section saves and reopens in a new engine; the render matches the comp sample for sample.
- `TakeLaneTests.DeactivatedSideFades` — deactivating the left section fades the right one's head at frame 30000; reactivating it restores the full-level join.
- `TakeLaneTests.UndoKeepsJoins` — comp, flatten, undo twice renders as the recording did; redo twice renders as the flatten did.

## Local Verification

- Commands run: native Release build (`CC=gcc CXX=g++`), `dotnet build` of the smoke project, `Nota.SmokeTest.dll --takes` → TAKES PASSED, then the full `Nota.SmokeTest.dll` again after the review fixes → SMOKE TEST PASSED (includes the render-pool guards).
- Manual checks: take lanes of a moved section, develop build versus this branch. The moved clip stays on the track; the lane no longer lights the part of the take under the clip's new range.

## Deviations from Assessment

`overwriteAudioClipsInRange` takes the caller's samples-per-beat instead of always reading the authoring tempo. A comp and a loop take pass the authoring tempo. Stopping a recording measures the new clip with that same tempo, because playback will, and passes it into the carve. When the transport has already caught up, the two clocks match.

`publishRaw` recomputes fades on tracks the new graph does not share with the one still playing. That covers a clip moved to another track, and any other edit that publishes clones without going through `republishWithTrackRaw`. Shared tracks are left alone.

A join that already played full-level stays full-level when the clips still meet, so flatten can keep a reversed section as its own clip without putting the dip back. The merged clip's facing edge is set off before publish when that neighbour's edge was off.

Second Greptile round:

- **Undo changed audio still playing.** `publishRaw(g, restored)`: undo, redo and a take retry pass `restored = true`. A restored track was published before and a render may still walk it, so when its derived fades differ it is cloned and the clone is settled; it is never written in place. An edit's own clones are still settled in place.
- **Reopening brought back the dip.** A join whose takes are gone (flatten keeping a reversed neighbour) cannot be derived again, so `AudioClipDto.JoinedEdges` saves it (optional, written only when non-zero; no format bump — an older file has none and an older app ignores it). Load restores the whole track's edges in one call after every clip is in, and the join is kept only where both clips still meet.
- **Large comps.** `takeJoinEdges` maps take id → group once (sorted, binary search), sorts the active clips' heads and finds each tail's partner by binary search: O(n log n + T log T) per published track, both passes in one.
- **Deactivated sections.** A silent clip joins nothing in either pass, so the half that still plays fades at the shared edge.

## Follow-ups

- None from review.
