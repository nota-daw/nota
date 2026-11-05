# Tasks: Clip Editor Track Mode

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: Foundational

- [X] T001 `ClipSet` track mode: `trackMode` constructor flag, `TrackMode`, `IsMulti`, origin at 0 in `src/managed/Nota.Application/Editing/ClipSet.cs`
- [X] T002 `ClipSet.Add` for clips that appear on the track in `src/managed/Nota.Application/Editing/ClipSet.cs`
- [X] T003 [P] `ClipEditorTrackMode` setting in `src/managed/Nota.Application/Settings.cs`
- [X] T004 [P] Smoke tests for T001–T002 in `tests/Nota.SmokeTest/MultiClipTests.cs`

## Phase 2: User Story 1 — see every clip of a track (P1)

- [X] T005 [US1] `BuildClipEditor` opens the active clip's whole track in track mode; `MidiClipsOf` in `src/managed/Nota.App/MainWindow.Detail.cs`
- [X] T006 [US1] Clip | Track segmented control (`ModeControl`) at the top of the inspector's Clip section in `src/managed/Nota.App/ClipEditorView.cs` and `ClipPropsView.cs`; header count uses the clip count
- [X] T007 [US1] `SetClipTrackMode` saves the setting and reopens the editor in `src/managed/Nota.App/MainWindow.Detail.cs`
- [X] T008 [US1] Palette command `roll.trackMode` in `CommandCatalog.cs` and `MainWindow.Commands.cs`

## Phase 3: User Story 2 — edit any note (P1)

- [X] T009 [US2] No new code: spec 029 editing applies to the track set (covered by `--multiclip`)

## Phase 4: User Story 3 — follow the track (P2)

- [X] T010 [US3] `FocusTrackClip` + `PianoRollView.RevealClip`: same-track selection makes the clip active without a rebuild (double-click, Clip tab, `FollowClipSelection`)
- [X] T011 [US3] `AddNewTrackClips` in `ReloadEditorNotes` and `OnClipGeometryChanged`
- [X] T012 [US3] "Edit selected clips together" leaves track mode

## Phase 5: Polish

- [X] T013 CHANGELOG `[Unreleased]` and FEATURES.md entries

### Nota closing tasks

- [X] T014 `scripts/build-linux.sh` passes (smoke test included)
- [X] T015 No new source files (SPDX n/a); no new dependency

## Phase 6: Convergence

- [X] T016 Get the smoke test green on CI (macOS, Windows, Linux) — locally `scripts/build-linux.sh` fails only the two render-pool overload timing guards, which fail the same way on unmodified `develop` on the 4-core VM — per Constitution IV / T014 (partial). CI run 41124206352 passed on all three platforms.
