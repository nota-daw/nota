# Contract: the `theme` message, with skin geometry (Nota → phone)

Extends [spec 014's theme message](../../014-remote-studio/contracts/theme-message.md).
Every rule there (T1–T5) still holds. Two optional fields join each theme in `th`:

```json
"th": {
  "night-shift": {
    "name": "Night Shift", "dark": true, "c": { "…": "…" },
    "font": "IBM Plex Sans",
    "r": { "card": 4, "group": 4, "field": 3, "cell": 3, "valueBar": 2, "switch": 2, "fine": 1 }
  },
  "studio-dark": { "name": "Studio Dark", "dark": true, "c": { "…": "…" } }
}
```

| Field | Meaning |
|---|---|
| `font` | The skin's typeface family as the theme file names it. Absent → Geist. |
| `r` | Every radius step in whole px, as the desktop draws it under this skin (`SkinGeometry.RadiusOf`). Absent → the design scale (card 9, group 7, field 6, cell 5, valueBar 4, switch 3, fine 2). |

## Rules

- **G1** `font` is present only when the theme names a family; `r` only when the theme's
  radii differ from the design scale. A built-in theme carries neither.
- **G2** `r`, when present, has all seven keys.
- **G3** The phone puts `font` ahead of Geist in its font stack; a family the phone lacks
  falls through to Geist.
- **G4** An older phone ignores both fields; a phone receiving a theme without them wears
  Geist and the design scale (resetting any skin geometry worn before).
- **G5** A geometry change alone (same colours) re-sends the message (T1).
