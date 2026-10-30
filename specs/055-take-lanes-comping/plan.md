# Implementation Plan: Take lanes and comping (audio loop recording)

**Branch**: `cursor/take-lanes-comping-3af4` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/055-take-lanes-comping/spec.md`

## Summary

Audio loop recording keeps every pass as a **take** on the track instead of overwriting the
loop-region buffer in place. Takes are structural track data in the engine graph (so undo/redo
come from the snapshot history, AR-5). The **comp** is the track's ordinary clips: a comp section
is a clip that plays a slice of a take's sample and carries that take's id. Comping a range,
deleting a take and flattening are message-thread graph edits, one undo step each. The audio
thread plays nothing new — only ordinary clips — so the render path is untouched. The managed
side saves takes in the bundle (format v29), and the arrangement draws foldable take lanes under
the track and turns a horizontal drag on a lane into a comp edit.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-5 (takes live on the
  immutable `Track` snapshot, every edit republishes), AR-6 (pass buffers are copied by the
  message-thread drain, never the audio thread), AR-7 (new `nota_track_take_*` C functions).
- **UI work** → [`DESIGN.md`](../../DESIGN.md): take lanes reuse the lane palette, the track
  colour for in-use sections, the group disclosure chevron for open/close.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed app)

**Primary Dependencies**: none new

**Storage**: `.nota` bundle — `project.json` gains `takes` per track and `takeId` per audio clip
(format v29); take audio is content-addressed in `samples/` like clip audio (shared with the
comp sections that use it).

**Audio-Thread Impact**: None. The capture ring and its drain are unchanged; the drain (message
thread, `drainInputQueue`) copies a completed pass when the loop cursor wraps. Comp sections are
ordinary `AudioClip`s; no per-block take logic.

**Testing**: `tests/Nota.SmokeTest/TakeLaneTests.cs` drives a device-free multi-pass loop take
through a new test entry point (`nota_take_record_test`) and checks takes, the default comp
(sample-exact against today's single-buffer result), comp/undo/redo, delete, flatten
(sample-exact) and save/reopen.

**Target Platform**: macOS, Windows, Linux (no platform code)

**Project Type**: desktop app

**Performance Goals**: no change to render cost; one extra loop-length buffer copy per pass on
the message thread.

**Constraints**: AR-5/6/7; takes must not change the result of recordings that make one pass.

**Scale/Scope**: tens of takes per track; a loop of minutes.

## Constitution Check

- I. Real-time safety — pass. No audio-thread change; allocations happen in the message-thread drain and graph edits.
- II. Boundary discipline — pass. Plain C functions and one POD struct (`NotaTakeInfo`).
- III. Licensing — pass. New files carry the SPDX header; no dependency.
- IV. Verification gate — pass. Smoke test extended (engine behaviour change).
- V. One logical change — pass. One feature (#187).
- VI. Docs in sync — CHANGELOG, FEATURES.md, user manual page (nota-docs), no new shortcut.
- VII. Spec first — this spec + plan + tasks.
- VIII. — n/a.

## Design

### Engine (`src/native/nota.engine`)

- `Track.h`: `struct AudioTake { sample, id, number, group, originBeat, sourceOffsetFrames,
  lengthFrames }`, `std::vector<AudioTake> takes` on `Track`; `int32_t takeId` on `AudioClip`
  (0 = not from a take). `cloneTrack` copies `takes`.
- `Engine_Recording.cpp`: loop punch keeps a list of completed passes. When the write cursor
  wraps, the pass `[passFrom, loopFrames)` is copied into a zeroed loop-length buffer. On stop,
  the open pass `[passFrom, reached)` is added; with two or more passes the take path runs:
  every pass becomes a take (new group, numbers continuing on the track) and the default comp is
  built newest-first (each frame from the latest pass that recorded it) as integer-frame clips
  tagged with their take id, carving the loop region exactly as today. One pass → today's path.
- `Engine_Takes.cpp` (new): `takeCount/takeInfo/takePeaks`, `compTake` (clamp the beat range to
  the take, round to whole frames, carve, add the section), `deleteTake` (clips keep playing,
  their take id is cleared), `flattenTakes` (per group: aligned plain sections are summed into
  one new buffer → one clip; others keep playing untagged; the group's takes go),
  `restoreTake`/`setClipTakeId` (load; the latter re-points a matching clip at the take's buffer
  so a reopened comp shares its audio), and `takeRecordTest` (device-free multi-pass take).
- `findSampleById` also searches takes (project save registers their audio).

### C ABI — see [contracts/c-abi.md](contracts/c-abi.md)

### Managed

- `EngineContracts.cs`: `NotaTakeInfo`. `IAudioEngine` + `NotaEngine.Takes.cs` + `NativeMethods`.
- Persistence: `TrackDto.Takes` (`AudioTakeDto`), `AudioClipDto.TakeId`; save registers each
  take's sample; load restores takes first (old id → new id map), then the clips, then tags
  them. Format v29 (additive, no migration).
- Arrangement (`ArrangementView.Takes.cs`, new): open/closed set (view state), take lane height,
  geometry (`RowHeightAt` = clip area + open lanes; clip drawing and hit tests use the clip area),
  take lane drawing (waveform, "Take N", in-use highlight from clips' take ids), the swipe gesture
  (snap like other drags, Alt bypasses), and a lane context menu (Use whole take, Delete take,
  Flatten takes). Header: a chevron + count on audio tracks with takes.

## Project Structure

### Documentation (this feature)

```text
specs/055-take-lanes-comping/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/c-abi.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code

```text
src/native/nota.engine/
├── include/nota/nota_engine.h          # NotaTakeInfo + nota_track_take_* / nota_clip_*take_id
├── src/engine/Track.h                  # AudioTake, Track::takes, AudioClip::takeId
├── src/engine/Engine.h / Engine.cpp    # declarations, cloneTrack
├── src/engine/Engine_Recording.cpp     # passes → takes + default comp
├── src/engine/Engine_Takes.cpp         # new: take edits, peaks, load, test hook
├── src/engine/Engine_Tracks.cpp        # findSampleById searches takes
├── src/abi/nota_engine_track.cpp       # C wrappers
└── CMakeLists.txt                      # Engine_Takes.cpp
src/managed/
├── Nota.Application/Engine/{EngineContracts,IAudioEngine}.cs
├── Nota.Infrastructure/Interop/{NativeMethods.Tracks,NotaEngine.Takes}.cs
├── Nota.Infrastructure/Persistence/{ProjectModel,ProjectService}.cs
└── Nota.App/ArrangementView{,.LaneControl,.Takes}.cs
tests/Nota.SmokeTest/{TakeLaneTests,Program}.cs
```

**Structure Decision**: engine owns take data (undo for free), managed owns persistence and UI,
per the existing split.

## Complexity Tracking

None.
