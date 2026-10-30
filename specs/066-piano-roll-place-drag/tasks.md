# Tasks: Shape a note while placing it

**Input**: [spec.md](spec.md), [plan.md](plan.md)

- [x] T001 [US1–3] `NotePlacementDrag` (kind from modifiers, velocity from drag) in
  `src/managed/Nota.Application/Editing/NotePlacementDrag.cs`
- [x] T002 [US1] `Mode.Place` entered by the double-click placement; plain drag moves the
  note through the Move path with `_rowGrab` in `src/managed/Nota.App/PianoRollView.cs`
- [x] T003 [US2] ⇧ drag runs the Resize path; edge highlight while it does
- [x] T004 [US3] ⌘/Ctrl drag sets velocity; no auto-scroll while it does
- [x] T005 Re-anchor on a modifier change (FR-004); remember the note on release (FR-005)
- [x] T006 Smoke checks in `tests/Nota.SmokeTest/NoteEditingTests.cs`
- [x] T007 Shortcuts pane (`PreferencesWindow.cs`), `FEATURES.md`, `CHANGELOG.md`
- [x] T008 `scripts/build-linux.sh` passes
