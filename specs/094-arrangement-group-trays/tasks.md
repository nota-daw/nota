---

description: "Tasks for Arrangement group trays (specs/094, issue #584)"
---

# Tasks: Arrangement group trays

**Input**: Design documents from `specs/094-arrangement-group-trays/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/header-geometry.md](contracts/header-geometry.md),
[quickstart.md](quickstart.md)

**Tests**: The plan asks for `GroupTrayTests` in the smoke test (the layout rules); visual
checks follow the quickstart.

**Organization**: Tasks are grouped by user story (US1 trays, US2 meters and columns, US3
classic staircase).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

## Path Conventions

- Managed app: `src/managed/Nota.App/`, pure layout: `src/managed/Nota.Presentation/`
- Tests: `tests/Nota.SmokeTest/`

---

## Phase 1: Setup

**Purpose**: Branch and baseline

- [X] T001 Create branch `feat/arrangement-group-trays` off `develop` in the main checkout (carry the untracked `specs/094-arrangement-group-trays/`); confirm `scripts/build.sh` builds the managed app (`dotnet build src/managed/Nota.App/Nota.App.csproj`) before any change

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The pure layout and the look plumbing every story draws from

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 Create `GroupTrayLayout` in `src/managed/Nota.Presentation/GroupTrayLayout.cs` (SPDX header): input record `TrayRow(Id, ParentId, IsGroup, Folded, ColorIndex, HiddenTracks)`, output `TraySlice(Depth, Chain: TrayRef[], Heads, Closings, ContentLeft, SpineX, FoldedCount)` and `TrayRef(GroupId, ColorIndex, Level)`; `Compute(IReadOnlyList<TrayRow> rows, bool classic)` per data-model.md rules — chain = ancestors outer → inner plus the row itself if a group; tray T closes on the last row whose chain contains T, innermost first, a folded or empty group closing on its own row; new look `Step = 6`, `ContentLeft = Chain.Length * Step` (0 at top level); classic `SpineX = Depth * 10`, `ContentLeft = SpineX + 3`; `FoldedCount = HiddenTracks` only for a folded group; constants `Step`, `EdgeW = 3`, `ClosingH = 4`, `ClassicStep = 10` public for the view and tests
- [X] T003 [P] Write `tests/Nota.SmokeTest/GroupTrayTests.cs` (SPDX header) on the quickstart project shape (Group ⊃ DRUMS{2 tracks}, Instruments folded{4}; then top-level Consort): depth-2 `ContentLeft` − top-level ≤ 12 (SC-001); DRUMS closes on "Audio 4" with Closings 1; folded Instruments row closes 2 (Instruments then Group); Consort closes 0 and has an empty chain; every visible group closed exactly once; folded count 4 and a folded group holding a nested group counts tracks only; classic SpineX 0/10/20 for depths 0/1/2; `ClosingH * maxClosings ≤ rowH − 20` for row heights 30 and 26 at depth ≤ 3; register `GroupTrayTests.Run()` in `tests/Nota.SmokeTest/Program.cs`
- [X] T004 Add `HeaderLook` (`Look`, default `Look.New`) to `ArrangementView` in `src/managed/Nota.App/ArrangementView.cs` next to `ClipStyle`: setter stores, calls `RebuildHeaders()` and invalidates `_lanes`; set it in `src/managed/Nota.App/MainWindow.axaml.cs` at start-up from `NotaThemeService.Look` and subscribe `NotaThemeService.Changed` to update it only when the look differs (FR-013, research R6)
- [X] T005 Feed the layout in `src/managed/Nota.App/ArrangementView.Groups.cs`: after `ApplyHierarchy`, build `TrayRow`s from `_tracks` (Id, GroupId, IsGroup, `_fold.IsFolded`, ColorIndex, `GroupSlotCount` for groups) and keep `Dictionary<int, TraySlice> _traySlices` from `GroupTrayLayout.Compute(rows, HeaderLook == Look.Classic)`; recompute on every hierarchy rebuild and on a `HeaderLook` change

**Checkpoint**: layout computed and tested; headers still drawn the old way

---

## Phase 3: User Story 1 - Read nested groups at a glance in the new look (Priority: P1) 🎯 MVP

**Goal**: Groups as trays in the new look (FR-001–FR-006, FR-014)

**Independent Test**: Quickstart § 2 steps 1 and 3 and § 3 fold / reorder in the new look

- [X] T006 [P] [US1] Create `GroupTrayLayer` in `src/managed/Nota.App/Controls/GroupTrayLayer.cs` (SPDX header, custom `Render`, not hit-testable): given a `TraySlice`, the header width, the row height and `TrayInsetR = 4`, draw each tray in the chain outer → inner from `x = Level * Step` to the right edge — wash `TrackTint(color, 0.16)` when the row heads that tray, else `TrackTint(color, 0.08)`; its 3 px edge in `TrackBrush(color)`; rounded top-right (`NotaRadius` field step) on a head row; then the closing strips (4 px each, innermost above outer) at the row bottom with the bottom-right corner rounded; then, for a member track, the card body (`Brush.Panel`, or `TrackHeaderSelected` when selected) from `ContentLeft` to `width − TrayInsetR`, above the closing strips; repaint on `NotaThemeService` change through the existing invalidate path
- [X] T007 [US1] In `src/managed/Nota.App/ArrangementView.cs` replace the new-look use of `HeaderBars` in `BuildTrackCard` and `BuildGroupBar`: put a `GroupTrayLayer` for the row's slice under the content; drop the 6 % member `Border` tint and the 16 % group `Border` tint; the track's own 3 px bar at `ContentLeft` (inset 6 top / bottom, as today); content margin left `ContentLeft + bar + NameGap`, bottom `Closings * ClosingH`; keep `_headerCards` spine / selection repaint working (`UpdateHeaderSelection` must repaint the layer's card body for a member)
- [X] T008 [US1] New-look slim group row in `BuildGroupBar` (`src/managed/Nota.App/ArrangementView.cs`): chevron, name, M / S / (Cue) / Link, no "Group" tag; the selected-group full card (`BuildTrackCard` for a group) also sits on its tray head slice
- [X] T009 [US1] Folded count (both looks) in `BuildGroupBar` and the group branch of `BuildTrackCard` in `src/managed/Nota.App/ArrangementView.cs`: when `FoldedCount` is set, show ` · N` after the name via `TypeTag`-style text (Label 11, ink 3), trimming with the name (FR-006)
- [X] T010 [US1] Group lane wash in `src/managed/Nota.App/ArrangementView.LaneControl.cs` (~1158): keep `TrackTint(color, 0.16)` on a group's own lane only when `_o.HeaderLook == Look.New`; member lanes unwashed (FR-014)

**Checkpoint**: new look shows trays; quickstart § 2.1 passes

---

## Phase 4: User Story 2 - Group controls and meters that don't collide (Priority: P1)

**Goal**: Meters inside the card, group meter in the right column, fixed control columns (FR-005, FR-007, FR-008, FR-009)

**Independent Test**: Quickstart § 2 step 2 and step 6, all three row heights, four themes

- [X] T011 [US2] Header meters in `src/managed/Nota.App/ArrangementView.cs`: set `MinHeight = 0` on every header `MeterBar` (track cards, group cards, slim group rows); margins equal top and bottom inside the space left after closing strips, so no meter pixel leaves the card on Compact (30), Standard (50+) and Large rows (research: `MinHeight = 40` is the overflow's root cause)
- [X] T012 [US2] New-look slim group row meter in `BuildGroupBar` (`src/managed/Nota.App/ArrangementView.cs`): replace the horizontal rail with a vertical `MeterBar` docked right with the same width and right margin as a track card's meter, so both sit in one column; keep `_meters[t.Id]` registration
- [X] T013 [US2] One right content edge in `src/managed/Nota.App/ArrangementView.cs`: lay out the chips / volume / pan / picker / meter of every row (top-level and member, track and group) against the same right edge (`HeaderW − TrayInsetR − HeaderRightPad` or equivalent constant), so the M chip of rows of the same kind lines up at depths 0–4; the name trims first; update the picker room maths in the Standard tier (`room = HeaderW − x − …`) to the new left / right edges

**Checkpoint**: meters and controls correct in the new look at every row height

---

## Phase 5: User Story 3 - The 0.59.2 staircase in the classic look (Priority: P2)

**Goal**: Classic look headers as in v0.59.2 (FR-010–FR-013)

**Independent Test**: Quickstart § 2 steps 4–5 against a v0.59.2 build

- [X] T014 [US3] Classic geometry in `src/managed/Nota.App/ArrangementView.cs`: when `HeaderLook == Look.Classic`, build cards with the 0.59.2 column layout (`git show v0.59.2:src/managed/Nota.App/ArrangementView.cs`, `SpineGrid`): empty column `SpineX`, the row's own 3 px spine full height (accent when selected), body after it; no `GroupTrayLayer`, no tints; track cards on `Brush.SurfaceCard`, group cards on `Brush.SurfaceRaised`; keep today's controls (sends, Cue, Link marks, takes toggle, player tag)
- [X] T015 [US3] Classic slim group row in `BuildGroupBar` (`src/managed/Nota.App/ArrangementView.cs`): 0.59.2's — chevron, name 11 / SemiBold (+ folded count), M / S / (Cue) / Link 3 apart, the "Group" tag, and a horizontal `MeterBar` (`MinWidth = 0`) docked bottom of a `DockPanel` with margin 0,0,0,3 so it takes its own line under the title; body margin 8,0,8,0
- [X] T016 [US3] Check the live switch: changing Preferences → Appearance → Look rebuilds headers in the other look with fold states, row heights and selection kept (`HeaderLook` from T004); MCP `set_look` takes the same path

**Checkpoint**: both looks correct and switchable live

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T017 Remove now-unused code in `src/managed/Nota.App/ArrangementView.cs` (`HeaderBars`' per-ancestor bars, `GroupIndent = 14`, `GroupBarW`) once both looks use the slices; keep comments explaining why each layer exists
- [X] T018 [P] DESIGN.md § Track header: replace the "Inside a group the member is indented 14 …" bullet with the tray rules (new look) and the staircase (classic look); state the header meter fits inside the card; mention the folded count
- [X] T019 [P] `CHANGELOG.md` `[Unreleased]`: Changed — groups in the Arrangement headers drawn as trays (new look) / 0.59.2 staircase (classic), folded count; Fixed — header meters spilling out of the card, group meter under the buttons (#584)
- [X] T020 Visual verification per quickstart § 2–3 with the headless-Avalonia harness: both looks × Studio Dark / Studio Light / Ember Graphite / Ember Paper × Compact / Standard / Large; fix anything off the contract — done with /private/tmp/n094harness (24 shots + fold, selected group, live look switch); not exercised headlessly: drag-reordering into / out of a group and dragging a row seam inside a tray
- [X] T021 `DesignTokenCheck` clean: no raw colours or radii in the new code (`tests/Nota.SmokeTest/DesignTokenCheck*.cs` allow-lists only if a value is a deliberate geometry constant)

### Nota closing tasks *(always include — these are the PR gates)*

- [X] T022 Run `scripts/build.sh` until native + managed + smoke test are green — builds green; smoke test all pass except the render-pool timing checks ("overloaded", VST3 deadline), which fail on this machine on clean develop too
- [X] T023 AGPL-3.0-only SPDX two-line header on `GroupTrayLayout.cs`, `GroupTrayLayer.cs`, `GroupTrayTests.cs`
- [X] T024 New dependency: none → N/A
- [X] T025 User-visible change → `CHANGELOG.md` `[Unreleased]` entry (T019)
- [X] T026 FEATURES.md via `nota-features` (folded group count; group display per look) and user docs via `nota-docs` (arrangement groups page and screenshots, EN + RU); shortcuts: none → N/A — FEATURES.md done; nota-docs in ~/repos/nota/nota-docs-584 (branch docs/arrangement-group-trays): basics/tracks + views/arrangement EN + RU, 5 arrangement shots × 2 themes retaken; FR left behind (allowed)
- [X] T027 Engine behaviour change: none → N/A (smoke test extended by T003 for the layout)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)** → **Foundational (T002–T005)** → user stories.
- **US1 (T006–T010)** and **US2 (T011–T013)** both edit `ArrangementView.cs` builders; do
  US1 first, then US2 (same file, same methods).
- **US3 (T014–T016)** after US1/US2 (shares the builders; the classic path branches off them).
- **Polish (T017–T027)** after all stories.

### Within stories

- T002 before T003 (tests compile against the layout) and T005.
- T004 before T005 (layout depends on the look) and T010, T016.
- T006 before T007; T007 before T008–T009.

### Parallel Opportunities

- T003 (tests) alongside T004 once T002 exists.
- T006 (`GroupTrayLayer.cs`, new file) alongside T005.
- T018 (DESIGN.md) and T019 (CHANGELOG) alongside T020.

### Parallel Example: Foundational

```text
T003 GroupTrayTests.cs       (tests/Nota.SmokeTest)
T004 HeaderLook plumbing     (ArrangementView.cs, MainWindow.axaml.cs)
```

## Implementation Strategy

### MVP

T001–T013: the new look (the default) with trays, meters that fit and fixed columns — that
alone fixes what users see by default.

### Incremental delivery

1. Foundation (layout + tests + look plumbing).
2. US1 trays → screenshot check.
3. US2 meters / columns → screenshot check at three row heights.
4. US3 classic → side-by-side with v0.59.2.
5. Polish, docs, build gate.
