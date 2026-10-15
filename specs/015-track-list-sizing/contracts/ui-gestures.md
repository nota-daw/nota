# Contract: arrangement sizing gestures and menu

The user-facing interface for this feature. The Preferences → Shortcuts list
(`nota-shortcuts`) must show every row below.

| Where | Gesture | Result |
|-------|---------|--------|
| Bottom edge of a track row (header or lane, ±3 px band) | hover | vertical-resize cursor |
| same | drag | that track's height follows; snaps to 30 / 64 / 128 within 4 px; clamped 30–256 |
| same, the track is in a multi-selection | drag | every selected track takes the height |
| same | Alt/Option + drag | every track row takes the height |
| same | double-click | reset to 64 (the track, or the selection it is in) |
| Seam between headers and lanes (±3 px) | hover | horizontal-resize cursor |
| same | drag | header column width follows; clamped 160–480 (and lanes ≥ 240 px) |
| same | double-click | reset to 228 |
| Track context menu | Track height ▸ Compact / Standard / Large | applies to the clicked track or the selection |
| same | Track height ▸ All tracks: Compact / Standard / Large | applies to every track row |

Not resizable: slim group bars (26 px), return and master rows (40 px). A selected group
opens to its stored height (default 64). Resizing is not undoable and is saved with the
project.
