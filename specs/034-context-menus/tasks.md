---

description: "Task list for Studio context menus"
---

# Tasks: Studio context menus

**Input**: Design documents from `specs/034-context-menus/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/menu-kit.md](contracts/menu-kit.md), [quickstart.md](quickstart.md)

**Tests**: The plan adds a design guard (`RunMenus`) and a unit test for the recent-instruments list; both are included. No other tests.

**Organization**: Tasks are grouped by user story. Paths are relative to the repo root; `App/` = `src/managed/Nota.App/`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1 – US5)

---

## Phase 1: Setup

- [X] T001 Create feature branch `034-context-menus` off `develop` (the spec folder `specs/034-context-menus/` moves with it)
- [X] T002 Re-grep every menu site (`new MenuFlyout|new ContextMenu|<ContextMenu|<MenuFlyout|FillMapMenu|ContextRequested|IsRightButtonPressed`) in `src/managed/Nota.App` and reconcile with the inventory table in `specs/034-context-menus/plan.md`; add any missing site as a row

---

## Phase 2: Foundational (blocking)

**Purpose**: the shared `MenuItem` theme and `MenuKit` primitives every story uses.

- [X] T003 [P] Add `Shortcut.Spell(KeyGesture)` (macOS `⌘G`, `⇧⌘G`, `⌫` — modifiers in the order ⌃⌥⇧⌘, no separators; elsewhere `Ctrl+Shift+G`, `Del`) reusing the existing `SpellText` rules in `src/managed/Nota.Application/Input/Shortcut.cs` — done as `MenuKit.Spell(KeyGesture)` in `src/managed/Nota.App/MenuKit.cs`, since Nota.Application has no Avalonia types; it builds a `Shortcut` and calls `ToPlatformString`
- [X] T004 [P] Add `GlyphKind`s needed by menus but missing (check: Snowflake/Freeze, ChainLink, Palette, Input, Copy, Cut, Paste, Duplicate, Trash, Folder, Ungroup, Plus, Edit, Note exist; add only what is missing, one-line comment each) in `App/Controls/Glyph.cs`
- [X] T005 Write the Nota `ControlTheme` for `MenuItem` per [contracts/menu-kit.md](contracts/menu-kit.md) § MenuItem theme — columns icon 16 · label * · value-or-shortcut auto · caret 12; 24 tall; radius `Radius.Clip`; shortcut via a converter calling `Shortcut.Spell` in Geist Mono 11 `TextMuted`; value cell bound to attached `MenuKit.Value`; caret `Glyph(ChevronRight, 8)`; highlight = `Accent` fill + `TextOnAccent` on every part with 120 ms transition; disabled = `TextDisabled`, no fill; classes `.danger` (label + icon `Danger`, disabled wins) and `.inline` (no highlight, no icon/caret columns, content spans) — replacing the current `MenuItem` styles in `App/Theme/NotaTheme.axaml`
- [X] T006 Set menu frame `MinWidth` 200 on `MenuFlyoutPresenter` and `ContextMenu`, keep seam ring / radius 3 / padding 4 / no blur, Separator 1 px `Rule` margin 4,4 in `App/Theme/NotaTheme.axaml`
- [X] T007 Extend `MenuKit` with attached property `Value` (implemented as `Trailing`: a value string, a switch or a colour chip), `ValueItem(header, icon, value)`, `Danger(header, icon, click, gesture?)`, `ClipboardGroup(copy, cut, paste, duplicate, canPaste)` (order Copy ⌘C · Cut ⌘X · Paste ⌘V · Duplicate ⌘D) in `App/MenuKit.cs` (labels stay sentence case at the call site — `RunLabels` checks them — rather than being re-cased by `NotaText.Label`, which would touch track and preset names)
- [X] T008 Add `DesignTokenCheck.RunMenus` per research R6 (no "Color"/"Track height" submenus; `MenuItem.Icon` only via `MenuKit.Icon`; no hand-built "⌘+"/"Ctrl+" gesture strings in menus; destructive headers `Delete|Remove|Clear|Discard|Flatten` only via `MenuKit.Danger` or class `danger`), a shrink-only `MenuAllowList.txt` pre-filled with today's violations, and wire it into the smoke-test runner in `tests/Nota.SmokeTest/DesignTokenCheck.Menus.cs`, `tests/Nota.SmokeTest/MenuAllowList.txt`, `tests/Nota.SmokeTest/DesignTokenCheck.cs`

**Checkpoint**: every existing menu already renders with the new frame, columns, `⌘G` spelling and glyph caret.

---

## Phase 3: User Story 1 — Every context menu looks and reads the same (P1) 🎯 MVP

**Goal**: all ~70 menus use neutral icons, `MenuKit` items, danger ink for destructive commands, no hand-tinted parts.

**Independent Test**: open each menu in the plan's inventory in all four themes; each passes the contract table; `RunMenus` passes with an empty allow list.

- [X] T009 [US1] Freeze menus: replace `SnowflakeIcon()`, `ChainLinkIcon()` and `GlyphIcon(…, accent:true)` in menu items with `MenuKit.Icon`, make "Discard edits" and "Flatten …" `MenuKit.Danger` in `App/MainWindow.Freeze.cs` and `App/MainWindow.LiveFreeze.cs`
- [X] T010 [P] [US1] Arrangement menus (inventory 1–10): icons via `MenuKit.Icon`, destructive via `MenuKit.Danger`, clipboard via `MenuKit.ClipboardGroup` in `App/ArrangementView.cs`, `App/ArrangementView.Tracks.cs`, `App/ArrangementView.LaneControl.cs`, `App/ArrangementView.Automation.cs`, `App/ArrangementView.Sections.cs`, `App/ArrangementView.Controls.cs`
- [X] T011 [P] [US1] Session menus (inventory 11–16) the same way in `App/SessionView.Grid.cs`, `App/SessionView.Toolbar.cs`, `App/SessionView.Inspector.cs`
- [X] T012 [P] [US1] Modular menus (inventory 17–19) in `App/ModularView.cs`
- [X] T013 [P] [US1] Device chain + racks + macros (inventory 20–23) in `App/DeviceChainView.Clipboard.cs`, `App/DeviceChainView.Shell.cs`, `App/DeviceChainView.Fold.cs`, `App/DeviceCards/Rack/RackCardView.cs`, `App/DeviceCards/Rack/RackCardView.Drum.cs`, `App/DeviceCards/Rack/MacroPanel.cs`, and `FillMapMenu` in `App/DeviceCards/Instruments/RhythmInstrumentCard.cs`
- [X] T014 [P] [US1] In-card menus (inventory 24–28) in `App/DeviceCards/Instruments/*.cs`, `App/DeviceCards/Bodies/*.cs`, `App/DeviceCards/Midi/ScaleMidiBody.cs`, `App/Controls/Eq8Views.cs`, `App/Controls/DynamicEqCurve.cs`, `App/DeviceCardKit.cs`, `App/ClipEditorKit.cs`
- [X] T015 [P] [US1] Browser, History, preview, snap, updates, metronome (inventory 29–35) in `App/BrowserView.cs`, `App/HistoryView.cs`, `App/PreviewPlayer.cs`, `App/SnapChip.cs`, `App/MainWindow.Updates.cs`, `App/MainWindow.axaml.cs`, `App/MainWindow.axaml`
- [X] T016 [US1] Empty `tests/Nota.SmokeTest/MenuAllowList.txt` (or leave only justified, commented exceptions) and run `scripts/build.sh` green

**Checkpoint**: US1 complete — consistent menus app-wide without inline rows.

---

## Phase 4: User Story 2 — Colour and height in one click (P1)

**Goal**: header row + colour grid + height segments on track, multi-track and return/master menus.

**Independent Test**: right-click a track → swatch recolours it (one undo step) → reopen → "Large" resizes it; Option-click resizes all.

- [X] T017 [US2] Create `MenuKit.Header(name, colour?, badge)` (inline, non-interactive, 28 tall, name 600 truncating with ellipsis, badge Geist Mono muted), `MenuKit.ColourGrid(current, pick)` (9 columns × 3 rows base/light/dark, 14 px swatches, gap 4, + "Auto" key; current ringed 1 px `TextPrimary` with 1 px gap; arrows move, Enter picks; closes the owning flyout) and `MenuKit.Segments(label, options, chosen, pick(value, allModifier))` (segmented keys butted on seams, chosen in accent, Option/Alt detected as `allModifier`) in new `App/MenuKit.Rows.cs` (SPDX header)
- [X] T018 [US2] Rebuild `ShowTrackMenu`: Header (track colour, name, "Audio · 3" / "Instrument · 3" / "Group · 3"), ColourGrid (replaces `BuildColorSubmenu`), Segments "Height" Compact/Standard/Large (none chosen on a custom height; click → `SetTrackHeights(one, h)`, Option → `SetAllTrackHeights(h)`) in `App/ArrangementView.cs` and `App/ArrangementView.Sizing.cs` (remove `BuildTrackHeightSubmenu` and the "All tracks: …" items)
- [X] T019 [US2] Rebuild `ShowTracksMenu` with Header "N tracks" (no colour chip), ColourGrid and Segments applying to all ids as one undo group in `App/ArrangementView.Tracks.cs`
- [X] T020 [US2] Rebuild `ShowFooterMenu` (return / master) with Header + ColourGrid; no height row for master in `App/ArrangementView.cs`; delete `BuildColorSubmenu`, `TrackColorNames`, `ShadeNames` if unused
- [X] T021 [US2] Session track header menu (`ShowHeaderMenu`) gets the same Header + ColourGrid in `App/SessionView.Grid.cs`
- [X] T022 [US2] Run `nota-shortcuts` skill: add "Option-click a height key — every track" to the Shortcuts pane

**Checkpoint**: US1 + US2 — the requested mockup is live.

---

## Phase 5: User Story 3 — Current state without opening submenus (P2)

**Goal**: value column on Record input / MIDI from; Live freeze switch.

**Independent Test**: set a record input to another track; its name shows on the right of "Record input"; Live freeze switch reflects and toggles the link.

- [X] T023 [US3] Create `MenuKit.SwitchItem(header, icon, isOn, toggle)` drawing `DeviceCardKit.Switch` (18 × 10, accent when on) in the value column in `App/MenuKit.Rows.cs`
- [X] T024 [US3] Record input and MIDI from become `MenuKit.ValueItem` showing the current source ("In 1–2" style hardware label, "Master", track / return name; "None" for MIDI from) in `App/ArrangementView.cs`
- [X] T025 [US3] `BuildTrackFreezeMenu`: "Live freeze" as `SwitchItem` on both sides of a link (off → `LiveFreezeAsync(trackId)`, on → `UnfreezeLink(src)`); while linked keep Edit source / Done — re-freeze / Discard edits and `Flatten (remove source)` (Danger ink) below it; drop the separate "Unfreeze (wake source)" item; leave the switch out for tracks that cannot be live-frozen or are frozen in place in `App/MainWindow.Freeze.cs`
- [X] T026 [US3] Same switch in the freeze buttons' own menus (`EnsureFreezeMenu`, `EnsureLiveFreezeMenu`) in `App/MainWindow.Freeze.cs`, `App/MainWindow.LiveFreeze.cs`

---

## Phase 6: User Story 4 — Same order everywhere (P2)

**Goal**: header rows on all named-object menus; group order and short labels (FR-007, FR-013).

**Independent Test**: clip, device, Session slot and track menus show the clipboard group and destructive group in the same places with the same labels.

- [X] T027 [P] [US4] Clip and multi-clip menu: Header (clip colour, name, "MIDI clip · 4 bars" / "Audio clip" / "3 clips"), labels Copy/Cut/Paste/Duplicate/Delete, group order in `App/ArrangementView.LaneControl.cs`
- [X] T028 [P] [US4] Section marker and automation lane menus: Header (section name; parameter name with device in badge) and order in `App/ArrangementView.Sections.cs`, `App/ArrangementView.LaneControl.cs`
- [X] T029 [P] [US4] Session slot (with clip) and scene menus: Header + order in `App/SessionView.Grid.cs`
- [X] T030 [P] [US4] Device card header / folded strip menu: Header (device name, badge kind "Audio effect · 2" etc.), order (A/B · move · clipboard · Delete) in `App/DeviceChainView.Clipboard.cs`, `App/DeviceChainView.Shell.cs`
- [X] T031 [P] [US4] Rack chain, drum pad, macro, EQ band menus: Header + order in `App/DeviceCards/Rack/RackCardView.cs`, `App/DeviceCards/Rack/RackCardView.Drum.cs`, `App/DeviceCards/Rack/MacroPanel.cs`, `App/Controls/Eq8Views.cs`, `App/Controls/DynamicEqCurve.cs`
- [X] T032 [P] [US4] Modulate menu: Header (parameter name) in `App/ModularView.cs`
- [X] T033 [P] [US4] Browser item and History version menus: Header (item name + type; version label + date) and order in `App/BrowserView.cs`, `App/HistoryView.cs`
- [X] T034 [US4] Track menu final order per FR-013: Header → colour → height → Rename… / Record input / MIDI from → freeze group → Add track / Group tracks / Ungroup → clipboard → Delete; short labels ("Copy", not "Copy track") in `App/ArrangementView.cs`, `App/ArrangementView.Tracks.cs`

---

## Phase 7: User Story 5 — Recent instruments in Add track (P3)

**Goal**: persisted "Recent" group under Add track.

**Independent Test**: add two instruments, restart, see both under Recent (most recent first); click one → new track after the clicked one.

- [X] T035 [P] [US5] Add `RecentInstrument { Kind ("builtin" | "plugin"), Id, Name, FactoryPreset? }` and `Settings.RecentInstruments : List<RecentInstrument>` (most recent first; missing / malformed → empty) in `src/managed/Nota.Application/Settings.cs`
- [X] T036 [P] [US5] Add pure helper `RecentInstruments.Record(list, entry)` — "Identity = (Kind, Id, FactoryPreset). Recording an entry already present moves it to the top; no duplicates. Capacity 5; recording a sixth drops the oldest." — and `Visible(list, isInstalled)` hiding (not deleting) plug-ins missing from the scan, in new `src/managed/Nota.Application/RecentInstruments.cs` (SPDX header)
- [X] T037 [US5] Unit test the rules above (move-to-top, de-dupe, cap 5, hide missing plug-in, empty on malformed) in new `tests/Nota.SmokeTest/RecentInstrumentsTests.cs` and wire it into the runner
- [X] T038 [US5] Record on every instrument add/replace in `src/managed/Nota.Presentation/DeviceInsertService.cs` (`CreateInstrumentTrack`, `Insert`, `TryReplaceInstrument`, `RouteToTrack`) and save via the settings service
- [X] T039 [P] [US5] Record "Nota Volt" on `AddTrack(NewTrackKind.Instrument)` in `App/MainWindow.axaml.cs`
- [X] T040 [P] [US5] Record on MCP instrument adds in `src/managed/Nota.Mcp/Tools/InstrumentTools.cs`, `src/managed/Nota.Mcp/Tools/PluginTools.cs`
- [X] T041 [US5] "Add track" submenu: "Instrument", "Audio", "Return", then a "Recent" caption and up to 5 entries (left out when empty); clicking creates the instrument track after the anchor via `DeviceInsertService.CreateInstrumentTrack` as one undo step; shared by Arrangement (`AddTrackItems`) and Session (`ShowAddTrackMenu`) in `App/ArrangementView.cs`, `App/SessionView.Grid.cs`

---

## Phase 8: Polish & cross-cutting

- [X] T042 Update `DESIGN.md` § Menus (header row, colour grid, segments, value column, switch item, danger item, group order) and § Accepted exceptions if needed; add a specimen menu to `scripts/design-html.py` and regenerate `DESIGN.html`
- [X] T043 [P] `CHANGELOG.md` `[Unreleased]` entry (Changed: context menus; Added: Recent instruments in Add track, Live freeze switch)
- [X] T044 [P] Run `nota-features` skill → `FEATURES.md`
- [X] T045 [P] Run `nota-docs` skill → user docs (EN + RU) and re-shot menu screenshots
- [X] T046 Headless menu-content shots in all four themes per [quickstart.md](quickstart.md) § 2; fix anything off-contract
- [X] T047 `scripts/build.sh` green (smoke test incl. `RunMenus`, `RunIcons`, `RunLabels`, `RunThemes`, `RecentInstrumentsTests`)
- [X] T048 Re-check the spec's Nota Compliance Checklist; run `/speckit-converge`

---

## Dependencies & Execution Order

- Setup (T001–T002) → Foundational (T003–T008) → stories.
- US1 (T009–T016) first: it removes the per-site styling the later stories would otherwise fight.
- US2 depends on Foundational + T017; independent of US3–US5.
- US3 depends on Foundational (T023 lives in `MenuKit.Rows.cs` created by T017 — do T017 first).
- US4 depends on T017 (Header) and on US1's per-file passes.
- US5: T035–T037 can start right after Foundational; T041 after T018 (same file).
- Polish last.

## Parallel Opportunities

- T003 ∥ T004 (different files).
- US1: T010 – T015 touch disjoint files — run in parallel after T009.
- US4: T027 – T033 are disjoint files.
- US5: T035 ∥ T036; T039 ∥ T040.
- Polish: T043 ∥ T044 ∥ T045.

## Implementation Strategy

1. **MVP = Foundational + US1**: every menu already consistent (frame, columns, `⌘G`, neutral icons, danger ink).
2. Add **US2** — the mockup's centrepiece; demo.
3. **US3** and **US4** — state in place and consistent order.
4. **US5** — Recent instruments.
5. Polish, docs, converge. One PR, one commit per phase/view (plan § Complexity Tracking).

---

## Phase 9: Convergence

- [X] T049 Record instrument presets in the recent list: `DeviceInsertService.ApplyPresetCore` swaps the instrument via `SetTrackBuiltinInstrument` (and builds new preset tracks) without raising `InstrumentAdded`; raise it there with the factory preset id so `RecentInstrument.FactoryPreset` is filled, in `src/managed/Nota.Presentation/DeviceInsertService.cs` and `src/managed/Nota.App/MainWindow.Recent.cs` per FR-012a (partial)
- [X] T050 Decide and document whether tracks created as a side effect (Convert to MIDI, MIDI file import, MCP `AiTools` Volt tracks, the demo project) count as "added instruments"; either record them through `RecentInstruments.RecordAndSave` or state the exclusion in spec Assumptions, in `src/managed/Nota.App/MainWindow.Convert.cs`, `src/managed/Nota.App/MainWindow.MidiImport.cs`, `src/managed/Nota.Mcp/Tools/AiTools.cs` per FR-012a (partial)
- [X] T051 Verify keyboard use of the inline rows (arrows move the cursor ring in the colour grid and height keys, Up/Down cross grid rows, Enter applies and closes, Escape closes) with a headless synthetic-key check in the menu harness and fix what fails, in `src/managed/Nota.App/MenuKit.Rows.cs` per FR-017 (partial)
- [X] T052 Give the track, multi-track and footer menus a 248 px minimum width so the colour grid and height keys never wrap on short track names, in `src/managed/Nota.App/ArrangementView.cs` / `ArrangementView.Tracks.cs` (MenuFlyout presenter `MinWidth`) per FR-001 (partial)
- [ ] T053 Review the Vocoder sidechain-source menu change: its per-track colour squares were replaced by a tick on the current source; confirm with the maintainer or restore the track colours as `MenuKit.Swatch` in the value column, in `src/managed/Nota.App/DeviceCards/Bodies/VocoderDeviceBody.cs` per FR-011 (unrequested)
