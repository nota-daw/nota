# Data model: Classic look

Phase 1 of [plan.md](plan.md). Decisions behind each item: [research.md](research.md).

## Look

| Field | Values | Notes |
|---|---|---|
| `Look` | `New` (default), `Classic` | Pure enum in `Nota.Application` |

- **Stored** as `Settings.Look` (string): `""` or `"new"` → New, `"classic"` → Classic;
  anything else reads as New and is never rewritten on read (as `Appearance.Resolve`).
- **Lifecycle**: read at startup before the first window; changed from Preferences →
  Appearance or MCP `set_look`; written by the caller that changed it (as the accent).
- Independent of `AppearanceChoice`; follow-system and per-mode picks are unaffected.

## Clip style

| Field | Values | Notes |
|---|---|---|
| `Settings.ClipStyle` | `""` (follow the look), `"solid"`, `"tinted"` | unknown reads as `""` |

`Looks.ClipStyleOf(Settings)` → `Solid` when stored `"solid"`, or `""` under Classic;
`Tinted` otherwise. A pick in Preferences stores `"solid"`/`"tinted"` and is never cleared by
a look change.

## Classic palette

One per built-in theme id (`studio-dark`, `studio-light`, `ember-graphite`,
`ember-paper`).

| Field | Rule |
|---|---|
| `id`, `name`, `kind` | Equal to the built-in it belongs to |
| `colors` | Every catalogue token (122). The 107 tokens of 0.59.2 equal `v0.59.2`; the 15 added by #296 have classic values (research R7) |

- Format 1 file, embedded, not listed among themes, not selectable on its own.
- Validation: F1–F6 must pass; T3 readability may warn only on pairings made solely of
  0.59.2 tokens at their 0.59.2 values (R8).

## Classic geometry

`SkinGeometry(FontFamily: null, Roundness: 0.4, Steps: Card 3, Group 3, Field 3, Cell 2,
ValueBar 2, Switch 2, Fine 2)` — a constant in `Nota.Application` (`Looks.ClassicGeometry`).

## Skin geometry (extended)

`SkinGeometry` gains `ValuesFamily: string?` (the `font.values` of a format-3 file) and a
`HasRadius: bool` (the file carried a `radius` object, even one equal to the defaults).

## Composition

`Looks.Compose(SkinGeometry skin, Look look) → WornGeometry`:

| Out | Rule |
|---|---|
| `FontFamily` | `skin.FontFamily` (null → Geist) |
| radii | `skin.HasRadius` → skin's roundness and steps; else Classic → classic geometry; else design scale |
| `ValuesFamily` | `skin.ValuesFamily` if installed; else New → Geist (`Font.UI` URI), Classic → bundled Geist Mono |

Colours: `look == Classic && theme.IsBuiltIn` → classic palette for `theme.Id`; else the
theme's own colours. Accent is worn over the result in both looks.

## Text roles

| Role | Resource / API | New | Classic |
|---|---|---|---|
| Words | `Font.UI`, `NotaFonts.Sans*` | Geist | Geist |
| Values and readouts | `Font.Measure`, `NotaFonts.Measure*` | Geist (tabular) | Geist Mono |

Membership of the values role is fixed by 0.59.2 (spec FR-009); text classes
`TextBlock.Mono`, `.Readout`, `.Value`, `.Eyebrow` carry it; `TextBlock.Number` is the
words-role class for numbers added after 0.59.2.
