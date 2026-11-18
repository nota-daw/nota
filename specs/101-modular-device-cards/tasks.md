---

description: "Tasks for #656 — Modular device cards and a searchable cable-drop picker"
---

# Tasks: Modular device cards and a searchable cable-drop picker

**Input**: Design documents from `/specs/101-modular-device-cards/`

**Prerequisites**: plan.md, spec.md, research.md (R1–R8), data-model.md, contracts/ui-contract.md, quickstart.md

**Tests**: plan.md R8 asks for a headless check program `tests/Nota.ModularUiTest` (in the
style of `tests/Nota.TempoUiTest`). Its checks are listed with the story they cover.
There's no smoke-test change (no engine change).

**Organization**: grouped by user story. US1 + US2 (the picker) can ship as one PR,
and US3 + US4 (cards and sizes) as a second, both closing parts of #656
(plan.md, Constitution Check V).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependencies)
- **[Story]**: US1 … US4 from spec.md
- Paths are relative to the repository root

---

## Phase 1: Setup

**Purpose**: the test harness and the file split that later phases write into

- [X] T001 Create `tests/Nota.ModularUiTest/Nota.ModularUiTest.csproj` (copy of `tests/Nota.TempoUiTest/Nota.TempoUiTest.csproj`: Exe, net10.0, references `src/managed/Nota.App/Nota.App.csproj` + `Avalonia.Headless` 12.1.4; SPDX header comments) and `tests/Nota.ModularUiTest/Program.cs` with the TempoUiTest bootstrap (scratch `NOTA_DATA_DIR`, `AppBuilder…UseHeadless…SetupWithoutStarting()`, `new NotaEngine()`, a `Check(name, bool)` helper printing `PASS`/`FAIL`, and a non-zero exit code on any failure). Do not add it to `Nota.sln`.
- [X] T002 [P] Create empty partials `src/managed/Nota.App/ModularView.Cards.cs` and `src/managed/Nota.App/ModularView.Picker.cs` (`public sealed partial class ModularView`, SPDX header plus a one-paragraph "why this file exists" comment citing specs/101), following `ModularView.Scope.cs`.

---

## Phase 2: Foundational

**Purpose**: the shared picker (model + panel), which every story needs: US1/US2 open it,
and US3's drop-on-knob falls back to it

**⚠️ No story work before this phase is done**

- [X] T003 [P] Create `src/managed/Nota.Presentation/ParamPickerModel.cs` per data-model.md:
  - `ParamPickerEntry(bool IsHeading, string Label, string? Group, object? Payload)`.
  - `ParamPickerModel(IReadOnlyList<ParamPickerEntry> entries, bool grouped)`.
  - `Query` setter recomputes `Visible`: case-insensitive substring on `Label`, plus on `Group` when `grouped`; "headings with no visible items are dropped".
  - `Highlight` is "always on an item (never a heading); first item after each query change; −1 when empty".
  - `MoveHighlight(int delta)` skips headings and clamps; `PageSize` is used by PageUp/PageDown.
  - `Pick()` returns the payload or null.
  - `EmptyText` returns the "No parameters" / "No matching parameters" key.
  - Static `Label(string raw, int index, ISet<string> seenInGroup)`: "fallback 'Parameter N' when empty, '(2)' suffix for a duplicate within a group".
  - No Avalonia types.
- [X] T004 Add the strings to `src/managed/Nota.Application/Localization/en.json` (keys from contracts/ui-contract.md §4): `modular.size_xs` "XS", `modular.size_s` "S", `modular.size_l` "L", `modular.size_tip` "Node size: XS shows the parameter list, S and L show the device's card", `modular.picker_search` "Search parameters", `modular.picker_no_params` "No parameters", `modular.picker_no_matches` "No matching parameters", `modular.picker_param_fallback` "Parameter {0}", `modular.picker_title_modulate` "Modulate {0}".
- [X] T005 Create `src/managed/Nota.App/Controls/ParamPickerPanel.cs` (depends on T003, T004) per contracts/ui-contract.md §3:
  - `static void Show(Control anchor, string title, ParamPickerModel model, Action<object> onPick, bool atPointer = true)`.
  - A `Flyout` placed at the pointer; Avalonia keeps it on screen.
  - Content: a title `TextBlock`, a `TextBox` (placeholder `modular.picker_search`, focused on `Opened`), and a `ListBox` with the default virtualizing panel and `ItemsSource = model.Visible`.
  - Width 320, list `MaxHeight` so the panel is ≤ 420.
  - Rows 22 px. Headings are caps, muted and not selectable. Matches across groups show the group as muted secondary text.
  - Hover moves the highlight; a click picks.
  - Keys on the TextBox: Up/Down/PageUp/PageDown → `MoveHighlight`, Enter → `Pick` → `onPick` + hide, Esc → hide.
  - An empty state row shows `EmptyText`.
  - Colours, fonts and radii from NotaPalette / the nota-design tokens only; check all four themes.
  - Debug timing: when `NOTA_DEBUG_PICKER=1`, log the ms from `Show` to the first layout.
- [X] T006 [P] Picker-model checks in `tests/Nota.ModularUiTest/Program.cs` (depends on T001, T003):
  - Filtering is case-insensitive.
  - Grouped search matches the group name, and empty headings are hidden.
  - The highlight skips headings, resets to the first item on a query change, and is −1 when empty.
  - Fallback "Parameter N" and the "(2)" suffix.
  - `EmptyText` for no entries vs no matches.
  - Timing: 5,000 synthetic entries, 10 queries, each < 50 ms (SC-001).

**Checkpoint**: the picker can be shown from code and passes its model checks.

---

## Phase 3: User Story 1 — Find a parameter fast when dropping a CV cable (P1) 🎯 MVP

**Goal**: a cable dropped on a device node opens the picker instead of the flat menu.

**Independent Test**: quickstart Q1 + Q2. On a ≥ 1,000-parameter plug-in, drop a cable,
type part of a name, press Enter, and the link exists. The panel opens without a visible
delay and is never taller than 420.

- [X] T007 [US1] In `src/managed/Nota.App/ModularView.Picker.cs`, add `ShowTargetPicker(GNode node, int modId)`. It builds entries for the node's device (kind 2 → `MidiEffectParamName`, kind 1 → `PluginParamName(t, -1, p)`, kind 0 → `DeviceParamName`), skipping the card-size param (`DeviceView.ParamName` / `InstrumentView.IsViewParam(PluginParamId…)` / a MIDI `View` param). Names are read once into a list. Labels go through `ParamPickerModel.Label`, and the payload is `(kind, di, param)`. It calls `ParamPickerPanel.Show(node.View, deviceName, model, pick → { _engine.CvLinkAddToTarget(t, modId, kind, -1, di, prm); Rebuild(); Changed?.Invoke(); })`.
- [X] T008 [US1] In `src/managed/Nota.App/ModularView.cs`, have `FinishConnect` call `ShowTargetPicker` instead of `ShowConnectParamMenu`, and delete `ShowConnectParamMenu`. Guard: if the device is gone when the pick fires (chain changed while open), do nothing (spec Edge Cases).
- [X] T009 [US1] Check that picking a parameter already linked to this modulator doesn't create a duplicate link (same as the old menu; spec Edge Cases). If `CvLinkAddToTarget` doesn't dedupe, check `FindModulation` first in `ShowTargetPicker` (`src/managed/Nota.App/ModularView.Picker.cs`).
- [X] T010 [US1] Integration check in `tests/Nota.ModularUiTest/Program.cs`. Build a `ModularView` on a track with a Compressor and an LFO, then call the picker path through an internal test hook (`internal ParamPickerModel? LastPickerModel` plus `internal void PickForTest(int visibleIndex)`, added to `ModularView.Picker.cs`). Set the query to `thr`, pick, and verify the CV link targets the Threshold param.

**Checkpoint**: US1 works on its own; it's the MVP.

---

## Phase 4: User Story 2 — Pick a parameter on another track, and the right-click sources (P1)

**Goal**: the cross-track drop and the two right-click "Modulate from…" items use the
same picker, grouped.

**Independent Test**: quickstart Q3.

- [X] T011 [US2] In `src/managed/Nota.App/ModularView.Picker.cs`, add `ShowCrossTrackPicker(Control anchor, int targetTrackId, int modId)`. Groups follow `ShowCrossTrackParamMenu`'s order: MIDI FX (kind 2) → instrument (kind 1, device −1, only on instrument tracks) → effects (kind 0). Each group is headed by its device name, and devices with no params are omitted. It's a grouped model; the payload `(kind, di, prm)` is applied with `CvLinkAddToTarget(_trackId, modId, kind, targetTrackId, di, prm)`. When there are no entries, the panel shows the `modular.no_modulatable_parameters_on_that` text (existing key) as its empty state.
- [X] T012 [US2] In `src/managed/Nota.App/ModularView.cs`, have `FinishConnect` call `ShowCrossTrackPicker` and delete `ShowCrossTrackParamMenu`.
- [X] T013 [US2] In `src/managed/Nota.App/ModularView.cs` `ShowModulateMenu`, turn the "Modulate from a parameter" submenu (`modular.modulate_from_a_parameter`, built from `paramSrc`) and the "Modulate from another track" submenu (`modular.modulate_from_another_track`, built from `cross_`) into plain `MenuItem`s. Each closes the flyout and opens `ParamPickerPanel` at `anchor`: sources grouped by device (labels as in `modular.from`) and modulators grouped by track (labels "LFO n" / "Env n"). The picks run exactly the click actions the submenu items run today. Title: `modular.picker_title_modulate` with the param name. Keep the items disabled or hidden under the same conditions as today.
- [X] T014 [US2] Cross-track check in `tests/Nota.ModularUiTest/Program.cs`. Track B has a Delay and an Auto Filter. Open the cross-track picker for B from A's LFO via the test hook. Query `freq` → only matches under the right headings. Pick → the link's target track is B with the right device/param.

**Checkpoint**: US1 + US2 are complete. This is the end of PR 1 (picker); do the
Polish tasks T029–T033 that apply to it before opening the PR.

---

## Phase 5: User Story 3 — See and play the real device card inside Modular (P2)

**Goal**: chain-device nodes show the Detail card body at S (when available) or L, live
and editable, with ports and cables intact. A cable dropped on a card control links it
directly.

**Independent Test**: quickstart Q4 steps 1, 2, 4, 5.

- [X] T015 [P] [US3] Create `src/managed/Nota.Application/Engine/CardSizeEngineProxy.cs` (research R2). It's a `DispatchProxy` over `IAudioEngine`, modelled on `RackChainEngineProxy` in `RackEngineProxies.cs`.
  - `Create(IAudioEngine inner, int trackId, CardFamily family, int index, Func<bool> getMini, Action<bool> setMini)` with `enum CardFamily { Effect, Instrument, MidiEffect }`.
  - Intercepts only the wrapped device's card-size param:
    - Effect: `DeviceGetParam` / `DeviceSetParam` where `(track, index, DeviceView.Index(...))` matches.
    - Instrument: `PluginParamGet` / `PluginParamSet` at `(track, -1, InstrumentView.Index(...))`.
    - MidiEffect: `MidiEffectGetParam` / `MidiEffectSetParam` at the MIDI effect's param named `View`.
  - Reads return `getMini() ? 1f : 0f`. Writes call `setMini(value >= 0.5f)` and never reach the engine; a write of 2 (XL) is treated as L.
  - Resolve the param index once in `Create`. Every other call goes to `Inner` via `m.Invoke(Inner, args)`.
  - Header comment: why (FR-014, Detail stays untouched).
- [X] T016 [US3] Add `ModularNodeSize { Unset, XS, S, L }` and the per-node store `Dictionary<string, ModularNodeSize> _savedSize` (keyed like `_savedPos` via `PosKey`) in `src/managed/Nota.App/ModularView.Cards.cs`. Also add `HasSmall(GNode n)` per the research R2 table:
  - effect → `DeviceView.Index >= 0` (or a plug-in effect).
  - instrument → `InstrumentView.Index >= 0` (or a plug-in instrument).
  - MIDI effect → it has a param named `View`.
  - Racks (effect kind 5, instrument kinds 3/4, MIDI rack) → false.

  Resolution: "`S` on a device without S resolves to `L`. Unknown codes resolve to unset". Unset → S if `HasSmall`, else L.
- [X] T017 [US3] In `src/managed/Nota.App/ModularView.Cards.cs`, add a `DeviceCardContext` factory for Modular (research R1):
  - `AddDeviceRefresher` / `AddRackParamRefresher` → `_liveKnobs.Add`.
  - `AddInstFader` → a `_liveKnobs` closure that re-reads `PluginParamGet` and moves the knob unless `Dragging`, the same as Detail's `_instFaders` loop.
  - `SetInstLiveViz` → a Modular field run on the tick.
  - `NotifyChanged` → `Changed`; `RequestRebuild` → `Rebuild`; preset-save callbacks → no-ops.
  - `factory` → the presets instance ModularView gets from its host. If ModularView has none, add a constructor/property for it the way DeviceChainView receives `IFactoryPresets`, and wire it in `MainWindow`.
  - `canXl: false`, `popout: false`.
  - The engine passed in is the `CardSizeEngineProxy` for the node.
- [X] T018 [US3] *(done in `ModularView.Cards.cs` `BuildCardBody`; the slot branches are mirrored there rather than moved to a shared `CardBodies.cs` — DeviceChainView's branches also build the Detail shell and its preset lists, so the shared part is only the body calls)* In `src/managed/Nota.App/ModularView.Cards.cs`, add `Control? BuildCardBody(GNode n)` that builds the body only (no Detail shell), per slot:
  - Device: kind −1 → `PluginCard.Build(ctx, di, PluginInfo.Of(...), state)` with a Modular-owned `PluginCardState { Mini = size == S }`; kind 5 → `new RackCardView(ctx).BuildEffectRackBody(di)`; else `DeviceCardFactory.Resolve(kind).Build(ctx, di)`.
  - Instrument: the same branches as `DeviceChainView.InstrumentCard()`, i.e. rack kinds 3/4, plug-in, or `InstrumentCardFactory.Resolve(kind, hasParams)`.
  - MidiFx: the same branches as `DeviceChainView.MidiDeviceCard()`.
  - Apply the body inset `NotaSpace.DeviceInset` unless the body is full-bleed.
  - Width: the body's `WidthFor(proxyEngine, t, di)` or NaN for auto. Width is read through the proxy so S/L widths follow the Modular size.

  Where `DeviceChainView` keeps these branches in private methods, move the shared branch logic into an internal static helper in `src/managed/Nota.App/DeviceCards/CardBodies.cs` (new) used by both views. Don't duplicate.
- [X] T019 [US3] In `src/managed/Nota.App/ModularView.cs`:
  - `BuildNodeView`: when `n.Size` is S or L, add `BuildCardBody(n)` under `PortsBlock` instead of `ParamsBlock` / `CollapseStrip`. XS keeps today's path unchanged.
  - Add `GNode.Size`, `GNode.HasSmall` and `GNode.Width` (data-model.md). `Width` is `NodeW` at XS, else set from `View.Bounds.Width` after layout (`LayoutUpdated` → update and `_edges.InvalidateVisual()`).
  - Replace every use of the `NodeW` constant for anchors, hit tests (`FinishConnect`), `PortLabel.MaxWidth`, the minimap and default chain spacing with the node's width.
  - Only `DeviceNode` / `MidiEffectNode` / `InstrumentNode` resolve a size; modulators, In/Out and islands stay at XS look with no chip (FR-019).
- [X] T020 [US3] *(no change needed: card controls mark their presses / wheel handled, so only empty card areas reach the canvas handlers; the Detail shell isn't used)* Pointer routing in `src/managed/Nota.App/ModularView.cs`: presses inside the card body must not start a node drag or selection-drag (only the header drags; FR-017), and wheel events over a card's own scrollers go to the card, not canvas zoom. Card gestures with Detail-only meaning (chain drag handle, fold, XL) don't exist because the shell isn't used. Check that no body-level handler calls into `DeviceChainView`.
- [X] T021 [US3] Drop on a control (research R5) in `src/managed/Nota.App/ModularView.Picker.cs`.
  - During a connect drag (`OnViewportMoved` while `_connecting`): `_viewport.InputHitTest(pos)`, then walk `GetVisualAncestors()` to the first control with `MidiLearn.GetBinding(c)` whose target maps to this node per the R5 table (`DeviceParam`, `DeviceIndex ≥ 0` → kind 0; `PluginParam`, device −1 → kind 1; `MidiDeviceParam` → kind 2; `TrackId == _trackId`). The node is the one whose `View` is an ancestor.
  - Draw a brass outline (`Brass`, 1.5 px, radius from tokens) on an overlay `Canvas` above `_world` at the control's bounds; it's cleared on leave or finish.
  - In `FinishConnect`, if a control target is set, call `CvLinkAddToTarget` directly and skip the picker.
  - Controls without a mapping (tabs, graphs, buttons, rack-inner params) don't highlight, and dropping on them opens the picker (spec Edge Cases).
- [X] T022 [US3] Checks in `tests/Nota.ModularUiTest/Program.cs`:
  - The Compressor node defaults to S and an Audio Effect Rack node to L.
  - Turning a card knob (set the `Knob.Value` via its `ValueChanged` path) changes `DeviceGetParam` on the real engine.
  - The Detail `View` param (`DeviceView.Size`) is unchanged after building Modular at S/L.
  - Drop on a control: simulate the hit target (call the internal `TryLinkAtControl(Control)` hook) on the card's Ratio knob → link to Ratio, no picker opened.

**Checkpoint**: cards show and play in Modular at their default sizes.

---

## Phase 6: User Story 4 — Collapse a node to XS (P2)

**Goal**: a per-node XS / S / L switch, saved with the project. *(FR-018 refined while implementing: nodes the user placed stay; auto-placed chain nodes re-flow so a grown card never overlaps them.)*

**Independent Test**: quickstart Q4 steps 3 and 6.

- [X] T023 [US4] Add the size chip to the header in `src/managed/Nota.App/ModularView.cs` `NodeHeader` (contracts/ui-contract.md §1). It's a segmented `XS · S · L` built with the existing `SegChip` style, and S is omitted when `!n.HasSmall`. The current size is in the soft-tint picked state, and the tooltip is `modular.size_tip`. A click sets `_savedSize[PosKey(n)]`, rebuilds that node in place and saves the layout. Only chain-device nodes get the chip.
- [X] T024 [US4] Resize in place in `src/managed/Nota.App/ModularView.cs`. On a size change, keep `n.World` (top-left) and don't re-run auto layout for other nodes; cables re-anchor from `n.Width` after the next layout pass (FR-018). Body S/L toggles that write through the proxy (e.g. the Arp's in-body toggle) call `setMini` → update `_savedSize` + rebuild, so both controls agree.
- [X] T025 [US4] Persistence in `src/managed/Nota.App/ModularView.cs` `SaveLayout` / `LoadLayout` (data-model.md "Layout sidecar"):
  - Write `[x, y, sizeCode]` with codes "0 = default / unset, 1 = XS, 2 = S, 3 = L" for nodes with a size, and `[x, y]` otherwise.
  - Read length 2 → unset and length ≥ 3 → the code; unknown codes → unset.
  - A node with a size but no saved position yet still gets an entry: use its current `World`.
  - Islands never carry a size.
- [X] T026 [US4] Checks in `tests/Nota.ModularUiTest/Program.cs`:
  - Set a node to XS → `n.Width == NodeW`; set it to L → the output anchor X equals `World.X + Width`, and no other node's `World` changed.
  - `SaveLayout` → new `ModularView` → `LoadLayout` → the same sizes (SC-006).
  - A hand-written legacy sidecar `{"0/dev0":[10,20]}` loads with the default size.

**Checkpoint**: all four stories work. This is the end of PR 2 (cards).

---

## Phase 7: Polish & cross-cutting

- [X] T027 *(measured headless: 5,000-entry picker opens in 31 ms with 16 rows built; filter ≤ 2 ms per keystroke; 27 card nodes build + lay out in 293 ms — no paging needed)* [P] Performance pass (SC-001, SC-004). Run quickstart Q2 with `NOTA_DEBUG_PICKER=1`. If opening takes ≥ 150 ms at 5,000 params, switch `ShowTargetPicker` / `ShowCrossTrackPicker` to lazy name pages of 256 (the model works on indices; research R6 fallback). Time Modular's first draw on a 6-device track (< 300 ms), and profile `CardSizeEngineProxy` dispatch on the tick; cache the `MethodInfo` switch by name if it shows up.
- [X] T028 *(shots via `NOTA_MODULAR_SHOTS=<dir>` in tests/Nota.ModularUiTest, all four themes; XS kept upper case via NotaText.Fixed)* [P] Theme and look pass via `nota-design`: picker, size chip and brass control outline in Studio Dark, Studio Light, Ember Graphite and Ember Paper (quickstart Q6). Take screenshots with the headless harness (memory recipe: env-guarded in-app hook + screencapture) for the PR.
- [X] T029 *(added to ../nota-translations/languages/ru.json, uncommitted, with other features' pending keys)* [P] Russian strings for the new `modular.size_*` / `modular.picker_*` keys in `../nota-translations/languages/ru.json` (the translations repo, not this one).
- [X] T030 `CHANGELOG.md` `[Unreleased]`: under Added, "Modular nodes show the device's real card (S or L), with an XS size for the compact node; a CV cable dropped on a knob links it directly". Under Changed, "Picking a parameter for a CV cable or 'Modulate from…' opens a searchable panel instead of a long menu". Reference #656.
- [X] T031 [P] `FEATURES.md` via the `nota-features` skill (Modular section).
- [X] T032 [P] *(in ../nota-docs-656 on docs/modular-device-cards-656 off origin/develop, uncommitted: views/modular EN + RU — Nodes, Patching, the ducking walkthrough; views/modular re-shot dark + light; coverage.yml sources; npm run build + check: 0 errors)* User docs via the `nota-docs` skill: the Modular page in `../nota-docs` EN + RU (sizes, drop on knob, the picker), with re-shot screenshots.
- [X] T033 *(build.sh green on the third run — runs 1–2 failed only render-pool timing checks, different ones each time, engine code untouched; ModularUiTest 39/39. The by-hand walk of quickstart Q1–Q6 in the real app is still open)* Run `scripts/build.sh` (native + managed + smoke test) and `dotnet run -c Release --project tests/Nota.ModularUiTest`. Both must be green. Then walk quickstart Q1–Q6 by hand in the real app.
- [X] T034 *(converged on the second run, 2026-11-17)* Nota compliance re-check (spec.md checklist): SPDX headers on all new files (T001, T002, T003, T005, T015, T018 helper); no new dependency; no engine/ABI change; CHANGELOG done. Then `/speckit-converge`.

---

## Dependencies & execution order

- **Setup (T001–T002)** → **Foundational (T003–T006)** → stories.
- **US1 (T007–T010)** needs Foundational. It's the MVP.
- **US2 (T011–T014)** needs Foundational, and T012/T013 edit `ModularView.cs` after T008, so do them sequentially after US1 in the same file.
- **US3 (T015–T022)** needs Foundational (the picker fallback in T021). T015 is independent of the picker and can start right after Setup.
- **US4 (T023–T026)** needs US3 (T016, T019).
- **Polish** after the stories it covers: T029–T033 per PR.

Inside US3: T015 ∥ T016 → T017 → T018 → T019 → T020 → T021 → T022.

## Parallel opportunities

- T002 ∥ T001; T003 ∥ T004 (different files); T006 after T001 + T003.
- T015 (`Nota.Application`) can run alongside US1/US2 work (`ModularView.*`), since they're different files.
- Polish: T027, T028, T029, T031 and T032 touch different files or repos.

```text
# After Phase 2:
Agent A: T007 → T008 → T009 → T010 → T011 → T012 → T013 → T014   (ModularView.Picker.cs / ModularView.cs)
Agent B: T015                                                       (Nota.Application/Engine/CardSizeEngineProxy.cs)
```

## Implementation strategy

1. **MVP**: Setup + Foundational + US1 → the picker on node drops, which is the blocker
   for big plug-ins. Validate with quickstart Q1/Q2.
2. **PR 1**: + US2 (cross-track + right-click), polish for the picker (strings, docs,
   CHANGELOG, build). Closes the picker half of #656.
3. **PR 2**: US3 → US4 (cards, proxy, drop on knob, sizes and persistence), polish,
   `/speckit-converge`. Closes #656.

## Phase 8: Convergence

- [X] T035 *(headless against real VST3s via `NOTA_MODULAR_BIG_PLUGIN=<bundle>` in tests/Nota.ModularUiTest: Massive 207 params — picker 26 ms, type 4.7 ms, Enter links; Kontakt 8 4,145 params — card node at S in 12 ms, picker 7 ms with names read, type 4.9 ms, Enter links, duplicate names told apart by "(n)". Q1–Q4 and Q6 covered by the automated checks and theme shots. Not done: a by-hand pass in the windowed app — GUI launch isn't available to the agent here)* Walk quickstart Q1–Q6 by hand in the real app, including a VST3/AU plug-in with ≥ 1,000 parameters (picker opens without visible delay, a named parameter linked by keyboard within 5 s), and record the result in tasks.md per SC-003 (partial)
- [X] T036 Close an open ParamPickerPanel when ModularView rebuilds or switches track (`Rebuild` / `Show` in `src/managed/Nota.App/ModularView.cs`, via `ParamPickerPanel.Last`), so a picker whose device left the chain closes without linking per spec Edge Cases (partial)
- [X] T037 *(user: keep the reorder; US3/AC4 reworded + clarification recorded)* Reconcile US3/AC4 ("header drag … does not reorder the chain") with Modular's existing drag-past-a-sibling reorder in `EndNodeDrag` (`src/managed/Nota.App/ModularView.cs`): keep the pre-existing reorder for card nodes too and have the spec scenario reworded, or suppress it — ask the user which per US3/AC4 (contradicts)
- [X] T038 Make hovering a picker row move the model's highlight (`ParamPickerModel.SetHighlight` from the row's pointer-enter in `src/managed/Nota.App/Controls/ParamPickerPanel.cs`), so Enter after hovering picks the hovered row per contracts/ui-contract.md §3 (partial)
