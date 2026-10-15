---

description: "Tasks for 015-track-list-sizing — resizable track height and track-list width"
---

# Tasks: Resizable track height and track-list width

**Input**: Design documents from `specs/015-track-list-sizing/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: The spec does not request automated tests. Verification is by the
[quickstart.md](quickstart.md) scenarios and screenshots. The smoke test is unchanged and
must stay green.

**Organization**: Tasks are grouped by user story. US4's compact layout moves forward
into Foundational, because US1 and US2 reach 30 px rows and would clip controls without it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story from spec.md (US1–US4)

All paths are relative to the repository root. `AV` = `src/managed/Nota.App/ArrangementView`.

---

## Phase 1: Setup

- [X] T001 Create the partial `src/managed/Nota.App/ArrangementView.Sizing.cs` with the two-line AGPL-3.0-only SPDX header plus a comment header explaining *why* it exists (arrangement row/column sizing: state, snap/clamp, drag controllers, sidecar), `namespace Nota.App; public sealed partial class ArrangementView { }`

---

## Phase 2: Foundational (blocks all stories)

**Purpose**: per-track row geometry, a variable header width, and header tiers, so every story can change sizes safely.

- [X] T002 In `src/managed/Nota.App/ArrangementView.Sizing.cs` add the constants `DefaultRowHeight = 64`, `MinRowHeight = 30`, `MaxRowHeight = 256`, presets `CompactRowHeight = 30`, `StandardRowHeight = 64`, `LargeRowHeight = 128`, `SnapPx = 4`; `DefaultHeaderW = 228`, `MinHeaderW = 160`, `MaxHeaderW = 480`, `MinLanesW = 240`; the state `private readonly Dictionary<int, double> _trackHeights` (trackId → px; an absent key means 64) and `private double _headerW = DefaultHeaderW`; the helpers `ClampRowHeight`, `SnapRowHeight(h)` (within 4 px of 30/64/128 → that preset, then clamp to [30, 256]) and `TrackRowHeight(TrackVM t)` (the stored height or 64)
- [X] T003 In `src/managed/Nota.App/ArrangementView.cs` remove `const double RowHeight = 64`: `RebuildRowGeometry()` and `RowHeightAt(i)` use `IsSlimRow(t) ? GroupRowHeight : TrackRowHeight(t)`; `RowTop(i)` extrapolates past the end with `DefaultRowHeight`; `_lanes.Height` / `_headers.Height` (`Math.Max(RowHeight, RowsHeight)` at the three sites) use `DefaultRowHeight`; `BuildTrackCard`'s `Height = RowHeight` becomes `Height = RowHeightAt(index)` (pass the effective height in)
- [X] T004 In `src/managed/Nota.App/ArrangementView.cs` turn `const double HeaderW = 228` into `private double HeaderW => _headerW;`. Keep every `Width = HeaderW` working, and register each fixed-width header-column part in a new `private readonly List<Control> _headerColumnParts` as it is built: `_headers`, `zoomBar`, `leftBackdrop`, `_footerHeaders`, `footerHeaderPanel`, `_trackDropLine`, `_headerDropRing`, and every `HeaderPanel(...)` result (overview and sections rows)
- [X] T005 In `src/managed/Nota.App/ArrangementView.Sizing.cs` add `PruneTrackHeights()` (drop ids no longer in `_tracks`, keep collapsed-group children: prune only against engine track ids) and call it from `Refresh` in `src/managed/Nota.App/ArrangementView.cs` before `RebuildRowGeometry()`
- [X] T006 In `src/managed/Nota.App/ArrangementView.Sizing.cs` add `enum HeaderTier { Compact, Standard, Large }` and `static HeaderTier TierFor(double h) => h < 56 ? Compact : h < 112 ? Standard : Large`
- [X] T007 In `src/managed/Nota.App/ArrangementView.cs` refactor `BuildTrackCard(TrackVM t)` so the control factories (name/type-tag row, M/S/● chips, input/MIDI combo + monitor, fader + dB with `MixGesture` undo and automation-write wiring, pan bar, meter, spine, card shell plus `AttachHeaderGestures`, `_headerCards` / `_volFaders` / `_volDb` / `_panBars` / `_meters` registration) are shared helpers. `BuildTrackCard` then dispatches on `TierFor(height)` to `BuildStandardCard` (today's layout, pixel-identical at 64 px) and the tier builders below
- [X] T008 In `src/managed/Nota.App/ArrangementView.cs` add `BuildCompactCard`: one line — spine, name (ellipsised), type tag (omitted when the card width leaves the name under ~40 px), M / S / ● chips (● only on instrument/audio tracks, as today), meter on the right; no combo, fader or pan; vertically centred; it must fit 30 px with no clipping. A group selected at compact height uses the same layout plus its disclosure triangle

**Checkpoint**: The app builds; at the default sizes the arrangement looks and behaves exactly as before (quickstart #10).

---

## Phase 3: User Story 1 — Resize a track's height (P1) 🎯 MVP

**Goal**: drag a track's bottom edge (header or lane) to resize it; snap, clamp, double-click reset, multi-selection, persistence.

**Independent Test**: quickstart #1–#4, #7 (row part), #11, #12 (heights), #13.

- [X] T009 [US1] In `src/managed/Nota.App/ArrangementView.Sizing.cs` add the row-resize controller: `BeginRowResize(int trackId, double pointerY, KeyModifiers mods)` (target set = all track rows when Alt/Option is held — wired in US2 — the multi-selection when `trackId` is in `SelectedTrackIds()` with Count > 1, otherwise `{trackId}`; remember the start height and pointer Y), `UpdateRowResize(double pointerY)` (h = `SnapRowHeight(start + dy)`, write to `_trackHeights` for the target set, store none at 64), and `EndRowResize()`; plus `ResetRowHeight(int trackId)` (the track or its multi-selection → 64)
- [X] T010 [US1] In `src/managed/Nota.App/ArrangementView.Sizing.cs` add `RelayoutRowsLive()` per research R4: `RebuildRowGeometry()`; for each card in `_headerCards` set `Canvas.SetTop` and `Height` from `RowTop`/`RowHeightAt`; rebuild only the cards whose `TierFor` changed (via `BuildHeaderCard`, replacing the child in `_headers`); update `_headers.Height` and `_lanes.Height`; move the seam grips; `InvalidateVisual` on `_lanes` and `_overlay`. `EndRowResize` calls the full `RebuildHeaders()` once
- [X] T011 [US1] In `src/managed/Nota.App/ArrangementView.cs` (inside `RebuildHeaders`) add one transparent 6 px seam grip `Border` per non-slim track row at `Canvas.Top = RowTop(i+1) - 3`, `Width = HeaderW`, `Cursor = SizeNorthSouth`, added after the cards (on top). PointerPressed with the left button → capture, `BeginRowResize` (double-click, `ClickCount == 2` → `ResetRowHeight`); PointerMoved → `UpdateRowResize(e.GetPosition(_headers).Y)`; PointerReleased / CaptureLost → `EndRowResize`. Mark the events Handled so `AttachHeaderGestures` (select/reorder) does not fire. Keep the grips in a list so `RelayoutRowsLive` can move them
- [X] T012 [US1] In `src/managed/Nota.App/ArrangementView.LaneControl.cs` add a seam-band hit test `int SeamRowAt(double y)` (row i whose `RowTop(i+1)` is within ±3 px of y, skipping slim group rows; −1 otherwise). In PointerMoved (no button) show `SizeNorthSouth` over the band. In PointerPressed check it **before** clip / trim / automation / selection hit tests and start `BeginRowResize` (double-click → reset); route move/release to `UpdateRowResize` / `EndRowResize` while a resize is active
- [X] T013 [US1] In `src/managed/Nota.App/ArrangementView.Sizing.cs` keep the dragged edge under the pointer near the bottom of the viewport: during `UpdateRowResize`, if the pointer is within 16 px of the scroller's bottom while growing, raise `_scroller.Offset.Y` by the delta; after shrinking, clamp the offset to the new extent
- [X] T014 [US1] In `src/managed/Nota.App/ArrangementView.Tracks.cs` carry heights over: in `DuplicateTracks`, after `newIds`, copy `_trackHeights[ids[i]]` → `newIds[i]` for `i < min(count)`; in `CopyTracks` (and cut) store the selection's heights in order in a new `private List<double?> _clipboardHeights`; in `PasteTracksAfter` apply them to `newIds` in order (counts may differ — apply only matching positions). New tracks get no entry (default 64)
- [X] T015 [US1] In `src/managed/Nota.App/ArrangementView.Sizing.cs` add the sidecar per [contracts/track-view-sidecar.md](contracts/track-view-sidecar.md): `public void SaveTrackView(string bundleDir)` writes `track-view.json` `{ "version": 1, "headerWidth": …, "trackHeights": [{ "index", "height" }] }` (track index in engine order via `_engine.TryGetTrackInfo(i, out ti)`; list only non-default heights; omit `headerWidth` when 228; delete the file when everything is at its default; never throw — wrap in try/catch like `SaveSections`). Add `public void LoadTrackView(string bundleDir)`: clear the state; when the file is absent or corrupt use defaults; clamp heights to [30, 256] and width to [160, 480]; skip out-of-range indices; ignore files with `version > 1` and unknown keys. Also add `public void ResetTrackView()`
- [X] T016 [US1] In `src/managed/Nota.App/MainWindow.Files.cs` call `Timeline.SaveTrackView(dir)` after `Timeline.SaveSections(dir)` in `DoSaveAsync` (before `RecordVersion`), `Timeline.LoadTrackView(dir)` after `Timeline.LoadSections(dir)` on open (before `RefreshAfterLoad()`), and `Timeline.ResetTrackView()` next to `Timeline.ClearSections()` on New. Check that the History restore path reopens through the same load (quickstart #14)

**Checkpoint**: US1 is fully usable — single and multi-track resizing, saved with the project.

---

## Phase 4: User Story 2 — Resize all tracks at once (P1)

**Goal**: Alt/Option-drag resizes every track; a "Track height" context submenu offers presets for the selection or all tracks.

**Independent Test**: quickstart #5, #6, #15.

- [X] T017 [US2] In `src/managed/Nota.App/ArrangementView.Sizing.cs` complete the Alt/Option branch of `BeginRowResize`: the target set = every non-return track id in the engine (including collapsed children, so they match on expand) while group slim bars stay 26 px. Add `SetTrackHeights(IEnumerable<int> ids, double h)` and `SetAllTrackHeights(double h)`, each followed by `Refresh`/`RebuildHeaders`
- [X] T018 [US2] In `src/managed/Nota.App/ArrangementView.cs` (`ShowTrackMenu`) and `src/managed/Nota.App/ArrangementView.Tracks.cs` (`ShowTracksMenu`) add a "Track height" `MenuItem` beside the colour submenu with radio items Compact / Standard / Large (ticked when every target is at that preset) for the clicked track or the selection, a `Separator`, then "All tracks: Compact" / "All tracks: Standard" / "All tracks: Large"; use `MenuKit` styling as the neighbouring items do
- [X] T019 [US2] Performance pass in `src/managed/Nota.App/ArrangementView.Sizing.cs`: Alt-drag on a 40-track project must follow the pointer with no visible lag. Make sure `RelayoutRowsLive` rebuilds only cards whose tier changed, and coalesce pointer moves to one relayout per frame (`Dispatcher.UIThread.Post(…, DispatcherPriority.Render)` with a pending flag) if needed

**Checkpoint**: US1 and US2 together cover the issue's height requests.

---

## Phase 5: User Story 3 — Resize the track-list width (P2)

**Goal**: drag the seam between headers and lanes; every header-column part follows; double-click resets; saved with the project.

**Independent Test**: quickstart #7 (seam), #8, #12 (width).

- [X] T020 [US3] In `src/managed/Nota.App/ArrangementView.Sizing.cs` add `SetHeaderWidth(double w, bool final)`: clamp to [160, 480] and to `Bounds.Width − 240`; set `_headerW`; set `Width` on every `_headerColumnParts` entry and every live card in `_headerCards` plus the seam grips; `InvalidateVisual` the lanes and overlay; when `final`, call `RebuildHeaders()` and rebuild the footer rows (return/master) so their inner layouts re-measure
- [X] T021 [US3] In `src/managed/Nota.App/ArrangementView.cs` add the column seam grip: a transparent 6 px `Border` with `Cursor = SizeWestEast`, `HorizontalAlignment = Left`, `Margin.Left = HeaderW − 3`, spanning the arrangement's full height (from the overview row through the footer) on the outer layout grid, on top. Drag → `SetHeaderWidth(startW + dx, final: false)`, release → `final: true`, double-click → `SetHeaderWidth(228, true)`. Update its margin inside `SetHeaderWidth`. It must not block the lanes' left-edge clip gestures beyond its 3 px overlap
- [X] T022 [US3] In `src/managed/Nota.App/ArrangementView.cs` handle the window shrinking: on the view's `SizeChanged`, re-clamp `_headerW` so the lanes keep ≥ 240 px (the lanes shrink first, then the headers clamp), without overwriting the user's stored width unless it no longer fits
- [X] T023 [US3] Check narrow widths in `src/managed/Nota.App/ArrangementView.cs`: at 160 px the standard and large cards must not overlap — the input/MIDI combo shrinks (star column), the dB readout and type tag hide below a width threshold, and the name ellipsises; the group bar and footer rows likewise

**Checkpoint**: The width works independently of height.

---

## Phase 6: User Story 4 — Headers that adapt to their size (P2)

**Goal**: the large layout above 112 px (compact and standard were delivered in Foundational).

**Independent Test**: quickstart #9 (all heights × widths), FR-010 scenario 4 reachability.

- [X] T024 [US4] In `src/managed/Nota.App/ArrangementView.cs` add `BuildLargeCard` using the shared factories from T007: row 1 name + type tag; row 2 M / S / ●; row 3 the input combo + monitor chip (audio) or the MIDI-from combo (instrument) at full width; row 4 the volume fader with a dB readout (taller hit area); row 5 a full-width pan bar; the meter at full card height on the right. Lay rows out top-aligned with the Studio spacing from `DESIGN.md`; there must be no clipping at 112 px and the layout must look intentional at 256 px (extra space below, nothing stretched oddly)
- [X] T025 [US4] In `src/managed/Nota.App/ArrangementView.cs` make sure a selected group (which opens from a slim bar) uses `TierFor(TrackRowHeight(group))` and its builder keeps the disclosure triangle and drops record-arm/input as `BuildTrackCard` does today
- [X] T026 [US4] Confirm reachability (FR-010 scenario 4) in `src/managed/Nota.App/ArrangementView.cs`: in compact, volume/pan stay reachable from the mixer, and record input / MIDI from from the track context menu (already present). Document anything missing in the PR; add no new controls

**Checkpoint**: All four stories are complete.

---

## Phase 7: Polish & Cross-Cutting

- [X] T027 [P] Studio design review per `DESIGN.md` / the `nota-design` skill: the seams highlight subtly on hover (neutral `Brush.BorderStrong` or similar token, no accent), cursors are right, and compact, standard and large look consistent in all four colour themes
- [X] T028 Run every scenario in [quickstart.md](quickstart.md), with screenshots at heights 30/56/64/128/256 × widths 160/228/480 (headless Avalonia or the in-app hook, `NOTA_DATA_DIR` isolated); confirm SC-001…SC-006
- [X] T029 Run the `nota-shortcuts` skill: add the gestures from [contracts/ui-gestures.md](contracts/ui-gestures.md) (edge drag, Alt/Option-drag for all, double-click reset, column seam drag and reset) to Preferences → Shortcuts

### Nota closing tasks *(PR gates)*

- [X] T030 Run `scripts/build.sh` (or `scripts/build-linux.sh` / `scripts/build-win.ps1`) until native + managed + smoke test are green
- [X] T031 AGPL-3.0-only SPDX two-line header on the new `src/managed/Nota.App/ArrangementView.Sizing.cs` (see CONTRIBUTING.md)
- [X] T032 New dependency: N/A (none added)
- [X] T033 `CHANGELOG.md` `[Unreleased]` → Added: resizable track heights (edge drag, Alt-drag for all, presets), resizable track-list width, size-adaptive track headers; reference nota-daw/nota#52
- [X] T034 Run `nota-features` (FEATURES.md) and `nota-docs` (arrangement page EN + RU in `../nota-docs`, with re-shot screenshots)
- [X] T035 Engine behaviour change → smoke test: N/A (UI-only; no engine change)

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002–T008)** → the user stories.
- **US1 (T009–T016)**: after Foundational. MVP.
- **US2 (T017–T019)**: after US1 (reuses its controller and relayout).
- **US3 (T020–T023)**: after Foundational only (T004); independent of US1/US2, but T015's sidecar already carries `headerWidth` — if US3 ships first, move the width parts of T015/T016 here.
- **US4 (T024–T026)**: after Foundational (T007). Testable through the US2 presets or by seeding `_trackHeights`.
- **Polish (T027–T035)**: after the desired stories.

Within a story the tasks are sequential: most edit `ArrangementView.cs` / `ArrangementView.Sizing.cs`.

## Parallel Opportunities

Most tasks touch the same two files, so true parallelism is limited:

- T012 (`ArrangementView.LaneControl.cs`) can run alongside T011 (`ArrangementView.cs`) once T009/T010 exist.
- T014 (`ArrangementView.Tracks.cs`) and T016 (`MainWindow.Files.cs`) can run in parallel with each other after T015's signatures exist.
- US3 (T020–T023) and US4 (T024–T026) can run in parallel after Foundational if one person owns `ArrangementView.cs` merges.
- T027 and the docs tasks T033/T034 can run in parallel at the end.

```text
After T010:  T011 (ArrangementView.cs)  ‖  T012 (ArrangementView.LaneControl.cs)
After T015:  T014 (ArrangementView.Tracks.cs)  ‖  T016 (MainWindow.Files.cs)
```

## Implementation Strategy

1. **MVP**: Setup + Foundational + US1 → per-track resizing, saved with the project. Validate quickstart #1–#4, #10–#13.
2. **+US2**: resize all/presets → the issue's height request is complete.
3. **+US3**: width seam.
4. **+US4**: the large layout.
5. Polish, docs, build gate, `/speckit-converge`.

## Notes

- Resizing is never put into undo, and it does not mark the project as having unsaved changes (research R2); the sizes save with the next save.
- Don't commit: the user reviews and commits.

## Phase 8: Convergence

- [X] T036 Stop the standard header layout clipping between 56 and ~61 px: its content needs ~61 px of visible height (measured: 67 px including the 6 px top/bottom margins), so at 56–60 px the volume/pan row is cut in half. In `src/managed/Nota.App/ArrangementView.Sizing.cs` raise the Compact→Standard boundary in `TierFor` from 56 to the measured fit (≈62 px, keeping 64 px standard and 30 px compact inside their tiers), or tighten the standard card's vertical margins at low heights in `src/managed/Nota.App/ArrangementView.cs`. Then update the 56 px figures in `specs/015-track-list-sizing/data-model.md` / `research.md` R7 only if the plan's tuning clause is used, and re-run the headless probe (every height 30–256 fits) per FR-010 / SC-003 (partial)
