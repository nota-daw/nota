---

description: "Task list for the Docked Mixer"
---

# Tasks: Docked Mixer

**Input**: Design documents from `/specs/054-docked-mixer/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/ui-commands.md](contracts/ui-commands.md),
[quickstart.md](quickstart.md)

**Tests**: No automated test tasks — the spec does not ask for them and the managed UI has
no unit-test project. Each story is validated with the matching
[quickstart.md](quickstart.md) scenarios (headless Avalonia harness + screenshots when the
GUI can't be launched); `scripts/build.sh` is the PR gate.

**Organization**: Tasks are grouped by user story so each story can be built and checked
on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

All paths are relative to the repository root. `App/` below means
`src/managed/Nota.App/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Persisted preference fields and the new command id, used by every story.

- [X] T001 [P] Add `MixerFloating` (bool, default `false` → docked), `MixerShown` (bool, default `false`) and `MixerDockHeight` (double, default `0` = natural height) next to `MixerView` in `src/managed/Nota.Application/Settings.cs`, each with a `<summary>` in the file's style; data-model rule: "Unknown / missing fields in an older `settings.json` take the defaults (docked, hidden)"
- [X] T002 [P] Add `C("view.mixerFloat", "Mixer in its own window", "View", "", "dock undock float pop out mixer window")` after `view.mixer` in `src/managed/Nota.Application/Palette/CommandCatalog.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Height-adaptive strips (FR-014 — needed for any docked height and also
improves the floating window) and the `BodyGrid` refactor that makes room for a second
bottom panel.

**⚠️ CRITICAL**: The docked panel (US1) depends on both parts.

### Height-adaptive `MixerView` (research R4)

- [X] T003 In `App/MixerView.cs` make the fader height variable: replace the fixed `Height = 190` grid in `FaderRow` with a height driven by a field (default 190, minimum ≈ 72), keep a list of the fader rows/meters so a height change only sets `Height` on existing controls (no `Refresh()` rebuild); the master strip follows the same height
- [X] T004 In `App/MixerView.cs` add tier selection: on `_main.SizeChanged` (and after `Refresh()` / `ApplyView()`), compute the strip-area height and pick, in order, Full → hide I/O rows → hide sends rows → shrink the fader toward its minimum; effective visibility of a section = user toggle on AND (fits OR pinned); only toggle `IsVisible` on the existing `_ioSections` / `_sendSections`
- [X] T005 In `App/MixerView.cs` expose `public double MinContentHeight` (header + pan + fader at minimum + dB + M/S/Arm + footer + row margins, measured from the built controls, not hard-coded) and an event `MinContentHeightChanged` raised when it changes (strip rebuild, UI scale)
- [X] T006 In `App/MixerView.cs` give the footer `Sends` / `I/O` toggles the auto-hidden state from contracts/ui-commands.md: muted paint + tooltip "Hidden: not enough height · click to show anyway"; click while auto-hidden → pinned visible and `_stripsScroller.VerticalScrollBarVisibility = Auto`; click again → off; turn vertical scrolling back off when no section is pinned
- [X] T007 In `App/MixerWindow.cs` set `MinHeight` from the hosted `MixerView.MinContentHeight` + title bar (`BarH`), scaled by `NotaUiScale`, and update it on `MinContentHeightChanged`; check the Stereo map (`App/StereoMapView.cs`) at that height and give it a `MinHeight` that fits if it clips (research R4)

### `BodyGrid` refactor (research R2, R3)

- [X] T008 In `App/MainWindow.Detail.cs` add `LayoutBottomPanels()` that rebuilds `BodyGrid.RowDefinitions` and the `Grid.Row` of `BrowserGrid`, `DetailSplitter`, `DetailPanel` for the visible set `[main * (MinHeight 120)] [mixer splitter, mixer]? [detail splitter, detail]?`, plus `DetailRow` (current detail `RowDefinition`) accessor; with the Mixer absent the result must equal today's three-row layout
- [X] T009 Replace every `BodyGrid.RowDefinitions[2]` use with `DetailRow` and route show/hide through `LayoutBottomPanels()` in `App/MainWindow.Detail.cs` (`ShowDetail`, the hide at ~line 896), `App/MainWindow.DetailWindow.cs` (`OnToggleDetailWindow`) and `App/MainWindow.axaml.cs` (`DetailSplitter.DragCompleted` — keep storing the row's `ActualHeight`, the #53 rule)
- [X] T010 Add `FitBottomPanels()` in `App/MainWindow.Detail.cs`, called on `BodyGrid.SizeChanged` and after `LayoutBottomPanels()`: keep the main view ≥ 120, shrink the Mixer row toward its minimum first (its strips adapt; device cards have a fixed height), then the Devices row; never write remembered heights from a clamp
- [X] T011 Regression pass of the Devices / Pattern / Clip panel with the Mixer hidden: open Devices, switch to Clip, drag the splitter, re-select clips (no #53 shrink), Tab cycling, pop out to `DetailWindow` and dock back — behaviour identical to `develop`

**Checkpoint**: The floating Mixer adapts to its height; the bottom panel works as before
on the new row builder.

---

## Phase 3: User Story 1 — Mix without leaving the main window (Priority: P1) 🎯 MVP

**Goal**: View → Mixer / ⌘⇧M shows the full Mixer docked under the main view; it
resizes, remembers its height, and hides again.

**Independent Test**: quickstart scenarios 1–8 and 14–17 in Arrangement view.

- [X] T012 [US1] In `App/MainWindow.axaml` add `MixerSplitter` (6 px transparent `GridSplitter` over the seam, like `DetailSplitter`) and `MixerPanel` (`Border` docked on a seam, `Radius.Docked`, same look as `DetailPanel`) inside `BodyGrid`; toolbar 32 px with `Mixer` caption, `Strips | Stereo map` segments, spacer, pop-out (`Glyph Kind=PopOut`, tooltip "Open the mixer in a window") and close (`Kind=Close`, tooltip "Hide the mixer") buttons; body `ContentControl x:Name="MixerHost"`; follow `DESIGN.md` / `nota-design`
- [X] T013 [US1] Extend `LayoutBottomPanels()` in `App/MainWindow.Detail.cs` with the Mixer rows (Mixer directly under the main view, Devices under it — FR-004), `MixerRow` accessor, row `MinHeight` = toolbar + `MixerView.MinContentHeight` (scaled), height = `MixerDockHeight` clamped, or the natural full-strip height when `0`
- [X] T014 [US1] Rewrite `App/MainWindow.Mixer.cs` as the placement controller (research R1): `ShowMixer()`, `HideMixer()`, `ToggleMixer()` (bound to `view.mixer`), `IsMixerShown`; docked path = `SetHost(MixerHost, _mixer)`, `_mixer.IsVisible = true`, `_mixer.Refresh()`, `LayoutBottomPanels()`; hide path detaches via `SetHost(MixerHost, null)`, sets `IsVisible = false`; keep the existing floating code path working for `MixerFloating == true`; update the file header comment
- [X] T015 [US1] Wire the panel toolbar in `App/MainWindow.Mixer.cs`: segments ↔ `_mixer.View` + `settings.Current.MixerView` (same as `MixerWindow.ViewPicked`), close button → `HideMixer()`; keep the segments in sync when the view changes elsewhere
- [X] T016 [US1] In `App/MainWindow.Commands.cs` bind `view.mixer` to `ToggleMixer()` (replacing `ToggleMixerWindow`); `App/MainWindow.Input.cs` ⌘⇧M keeps running `view.mixer` — fix its comment
- [X] T017 [US1] Persist in `App/MainWindow.Mixer.cs` / `App/MainWindow.axaml.cs`: `MixerSplitter.DragCompleted` stores `MixerRow.ActualHeight` (design units) to `MixerDockHeight`; every user show/hide writes `MixerShown`; `settings.Save()` after each
- [X] T018 [US1] Restore on start in `App/MainWindow.axaml.cs` `Opened` (after the welcome / recovery flow): if `MixerShown` and not `MixerFloating`, `ShowMixer()` docked
- [X] T019 [US1] In `App/MainWindow.Palette.cs` `SnapshotContext`: focus within `MixerPanel` → `PaletteOrigin.Mixer`, track = `Timeline.SelectedTrackId` (mirror the `MixerWindow` branch); palette stays in `PaletteLayer`
- [X] T020 [US1] Verify in the docked panel (quickstart 14–17): Space / Return / ⌘Z work with a fader focused, Escape does not hide the docked Mixer, MIDI learn via the main `LearnOverlay` maps a docked fader (wire `LearnOverlay.Refresh()` on show if highlights don't appear), UI scale 125% and Studio Light restyle live; fix what fails in `App/MainWindow.Mixer.cs`

**Checkpoint**: The docked Mixer works in Arrangement — MVP.

---

## Phase 4: User Story 2 — Docked Mixer in every main view (Priority: P2)

**Goal**: The docked Mixer stays shown with the same view and height across Arrangement,
Session and Modular; Session's own sections are untouched and in sync.

**Independent Test**: quickstart scenario 9.

- [X] T021 [US2] Check the view switches (`OnShowSession`, `OnShowModular`, arrangement) in `App/MainWindow*.cs` for code that hides bottom panels or resets `BodyGrid` rows; make them leave `MixerPanel`, `MixerRow` height and `_mixer.View` alone, and call `LayoutBottomPanels()` if they change the Devices panel
- [X] T022 [US2] With the Mixer docked in Session, change volume / pan / sends in `App/SessionView.Mixer.cs` sections and in the docked strips; confirm each side follows the other (both read the engine on the UI tick); fix any missing refresh in `App/MainWindow.axaml.cs` tick (`_mixer.UpdateMeters()`)

**Checkpoint**: US1 + US2 work together.

---

## Phase 5: User Story 3 — Switch between docked and floating (Priority: P2)

**Goal**: Pop out from the panel, dock back from the window, toggle from View menu /
palette; last placement and shown state survive a restart.

**Independent Test**: quickstart scenarios 10–13 and 18.

- [X] T023 [US3] In `App/MixerWindow.cs` add a dock button at the right of the title bar (tooltip "Dock the mixer in the main window") and `public event Action? DockRequested`; clicking raises it and closes the window; plain close / Escape stays "hide"
- [X] T024 [US3] In `App/MainWindow.Mixer.cs` add `SetMixerFloating(bool)` (research R7): writes `MixerFloating`; if shown, moves the Mixer — docked → `SetHost(MixerHost, null)`, hide the panel rows, open `MixerWindow` with the existing setup (view, `FitWidth`, MIDI learn, `StripsChanged`); floating → close the window without recording hidden, then `ShowMixer()` docked; panel pop-out button and `DockRequested` call it
- [X] T025 [US3] In `App/MainWindow.Commands.cs` bind `view.mixerFloat` to `SetMixerFloating(!MixerFloating)`; in `App/MainWindow.axaml` add View → "Mixer in its own window" (`app:Commands.Id="view.mixerFloat"`, checkable) after "Mixer", and keep its check state in sync from `App/MainWindow.Mixer.cs`
- [X] T026 [US3] Floating persistence in `App/MainWindow.Mixer.cs`: user close of the window writes `MixerShown = false`, but not when `_shuttingDown` is set (`App/MainWindow.DetailWindow.cs` sets it) or when the close comes from a dock / float switch; restore in `App/MainWindow.axaml.cs` `Opened`: `MixerShown && MixerFloating` → open the window after the main window is shown

**Checkpoint**: All stories work; quickstart 1–18 pass.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T027 [P] Fix stale comments: `App/MixerView.cs` header ("a third Detail mode"), `App/MainWindow.axaml` Detail panel comment ("… / Mixer for the selected track"), the `_mixer` creation comment in `App/MainWindow.axaml.cs`
- [X] T028 Run all [quickstart.md](quickstart.md) scenarios (headless harness with `NOTA_DATA_DIR=/tmp/dockmixer/data` where the GUI can't launch); capture docked / floating / both-panels / low-height screenshots for the PR

### Nota closing tasks *(always include — these are the PR gates)*

- [X] T029 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T030 AGPL-3.0-only SPDX two-line header on every new source file — N/A unless a new file is added (plan adds none)
- [X] T031 New dependency — N/A
- [X] T032 `CHANGELOG.md` `[Unreleased]` → Added: the Mixer docks in the main window (Arrangement, Session, Modular) and can pop out / dock back; strips adapt to the height
- [X] T033 FEATURES.md via `nota-features`; Mixer page EN + RU in `../nota-docs` with new screenshots via `nota-docs`; `nota-shortcuts`: confirm ⌘⇧M entry text still fits (no new binding)
- [X] T034 Engine behaviour change → smoke test — N/A (UI-only)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001–T002)**: none.
- **Foundational (T003–T011)**: T003 → T004 → T005 → T006/T007; T008 → T009 → T010 → T011. The two tracks are independent of each other.
- **US1 (T012–T020)**: needs T001, T005, T008–T010.
- **US2 (T021–T022)**: needs US1 (there is nothing docked to keep otherwise).
- **US3 (T023–T026)**: needs US1 docked path and T002; T023 can start any time after T007.
- **Polish (T027–T034)**: after the stories.

### Within stories

- US1: T012 → T013 → T014 → T015–T019 → T020.
- US3: T023 → T024 → T025, T026.

### Parallel Opportunities

- T001 ∥ T002.
- MixerView track (T003–T007) ∥ BodyGrid track (T008–T011) — different files.
- T023 (`MixerWindow.cs`) ∥ US1 tasks in `MainWindow.*`.
- T027 ∥ T028.

## Parallel Example

```text
Track A: T003 → T004 → T005 → T006 → T007   (App/MixerView.cs, App/MixerWindow.cs)
Track B: T008 → T009 → T010 → T011          (App/MainWindow.Detail.cs, DetailWindow.cs, axaml.cs)
```

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (the floating Mixer already gains adaptive strips).
2. US1 → validate quickstart 1–8, 14–17 → the docked Mixer is usable.

### Incremental Delivery

1. + US2 → view switching verified.
2. + US3 → pop-out / dock, restart restore.
3. Polish, docs, `scripts/build.sh`, PR with screenshots (`nota-pr`).

## Notes

- One `MixerView` instance throughout; always move it with `SetHost`, never assign `Content` directly (#164).
- Only a finished splitter drag writes a height; clamps never do.

## Phase 7: Convergence

- [X] T035 Re-run `scripts/build.sh` on an idle machine (Rider closed) until native + managed + smoke test are green; if the render-pool `overloaded` checks still fail, investigate before the PR per Constitution IV (partial)
- [X] T036 [US2] Make Session's own mixer sections follow the engine on the UI tick — volume (`MiniFader` + dB read-out), pan (`PanBar`) and M / S / Arm toggles in `src/managed/Nota.App/SessionView.Mixer.cs`, skipping a control being dragged — so changes made in the docked Mixer show there per FR-012, US2/AC2 (partial)
- [X] T037 In `src/managed/Nota.App/MixerView.cs` include the Stereo map layout's grid margins (8 + 8) in `MinContentHeight` while `View == StereoMap`, and raise `MinContentHeightChanged` when the view changes, so M / S / Arm stay visible at the minimum docked / window height per FR-014, T007 (partial)
