# Research: Choose the accent colour

All decisions were prototyped in [`mockup/build.py`](mockup/build.py), which computes every
value the mockup shows from the four theme files.

## R1 — An overlay on the theme, not more themes

- **Decision**: The accent is applied by a function `(theme colours, accent) → accent tokens`,
  and the app wears the resulting *accented theme*. Theme files stay as they are.
- **Rationale**: 4 themes × 8 accents of hand-tuned files would be 32 files to keep in step;
  a future user-made theme would need its own accent set. One function also gives Nota
  Remote, the theme previews and Fluent's ramp the right values for free.
- **Alternatives**: per-theme accent tables in each JSON (rejected: drift, no custom colour);
  re-tinting only `Accent` (rejected: the tint, line, hover and ramp would stay orange).

## R2 — Derivation (OKLCH)

- **Decision**: For theme `T` with accent `A = (La, Ca, ha)` and a target `(L1, C1, h1)`, every
  token `K` of the accent family with `(Lk, Ck, hk)` becomes
  `(Lk + ΔL, Ck · C1/Ca, hk + (h1 − ha))`, gamut-mapped by reducing chroma (×0.95 steps)
  until in sRGB. `ΔL = L1 − La` for **solid** tokens (`Accent`, `AccentBright`, `AccentHover`,
  `AccentDeep`, `AccentPale`, `SystemAccent*`) and `0` for **blend** tokens (`AccentDim`,
  `AccentEdge`, `AccentMute`, `AccentSubtle`, `BorderBrass`) — washes over the theme's
  surfaces keep the theme's lightness, or a dark custom colour would sink the "picked" tint
  into the ground. `TextOnAccent` = the derived ink on the new `Accent` (dark `#17181A` or
  white, as `NotaPalette.ReadableInkOn`).
- **Presets** take `L1 = La` (the theme's own lightness); a **custom** colour takes its own
  `L1`, so it is worn exactly where it passes.
- **Orange** is the identity: it returns the theme's own values without computing (FR-004,
  SC-003). The prototype confirms the maths is also an identity for all 17 tokens × 4 themes.
- **Rationale**: OKLCH is perceptually even, so keeping L keeps contrast, and hue rotation
  keeps the theme's designed relations (bright/deep/subtle steps).
- **Alternatives**: HSL rotation (rejected: unequal perceived lightness across hues — yellow
  vs blue); mixing the accent over the panel per token (rejected: needs a per-token alpha
  the theme files do not record).

## R3 — The rules a derived accent must pass

Per theme, on the derived values:

| Rule | Pairing | Minimum | Source |
|---|---|---|---|
| T3.4 | `AccentBright` on `BgApp`, `Panel`, `BgSunken` | contrast 2.3 | DESIGN.md bar |
| T3.5 | derived ink on `Accent` | contrast 4.5 | DESIGN.md bar |
| T3.8 | `Accent` vs `Record`, `Mute`, `Solo` | ΔE76 ≥ 15 | DESIGN.md bar |
| A1 | `Accent` vs `Success` (meter green) | ΔE76 ≥ 15 | issue #311 |
| A2 | `Accent` vs `Record`, `Mute`, `Solo`, `Success` | Δa\*b\* ≥ 15 | new |
| A3 (presets only) | `Accent` vs `Track1`–`Track9` | ΔE76 ≥ 15 | issue #311 |

- **A2** is ΔE with lightness left out. Without it a copy of the mute amber passes T3.8 by
  being lighter. Orange's smallest Δa\*b\* today is 16.7 (vs `Mute`, Ember Paper), so every
  theme passes A2 as shipped.
- **Custom**: A1/A2/T3.8 are checked on the **chosen colour itself** (in gamut, before any
  adjustment) *and* on the result, so moving lightness can never talk a colour past the
  record red. Refused when chroma < 0.05 (a grey), when it clashes, or when no lightness
  within ±0.12 of its own passes T3.4/T3.5.
- **Fit**: lightness steps of 0.005, trying +n then −n, nearest first; presets search up to
  ±0.40, custom up to ±0.12.

## R4 — The preset list

- **Decision**: Orange (identity), Lime (C 0.15, h 125°), Aqua (0.12, 185°), Sky (0.13, 224°),
  Violet (0.15, 295°), Magenta (0.16, 330°), Pink (0.15, 355°).
- **Rationale**: spread around the wheel avoiding the four state colours: no yellow (mute),
  red (record), green (meter) or mid blue (solo — Sky at 232° failed A2 in Studio Dark,
  224° passes). All pass every rule incl. A3 in all four themes (mockup matrix §4).
- Values (Accent per theme) are in the mockup and asserted by `RunAccent`.

## R5 — Storage and resolution

- **Decision**: `Settings.Accent` = `""` (Orange) | a preset id | `"custom"`;
  `Settings.CustomAccent` = last custom colour `#RRGGBB` (kept when a preset is chosen,
  offered again by Custom). Unknown id or malformed hex → Orange, nothing rewritten.
- **Alternatives**: storing the hex in `Accent` directly (rejected: loses the remembered
  custom colour when a preset is picked, US2-5).

## R6 — Wearing it

- `NotaThemeService` keeps the accent next to the appearance choice; `ApplyActive` wears
  `NotaAccent.Wear(theme, accent)` (cached per theme id + accent, so `ReferenceEquals` still
  short-circuits a no-op). `Worn(theme)` gives previews and Remote the same objects.
- Startup: `App` resolves the accent with the theme, before the first window paints.
- Remote: `MainWindow.Remote` caches `RemoteTheme` by theme id — key it by the worn object
  instead, and include the accent in the appearance key, so the phone is re-sent the theme
  when the accent changes (the hub already re-sends on a new appearance reference).

## R7 — The picker control

- **Decision**: An Avalonia `Flyout` under the Custom button (as the section rename flyout in
  `ArrangementView.Sections.cs`), holding a `ColourPicker`: a 216 × 132 saturation/value area
  and a 216 × 10 hue strip, each a `WriteableBitmap` filled from HSV maths (rebuilt only
  when the hue changes), handles drawn with `HandleSel` + `Seam`, the before/after chip, a
  hex `TextBox.field` in Geist Mono, the *In themes* strip (4 chips with the derived accent,
  a dot where adjusted) and the note line.
- Live: every drag/valid hex applies (`NotaThemeService.SetAccent`) without saving; closing
  the flyout saves; Esc restores the accent worn before it opened and closes. A refused
  colour is shown in the chip and the note (Danger ink) but not applied.
- **Rationale**: bitmaps keep the "no gradients in views" ban intact (the spectrum is
  content, like a waveform); listed in DESIGN.md's accepted exceptions.
- **Alternatives**: Avalonia's `ColorPicker` (Avalonia.Controls.ColorPicker package — rejected:
  a new dependency, Fluent look, no per-theme preview).

## R8 — Accent vs other colours in views

- Device identity hues (`Ink`), graph chromas, track/return colours, state colours, meters:
  untouched by construction (only the accent family is replaced).
- Existing code that compares against accent brushes by reference (`Inactive.cs`) keeps
  working: slots are the same objects, only their colour changes.
