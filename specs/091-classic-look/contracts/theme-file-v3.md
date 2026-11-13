# Contract: Theme file, format version 3

Extends [087 format 2](../../087-user-skins/contracts/theme-file-v2.md). Format 1 and 2
stay valid unchanged; the built-ins stay format 1.

```json
{
  "formatVersion": 3,
  "id": "night-shift",
  "name": "Night Shift",
  "kind": "dark",
  "font": { "family": "IBM Plex Sans", "values": "IBM Plex Mono" },
  "radius": { "scale": 0.5 },
  "colors": { "...": "every catalogue token" }
}
```

## Rules (changes only)

| # | Rule | On violation |
|---|---|---|
| F1″ | `formatVersion` is `1`, `2` or `3`. | problem |
| V1′ | `font` and `radius` appear only when `formatVersion` ≥ 2; `font.values` only when it is `3`. | problem, names the key |
| V2′ | `font` is an object with optional `family` and (format 3) optional `values`, each a string of 1–100 characters after trimming; at least one present; no other keys. | problem, names `font.family` / `font.values` |
| W1′ | Each named family (`font.family`, `font.values`) is installed. | warning; the look's default is used |

## Meaning

- `font.values`: the typeface for values and readouts (spec FR-009) under both looks.
- Omitted `font.values` → Geist under New, Geist Mono under Classic.
- A `radius` object (any content) means the skin's radii are worn under both looks;
  without one, New wears the design scale and Classic the classic scale.

## Writing

- Nota writes `formatVersion` 3 only when `font.values` is set; else 2 when `font` or
  `radius` is set; else 1 (as before).
