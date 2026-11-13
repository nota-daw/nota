---
description: "Tasks for 095 — Modular tab in the browser, arrangement track headers in Modular"
---

# Tasks: Modular tab in the browser, arrangement track headers in Modular

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/ui-contract.md](contracts/ui-contract.md),
[quickstart.md](quickstart.md) · Issue nota-daw/nota#592

**Tests**: smoke tests only for the pure pieces (plan § Testing); views are verified by the
quickstart and the headless screenshot harness.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Create branch `feat/modular-browser-headers` off an up-to-date `develop` (spec files carried over untracked)

---

## Phase 2: Foundational

**Purpose**: the pure Presentation pieces and the BrowserItem kind every story builds on.

- [X] T002 [P] Add `BrowserItemKind.ModularNode` and init-only `string Keywords { get; init; } = ""` to `BrowserItem` in src/managed/Nota.Presentation/ViewModels/BrowserViewModel.cs
- [X] T003 [P] Create `ModularBrowserItems` (`Build(nodes)` one row per node in catalogue order: `Name`, `Kind = ModularNode`, `BuiltinKind = node.Kind`, `Sub = node.Sub`, `Tip = description`, `Keywords = aliases joined by spaces`; `Matches(item, query)`: empty matches, else every whitespace-separated word occurs case-insensitively in name, category or keywords) in src/managed/Nota.Presentation/ModularBrowserItems.cs
- [X] T004 [P] Create `BrowserTabMemory(startTab, modularTab)` with `Picked(tab)`, `Enter()` (→ `ModularTab ?? modularTab`), `Leave()` (→ `OutsideTab`); the Modular tab index is never stored as `OutsideTab` — in src/managed/Nota.Presentation/BrowserTabMemory.cs
- [X] T005 Add `ModularBrowserTests` (every `ModularCatalog.All` node mapped in order; "wobble" → LFO, "sidechain" → Envelope Follower, "monitor" → Scope, "env foll" → Envelope Follower; tab memory: first Enter → modular tab, Picked in Modular survives Leave/Enter, Leave returns the outside tab) in tests/Nota.SmokeTest/ModularBrowserTests.cs and register it in tests/Nota.SmokeTest/Program.cs

**Checkpoint**: Presentation compiles; smoke tests for the pure pieces pass.

---

## Phase 3: User Story 2 — Arrangement track headers in Modular (P1)

**Goal**: Modular's Track view shows the arrangement's header column (same instance, headers-only), on the configured side.

**Independent Test**: quickstart Q2.

- [X] T006 [US2] Create src/managed/Nota.App/ArrangementView.HeadersOnly.cs: `HeadersOnly` property hiding overview row, sections row, ruler + zoom bar, lanes, overlay, footer lanes and h-scroll; the top band becomes a header-width 34 px strip with the "Tracks" caption, the bottom band a 22 px strip; the view docks on the header side with `Width = HeaderColumnWidth`; `HeaderColumnWidth` + `HeaderColumnWidthChanged` (raised from `SetHeaderWidth` and side changes); keep fields for the bands it toggles in src/managed/Nota.App/ArrangementView.cs
- [X] T007 [US2] Add `TrackAtHeaderPoint(Point topLevelPoint)` (tracks, groups, returns; not master) and `SetColumnHint(bool)` (brass column divider) to src/managed/Nota.App/ArrangementView.HeadersOnly.cs
- [X] T008 [US2] Remove the "Tracks" rail from src/managed/Nota.App/ModularView.cs (`_sidebar`, `_sidebarPanel`, `TrackRow`, the list column); keep the empty-state hint; cross-track CV drop in `FinishConnect` asks `TrackAtPoint` (host) and opens `ShowCrossTrackParamMenu` on the returned anchor; `BeginConnect`/`FinishConnect` raise `ConnectHintChanged`
- [X] T009 [US2] In `SetView` (src/managed/Nota.App/MainWindow.axaml.cs): Modular shows `Timeline` with `HeadersOnly = true`, insets `_modular.Margin` by the column width on the header side, updates on `HeaderColumnWidthChanged` and on `ApplyHeadersRight`; other views reset `HeadersOnly` and the margin; wire `TrackAtPoint` → `Timeline.TrackAtHeaderPoint`, `ConnectHintChanged` → `Timeline.SetColumnHint`; keep `Timeline.ModularTrackId` in step with the shown track
- [X] T010 [US2] Add `ArrangementShown => Timeline.IsVisible && !Timeline.HeadersOnly` and use it where `Timeline.IsVisible` means "the arrangement is the view" in src/managed/Nota.App/MainWindow.Menu.cs (Copy to Session) and src/managed/Nota.App/MainWindow.Input.cs (clip nudge arrows); leave track-header keys working in Modular
- [X] T011 [US2] Verify with the headless harness: Modular with headers left and right, new and classic look, arrangement unchanged when shown (quickstart Q2 steps 1, 5, 6, 8)

**Checkpoint**: US2 works; arrangement unchanged.

---

## Phase 4: User Story 1 — Modular nodes from the browser (P1)

**Goal**: a Modular tab listing catalogue nodes; drag and double-click add nodes.

**Independent Test**: quickstart Q1 steps 1–7.

- [X] T012 [P] [US1] Add `GlyphKind.PatchCable` (16-unit grid, the rail's stroke weight, distinct from `GlyphKind.Modular`) in src/managed/Nota.App/Controls/Glyph.cs
- [X] T013 [US1] Add the Modular tab to src/managed/Nota.App/BrowserView.cs: `ModularTab = 9`, `TabCount = 10`, rail position after MIDI effects, tooltip `browser.modular`, `_sources[ModularTab] = ModularBrowserItems.Build(ModularCatalog.All)`, `ApplyFilter` uses `ModularBrowserItems.Matches` for it, no audition / chips / favourites for it; rows show category and the description tooltip
- [X] T014 [US1] Add `SetModularContext(bool)` with `BrowserTabMemory` (tab hidden outside Modular; Enter/Leave pick the tab; `SelectTab` reports `Picked`) in src/managed/Nota.App/BrowserView.cs; call it from `SetView` in src/managed/Nota.App/MainWindow.axaml.cs
- [X] T015 [US1] Add `IsModularNodeDrag`; make `IsAcceptableDrag` refuse ModularNode drags in src/managed/Nota.App/BrowserView.cs
- [X] T016 [US1] In src/managed/Nota.App/ModularView.cs accept ModularNode drags on the canvas only when `CanAddNodes` (Track view, track shown) with the drop glow; on drop call `AddModulatorAt(kind, dropPoint)` (new optional viewport-point parameter); `StatusRequested` for refusals
- [X] T017 [US1] Route browser `ItemActivated` for ModularNode rows to `_modular.AddModulatorAt(kind)` (centre) or the status reason ("Open a track in Modular") in src/managed/Nota.App/MainWindow.Browser.cs; `Timeline.Refresh()` after an add, as the palette does
- [X] T018 [US1] Verify quickstart Q1 steps 1–7 in the running app

---

## Phase 5: User Story 3 — Group graphs (P2)

**Goal**: a group's header opens its effect chain as a graph; modulation works on it.

**Independent Test**: quickstart Q4.

- [X] T019 [US3] In src/managed/Nota.App/ModularView.cs accept group tracks in `Show`; source node "Tracks in" (badge `GROUP`) for groups; `Show(engine.MasterTrackId)` is a no-op; add `ShowTrack(id)` (leaves Global view); Global view keeps skipping groups
- [X] T020 [US3] Verify quickstart Q4 (LFO → group Auto Filter cutoff moves during playback; save / reopen keeps patch and positions)

---

## Phase 6: User Story 5 — Show in Modular (P2)

**Goal**: track menus jump to the track's graph.

**Independent Test**: quickstart Q3.

- [X] T021 [P] [US5] Add **Show in Modular** (`GlyphKind.Modular`, naming / inputs group) to `BuildTrackMenu` and to `ShowFooterMenu` for returns only, disabled when `HeadersOnly && trackId == ModularTrackId`; raise `ShowInModularRequested` in src/managed/Nota.App/ArrangementView.cs
- [X] T022 [P] [US5] Add **Show in Modular** to `ShowHeaderMenu` (not the master column) and raise `ShowInModularRequested` in src/managed/Nota.App/SessionView.Grid.cs
- [X] T023 [US5] Handle `ShowInModularRequested` from both views in src/managed/Nota.App/MainWindow.axaml.cs: `SetView(Modular)`, select the track, `_modular.ShowTrack(t)`
- [X] T024 [US5] Verify quickstart Q3

---

## Phase 7: User Story 4 — Remove + Add (P3)

- [X] T025 [US4] Remove the **+ Add** flyout and the unused `AddModulator(int)` from src/managed/Nota.App/ModularView.cs; palette Modular entries unchanged (quickstart Q1 step 8)

---

## Phase 8: Polish & cross-cutting

- [X] T026 Add the new strings (`browser.modular`, `common.show_in_modular`, `modular.tracks_in`, `modular.open_a_track_to_add_nodes`, …) to src/managed/Nota.Application/Localization/en.json; drop keys that became unused (`modular.add`, `modular.tracks`, `modular.no_tracks_yet` if unused)
- [X] T027 RU strings for the new keys — follow the project's reference-sync flow for translations (the repo keeps only the en.json reference; RU follows the reference sync)
- [X] T028 [P] CHANGELOG.md `[Unreleased]` entry (Added: Modular browser tab, headers in Modular, group graphs, Show in Modular; Removed: Modular + Add)
- [X] T029 [P] FEATURES.md via the nota-features skill (Modular View, Browser, Windows and interface)
- [X] T030 [P] User docs via the nota-docs skill (Browser and Modular pages, EN + RU) in ../nota-docs
- [X] T031 Shortcuts check via the nota-shortcuts skill (no new bindings expected; confirm)
- [ ] T032 `scripts/build.sh` green (smoke test incl. `ModularBrowserTests`, `DesignTokenCheck`, localisation check)
- [X] T033 `/speckit-converge` reports Converged

---

## Dependencies & Execution Order

- T001 → T002–T005 (T002–T004 parallel; T005 after T003/T004).
- US2 (T006–T011) after Phase 2; it is the riskiest, done first.
- US1 (T012–T018) after Phase 2; T016 needs T008 (ModularView after rail removal) to avoid
  conflicting edits; T014 touches `SetView` after T009.
- US3 (T019–T020) after T008.
- US5 (T021–T024) after T006 (`HeadersOnly`, `ModularTrackId`) and T019 (`ShowTrack`).
- US4 (T025) after T016.
- Polish after all stories.

## Parallel examples

- Phase 2: T002, T003, T004 together.
- US1: T012 alongside T013.
- US5: T021 and T022 together (different files).
- Polish: T028, T029, T030 together.

## Implementation Strategy

US2 first (biggest risk, the headline change), then US1 (the tab) — together they are the
issue's MVP. US3, US5 and US4 follow as small increments; polish and the build gate close.

---

## Phase 9: Convergence

- [X] T034 Align `ModularTrackId` in specs/095-modular-browser-headers/contracts/ui-contract.md and data-model.md with the code (`Func<int>?` asked when a menu opens, so it never goes stale) per plan: contracts/ui-contract.md (contradicts)
