# Tasks: Tempo track and time scale

## Setup and foundations

- [x] T001 Inspect approved #535, repository and spec in specs/086-tempo-track/.
- [x] T002 Add compiled map and inheritance in engine/TempoMap.h and Graph.cpp.
- [x] T003 Add C ABI and managed port/wrappers in nota_engine.h, IAudioEngine.cs and NotaEngine.Tempo.cs.

## US1 - Audible editable tempo

Independent test: shapes affect playback; timing and project data round-trip.

- [x] T004 [US1] Integrate Transport, scheduling and metronome in engine/Transport.* and Engine_Render.cpp.
- [x] T005 [US1] Persist map and migrate v30 in Infrastructure/Persistence/Project*.
- [x] T006 [US1] Implement collapsible/resizable lane, point and handle edits in App/ArrangementView.Tempo.cs.
- [x] T007 [US1] Add timing, validation, render and persistence checks in tests/Nota.SmokeTest/TempoMapTests.cs.

## US2 - Multi-selection

Independent test: marquee deletion and one undo restore all noninitial points.

- [x] T008 [US2] Implement marquee, Delete/Escape and menu suppression in App/ArrangementView.Tempo.cs.

## US3 - Time scale

Independent test: three mapped sections keep previous anchors and reference time.

- [x] T009 [US3] Implement indicator, Ctrl bar drag, Alt, snap/cancel in App/ArrangementView.Tempo.cs.
- [x] T010 [US3] Preserve unwarped audio seconds in engine/Engine.cpp and Engine_Render.cpp.
- [x] T011 [US3] Verify mapping, no-marker edits and undo in TempoMapTests.cs.

## Polish and validation

- [x] T012 Update localization, shortcuts, CHANGELOG.md, FEATURES.md and nota-docs with the Nota skills.
- [x] T013 Build and test with scripts/build-win.ps1; record specs/086-tempo-track/verification.md.
- [x] T014 Run convergence against specs/086-tempo-track/ and resolve gaps.

Dependencies: T002 -> T003 -> T004; US1 enables US2/US3; then validation.
Parallel opportunity: read-only native research during specification. Shared-file
implementation stays sequential. Deliver working slices, then run full checks.

## Phase 6: Convergence

- [x] T015 Make count-in duration, click spacing, accent grouping and capture-clock offsets denominator-aware, and add a 6/8 regression test per FR-003/FR-004 (partial).
- [x] T016 Map both endpoints of Arrangement MIDI preview notes through the tempo axis so curved ramps draw their real timing per US1/AC1 and FR-001 (partial).
- [x] T017 Rerun the complete Windows build gate after the persistence-version assertion fixes and record the final results in `specs/086-tempo-track/verification.md` per SC-005 and T013 (partial).
