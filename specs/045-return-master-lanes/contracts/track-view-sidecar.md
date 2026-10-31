# Contract delta: `track-view.json` (return and master heights)

Extends [specs/015's sidecar contract](../../015-track-list-sizing/contracts/track-view-sidecar.md).
The format stays `version: 1`; the change is additive.

```json
{
  "version": 1,
  "headerWidth": 260,
  "trackHeights": [
    { "index": 0, "height": 128 },
    { "index": 7, "height": 64 }
  ],
  "masterHeight": 128
}
```

Here index 7 is a return track.

| Key | Type | Meaning |
|-----|------|---------|
| `trackHeights[].index` | int | engine track index; may now point at a **return** |
| `trackHeights[].height` | number | px, clamped to [30, 256] on read |
| `masterHeight` | number | master row height in px, clamped on read; omitted → 30 |

Rules (in addition to specs/015):

- A return is listed only when its height ≠ 30 (its default). A track is listed only when its height ≠ 64.
- `masterHeight` is written only when ≠ 30.
- The file is deleted when header width, every track, every return and the master are at their defaults.
- Reading: a return entry sets that return's height; a missing return entry or a missing `masterHeight` means Compact (30). This also covers projects saved before this feature.
- Older builds skip return entries (their loader filters returns) and ignore `masterHeight`, so files open both ways.
