# Data Model: Resizable track height and track-list width

All state is view-side (in `ArrangementView`), and none of it reaches the engine.

## TrackRowHeight (per track)

| Field | Type | Rules |
|-------|------|-------|
| trackId | int | session id of a track row (not a return, not master) |
| height | double (px) | clamp to [30, 256]; absent means the default 64 |

- Held in `_trackHeights: Dictionary<int, double>`; it survives `Refresh`. Entries for
  deleted tracks are pruned on `Refresh`.
- **Effective row height** = `GroupRowHeight` (26) for a slim group bar (group not
  selected); otherwise the stored height, or 64.
- **Lifecycle**: created by a resize/preset; copied by duplicate/paste; the
  default-height entry is dropped (absent = 64); the whole map is reset on New/Open
  before the sidecar loads.

## HeaderWidth (per project)

| Field | Type | Rules |
|-------|------|-------|
| headerWidth | double (px) | clamp to [160, 480] and to `viewWidth − 240`; default 228 |

## HeaderTier (derived, never stored)

| Tier | Height range | Content |
|------|--------------|---------|
| Compact | < 62 px | one line: name, (type tag), M, S, ●, meter |
| Standard | 62–111 px | today's header |
| Large | ≥ 112 px | name/tag · M/S/● · input or MIDI-from (and monitor) · fader + dB · pan · tall meter |

## Presets

| Preset | Height |
|--------|--------|
| Compact | 30 px |
| Standard | 64 px (default) |
| Large | 128 px |

Snap: while dragging, a height within 4 px of a preset becomes that preset.

## Persistence mapping

The sidecar format (keyed by track index in engine order) is in
[contracts/track-view-sidecar.md](contracts/track-view-sidecar.md). The index ↔ id
mapping uses the same engine track enumeration as `freeze-links.json`
(`Engine.TryGetTrackInfo(i, …)`).
