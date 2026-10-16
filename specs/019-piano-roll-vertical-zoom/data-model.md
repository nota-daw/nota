# Data model: Piano roll vertical zoom

View state only. Nothing is added to the project document, the engine or the C ABI.

## Piano roll view state (per clip)

Existing in-memory record in `PianoRollView`, keyed by the clip identity
`(int, int)` set through `SetClipIdentity`, living in a static dictionary for the app
session.

| Field | Type | Status | Rule |
|-------|------|--------|------|
| `Ppb` | double | existing | horizontal zoom, pixels per beat, 6–400 |
| `Scroll` | double | existing | horizontal scroll, beats ≥ 0 |
| `VOffset` | double | existing | vertical scroll offset, px ≥ 0 |
| `HasV` | bool | existing | state was saved after layout |
| `RowH` | double | **new** | row height, px, clamped to 6–36; absent/first open → 12 |

### Rules

- Written on every zoom, scroll and fit (same `SaveView` call sites, plus the new zoom
  paths).
- Restored in `SetNotes` before the first layout; out-of-range values are clamped.
- One entry per clip; zooming one clip never changes another clip's entry
  (clarification Q1).
- Not undoable, does not mark the project modified, not persisted to disk.

## Derived values

- Total grid height = 88 × `RowH` (A0..C8).
- Max vertical offset = max(0, 88 × `RowH` − viewport height).
- Fit-to-notes row height = clamp(viewport height ÷ (note span + 2), 6, 36).
