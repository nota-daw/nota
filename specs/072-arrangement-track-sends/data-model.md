# Data model: Send levels in the arrangement track headers

## Send level (existing)

| Field | Type | Notes |
|---|---|---|
| track | track id | audio, instrument or group track (returns and master have none) |
| bus | 0…3 | the return's bus slot (A–D), `kMaxReturns` = 4 |
| level | float 0…1 | post-fader; 0 = off. Atomic on the track; saved in the project (`Sends[4]`) |

## Send value-undo step (new kind)

`MixChange { kind = TrackSend, trackId, deviceIndex = bus, before, after }` — one per
target per gesture; skipped on undo/redo when the track is gone or the bus is out of range.

## Send automation lane (new target)

| Field | Value |
|---|---|
| target | `Send` (5) |
| deviceIndex | bus 0…3 |
| paramIndex | −1 |
| points | beat, value 0…1, curve |

Rules: one lane per (track, bus); not nested (`chainUid` 0). Deleting a return:
lanes to its bus are removed, lanes to later buses get `bus − 1` in step with send levels;
reordering/removing devices never touches send lanes; cross-track clip moves carry them.

## Arrangement view state (sidecar `track-view.json`)

| Field | Type | Default |
|---|---|---|
| `showSends` | bool? | absent = true |
