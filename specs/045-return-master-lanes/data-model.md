# Data Model: Automation and resizable height for return and master rows

All of this is managed view state in `ArrangementView`, except return automation lanes,
which are existing engine track data.

## Row height (per return)

- **Key**: return track id (session-scoped), stored in `_trackHeights` with track heights.
- **Value**: px, clamped to [30, 256], snapped to 30 / 64 / 128 within 4 px while dragging.
- **Default**: 30 (Compact). Track rows keep 64. An entry equal to its row's default is not stored.
- **Lifecycle**: created by a seam drag or menu pick; removed when its return is deleted
  (`PruneTrackHeights` covers returns too); reset by a seam double-click.
- **Persisted**: `track-view.json` → `trackHeights[]` by engine track index.

## Master row height

- **Field**: `_masterHeight`, px, same clamp/snap, default 30.
- **Persisted**: `track-view.json` → `masterHeight` (omitted at default).
- **Reset**: New project / `ResetTrackView()` → 30.

## Footer geometry (derived, not stored)

- `_footerRowTops[0..returns+1]`: cumulative tops; the last entry is the footer content height.
- **Footer viewport**: `min(content height, cap)`, where cap ≈ half the vertical space
  shared by the track scroller and the footer. Past the cap, the footer scrolls vertically.
- **Header tier** per footer row: `TierFor(height)`.

## Return automation lane (existing)

- Same entity as a track lane: target (Volume / Pan / DeviceParam / PluginParam / nested)
  plus points (beat, value, curve). Owned by the return track in the engine graph and
  saved in the project document `tracks[].automation`.
- UI selection per return: `_autoTargets[returnId]`, as for tracks; the first automated
  lane is shown on the first visit in automation mode.

## Master volume automation (existing, unchanged)

- `_masterAuto` points, value range 0..2, committed with `SetMasterVolumeAutomation`.
