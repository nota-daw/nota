---

description: "Tasks: automation and resizable height for return and master rows"
---

# Tasks: Automation and resizable height for return and master rows

**Input**: Design documents from `specs/045-return-master-lanes/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: One smoke check (plan § Testing, constitution IV) locks in the engine's existing
return-automation behaviour. UI behaviour is verified with [quickstart.md](quickstart.md)
scenarios and headless screenshots.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

All app paths are under `src/managed/Nota.App/`. Nearly every task edits one of the
`ArrangementView.*.cs` partials, so few tasks are parallel.

---

## Phase 1: Setup

- [X] T001 Create branch `045-return-master-lanes` off `develop` (the plan setup did not switch branches)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Footer rows get real per-row geometry instead of `row * FooterRowH`. Both the return lane editor (US1) and resizing (US2) depend on it.

- [X] T002 In `src/managed/Nota.App/ArrangementView.Sizing.cs` add the per-kind default: `internal const double DefaultFooterRowHeight = CompactRowHeight;` and `DefaultHeightFor(int trackId)`, which returns 30 for a return (look up in `_returns`) and 64 otherwise. Make `StoredRowHeight` and `StoreRowHeight` use it: an entry equal to its row's default is removed. Add `private double _masterHeight = DefaultFooterRowHeight;`. Keep `AllTrackRowIds()` excluding returns (FR-016). Prune heights of returns that no longer exist: `PruneTrackHeights` must keep live return ids too, so build `alive` from every engine track, returns included.
- [X] T003 In `src/managed/Nota.App/ArrangementView.cs` replace `FooterRowH` with footer geometry:
  - add `double[] _footerRowTops` (length `_returns.Count + 2`), rebuilt by a `RebuildFooterGeometry()`;
  - add helpers `FooterRowTop(i)`, `FooterRowHeight(i)` (returns: `StoredRowHeight(id)`; master, i == `_returns.Count`: `_masterHeight`) and `FooterContentHeight`;
  - call `RebuildFooterGeometry()` from `Refresh()` before `RebuildFooterHeaders()`, and set `_footer.Height` / `_footerHeaders.Height` from `FooterContentHeight` (replacing `(_returns.Count + 1) * FooterRowH`);
  - rewrite `FooterRowAt(y)` to search `_footerRowTops`;
  - `SetFooterDropRow` positions `_footerDropRing` with `FooterRowTop(row)` / `FooterRowHeight(row)`;
  - `BuildSlimRow` takes the row height instead of `FooterRowH`, and `RebuildFooterHeaders` advances `y` by `FooterRowHeight(i)`.
- [X] T004 In `src/managed/Nota.App/ArrangementView.Controls.cs` make `FooterLaneControl` use the geometry: `MasterRowTop` → `_o.FooterRowTop(_o._returns.Count)`; `MasterValueToY` / `MasterYToValue` use the master row height; `Render` fills and draws the seam lines per row from `_footerRowTops`, as do the master hint text, the master curve and the drop highlight. Remove the `FooterRowH` constant once nothing references it.

**Checkpoint**: Footer renders exactly as before (each row now 30 px by default, see US3). Browser drops onto returns and master still land on the right row.

---

## Phase 3: User Story 1 - Automate a return track (Priority: P1) 🎯 MVP

**Goal**: Return rows in automation mode show a target pill and an editable lane identical to a track lane; playback follows; lanes save and undo.

**Independent Test**: [quickstart.md](quickstart.md) scenarios 3–7 and 17.

### Test for User Story 1

- [X] T005 [P] [US1] Add `tests/Nota.SmokeTest/ReturnAutomationTests.cs` (SPDX header) and register it where the other smoke test classes are invoked. The test should:
  - add an audio/instrument source with a send to a new return;
  - `AddAutomationLane(returnId, AutomationTarget.Volume, -1, -1)` and set points ramping 0 → 1 over 4 beats;
  - render a block near beat 0 and one near beat 4 and assert the return's meter / master level is lower at the start (use a deterministic WAV clip, not a synth note, per agent memory `smoke-level-tests`);
  - save the project to a temp bundle, reload it, and assert `AutomationLaneCount(returnId) == 1` with the same points;
  - repeat with a `DeviceParam` lane on a device on the return.

### Implementation for User Story 1

- [X] T006 [US1] Create `src/managed/Nota.App/ArrangementView.AutoEditor.cs` (SPDX header plus a comment on why it exists: one automation editor shared by the track lanes and the footer return lanes). Define a nested `IAutoRows` host interface:
  - `int Count`, `TrackVM TrackAt(int i)`, `double Top(int i)`, `double Height(int i)`, `int RowAtY(double y)`;
  - `bool Editable(int i)` (false for slim group bars);
  - `Control Owner`, `(int lo, int hi) VisibleRange`.

  Then define `sealed class AutoEditor`, holding the state and logic now inside `LaneControl`:
  - state: `_autoDrag`, `_autoDragTrack`, `_autoDragLo/Hi`, `_autoDragStartVal`, `_bendLeft/_bendRight/_bendTrack`, `_rangeTrack/_rangeAnchor`, `_autoLiveStarted`, `_hoverPoint/_hoverSegLeft/_hoverTrack`;
  - logic: `PillWidth`, `PillRect`, `ValueToY`, `YToValue`, `AutoPointerPressed`, `HitSegment`, `AutoBendMoved`, `AutoPointerMoved`, `CommitAutoDuringDrag`, `AutoPointerReleased`, `ShowAutoMenu`, hover handling and `DrawAutomation`.

  Every `_o._tracks[i]`, `RowTop(i)`, `RowH(i)`, `_o.RowAtY`, `_o.VisibleRowRange()`, `_o.IsSlimRowAt(i)` becomes a host call. Move the code; don't change behaviour.
- [X] T007 [US1] In `src/managed/Nota.App/ArrangementView.LaneControl.cs` implement `IAutoRows` over `_tracks` / `_rowTops`, hold one `AutoEditor`, and delegate the automation-mode branches of `OnPointerPressed` / `OnPointerMoved` / `OnPointerReleased` / `OnPointerExited` / `Render`, plus `IsEditingPoint`, to it. Delete the moved members. Build, then run quickstart scenario 17 (track automation unchanged) before going on.
- [X] T008 [US1] In `src/managed/Nota.App/ArrangementView.Controls.cs` give `FooterLaneControl` an `IAutoRows` over `_returns`: `Top` / `Height` come from the T003 geometry, `RowAtY` returns only return rows (the master is excluded), `Editable` is always true. Add an `AutoEditor` instance, and in automation mode:
  - route pointer events to the editor for return rows and keep the existing master-volume handler for the master row;
  - draw the return lanes with `AutoEditor.DrawAutomation` (scrim, curve, points, range band, target pill) on top of the row backgrounds and grid, below loop wash and playhead.

  Outside automation mode, draw a quiet label on return rows, as the master row does today.
- [X] T009 [US1] In `src/managed/Nota.App/ArrangementView.Automation.cs` make sure every path that looks up a `TrackVM` by id also searches `_returns`. Today only `OnAutomationTouched` does. Check `ShowAutoTargetMenu`, the range clipboard (`CopyAutoSelection` / paste / delete targets via `_autoSelTrack`), `RemapAutoTargets` callers and `Redraw()`, so the footer is invalidated (`_footerLanes.InvalidateVisual()`) whenever track lanes are. The target menu for a return lists Volume, Pan and the return's device chain (built-in, plugin, nested). Nothing is offered for MIDI-FX or the instrument, since returns have neither.
- [X] T010 [US1] Live sync: in `src/managed/Nota.App/ArrangementView.cs` the footer return fader / dB readout (`_footerFaders`, `_footerDb`) must follow automated volume during playback the way track header faders do. Find the meter/fader poll that updates track headers and extend it to return rows, so the related controls reflect the automated value (FR-003).
- [X] T011 [US1] Verify FR-004: a return point edit makes one undo step, like a track edit (`CommitAuto` → `SetAutomationPoints`). Undo / redo, then `Refresh()`, must reload return `AutoPoints` (`Refresh` already calls `LoadAutoPoints` for returns; confirm the footer redraws). Fix any gap in `src/managed/Nota.App/ArrangementView.cs`.

**Checkpoint**: Return automation works end to end; track automation unchanged; smoke test green.

---

## Phase 4: User Story 2 - Resize return and master rows (Priority: P1)

**Goal**: Return and master rows resize by seam drag and through the context menu; headers adapt; the footer caps at about half the height and scrolls; heights persist.

**Independent Test**: [quickstart.md](quickstart.md) scenarios 8–16.

- [X] T012 [US2] In `src/managed/Nota.App/ArrangementView.Sizing.cs` add the footer resize path: `BeginFooterResize(int footerRow, double y)`, `UpdateFooterResize(double y)` and `EndFooterResize()`. Targets: the dragged return, or the selected returns when it is one of several selected (`SelectedTrackIds()` ∩ returns), or the master alone. Alt is ignored (FR-016). Height = `SnapRowHeight(start + dy)`, written with `StoreRowHeight` or to `_masterHeight`. Each frame does `RebuildFooterGeometry()` + `RebuildFooterHeaders()` + `_footerLanes.InvalidateVisual()` (5 rows max). Add `ResetFooterRowHeight(int footerRow)`, which sets the targets back to 30, and `SetFooterHeights(IEnumerable<int> returnIds, bool master, double h)`.
- [X] T013 [US2] In `src/managed/Nota.App/ArrangementView.cs` / `ArrangementView.Sizing.cs` add header-side seam grips on `_footerHeaders`, one per footer row at its bottom edge, built like `RowSeamGrip`:
  - 6 px band, `SizeNorthSouth` cursor, `Brush.BorderStrong` line on hover;
  - wired to the T012 methods, with double-click resetting the row;
  - added at the end of `RebuildFooterHeaders()` above the cards.
- [X] T014 [US2] In `src/managed/Nota.App/ArrangementView.Controls.cs` add the lane-side seam band to `FooterLaneControl`: within ±3 px of a row bottom, set the `SizeNorthSouth` cursor on hover, start the T012 resize on press (before the automation / master handlers), and reset on double-click.
- [X] T015 [US2] In `src/managed/Nota.App/ArrangementView.Tracks.cs` add a `TrackHeightRow` overload / flag with no all-tracks modifier. Its handler ignores `all` and calls `SetFooterHeights`, and it marks the current preset from the footer heights. In `src/managed/Nota.App/ArrangementView.cs` `ShowFooterMenu`, add this "Height" row as its own group: for a return, it targets the selected returns when the clicked one is selected, else that return; for the master, the master.
- [X] T016 [US2] In `src/managed/Nota.App/ArrangementView.cs` make `BuildSlimRow` tier-aware, using `TierFor(height)` per [research.md R5](research.md):
  - **Compact**: one line with spine, name, mini fader, dB and meter. When the column is narrow, the fader shrinks first, then the dB readout is dropped.
  - **Standard**: today's two-line layout, vertically centred, plus an "M" `ChipToggle` for returns (`SetTrackMute`).
  - **Large**: standard plus a pan control row for returns, with the same pan control as track headers (one undo step per gesture via `MixGesture` with `MixTarget.Pan(id)`). The master keeps standard content.

  No control may be clipped or overlap at widths 160–480 px.
- [X] T017 [US2] In `src/managed/Nota.App/ArrangementView.cs` wrap `_footer` in a vertical `ScrollViewer` (`_footerScroller`, horizontal scrolling disabled, thin scrollbar matching the track scroller) docked at the bottom in place of `_footer`. Set its `MaxHeight` to `Math.Floor(0.5 * (footer area + centerArea height))` and recompute it in `OnViewSizeChanged` and after footer geometry changes. The footer seam and drag-drop handlers stay on `_footer`. Wheel over footer lanes scrolls the footer vertically when it overflows (FR-015).
- [X] T018 [US2] In `src/managed/Nota.App/ArrangementView.Sizing.cs` extend the sidecar per [contracts/track-view-sidecar.md](contracts/track-view-sidecar.md):
  - `TrackViewDto` gets `[JsonPropertyName("masterHeight")] double? MasterHeight`;
  - `SaveTrackView` writes return heights via the existing loop (entries are only present when they differ from the 30 default) and writes `MasterHeight` when `_masterHeight` ≠ 30. The file is deleted when everything is at its default;
  - `LoadTrackView` accepts return entries (drop the `!ti.IsReturn` filter) and reads `MasterHeight` clamped to [30, 256];
  - `ResetTrackView` sets `_masterHeight = 30`;
  - keep `version: 1`.

**Checkpoint**: Footer rows resize by drag and menu, headers adapt, the footer scrolls past the cap, and the layout survives save/reopen.

---

## Phase 5: User Story 3 - Compact by default (Priority: P2)

**Goal**: New projects, new returns and old projects show returns and master at 30 px.

**Independent Test**: [quickstart.md](quickstart.md) scenarios 1–2.

- [X] T019 [US3] Confirm that the T002/T018 defaults give Compact everywhere:
  - a new project (`ResetTrackView`);
  - `AddReturnTrack` from the UI (no stored entry → 30);
  - an old bundle without return entries or `masterHeight`;
  - undo of a return delete brings the return back at 30 (ids change, so no stale entry).

  Regular tracks still default to 64. Fix any path in `src/managed/Nota.App/ArrangementView.Sizing.cs` that assumes `DefaultRowHeight` for every row.

**Checkpoint**: All three stories work.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T020 Run every [quickstart.md](quickstart.md) scenario and take headless screenshots of return / master headers at 30 / 61 / 62 / 111 / 112 / 256 px × widths 160 / 228 / 480 (agent memory `nota-ui-verify`); fix clipping or overlaps
- [X] T021 Check the new handles and layouts against `DESIGN.md` with the `nota-design` skill: neutral seams, no new accent use, sentence-case labels

### Nota closing tasks *(always include — these are the PR gates)*

- [X] T022 Run `scripts/build.sh` until native + managed + smoke test (including `ReturnAutomationTests`) are green
- [X] T023 AGPL-3.0-only SPDX two-line header on `src/managed/Nota.App/ArrangementView.AutoEditor.cs` and `tests/Nota.SmokeTest/ReturnAutomationTests.cs`
- [X] T024 New dependency: N/A
- [X] T025 `CHANGELOG.md` `[Unreleased]` → Added: automation lanes on return tracks; resizable return / master rows with Compact / Standard / Large; Changed: return and master rows are compact by default
- [X] T026 `FEATURES.md` via the `nota-features` skill; user docs (arrangement, automation, mixing / returns pages, EN + RU) via `nota-docs`; Preferences → Shortcuts wording for the row-seam gestures ("track, return or master") via `nota-shortcuts`
- [X] T027 Smoke test extended for return automation (T005). No other engine behaviour changed.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)** → **Foundational (T002–T004)** → user stories.
- **US1** (T005–T011) and **US2** (T012–T018) both depend on Foundational. Within US1, T006 → T007 → T008 → T009–T011. Within US2, T012 → T013/T014 → T015–T018.
- **US3** (T019) depends on T002 and T018.
- **Polish** (T020–T027) after the stories.

### User Story Dependencies

- US1 and US2 are independent of each other; both touch `ArrangementView.Controls.cs` and `ArrangementView.cs`, so run them one after the other, not in parallel.
- US3 is a verification pass on US2's defaults.

### Parallel Opportunities

- T005 (smoke test, `tests/`) can run alongside T006–T011 (app).
- T021, T025 and T026 can run in parallel once the code is done.

---

## Parallel Example: User Story 1

```bash
Task: "Add tests/Nota.SmokeTest/ReturnAutomationTests.cs (return lane render + save/load)"
Task: "Extract AutoEditor into src/managed/Nota.App/ArrangementView.AutoEditor.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. T001–T004: footer geometry (no visible change except the 30 px default).
2. T005–T011: return automation. **Stop and validate** quickstart 3–7 and 17.

### Incremental Delivery

1. Foundational → US1 (return automation) → validate.
2. US2 (resizing, scroll, persistence) → validate 8–16.
3. US3 defaults → validate 1–2.
4. Polish and PR gates.

---

## Notes

- The T006/T007 extraction is a pure move; check track automation (quickstart 17) before building on it.
- Heights are view state: never in undo history, never sent to the engine.
- Commit after each logical group (repo style `(feat) …`); per agent memory, leave commits to the user unless asked.

## Implementation notes (2026-10-28)

- T008: return rows outside automation mode draw no label (the master keeps its "master
  volume" hint); a per-return hint added noise without telling the user anything new.
- T012/T014: footer seam drags read the pointer in `ArrangementView` coordinates. The footer
  is docked to the bottom and grows upwards, so its own coordinates move under the pointer
  while a row grows; footer-local coordinates made the drag run away. Until the footer hits
  its cap, the dragged row grows upwards and its bottom edge stays put.
- T012/T015: returns can't be multi-selected in the footer today (a click selects one), so
  "the selected returns" is the one selected return in practice. The code takes every
  multi-selected return, should footer multi-selection arrive.
- T013: a live footer drag moves the existing cards and grips (a rebuild would drop the grip
  holding the pointer) and rebuilds a card only when it crosses a layout tier.
- T020: verified with a headless harness (`/tmp/footerharness`): compact defaults (30/30/30),
  return lane drawing in automation mode, mixed heights, widths 160/480, the cap (content 768
  px in a 211 px scroller), a synthetic lane-side seam drag, and the sidecar round trip
  (`masterHeight` + a return entry). A stale-frame artifact after resizing the headless
  window disappears on a full redraw — not a layout issue (bounds checked).
- T026: docs edited in `../nota-docs` (arrangement + automation, EN + RU); the site build was
  not run here (node is not on PATH). No new screenshots.

## Phase 7: Convergence

- [X] T028 Let return rows join a multi-selection in the footer (⇧-click / ⌘-click on a return header in `src/managed/Nota.App/ArrangementView.cs` `BuildSlimRow`, adding to `_selTracks` as track headers do), so a seam drag or a Height preset on one selected return resizes every selected return per US2/AC3 and FR-007 (partial)
- [X] T029 Finish the header screenshot matrix for return and master rows at 30 / 61 / 62 / 111 / 112 / 256 px × widths 160 / 228 / 480 with `/tmp/footerharness`, and fix any clipped or overlapping control per SC-005 and FR-009 (partial)

### Convergence notes (2026-10-28)

- T028: ⌘/Ctrl-click a return header adds or removes it, ⇧-click takes the range from the
  selected return; a return selection never mixes with tracks (track commands stay
  track-only). Verified in `/tmp/footerharness matrix`: two selected returns resize together
  from one preset.
- T029: headers shot at 30 / 61 / 62 / 111 / 112 / 256 px × 160 / 228 / 480 px with an
  automated bounds check of every text, fader and pan inside its card — nothing clipped.
