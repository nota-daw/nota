# Tasks: Section renaming — cancel edits and restore automatic names

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1 — Naming rules (US3, FR-003/004/008)

- [x] T001 Add `SectionNames` helper in `src/managed/Nota.Application/Engine/SectionNames.cs`
- [x] T002 Smoke tests for numbering, normalisation, reset undo/redo, save/load in `tests/Nota.SmokeTest/SectionNamingTests.cs`, registered in `Program.cs`

## Phase 2 — Arrangement lane (US1, US2, US4)

- [x] T003 New sections start automatic; duplicate keeps mode; lane and menu header draw displayed names (`ArrangementView.Sections.cs`)
- [x] T004 Rename flyout: caption, placeholder, Reset button; Enter commits normalised text, Escape / dismiss discard
- [x] T005 Context-menu "Reset to default name" (disabled when automatic)
- [x] T006 Palette command `arrangement.resetSectionName` (catalog + binding)

## Phase 3 — Other surfaces (FR-009)

- [x] T007 Nota Remote gets displayed names (`MainWindow.Remote.cs`)
- [x] T008 MCP `list_sections` / `set_sections`: displayed name + `automatic`, descriptions

## Phase 4 — Docs and verification

- [x] T009 Shortcuts pane rows (Return / Esc in rename box)
- [x] T010 `FEATURES.md`, `CHANGELOG.md`
- [x] T011 `scripts/build-linux.sh` passes

## Compliance re-check

- [x] SPDX header on the new files; no dependency; no audio-thread or ABI change
