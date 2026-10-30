---
description: "Tasks for take lanes and comping"
---

# Tasks: Take lanes and comping (audio loop recording)

**Input**: [spec.md](spec.md), [plan.md](plan.md), [data-model.md](data-model.md), [contracts/c-abi.md](contracts/c-abi.md)

## Phase 1: Foundational (engine data + ABI)

- [x] T001 `AudioTake`, `Track::takes`, `AudioClip::takeId` in `src/native/nota.engine/src/engine/Track.h`; `cloneTrack` copies takes (`Engine.cpp`)
- [x] T002 `findSampleById` searches takes (`Engine_Tracks.cpp`)
- [x] T003 `NotaTakeInfo` + `nota_track_take_*` / `nota_clip_*take_id` / `nota_track_restore_take` / `nota_take_record_test` in `include/nota/nota_engine.h`, wrappers in `src/abi/nota_engine_track.cpp`
- [x] T004 `Engine_Takes.cpp` (new, CMakeLists): info, peaks, comp, delete, flatten, restore, tag, test hook
- [x] T005 Managed contracts: `NotaTakeInfo` (`EngineContracts.cs`), `IAudioEngine` members, `NativeMethods.Tracks.cs`, `NotaEngine.Takes.cs`

## Phase 2: US1 — every loop pass is kept (P1)

- [x] T006 [US1] Pass capture on wrap + take path on stop in `Engine_Recording.cpp` (one pass → today's path)
- [x] T007 [US1] Smoke: N passes → N takes, numbering, default comp sample-exact vs single-buffer reference, partial first/last pass ranges, one pass → no takes, loop off → no takes (`tests/Nota.SmokeTest/TakeLaneTests.cs`, `Program.cs` incl. `--takes`)

## Phase 3: US3 — comp by swiping (P1)

- [x] T008 [US3] Smoke: comp a range → that take plays there only, clamped to the take; undo/redo one step each
- [x] T009 [US3] Swipe gesture on a take lane with snap / Alt bypass and preview (`ArrangementView.Takes.cs`, `ArrangementView.LaneControl.cs`)

## Phase 4: US2 — take lanes in the arrangement (P1)

- [x] T010 [US2] Take VMs loaded in `Refresh` (`ArrangementView.cs`, `ArrangementView.Vms.cs`)
- [x] T011 [US2] Row geometry: open lanes add height; clips draw and hit-test in the clip area (`ArrangementView.cs`, `ArrangementView.LaneControl.cs`)
- [x] T012 [US2] Draw lanes: waveform over the take range, "Take N", in-use highlight (`ArrangementView.Takes.cs`)
- [x] T013 [US2] Header chevron + count on audio tracks with takes; folded by default (`ArrangementView.cs`)

## Phase 5: US4 — delete and flatten (P2)

- [x] T014 [US4] Lane context menu: Use whole take, Delete take, Flatten takes (`ArrangementView.Takes.cs`)
- [x] T015 [US4] Smoke: delete keeps the sound; flatten → one clip, sample-exact, takes gone; undo restores

## Phase 6: US5 — save and reopen (P1)

- [x] T016 [US5] `AudioTakeDto`, `TrackDto.Takes`, `AudioClipDto.TakeId`, format v29 (`ProjectModel.cs`, `ProjectService.cs`)
- [x] T017 [US5] Save takes / restore takes then tag clips (`ProjectService.cs`, `AudioClipState.cs`)
- [x] T018 [US5] Smoke: save + reopen restores takes, comp tags, audio

## Phase 7: Polish

- [x] T019 `CHANGELOG.md` `[Unreleased]`, `FEATURES.md` (nota-features), user manual (nota-docs)
- [x] T020 `scripts/build-linux.sh` green; `/speckit-converge`

## Compliance re-check

- [x] SPDX headers on new files (`Engine_Takes.cpp`, `NotaEngine.Takes.cs`, `ArrangementView.Takes.cs`, `TakeLaneTests.cs`)
- [x] No new dependency
- [x] No audio-thread change (AR-6); takes on the snapshot (AR-5); C ABI only (AR-7)
- [x] Smoke test extended
- [x] CHANGELOG / FEATURES / docs
- [x] DESIGN.md: lane palette, track colour, existing chevron glyph
