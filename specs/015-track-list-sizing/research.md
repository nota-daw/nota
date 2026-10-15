# Research: Resizable track height and track-list width

Feature: [spec.md](spec.md) · Plan: [plan.md](plan.md)

## R1 — Where the sizes are stored

- **Decision**: a new bundle sidecar, `track-view.json`, written by `ArrangementView`
  next to `sections.json` and called from `MainWindow.Files.cs` beside
  `Timeline.SaveSections` / `LoadSections`. Tracks are keyed by **track index** in engine
  order, as `freeze-links.json` does.
- **Rationale**: the project already keeps view-only state in sidecars (sections,
  modular layout, MIDI-learn, freeze links). The history in
  `Nota.Infrastructure/Persistence/ProjectHistory.cs` versions every `*.json` in the
  bundle, so restoring a version restores the sizes too. Old builds ignore the unknown
  file, which meets the backward-compatibility edge case. Track ids are only stable
  within a session (see the comment in `MainWindow.LiveFreeze.cs`), so the index is the
  persistent key.
- **Alternatives considered**: (a) a field in the engine's project manifest — needs a C
  ABI change for pure UI state, which is against AR-7 minimalism, so rejected;
  (b) app settings (`SettingsService`) — rejected in Clarifications (sizes belong to
  the project).

## R2 — Dirty state

- **Decision**: resizing does not mark the project as having unsaved changes; the sizes
  are written on the next save. This matches the sections and collapse state today.
- **Rationale**: "unsaved changes" is a fingerprint of the engine project
  (`MainWindow.Files.cs` → `_projects.Fingerprint`), and view sidecars are not part of
  it. Adding sidecars to the fingerprint would be a separate change. FR-013 allows
  either behaviour ("may mark").
- **Alternatives considered**: hashing `track-view.json` into the fingerprint — rejected
  because it would make the sections and modular layout inconsistent unless they were
  changed too; it is better as a follow-up that covers every sidecar.

## R3 — Row geometry with per-track heights

- **Decision**: replace the `RowHeight` constant with a per-track lookup
  `_trackHeights: Dictionary<int trackId, double>`, plus `DefaultRowHeight = 64`.
  `RebuildRowGeometry()` reads `TrackRowHeight(t)`; slim group rows keep
  `GroupRowHeight`. A selected group opens to its own stored height (default 64).
- **Rationale**: every y↔row conversion already goes through `_rowTops`,
  `RowHeightAt()` and `RowTop()`, and `LaneControl` reads `RowHeightAt` (`RowH(i)`), so
  clips, waveforms, notes and automation scale with no lane changes beyond the
  resize gesture itself. `RowTop()` extrapolation past the end keeps using the default.
- **Alternatives considered**: storing the height on `TrackVM` — rejected because
  `TrackVM`s are rebuilt on every `Refresh`; the dictionary survives, like
  `_autoTargets` does.

## R4 — Live resizing without lag (FR-015)

- **Decision**: while a drag runs, update the dictionary, call `RebuildRowGeometry()`,
  reposition the existing header cards (`Canvas.SetTop` plus `card.Height`), resize the
  `_headers` and `_lanes` heights, and invalidate the lanes and overlay. Rebuild a card
  (`BuildHeaderCard`) only when its **layout tier** changes. Do a full
  `RebuildHeaders()` once on release.
- **Rationale**: a full header rebuild creates dozens of controls per track (faders,
  combos); doing that per pointer move on 40 tracks would visibly stutter.
  `_headerCards` already gives the card per track id.
- **Alternatives considered**: rebuild on every move — simple but slow; scale transform
  — distorts text.

## R5 — Hit-testing the row seam vs. existing gestures (FR-014)

- **Decision**: a 6 px band centred on each track row's bottom edge (±3 px) is the
  resize zone. **Headers**: on `_headers` (a Canvas), add a transparent 6 px
  `Border` "seam grip" per track row at `RowTop(i+1) - 3`, above the cards, with
  `Cursor = SizeNorthSouth`. **Lanes**: `LaneControl` checks the zone first in
  `PointerMoved` (cursor) and `PointerPressed` (start a resize) before clip, trim or
  automation hit tests. Slim group bars and the gap past the last row get no seam.
  The reorder drag stays on the title strip (top 24 px), which never overlaps the
  bottom band at heights ≥ 30 px.
- **Rationale**: Avalonia's `GridSplitter` doesn't fit a canvas of variable rows, and
  separate grips keep the header code readable. Lanes are custom-drawn, so the check
  belongs in their pointer handlers.
- **Alternatives considered**: a resize zone only in headers — users expect the lane
  edge too (spec FR-001); overlay grips across the lanes — would block clip hit-testing
  along the whole edge.

## R6 — Snapping (FR-001a)

- **Decision**: during a drag, if `|h − preset| ≤ 4 px` for a preset in {30, 64, 128},
  use the preset. Clamp to [30, 256]. Alt/Option-drag applies the height to all track
  rows; a drag on a member of the multi-selection applies it to the selection.
- **Rationale**: 4 px is easy to hit but doesn't feel sticky; it matches the light snap
  agreed in Clarifications.

## R7 — Header layout tiers (FR-010, FR-011)

- **Decision**: `HeaderTier Tier(double h) => h < 62 ? Compact : h < 112 ? Standard : Large` (tuned from 56 after converge measured the standard layout at ~61 px of visible content).
  - **Compact**: one line — spine, name (ellipsised), type tag (dropped if the name
    would fall under ~40 px), M / S / ● chips, meter on the right; no input/MIDI combo,
    no fader/pan.
  - **Standard**: today's `BuildTrackCard`, unchanged.
  - **Large**: row 1 name + tag; row 2 M / S / ●; row 3 input / MIDI-from combo (and the
    monitor chip) full width; row 4 volume fader with a dB readout; row 5 a full-width pan
    bar; the meter takes the full height. Uses existing controls only (`MiniFader`,
    `PanBar`, `MeterBar`, combos) — no engine changes.
- **Rationale**: compact needs ≈ 22 px of content plus 4 px padding, so it fits in 30 px.
  Standard needs ~61 px of visible content, so it starts at 62 px. Large
  at 128 px fits five rows of about 18 px plus gaps.
- **Reachability** (FR-010 scenario 4): mute, solo and arm stay; volume, pan and
  input are in the mixer and the track context menu ("Record input", "MIDI from")
  already.

## R8 — Track-list width (FR-008, FR-009)

- **Decision**: replace `const HeaderW` with a property backed by `_headerW` (default
  228, clamp [160, 480]; also clamp so the lanes keep ≥ 240 px). Every fixed-width
  header-column part registers in `_headerColumnParts` when it is built (`_headers`, the
  `zoomBar`, `leftBackdrop`, `_footerHeaders`, `footerHeaderPanel`, every
  `HeaderPanel(...)` used by the overview and sections rows, and both drop rings);
  `SetHeaderWidth(w)` sets their `Width` and each live card's `Width` (via
  `_headerCards`), and the footer row cards are rebuilt on release. The resize grip is
  a 6 px transparent `Border` with `Cursor = SizeWestEast`, laid over the whole view
  height at `x = HeaderW - 3` on the outer grid. Double-click resets to 228.
- **Rationale**: the column widths are already `Auto,*`, so the lanes take the rest by
  themselves; only explicit `Width = HeaderW` assignments need updating.
  `IsInTrackHeaders` reads the property, so it stays correct.

## R9 — Duplicate / paste carry the height

- **Decision**: `DuplicateTracks(ids)` maps `ids[i] → newIds[i]` and copies the
  heights. `CopyTracks` stores the selection's heights in a view-side list (in order),
  and `PasteTracksAfter` applies them to `newIds` in order. When the counts don't match
  (a group copy expands to its children), only tracks whose source is known get a
  height; the rest stay at the default.
- **Rationale**: the track clipboard lives in the engine and knows nothing about view
  sizes; a parallel list in the view is enough.

## R10 — Context-menu presets (FR-006)

- **Decision**: a "Track height" submenu in `ShowTrackMenu` and `ShowTracksMenu`
  (Compact / Standard / Large for the selected tracks, a separator, then
  "All tracks: Compact / Standard / Large"), with radio ticks on the current preset.
- **Rationale**: it sits beside "Color" in the existing menu style (`MenuKit`); no new
  shortcut needed.

## R11 — Verification approach

- **Decision**: no engine behaviour changes, so the smoke test is unchanged (it must
  still pass). UI verification uses the memory recipe (headless Avalonia render, or the
  env-guarded in-app hook plus a screenshot, with `NOTA_DATA_DIR` isolation): screenshots
  at heights 30 / 56 / 64 / 128 / 256 and widths 160 / 228 / 480; sidecar round-trip
  checked by save → reopen.
