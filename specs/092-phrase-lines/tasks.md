# Tasks: Phrase lines in the arrangement

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: User Story 1 — see where a phrase starts (P1)

- [x] T001 `PhraseLines` (choices, `Normalize`, `StartsPhrase`, `IsPhraseLine`) in `src/managed/Nota.Application/Editing/PhraseLines.cs` (FR-002, FR-005)
- [x] T002 `PhraseLinesTests` in `tests/Nota.SmokeTest`, in the full run and alone as `--phrase-lines`
- [x] T003 `Settings.ArrangementPhraseBars` (FR-001)
- [x] T004 `ArrangementView.PhraseBars` and the phrase pen in `DrawGrid` — lanes and footer rows (FR-002)
- [x] T005 The ruler's phrase bars full height in the phrase pen (FR-003)
- [x] T006 Applied at start-up and on `Settings.Changed` in `MainWindow.axaml.cs` (FR-006)
- [x] T007 Settings → Appearance → Phrase lines segmented control and caption, texts in `en.json` (FR-001)

## Phase 2: User Story 2 — readable in every theme (P1)

- [x] T008 `PhrasePen` derived from `GridBar` and `TextTertiary`, no new token (FR-004)

## Phase 3: Polish

- [x] T009 `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` entries
- [x] T010 Linux build steps pass (engine, managed, smoke test incl. localization and design checks)
