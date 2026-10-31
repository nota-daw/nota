# Tasks: Sample rate and buffer size in the status bar

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: Foundation

- [x] T001 Add `AudioFormatStatus` (text, latency, shared buffer sizes, owner) in `src/managed/Nota.Presentation/ViewModels/AudioFormatStatus.cs`
- [x] T002 Settings → Audio uses `AudioFormatStatus.BufferSizes` in `src/managed/Nota.App/PreferencesWindow.cs`

## Phase 2: User Story 1 — see the audio format (P1)

- [x] T003 `EngineInfo` reads "48 kHz · 256" at start and after every apply in `src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs` (FR-001, FR-002)

## Phase 3: User Story 2 — change the buffer (P1)

- [x] T004 `BufferOwner` and `SetAudioBufferFrames` (stage, apply, resume playback, notify Settings) in `MainWindowViewModel.cs` (FR-004, FR-006)
- [x] T005 Ghost `AudioFormatButton` with tooltip in `src/managed/Nota.App/MainWindow.axaml` (FR-001)
- [x] T006 Buffer flyout in `src/managed/Nota.App/MainWindow.AudioFormat.cs`, wired in `MainWindow.axaml.cs` (FR-003, FR-005, FR-006, FR-007)

## Phase 4: Polish

- [x] T007 Smoke test `tests/Nota.SmokeTest/AudioFormatStatusTests.cs`, registered in `Program.cs`
- [x] T008 `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` entries
- [x] T009 `scripts/build-linux.sh` passes (engine, managed, smoke test incl. design checks)
