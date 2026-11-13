---
description: "Tasks for 097 — Toolbar overflow (#520)"
---

# Tasks: Toolbar overflow

**Input**: Design documents from `specs/097-toolbar-overflow/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/overflow-menu.md](contracts/overflow-menu.md),
[quickstart.md](quickstart.md)

**Tests**: the plan asks for `ToolbarOverflowTests` in the smoke test on the pure model.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 [P] Add `GlyphKind.More` (three dots, drawn from geometry like the other tool glyphs) in `src/managed/Nota.App/Controls/Glyph.cs`

## Phase 2: Foundational (blocking)

- [X] T002 Create the pure model `ToolbarPart` + `ToolbarOverflow` (`ShedOrder` = today's #303 order; `BarOrder` = Loop, LoopRecord, Tap, TimeSignature, Key, Grid, Follow, Snap, Automation, MidiLearn, Remote, DspMeter, Master with CpuBar / RemoteLabel placed beside DspMeter / Remote; `IsInfoOnly` true for CpuBar, RemoteLabel; `Entries(int shedSteps)` — "`shedSteps` is clamped to `0 … ShedOrder.Count`", "never contains duplicates; `Entries(n)` ⊆ `Entries(n + 1)`", "`Entries(0)` is empty", sorted by `BarOrder`) in `src/managed/Nota.Presentation/ToolbarOverflow.cs`
- [X] T003 [P] Add `ToolbarOverflowTests` (empty at 0, full order at max, clamping, monotonic, no duplicates, info-only omitted, bar-order sort, Snap+Grid case from US1 #1) in `tests/Nota.SmokeTest/ToolbarOverflowTests.cs` and register it in `tests/Nota.SmokeTest/Program.cs`
- [X] T004 Drive `TransportShedOrder` from `ToolbarOverflow.ShedOrder` through a part → controls map (TimeSignature → TimeSigCell, MidiLearn → RemotePanel, Loop → LoopSep + LoopCell, DspMeter → CpuMeter + CpuSep, …), keep the shed behaviour identical, and store `_shedSteps` after each fit in `src/managed/Nota.App/MainWindow.TransportFit.cs`

**Checkpoint**: build green; the bar sheds exactly as before.

## Phase 3: User Story 1 — Nothing on the toolbar becomes unreachable (P1) 🎯 MVP

**Goal**: every shed control has a working entry in the ⋯ menu.

**Independent Test**: quickstart steps 2, 3 and 6.

- [X] T005 [US1] Add `OverflowBtn` (`tp-icon` ToggleButton-free `Button`, `GlyphKind.More`, tooltip + accessible name `{app:T 'main.overflow'}`, a 5 px accent `OverflowDot` shown while anything is shed) in the bar's last column (Grid.Column 10) in `src/managed/Nota.App/MainWindow.axaml`; it is never in the shed order
- [X] T006 [US1] Update the dot from `_shedSteps` in `FitTransport` in `src/managed/Nota.App/MainWindow.TransportFit.cs`
- [X] T007 [P] [US1] Split `SnapChip.OpenMenu` into `BuildItems()` (returns the items) used by the chip and reusable as a submenu in `src/managed/Nota.App/SnapChip.cs`; expose the arrangement chip from `InitSnapGrid` in `src/managed/Nota.App/MainWindow.axaml.cs`
- [X] T008 [US1] Extract the master-fader construction into `CreateMasterFader()` (MiniFader + `MixGesture` undo + `ValueEntry`) used by the bar and the menu in `src/managed/Nota.App/MainWindow.axaml.cs`
- [X] T009 [US1] Create `MainWindow.TransportOverflow.cs`: `OnOverflowClick` builds a `MenuFlyout` with `MenuKit` from `ToolbarOverflow.Entries(_shedSteps)` per [contracts/overflow-menu.md](contracts/overflow-menu.md) — check items for Loop, LoopRecord, Follow (`SetFollowPlayhead`), Snap (same path as the magnet, ⌘4 shown), Automation (+ "Re-enable automation" while `ReenableAutoBtn` is visible), MIDI learn (`ToggleMidiLearn`); Tap (`StaysOpenOnClick`, `TapTempoBeat`, trailing BPM); Time signature ▸ radio submenu (2/4 3/4 4/4 5/4 6/8 7/8 9/8 12/8 + the current one if uncommon); Key… (closes, then `KeyPicker.Show` under `OverflowBtn`); Grid ▸ (`BuildItems`); Connect Phone… with the count; DSP line (disabled, refreshed by the CPU tick while open); Master fader item (`StaysOpenOnClick`) — in `src/managed/Nota.App/MainWindow.TransportOverflow.cs`
- [X] T010 [US1] Refresh the open menu's DSP line and Tap trailing text from the existing CPU tick / `TapTempoBeat` in `src/managed/Nota.App/MainWindow.axaml.cs`
- [X] T011 [P] [US1] Add the `main.overflow` and `overflow.*` strings (entry labels, DSP line format, Connect Phone count) in `src/managed/Nota.Application/Localization/en.json`

**Checkpoint**: US1 independently testable.

## Phase 4: User Story 2 — The overflow menu behaves like every Nota menu (P1)

**Goal**: predictable opening / closing and keyboard reach.

**Independent Test**: quickstart step 5; Tab to ⋯, Space opens with focus on the first item.

- [X] T012 [US2] Second click on ⋯ while open closes the menu (track the open flyout, hide instead of re-open); outside click only closes (flyout light dismiss, overlay input pass-through off) in `src/managed/Nota.App/MainWindow.TransportOverflow.cs`
- [X] T013 [US2] Keyboard: `OverflowBtn` focusable in tab order; opening by keyboard focuses the first item in `src/managed/Nota.App/MainWindow.TransportOverflow.cs`

## Phase 5: User Story 3 — Quick functions (P2)

**Goal**: interface scale from the toolbar.

**Independent Test**: quickstart step 4.

- [X] T014 [US3] Add the quick-functions section after a separator: "Interface scale ▸" radio submenu over `InterfaceScale.Presets`, current checked; picking sets `settings.Current.InterfaceScale`, `Save()`, `NotaUiScale.Set(p)` in `src/managed/Nota.App/MainWindow.TransportOverflow.cs`
- [X] T015 [P] [US3] Make the Settings scale picker follow `NotaUiScale.Changed` (re-check its buttons; unsubscribe on close) in `src/managed/Nota.App/PreferencesWindow.cs`
- [X] T016 [P] [US3] Strings for the quick functions in `src/managed/Nota.Application/Localization/en.json`

## Phase 6: Polish & Cross-Cutting

- [X] T017 Fix the stale "launch quantize" note in the bar's header comment and document the ⋯ button in the comments of `src/managed/Nota.App/MainWindow.axaml` and `src/managed/Nota.App/MainWindow.TransportFit.cs`
- [X] T018 Run [quickstart.md](quickstart.md) in the app (headless harness screenshots per the Nota UI verify recipe): narrow / wide, 100 % / 150 %, Studio Dark / Studio Light / classic look

### Nota closing tasks

- [ ] T019 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T020 AGPL-3.0-only SPDX two-line header on `ToolbarOverflow.cs`, `ToolbarOverflowTests.cs`, `MainWindow.TransportOverflow.cs`
- [X] T021 New dependency: N/A
- [X] T022 `CHANGELOG.md` `[Unreleased]` → Added: toolbar overflow menu (#520)
- [X] T023 FEATURES.md via `nota-features`; transport, main-window and appearance docs pages (EN + RU) via `nota-docs` in ../nota-docs-520 (branch docs/transport-overflow-520, transport-bar shot retaken); shortcuts — N/A (no key added)
- [X] T024 Engine behaviour change: N/A

---

## Dependencies & Execution Order

- Phase 1 and T002 → T003, T004 → US1 (T005–T011) → US2 (T012–T013, same file as T009) → US3 (T014–T016) → Polish.
- US3's T015 / T016 can run alongside US1 (different files).

### Parallel Opportunities

- T001 ‖ T002; T003 ‖ T004 after T002.
- In US1: T007, T011 ‖ T005/T006/T008.
- In US3: T015 ‖ T016 ‖ T014.

## Parallel Example: User Story 1

```text
T007 SnapChip.BuildItems     (SnapChip.cs)
T011 overflow strings        (en.json)
T008 CreateMasterFader       (MainWindow.axaml.cs)
```

## Implementation Strategy

MVP = Phases 1–3 (the disappearing-controls fix). US2 hardens the menu's open/close; US3 adds
the interface scale. Each checkpoint builds green.
