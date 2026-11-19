# Tasks: MIDI Learn for Session clip and scene launch buttons

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: User Stories 1 and 2 — map slots and scenes (P1)

- [x] T001 `SessionSlotLaunch` / `SessionSceneLaunch` target kinds, appended, buttons, in `src/managed/Nota.App/MidiLearn.cs` (FR-001, FR-006)
- [x] T002 `SessionLaunch.Press` / `Release` / `Scene` in `src/managed/Nota.Application/Controllers/SessionLaunch.cs` (FR-002, FR-004, FR-005)
- [x] T003 `ControlApplier.Trigger(t, velocity)` launches, `ControlApplier.Release` releases, in `src/managed/Nota.App/Controllers/ControlApplier.cs` (FR-002, FR-003)
- [x] T004 Note velocity and the button's way down through `MidiLearnService.ApplyNormalized` (FR-003)
- [x] T004a Note-offs in the MIDI Learn copy (`src/native/nota.engine/src/engine/MidiSurface.h`) and through `ControllerService`'s hand-over; no binding or activity on a note-off (FR-003)
- [x] T005 `SlotCell` and `SceneCell` expose their launch buttons as learn regions in `src/managed/Nota.App/SessionView.Grid.cs` (FR-001)

## Phase 2: User Story 3 — find and keep the mappings (P2)

- [x] T006 Slot mappings saved by track position (`TrackScoped`), the row in `ParamIndex` (FR-006)
- [x] T007 `MidiLearnService.SessionLabel` names them in the MIDI map tab; `midi_map.launch_clip` / `midi_map.launch_scene` in `en.json` (FR-007)

## Phase 3: Polish

- [x] T008 `tests/Nota.SmokeTest/SessionLaunchTests.cs`: launch and release (Trigger, Gate), empty slots with and without their stop button, record and stop, scenes, missing rows and tracks; `--session-launch` and the full run
- [x] T008a The MIDI surface self-test and `ControllerTests.SurfaceLayer` check a note's release reaches MIDI Learn as value 0
- [x] T009 `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` entries
- [x] T010 `scripts/build-linux.sh` passes (Linux)
