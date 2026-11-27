# Tasks for #1122

## Pointer editing
- [x] T001 [US1] Add adaptive zones, cursor feedback and grouped velocity drag in src/managed/Nota.App/PianoRollView.cs.
- [x] T002 [US1] Add cancellation and audition cleanup using the editor commit paths in src/managed/Nota.App/PianoRollView.cs.

- [x] T011 [US1] Add modifier-drag duplication with threshold, cancellation and one undo step in src/managed/Nota.App/PianoRollView.cs.

- [x] T012 [US1] Implement P0 Alt/Option edge stretch with a shared scale, grid snapping, Shift bypass and one undo step in src/managed/Nota.App/PianoRollView.cs.

## Structural commands
- [x] T003 [US2] Add split and join commands and expression handling in src/managed/Nota.App/PianoRollView.Articulations.cs and PianoRollView.Expression.cs.
- [x] T004 [US2] Carry mute state through src/managed/Nota.Application/Engine/EngineContracts.cs, src/native/nota.engine/include/nota/nota_engine.h, project persistence and native scheduling.
- [x] T005 [US2] Add mute commands and rendering in src/managed/Nota.App/PianoRollView.cs.

## Numerical editing
- [x] T006 [US3] Edit the existing selection readouts inline in src/managed/Nota.App/ClipPropsView.cs.

## Navigation
- [x] T007 [US4] Add middle-button pan in src/managed/Nota.App/PianoRollView.cs.
- [x] T008 [US4] Add dotted snapping in src/managed/Nota.Application/Editing/SnapGrid.cs and the Piano Roll grid picker.

## Integration
- [ ] T009 Update src/managed/Nota.Application/Localization/en.json, src/managed/Nota.Application/Input/ShortcutLayout.cs, FEATURES.md and CHANGELOG.md.
- [ ] T010 Verify each slice against specs/116-context-sensitive-midi-editing/spec.md and cover native playback and persistence changes in tests/Nota.SmokeTest.


- [x] T013 [US1] Implement single-click note creation with marquee and double-click arbitration in src/managed/Nota.App/PianoRollView.cs.

Local progress: T013 builds. T012 has a managed implementation and still needs
gesture verification, including undo/redo, clip limits and cancellation.
