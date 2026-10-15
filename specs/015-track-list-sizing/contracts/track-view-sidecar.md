# Contract: `track-view.json` bundle sidecar

Lives in the `.nota` bundle root next to `sections.json`. It is written on every save
and read on open. It is cosmetic: a write failure never fails the save, and a missing or
corrupt file means defaults.

```json
{
  "version": 1,
  "headerWidth": 260,
  "trackHeights": [
    { "index": 0, "height": 128 },
    { "index": 5, "height": 30 }
  ]
}
```

| Key | Type | Meaning |
|-----|------|---------|
| `version` | int | format version; readers ignore files with a higher major version |
| `headerWidth` | number | track-list width in px; omitted → 228 |
| `trackHeights[].index` | int | track index in engine order (`Engine.TryGetTrackInfo(i)`), as `freeze-links.json` uses |
| `trackHeights[].height` | number | row height in px, clamped to [30, 256] on read |

Rules:

- Only tracks that are not at the default height are listed; if every value is at its
  default, the file is deleted (as `sections.json` is when empty).
- Unknown keys are ignored (forward compatibility); out-of-range indices are skipped.
- Older Nota builds don't know the file and ignore it, so the project still opens.
- `ProjectHistory` snapshots it with the other `*.json` sidecars, so restoring a version
  restores the layout.
