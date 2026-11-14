# Tasks: A Mixer button in the toolbar

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: User Story 1 — one click to the Mixer (P1)

- [x] T001 `GlyphKind.Mixer` in `src/managed/Nota.App/Controls/Glyph.cs` (FR-001)
- [x] T002 `MixerToggle` after Automation in `src/managed/Nota.App/MainWindow.axaml`, running `view.mixer` (FR-001, FR-002)
- [x] T003 Tooltip naming the shortcut in `MainWindow.MenuKeys.cs`, text in `en.json` (FR-004)

## Phase 2: User Story 2 — always in step (P1)

- [x] T004 `SyncMixerToggle` from docking, undocking and opening and closing the window (launch restores through them) in `MainWindow.Mixer.cs` (FR-003)

## Phase 3: Polish

- [x] T005 Narrow-window shed order in `MainWindow.TransportFit.cs` (FR-006)
- [x] T006 Preferences → Shortcuts row mentions the button (FR-005)
- [x] T007 `CHANGELOG.md` `[Unreleased]`, `FEATURES.md` and `DESIGN.md` tool list
- [x] T008 Linux build steps pass (engine, managed, smoke test incl. localization, icon and design checks)
