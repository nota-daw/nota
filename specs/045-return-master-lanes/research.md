# Research: Automation and resizable height for return and master rows

## R1. Does the engine already support automation on returns?

- **Decision**: Yes; no engine change. Add a smoke check to lock it in.
- **Rationale**: A return is a `Track` of type `Return` (`Engine::addReturnTrack`), so
  `addAutomationLane` / `setAutomationPoints` find it through `findTrackAuthoring`.
  `Engine::applyAutomation` walks every track in the graph before the mix, including
  returns, and the return render pass (pass 2 in `Engine_Render.cpp`) reads `t.volume()`,
  `t.pan()` and runs `t.devices`, so automated values take effect. `ProjectService` saves
  and reloads `Automation` for every track, and returns go through the same loop after
  `AddReturnTrack()`. On the managed side, `Refresh()` already fills return `TrackVM`s
  with their automation target and points, and `OnAutomationTouched` already searches
  `_returns`. What's missing is a footer UI that shows and edits them.
- **Alternatives considered**: a send-level workaround (automating every send) was
  rejected by the spec.

## R2. How should the footer edit return automation?

- **Decision**: Extract the automation editor from `LaneControl` into a row-agnostic
  `AutoEditor` (new partial file). It holds the drag / bend / hover / range state and the
  press / move / release / draw logic, and reads rows through a small host interface:
  `Count`, `TrackAt(i)`, `Top(i)`, `Height(i)`, `RowAtY(y)`, plus the owning control
  (pointer capture, invalidate, menu anchor). `LaneControl` maps it to `_tracks` /
  `_rowTops`; `FooterLaneControl` maps it to `_returns` / footer row tops.
- **Rationale**: FR-002 asks for identical editing (snap, curves with Alt-bend, Shift
  range select, copy/paste, right-click menu, double-click add, fine drag). Duplicating the
  ~400 lines in `LaneControl` would drift; the code already works on a `TrackVM` plus row
  geometry, so the extraction is mostly mechanical (replace `_o._tracks[i]`, `RowTop(i)`,
  `RowH(i)`, `_o.RowAtY` with host calls).
- **Alternatives considered**:
  - Move returns into the scrolling track list: contradicts the spec's assumption that
    they stay pinned.
  - A cut-down footer editor (points only): fails FR-002.
  - Modelling master-volume as a pseudo `TrackVM`: its points live in `_masterAuto` with
    their own commit path (`SetMasterVolumeAutomation`) and range. The master row keeps
    its own handler, adapted to variable height (FR-005). Unifying it is optional later.

## R3. Where do return and master heights live?

- **Decision**: Return heights go in the existing `_trackHeights` (track id → height),
  with the default resolved per row kind: `DefaultHeightFor(id)` = Compact (30) for
  returns, Standard (64) for tracks. `StoreRowHeight` drops an entry when it equals that
  row's default. The master gets its own `_masterHeight` field (default 30), since its
  reserved id is not an engine track index.
- **Rationale**: Reuses clamp/snap and the prune logic. `AllTrackRowIds()` already
  excludes returns, so Alt-drag and "all tracks" leave the footer alone (FR-016) with no
  extra work.
- **Persistence**: the sidecar stays `version: 1`; return heights are written as
  `trackHeights` entries by engine index (as tracks are) and a new `masterHeight` key is
  added. Older builds skip return entries (their loader filters `IsReturn`) and ignore
  unknown keys, so files stay compatible both ways. A project without these entries
  opens with Compact returns and master (FR-012). See
  [contracts/track-view-sidecar.md](contracts/track-view-sidecar.md).

## R4. Footer cap and scrolling

- **Decision**: Wrap the footer grid (header canvas + `FooterLaneControl`) in a vertical
  `ScrollViewer` docked at the bottom. Its `MaxHeight` is half the height left for
  footer + track scroller, recomputed when the view resizes. The horizontal position
  still follows the lanes' shared `_scrollBeats` (the footer lanes draw in beat space, as
  they do today), so only vertical scrolling is new. The vertical scrollbar uses the
  same thin style as the track scroller.
- **Rationale**: FR-015. With at most 5 rows the footer rarely passes the cap, so a plain
  scroller is enough.
- **Alternatives considered**: stopping growth at the cap (rejected in Clarifications);
  making the footer split resizable (more UI than asked for).

## R5. Header layout per tier for return / master rows

- **Decision**: `BuildSlimRow` picks the tier with `TierFor(height)` (same thresholds as
  tracks: compact < 62 ≤ standard < 112 ≤ large):
  - **Compact** (30 px default): one line with spine, name, mini fader, dB readout and meter.
    When the column is narrow the fader shrinks first, then the dB readout goes.
  - **Standard**: today's 40 px layout (name + dB over a full-width fader), vertically
    centred, plus a Mute chip for returns on the name line (the engine already honours a
    return's mute).
  - **Large**: standard plus a pan control row for returns (pan is now automatable on
    returns, so it should be visible). The master has no pan, so it keeps standard
    content with a taller fader area.
- **Rationale**: FR-009 asks for the same tier rules with nothing clipped. Mute and pan
  are the controls a return lacks today that its new automation targets need.
- **Alternatives considered**: keeping a single layout and clipping it (fails FR-009);
  adding solo/arm (returns are exempt from solo, can't record).

## R6. Resizing gestures in the footer

- **Decision**: Footer rows get header-side seam grips (like `RowSeamGrip`) on
  `_footerHeaders` and a lane-side seam band in `FooterLaneControl`. Both go through a
  footer resize path that shares `SnapRowHeight` / `ClampRowHeight`, ignores Alt (FR-016),
  resizes the selected returns together when the dragged one is selected (US2-3), and
  rebuilds footer headers per frame (5 rows max). Double-click resets to Compact. The
  footer context menu gets the "Height" segmented row (`TrackHeightRow` with the
  all-tracks modifier turned off).
- **Rationale**: Same gestures as specs/015, so `nota-shortcuts` needs only a wording
  check ("track or return/master").

## R7. Hit testing and drop targets

- **Decision**: `FooterRowAt(y)`, `SetFooterDropRow`, `MasterRowTop` and
  `FooterLaneControl.Render` switch from `row * FooterRowH` to a `_footerRowTops` array
  rebuilt with the heights. `FooterRowH` goes away.
- **Rationale**: FR-010 and FR-013 (browser drops still land on the right bus).
