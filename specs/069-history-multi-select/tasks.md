# Tasks: Delete several versions at once

**Input**: [spec.md](spec.md)

## Phase 1: User Story 1 — delete a hand-picked set (P1)

- [x] T001 Multiple selection on the History list; Switch, double-click and Return act on a single selection; a refresh keeps the selection, in `src/managed/Nota.App/HistoryView.cs` (FR-001)
- [x] T002 Footer for several versions — count, time span, "The current version stays.", Delete N versions… (FR-002, FR-005)
- [x] T003 Context menu of a multiple selection, headed "N versions" (FR-003, FR-005, FR-006)
- [x] T004 `DeleteSelected`: one confirmation, one `DeleteMany`, the freed bytes on the status bar (FR-004)

## Phase 2: Polish

- [x] T005 `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` entries
- [x] T006 `scripts/build-linux.sh` passes (engine, managed, smoke test incl. design checks)
