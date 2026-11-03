# Data model: Per-release splash art

Everything here exists only in memory at launch. Nothing is persisted.

## ReleaseSeed

| Field | Type | Notes |
|---|---|---|
| `Version` | string | `AppInfo.Version`, with `+metadata` already stripped. Fallback `0.0.0`. |
| `Full` | uint | FNV-1a + finaliser over `Version` (research R3) |
| `Line` | uint | The same hash over `"major.minor"`. Used only for the family step (R4). |
| `Patch` | int | Parsed patch number. 0 if `Version` is not semver. |

Validation: a non-semver string still yields a seed, with `Line = Full` and `Patch = 0`.
It never throws.

## SplashScene

| Field | Type | Notes |
|---|---|---|
| `Version` | string | The seed's input, for labels in the review sheet |
| `Layout` | enum `TextLeft / TextRight / TextBottom` | Decides `ArtRect` and `TextRect` |
| `ArtRect` / `TextRect` | rect in the 420×300 card space | `TextLeft`: text 0,0,150,300 · art 150,0,270,300. `TextRight`: mirrored. `TextBottom`: art 0,0,420,196 · text 0,196,420,104. |
| `Family` | enum `Roll / Rings / Strata / Panels / Lissajous / Faders` | `(Line + 5·Patch) mod 6` |
| `Overlay` | Family? | Neutral and quiet, from the allowed pairs (R5) |
| `PaletteMode` | enum `Mono / Analog / Pair / Triad` | |
| `Palette` | int[] | 1–3 indices into `Track1..9` |
| `StrokeWeight` | double | One of {1.5, 2, 2.5, 3} |
| `ColourShare` | double | 0.35–0.85. The share of a family's elements in palette colours: notes in Roll, rings in Rings (the outer one always), faders in Faders (the touched one always), and cells in Panels (half of it). |
| `Wash` | enum `None / Radial / Linear` | Plus the focus point (0..1, 0..1) |
| `Items` | `IReadOnlyList<Prim>` | Drawn in order, clipped to `ArtRect` |

Invariants, all checked by the smoke test:
- every coordinate is finite and within one card size of the card. Motifs may run past the
  art region on purpose (outer rings, a roll's last notes), and the renderer clips them;
- `Items` is not empty;
- at most one live mark uses `Role.Accent`, which is at most 2 items (a playhead's line
  and head, the current step's spoke and ring);
- no item uses a semantic role (none exists in `Role`);
- `Overlay` items use neutral roles only.

## Prim (primitive)

| Kind | Geometry | Paint fields |
|---|---|---|
| `Rect` | x, y, w, h, corner radius | fill, stroke?, strokeWidth |
| `Line` | x1, y1, x2, y2 | stroke, strokeWidth (round caps) |
| `Polyline` | points[] | stroke?, fill?, strokeWidth (round joins) |
| `Polygon` | points[] | fill |
| `Circle` | cx, cy, r | fill?, stroke?, strokeWidth |

## Paint

`Solid(ColorRef)`, or `Linear(stops[], start, end, space)`, or
`Radial(stops[], centre, radius, space)`.

- A stop is `(offset 0..1, ColorRef)`.
- `space` is `Bounds` (relative to the shape) or `Art` (absolute in card space, for strokes
  that run across the whole art).

## ColorRef

| Field | Type | Notes |
|---|---|---|
| `Role` | enum | `Ground` (BgSunken), `App` (BgApp), `Panel`, `Raised` (SurfaceRaised), `Hover` (SurfaceHover), `Seam`, `Rule`, `Grid` (VizGrid), `Faint` (TextFaint), `Tertiary` (TextTertiary), `Ink` (VizInk), `Accent`, `Track` |
| `TrackIndex` | int | 0–8, used when `Role = Track` |
| `TowardBlack` | double 0..1 | Clip title bands, note outlines: 0.3–0.45 |
| `TowardGround` | double 0..1 | Panel gradient ends: 0.7 |
| `Alpha` | double 0..1 | Washes and velocity fades |

Resolution happens at draw time: the role's live `NotaPalette` slot `.Color` (or the
theme JSON value in the review sheet), then the mixes, then the alpha.
