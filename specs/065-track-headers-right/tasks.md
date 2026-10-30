# Tasks: Track headers on the right

**Input**: Design documents from `/specs/065-track-headers-right/`

**Prerequisites**: plan.md, spec.md

## Phase 1: Setup

- [x] T001 Add `ArrangementHeadersRight` (bool, default false) to `src/managed/Nota.Application/Settings.cs`

## Phase 2: User Story 1 — headers on the right (P1)

- [x] T002 [US1] Register the six two-column bands (ruler, overview, sections, tracks, footer, scrollbar) and the header dividers in `src/managed/Nota.App/ArrangementView.cs`, `ArrangementView.Overview.cs`, `ArrangementView.Sections.cs`
- [x] T003 [US1] `HeadersOnRight` property + `ApplyHeaderSide()` swapping columns, dividers, backdrop alignment, and rebuilding header/footer cards in `src/managed/Nota.App/ArrangementView.cs`
- [x] T004 [US1] Card borders (track, group, footer) on the lane-facing side in `src/managed/Nota.App/ArrangementView.cs`
- [x] T005 [US1] Column seam anchored on the lane-facing edge, drag sign reversed on the right, in `src/managed/Nota.App/ArrangementView.Sizing.cs`

## Phase 3: User Story 2 — persisted and reachable (P2)

- [x] T006 [US2] `view.headersRight` in `src/managed/Nota.Application/Palette/CommandCatalog.cs`, a checkbox item in `src/managed/Nota.App/MainWindow.axaml`, binding + toggle in `MainWindow.Commands.cs`
- [x] T007 [US2] Apply the setting at start and on `Settings.Changed`, keep the menu check in sync, in `src/managed/Nota.App/MainWindow.axaml.cs`
- [x] T008 [US2] Settings → Appearance → Track headers: Left / Right in `src/managed/Nota.App/PreferencesWindow.cs`

## Phase 4: Polish

- [x] T009 CHANGELOG `[Unreleased]` and FEATURES.md (nota-features); the user manual (`nota-daw/nota-docs`, arrangement page EN + RU) is a follow-up PR in that repo
- [x] T010 `scripts/build-linux.sh` green (build + smoke test); manual pass of spec scenarios on both sides
