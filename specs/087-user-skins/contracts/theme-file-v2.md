# Contract: Theme file, format version 2

Extends [spec 011's format version 1](../../011-colour-themes/contracts/theme-file.md).
Version 1 stays valid unchanged; the four built-ins stay version 1.

```json
{
  "formatVersion": 2,
  "id": "night-shift",
  "name": "Night Shift",
  "kind": "dark",
  "font": { "family": "IBM Plex Sans" },
  "radius": { "scale": 0.5, "steps": { "card": 4 } },
  "colors": { "BgApp": "#0B0A09", "...": "every catalogue token" }
}
```

## Rules

F1–F6 as in version 1, with F1 changed. F7 (readability) is a **warning** for user themes.
F8 applies to built-in Studio themes only.

| # | Rule | On violation |
|---|---|---|
| F1′ | `formatVersion` is `1` or `2`. | problem |
| V1 | `font` and `radius` appear only when `formatVersion` is `2`. | problem, names the key |
| V2 | `font`, if present, is an object with `family`: a string of 1–100 characters after trimming. No other keys. | problem, names `font.family` |
| V3 | `radius`, if present, is an object with optional `scale` (number, 0 ≤ scale ≤ 2, default 1) and optional `steps` (object). No other keys. | problem, names the key |
| V4 | `radius.steps` keys are among `card`, `group`, `field`, `cell`, `valueBar`, `switch`, `fine`; values are numbers 0–24. | problem, names `radius.steps.<key>` |
| W1 | `font.family` is installed on this system. | warning; Geist is used |
| W2 | Each T3 pairing ([011 ui-contract](../../011-colour-themes/contracts/ui-contract.md) § T3) meets its minimum. | warning per failed pairing: id, tokens, measured, minimum |

Validation reports **every** problem and warning (not the first one). A user theme loads when
it has no problems; warnings never block loading.

## Meaning

- **Typeface**: replaces Geist for all of Nota's own text, at the same sizes and weights.
- **Radius**: step radius = `steps[step]` if given, else `round(design × scale)`. Design values:
  card 9, group 7, field 6, cell 5, valueBar 4, switch 3, fine 2. Docked panels stay 0. Radii
  that are not on the scale are multiplied by `scale`.
- Omitted `font` → Geist. Omitted `radius` → the design scale.

## Files on disk

- Folder: `<Nota data folder>/Themes/` — the same root that `NOTA_DATA_DIR` relocates.
- Only `*.json` files are read, at most 256 KB each, in ordinal file-name order.
- The file name is free; the `id` inside is what Nota stores. Files saved by Nota (Duplicate,
  MCP `save_theme`) are named `<id>.json`, in catalogue key order, 2-space indented.
