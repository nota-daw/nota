---

description: "Task list for Mixer Groups and Compact Strips"
---

# Tasks: Mixer Groups and Compact Strips

**Input**: Design documents from `/specs/070-mixer-groups-compact-strips/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/ui-mixer.md](contracts/ui-mixer.md),
[quickstart.md](quickstart.md), the agreed [mockup](mockups/)

**Tests**: No automated test tasks — the spec does not ask for them and the managed UI has
no unit-test project. Each story is validated with the matching
[quickstart.md](quickstart.md) scenarios (headless Avalonia harness + screenshots);
`scripts/build.sh` is the PR gate.

**Organization**: Tasks are grouped by user story. Groups (US1 + US2) and compact strips
(US3) touch different parts of `MixerView` and can ship as two PRs (plan § Constitution
Check, V).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

All paths are relative to the repository root. `App/` below means
`src/managed/Nota.App/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The one persisted preference and the new strings, used across stories.

- [X] T001 [P] Add `MixerCompact` (bool, default `false`) next to `MixerView` in `src/managed/Nota.Application/Settings.cs`, with a `<summary>` in the file's style ("The Mixer's strip layout (specs/070): false = regular strips, true = compact 72 px strips. Shared by the docked panel and the pop-out window."); data-model rule: missing in an older `settings.json` → regular
- [X] T002 [P] Add the new strings to `App/Languages/en.json` and their French to `App/Languages/fr.json`: `Compact`, `Show compact strips: more tracks on screen`, `Fold group {0}`, `Unfold group {0}`, `{0} hidden strips`, `A hidden track is soloed`, `A hidden track is armed`, `A hidden track clipped · click to clear` (contracts/ui-mixer.md § Strings)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The hierarchical order every story builds on (research R1).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 In `App/ArrangementView.Groups.cs`, extract the child-map builder used by `ApplyHierarchy` (group id → children in engine order; a track whose `GroupId` is missing or not a group is a root) into a private static helper working on `NotaTrackInfo`, and add `internal static IReadOnlyList<HierarchyRow> HierarchyOrder(IAudioEngine eng)` returning `(int Index, int Id, int Depth, int ParentGroupId)` for every non-return track in DFS order (group, then its children), **ignoring collapse**. `ApplyHierarchy` keeps its `TrackVM` walk but takes its children from the same helper so the two orders cannot drift. Depth-guard the walk (≤ 32) against a malformed parent chain, like `GroupShadeFor`
- [X] T004 In `App/MixerView.cs` `Refresh()`, list non-return strips in `ArrangementView.HierarchyOrder(_engine)` order, then returns (still `DisplayOrder`'s second pass), then master; keep a per-strip record `(id, depth, parentGroupId, Border strip)` in a new `_strips` list (cleared with the other lists) for the band layer, folding and selection. Update the file's header comment (order now follows the Arrangement's hierarchy)

**Checkpoint**: The Mixer lists Bus, Drums, Kick, Snare, Hats, Bass, … for the quickstart project even with a scrambled engine order (quickstart scenario 1, order part).

---

## Phase 3: User Story 1 - See which strips belong to which group (Priority: P1) 🎯 MVP

**Goal**: Each group's strips sit under a named bar in the group's colour with a faint tint; nesting shows as stacked bars (FR-001 – FR-005).

**Independent Test**: quickstart scenarios 1, 2, 3 — bands correct for nested and ungrouped tracks, follow membership / rename / recolour / undo, show in the Stereo Map's strip column.

### Implementation for User Story 1

- [X] T005 [P] [US1] Create `App/Controls/GroupBandsLayer.cs` (SPDX header + a why-comment): a `Control` given a list of bands `(string Name, int ColorIndex, int Depth, Control FirstStrip, Control LastStrip, int HiddenCount)` and `BarH = 14`. `Render` translates each strip's bounds into its own space and draws, per band, (a) the tint `NotaPalette.Wash(ArrangementView.TrackBrush(color), ≈ 0x12)` from `Depth × BarH` down to its bottom, radius `NotaRadius.Clip` on the top corners, and (b) the bar `Wash(…, ≈ 0x52)` at `Depth × BarH`, height `BarH − 2`, radius `NotaRadius.Clip`, the name in Geist 9 / 600 `TextPrimary` with 5 px side padding, ellipsised to the bar width; when `HiddenCount > 0` append ` +N` in Geist Mono `TextSecondary` (mockup). Read colours inside `Render` (no snapshot, DESIGN.md non-negotiable). `ToolTip` = full name via hit test on the bars. No pointer handling otherwise (`IsHitTestVisible` only for bars)
- [X] T006 [US1] In `App/MixerView.cs`, wrap `_row` in a `Panel { _bands, _row }` as the scroller's content; set `_row.Margin` top to `10 + maxDepth × BarH` (maxDepth = deepest group level + 1, 0 without groups); after `Refresh` build the band list from `_strips` (first = group strip, last = last strip of its subtree; colour = `ArrangementView.EffectiveColorIndex(_engine, groupId)`; name = `TrackNames.Of`) and hand it to `_bands`; invalidate `_bands` on `_row.LayoutUpdated` (cheap: no rebuild). Returns, master and ungrouped strips get no band (FR-005)
- [X] T007 [US1] In `App/MixerView.cs`, add `maxDepth × BarH` to `CoreH` (so `MinContentHeight` / `NaturalContentHeight` include the bar rows — spec edge case "bar rows always stay") and raise `MinContentHeightChanged` when maxDepth changes on `Refresh`
- [X] T008 [US1] In `App/MixerView.cs` `UpdateMeters`, detect structural changes beyond the track count — a track's `GroupId`, a group's name or colour index changed (cheap signature over `TryGetTrackInfo` + `GetTrackColorIndex`, compared with the last built one) — and `Refresh()` then, so bands follow moves into / out of groups, renames, recolours and undo / redo (FR-004)
- [X] T009 [US1] Check the Stereo Map layout (`ApplyView`): the band panel moves with `_stripsScroller`, so bars show over the strip column; fix any clipping of the bar row at the column's top margin (US1 scenario 5)

**Checkpoint**: Quickstart scenarios 1–3 pass; screenshots match `mockups/regular-*.png` for the bands.

---

## Phase 4: User Story 2 - Fold a group in the Mixer (Priority: P1)

**Goal**: A group strip folds its subtree in the Mixer and the Arrangement at once; folded state shows a count and hidden solo / arm / clip; saved with the project; the Mixer follows the selection (FR-006 – FR-012).

**Independent Test**: quickstart scenarios 4–10.

### Implementation for User Story 2

- [X] T010 [P] [US2] Create `App/GroupFold.cs` (SPDX header + why-comment: one fold state for the Arrangement and the Mixer, specs/070 R2): `internal sealed class GroupFold` with `IsFolded(int groupId)`, `Toggle(int groupId)`, `Set(IEnumerable<int> ids)`, `Remove(int groupId)`, `Clear()`, `IReadOnlyCollection<int> Folded`, and `event Action? Changed` raised once per call that changed something
- [X] T011 [US2] In `App/ArrangementView.Groups.cs`, replace `_collapsed` with an injected `internal GroupFold Fold { get; set; } = new();` — `CollapsedGroups`/`SetCollapsedGroups`/`IsGroupCollapsed`/`ToggleCollapse`/`UngroupGroup` delegate to it; subscribe to `Fold.Changed` → `Refresh()` (and unsubscribe when `Fold` is replaced). Update the other readers: `App/ArrangementView.cs` lines ~2125/2127 and ~2217/2219 (glyph + `ToggleCollapse`) and `App/ArrangementView.Overview.cs` ~317/324 (`_o._collapsed.Contains` → `_o.Fold.IsFolded`). Note: `ArrangementView.cs` is also being edited on branch `071-detail-panel-double-click` — keep this change to those four lines
- [X] T012 [US2] In `App/ArrangementView.Sizing.cs`, persist the fold state in `track-view.json`: add `[JsonPropertyName("collapsedGroups")] public List<int>? CollapsedGroups` to `TrackViewDto` (engine track **indices**, "omitted when empty; an index that is out of range or not a group is skipped on load"); write it in `SaveTrackView` from `Fold.Folded` mapped id → index; include it in the "nothing to save → delete the file" check; read it in `LoadTrackView` (index → id, keep only groups) into `Fold.Set`; `ResetTrackView` calls `Fold.Clear()`. Keep `version: 1` (additive field)
- [X] T013 [US2] In `App/MainWindow.axaml.cs`, create one `GroupFold` next to `_mixer = new MixerView(vm.Engine)` (~line 479), assign it to `Timeline.Fold` and to the new `MixerView.Fold` property
- [X] T014 [US2] In `App/MixerView.cs`, add `public GroupFold? Fold { get; set; }` (subscribe to `Changed` → `ApplyFold()`); `ApplyFold()` sets `IsVisible` on each `_strips` entry to "no ancestor folded" without rebuilding (research R4), recomputes the band list (last visible strip, `HiddenCount` for folded groups = all descendants incl. nested) and invalidates `_bands`; `UpdateMeters` and `FollowStrips` skip strips whose border is not visible; call `ApplyFold()` at the end of `Refresh()`
- [X] T015 [US2] In `App/MixerView.cs` `Header(...)`, for group strips add the disclosure `Glyph` left of the name — `GlyphKind.ChevronDown` unfolded / `ChevronRight` folded, size 8, `TextSecondary`, as the Arrangement — in a focusable `Border` with the accessible name `Loc.F("Fold group {0}", name)` / `Loc.F("Unfold group {0}", name)`, left click and Space / Enter → `Fold.Toggle(id)`; repaint it in `ApplyFold`. No undo step (view state, FR-008)
- [X] T016 [US2] In `App/MixerView.cs`, add the folded group's hidden-state dots at the right of the header (6 px, `NotaPalette.Solo` / `Record` / `Danger`, shown only when true, tooltips from T002): on each `UpdateMeters` tick compute for each folded group whether any hidden descendant is soloed or armed (`TryGetTrackInfo`) and whether any hidden descendant's meter peaked ≥ 0 dBFS (`TryGetTrackMeter`, latched since the fold; a click on the clip dot clears the latch). No allocation per tick (reuse arrays sized on `Refresh`)
- [X] T017 [US2] Selection follow (research R5): in `App/MixerView.cs` add `public void FollowSelection(int trackId)` — clear the previous outline, find the strip, or when hidden its **outermost** folded ancestor's strip, give it a 1 px `NotaPalette.Accent` outline (an overlay `Border` inside the strip, no size change) and `BringIntoView()`; never unfold. In `App/MainWindow.Mixer.cs` subscribe `Timeline.TrackSelected` and `_modular.TrackActivated` to it (wiring in this file, not `MainWindow.Detail.cs`), and re-apply the current selection after `Refresh()`
- [X] T018 [US2] Confirm FR-012: the docked panel and `MixerWindow` reuse the same `MixerView` instance (specs/054), so fold state and outline carry over on pop-out / dock; check that `MixerWindow.FitWidth` uses visible strips only (`StripsWidth` measures `_row`, which already skips invisible children — verify)

**Checkpoint**: Quickstart scenarios 4–10 pass, including the byte-for-byte render check (SC-005) and the 100-track fold timing (SC-002); screenshots match `mockups/folded-*.png`.

---

## Phase 5: User Story 3 - Fit more strips with compact strips (Priority: P2)

**Goal**: A footer `Compact` toggle switches every strip to the 72 px layout from the mockup, with every regular action still available (FR-013 – FR-019).

**Independent Test**: quickstart scenarios 11–17.

### Implementation for User Story 3

- [X] T019 [P] [US3] Create `App/Controls/MeterFader.cs` (SPDX header + why-comment, research R7): a `Control` with the `VFader` surface — `Value` (0..1.5 linear), `Dragging`, `ValueChanged`, `GestureBegin` / `GestureEnd`, `SetValueExternal`, `EntryField(name, fmt, def)` — plus `Push(NotaMeter)` / `Reset()` like `MeterBar`. Layout: 14 px scale (Geist Mono 7, `TextDisabled`, marks `+6`, `0`, `−12`, `−48` at `MeterScale.NormDb`), two 18 px channels 3 px apart drawn with `MeterScale.DrawChannel`, an 11 px gutter with a 9 × 10 px left-pointing triangle at `MeterScale.NormDb(LinToDb(Value))` and a 1 px hairline across the channels at the same y — `VizInk` at rest, `Accent` while hovered or dragged (DESIGN.md: accent only while live). Interaction: left button only (right-click bubbles to MIDI Learn); relative vertical drag in dB over the meter height, Shift / Ctrl / ⌘ = fine (÷10); `ns-resize` cursor; a press-release without movement clears the peak hold instead; capture-lost ends the gesture. Never allocate in `Render`
- [X] T020 [US3] In `App/MixerView.cs`, introduce `StripMetrics` (record of width, header, I/O, sends, pan, fader min / comfort / max, buttons heights — data-model § StripMetrics: regular `154 · 24 · 34 · 42 · 38 · 72/120/190 · 28`, compact `72 · 24 · 18 · 24 · 22 · 72/120/220 · 66`) and make `CoreH`, `MinContentHeight`, `NaturalContentHeight` and `FitHeight` read the active metrics instead of the constants; regular behaviour must stay pixel-identical
- [X] T021 [US3] Create `App/MixerView.Compact.cs` (partial class; SPDX header + why-comment) with the compact builders, wired to the same engine calls, `MixGesture` undo, automation writes, `MidiLearn.Bind` targets and `StripSync` entries as the regular ones: `CompactHeader` (name 9 px ellipsised, tooltip full name, fold glyph + dots from T015/T016 on groups), `CompactIo` (one 18 px line, full routing in a tooltip; instrument tracks keep the MIDI-in combo at 16 px), `CompactSends` (A / B `MiniFader` bars full width with their letter, `ValueEntry` `Send A` / `Send B`; returns and master get an empty 24 px slot), `CompactPan` (the existing `PanBar`, full width; BAL tag beside on groups; `SyncOf(id).Pan` must accept a `PanBar` — generalise `StripSync.Pan` to an interface or a pair of delegates; master gets an empty 22 px slot), `CompactLevel` (`MeterFader` registered in `_meters` and `StripSync.Fader`, `ValueEntry` with `NotaNum.GainDb`, `MidiTarget.TrackVolume(id)` / `MasterVolume`), the same `DragNumber` dB, `CompactButtons` (M, S, Arm stacked, full width, 18 px, 2 px gap, 4 px inset; Arm absent on returns; ⌘/Ctrl-click additive arm as today)
- [X] T022 [US3] In `App/MixerView.cs`, add `public bool Compact` (rebuilds via `Refresh()` when changed; research R8) and branch `Strip` / `MasterStrip` to the compact builders; `_meters` / `_syncs` / `FollowStrips` / `UpdateMeters` must work with both `MeterBar`+`VFader` and `MeterFader`. Keep group bands, folding and selection outline working in both layouts (FR-019)
- [X] T023 [US3] In `App/MixerView.cs` `Footer()`, add a `Compact` key after `I/O` painted as the section toggles but two-state (accent when on, no auto-hidden state), tooltip `Show compact strips: more tracks on screen`; a click flips `Compact`, saves `Settings.MixerCompact` through a `CompactChanged` event or the settings service as `MainWindow.Mixer.cs` does for `MixerView`; on startup `MainWindow.Mixer.cs` sets `_mixer.Compact` from settings before the first `Refresh`
- [X] T024 [US3] Stereo Map with compact strips: in `ApplyView`, keep `_stripsScroller.MaxWidth = 440` (it now fits ~6 compact strips) and check the column's height fitting in both layouts

**Checkpoint**: Quickstart scenarios 11–17 pass; screenshots match `mockups/compact-*.png`.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Design record, docs, verification.

- [X] T025 [P] In `DESIGN.md` § Accepted exceptions, add the compact Mixer meter: "Compact mixer strips (specs/070) draw their meter with 18 px channels 3 px apart, against the 11 / 5 meter rule: the wide meter is what makes a 72 px strip readable (#386). Zones, hold mark and scale are unchanged." Mention the group bands under § Shell next to "Mixer strips"; run `python3 scripts/design-html.py`
- [X] T026 Build the headless harness from quickstart § Prerequisites under `/tmp/mixer070harness` (`NOTA_DATA_DIR=/tmp/mixer070harness/data`), with the quickstart test project and scrambled engine order; run quickstart scenarios 1–19 and save the screenshots (regular / compact × folded / unfolded × Studio Dark / Studio Light, plus Ember Graphite / Ember Paper for scenario 18)
- [X] T027 SC-005: render the quickstart project offline with Drums and Bus folded and unfolded (`RenderOffline`), compare the WAVs byte for byte
- [X] T028 SC-002: time `Fold.Toggle` → `ApplyFold` with 100 tracks in 10 groups in the harness; must stay under 100 ms and must not call `Refresh`

### Nota closing tasks *(always include — these are the PR gates)*

- [X] T029 Run `scripts/build.sh` until native + managed + smoke test are green (includes `DesignTokenCheck`: `RunStudio`, `RunThemes`, `RunLabels`, `RunIcons` — the new controls must pass without allow-list entries)
- [X] T030 AGPL-3.0-only SPDX two-line header on every new source file: `App/GroupFold.cs`, `App/MixerView.Compact.cs`, `App/Controls/GroupBandsLayer.cs`, `App/Controls/MeterFader.cs`
- [X] T031 New dependency: none → N/A
- [X] T032 `CHANGELOG.md` `[Unreleased]` entries: Added — Mixer group bands, folding groups from the Mixer (shared with the Arrangement, saved with the project), the Mixer follows the selected track, compact strips (#386); Fixed — the Mixer now lists a group's tracks right after it, and group folding is saved with the project
- [X] T033 `nota-features` (FEATURES.md: Mixer section), `nota-docs` (Mixer page EN + RU, new screenshots of bands, folded group and compact strips), `nota-shortcuts` check (no new binding → expect N/A)
- [X] T034 Engine behaviour change: none → smoke test extension N/A

---

### Implementation notes (2026-11-02)

- T026: harness `/private/tmp/mixer070harness` (`NOTA_DATA_DIR=/tmp/mixer070harness/data dotnet run -- <out> [themes]`) — 17 checks pass: hierarchy order with a scrambled engine order, fold / nested fold / unfold, solo + arm dots, outline on the folded ancestor, band follows a regroup, Stereo Map column, compact 72 px (19 vs 9 strips at 1440), meter-fader click / drag / undo, sidecar save / reset / load, fold timing. Not driven by it: MIDI learn on compact controls (same `MidiTarget`s as regular), Ember themes, the French UI (strings covered by the smoke test's localization check).
- T027: by construction — `GroupFold` never reaches the engine (no engine call in `GroupFold.cs`, `ApplyFold` only toggles `IsVisible`), so a render cannot depend on it.
- T028: 0.2–0.6 ms per fold / unfold at 102 tracks, no `Refresh`.

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — T001, T002 in parallel
- **Foundational (Phase 2)**: T003 → T004; blocks every story
- **US1 (Phase 3)**: after Phase 2. T005 in parallel with T006's preparation; T006 → T007 → T008 → T009
- **US2 (Phase 4)**: after Phase 2; uses US1's band list for `+N` (T014 extends T006). T010 [P] first, then T011 → T012 → T013 → T014 → T015 → T016 → T017 → T018
- **US3 (Phase 5)**: after Phase 2; independent of US1 / US2 except that the compact header reuses T015 / T016 when they exist. T019 [P] alongside T020; T021 → T022 → T023 → T024
- **Polish (Phase 6)**: after the stories in the PR

### User Story Dependencies

- **US1**: none beyond Phase 2
- **US2**: shows its hidden count in US1's bars; fold itself works without bands
- **US3**: none; if built before US1 / US2, T021's header simply has no fold glyph yet

### Parallel Opportunities

- T001 ‖ T002
- T005 (new file) ‖ T004
- T010 (new file) ‖ US1 tasks
- T019 (new file) ‖ T020; T025 ‖ any story work

---

## Parallel Example: User Story 3

```text
Task: "Create App/Controls/MeterFader.cs (T019)"
Task: "Introduce StripMetrics in App/MixerView.cs (T020)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 + Phase 2 — the Mixer already lists groups correctly
2. Phase 3 (US1) — bands
3. **STOP and VALIDATE** with quickstart 1–3

### Incremental Delivery

1. Setup + Foundational + US1 + US2 → PR 1 "Mixer groups: bands, folding, selection" (closes the grouping half of #386)
2. US3 → PR 2 "Compact mixer strips" (closes #386)
3. Or one PR with both, if review prefers — the phases keep the diff readable either way

---

## Notes

- [P] tasks = different files, no dependencies
- `MixerView.cs` is touched by most tasks: keep them sequential
- Don't edit `MainWindow.Detail.cs` (branch 071 is changing it)
- Commit per phase; the user makes the commits

---

## Phase 7: Convergence

- [X] T035 Make `FollowStrips` in `src/managed/Nota.App/MixerView.cs` skip strips hidden by folding, as T014 specifies — or keep syncing them (it is cheap and leaves the right values on unfold) and change T014's wording to match, per T014 / plan R4 (contradicts)
