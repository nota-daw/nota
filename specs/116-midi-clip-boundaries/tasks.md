# Tasks for #1121

## US1: Boundary handles

- [ ] T001 [US1] Verify retained MIDI content, controller and envelope timing during trim in src/native/nota.engine/src/engine/Engine_Tracks.cpp and tests/Nota.SmokeTest.
- [ ] T002 [US1] Add snapped and free boundary drag preview, duration feedback, and cancellation in src/managed/Nota.App/PianoRollView.cs.
- [ ] T003 [US1] Connect validated release to the existing trim and undo path in src/managed/Nota.App/MainWindow.Detail.cs.

## US2: Auto expansion

- [ ] T004 [US2] Add the default-off setting and clip editor toggle in src/managed/Nota.Application/Settings.cs and src/managed/Nota.App/ClipPropsView.cs.
- [ ] T005 [US2] Implement shared expansion math and clip ownership validation in src/managed/Nota.Application/Editing/ClipSet.cs.
- [ ] T006 [US2] Apply auto expansion to creation, move, resize, duplication and paste in src/managed/Nota.App/PianoRollView.cs and commit bounds and notes together in src/managed/Nota.App/MainWindow.Detail.cs.

## Integration

- [ ] T007 Update shortcut guidance, localization, FEATURES.md, CHANGELOG.md and the user manual for the implemented gestures.
- [ ] T008 Verify both edges, cancellation, trim recovery, five expansion triggers and single-step undo/redo using tests/Nota.SmokeTest and the Piano Roll.
