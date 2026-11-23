# Tasks: Clip launching ergonomics

**Input**: Design documents from `specs/112-clip-launch-ergonomics/`
**Prerequisites**: plan.md, spec.md

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Foundational

- [x] T001 [US1] Add the pure rule `SessionLaunchKey` in `src/managed/Nota.Application/Editing/SessionLaunchKey.cs` (FR-001–FR-003)

## Phase 2: User Story 1 — play and record the selected slot (P1) 🎯 MVP

- [x] T002 [US1] `SessionView.LaunchSelection` records into an empty armed slot and stops a recording slot via `SessionLaunchKey`, in `src/managed/Nota.App/SessionView.cs` (FR-001, FR-002, FR-011)
- [x] T003 [P] [US1] Smoke test for `SessionLaunchKey` in `tests/Nota.SmokeTest/ClipLaunchKeyTests.cs`, wired in `tests/Nota.SmokeTest/Program.cs`

## Phase 3: User Story 2 — stop track, launch row, stop all (P1)

- [x] T004 [US2] Default keys ⇧Return / ⌘Return / ⌘⇧Return for `session.stopTrack`, `session.launchScene`, `session.stopAll` in `src/managed/Nota.Application/Palette/CommandCatalog.cs` (FR-004–FR-007)
- [x] T005 [US2] Dispatch the three keys in the Session's tunnel next to `session.launchSelection` in `src/managed/Nota.App/MainWindow.Input.cs` (FR-008)
- [x] T006 [P] [US2] Smoke test: the defaults resolve in the Session scope and leave `transport.stop` on Return, in `tests/Nota.SmokeTest/ClipLaunchKeyTests.cs`

## Phase 4: User Story 3 — buttons you can hit (P2)

- [x] T007 [US3] Enlarge the empty-slot record circle, stop square and their click area; the per-track stop and Stop All glyphs, in `src/managed/Nota.App/SessionView.Grid.cs` (FR-009, FR-010)

## Phase 5: Polish

- [x] T008 CHANGELOG.md [Unreleased] entry and FEATURES.md (nota-features); check the Shortcuts screen (nota-shortcuts)
- [ ] T009 `scripts/build.sh` (Windows sibling) passes, smoke test included

## Dependencies

T001 → T002, T003. T004 → T005, T006. T007 independent. T008–T009 last.
