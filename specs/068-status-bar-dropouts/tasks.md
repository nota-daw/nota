# Tasks: Audio dropouts in the status bar

**Input**: [spec.md](spec.md)

## Phase 1: Foundation

- [x] T001 Add `DropoutCounter` (count since a reset point, change detection, text) in `src/managed/Nota.Presentation/ViewModels/DropoutCounter.cs`

## Phase 2: User Story 1 — see the dropouts (P1)

- [x] T002 `DropoutText` / `HasDropouts`, fed by `ReportDropouts` in `src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs` (FR-001, FR-006)
- [x] T003 Ghost `DropoutButton` with tooltip, left of the sample rate, in `src/managed/Nota.App/MainWindow.axaml` (FR-001, FR-005)
- [x] T004 `TextBlock.Caption.alert` (danger ink) in `src/managed/Nota.App/Theme/NotaTheme.axaml` (FR-002)

## Phase 3: User Story 2 — reset (P1)

- [x] T005 `ResetDropouts`, called by the button in `src/managed/Nota.App/MainWindow.axaml.cs` and after every audio apply in `MainWindowViewModel.ApplyAudioSettings` (FR-003, FR-004)

## Phase 4: Polish

- [x] T006 Smoke test `tests/Nota.SmokeTest/DropoutCounterTests.cs`, registered in `Program.cs`
- [x] T007 `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` entries
- [x] T008 `scripts/build-linux.sh` passes (engine, managed, smoke test incl. design checks)
