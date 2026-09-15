<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Nota design guideline — Ember

Nota's UI runs on **Ember**, one system in two variants:

- **Ember Graphite** (dark, the default) — warm graphite neutrals (hue ~40°) lit by a
  single brass accent. A DAW is used in dark rooms for long sessions, so this is what
  the app ships with.
- **Ember Paper** (light) — the same hue family and the same roles on a warm paper
  ground. Brass darkens to bronze: against paper a mark needs *less* lightness, not
  more, to carry the emphasis it had against graphite.

Preferences → Appearance offers Ember Graphite / Ember Paper / System. The choice applies
live — no restart — and System follows the OS appearance as it changes.

**[→ Open `DESIGN.html`](DESIGN.html) for the visual reference.** That page renders every
swatch, control specimen and card layout at true size in the real palette, and is the
faster way to answer "what should this look like". This file is the text version: the
rules, the token values, and what to do when they disagree.

## Source of truth, in order

| Rank | File | Authority |
|---|---|---|
| 1 | `src/managed/Nota.App/Theme/NotaTheme.axaml` | **Definitive.** Colour, geometry and control styles. |
| 2 | `src/managed/Nota.App/Theme/NotaPalette.cs` | Mirror of (1) for custom-drawn views that cannot resolve XAML resources cheaply. Must stay in sync by hand. |
| 3 | `src/managed/Nota.App/Theme/NotaThemeService.cs` | Applies a variant to both layers and repaints. |
| 4 | `src/managed/Nota.App/DeviceCardKit.cs` | Palette aliases and the atomic builders for device cards. |
| 5 | `DESIGN.html` / this file | Documentation. If these disagree with 1–4, **these are wrong** — fix them. |

**Never hardcode a colour or size in a view.** In XAML use `{DynamicResource Brush.*}` /
`{StaticResource Radius.*}`. In custom-drawn C# read from `NotaPalette` or `DeviceCardKit`.
A literal is not just off-palette — it is stuck in one variant.

## How theming works

The colour tokens in `NotaTheme.axaml` live in `ResourceDictionary.ThemeDictionaries`
under `Dark` and `Light`, so **`{DynamicResource Brush.*}` is mandatory** — a
`StaticResource` cannot follow the variant. Geometry, fonts and control heights are
variant-independent and stay in the shared dictionary as `StaticResource`.

`NotaPalette` serves the custom-drawn layer. Each token is a **slot**: one long-lived
`SolidColorBrush` whose `Color` is re-pointed on a variant change. Views (and the private
`static readonly` fields that alias them) keep the same brush object, so nothing has to be
rebuilt. Three ways to get a colour:

| Call | For | Light value |
|---|---|---|
| `NotaPalette.SurfaceCard` | a named token | authored by hand |
| `NotaPalette.Wash(slot, 0x24)` | a translucent tint of a token | tracks its source slot |
| `NotaPalette.Ink("#D06FB0")` | the identity hue of one visualiser, band or stored tag | derived (same hue, lightness reflected), or pinned in `InkOverrides` |

Two shapes cannot ride a slot and need care:

- **`Color` values.** A `static readonly Color` snapshots the variant. Declare it as an
  expression-bodied property (`static Color X => NotaPalette.Accent.Color;`).
- **Gradients.** `LinearGradientBrush` copies its stop colours. Build one with
  `NotaPalette.VGradient(...)`, and only ever into a `static` field — the registration
  that re-tints it is permanent.

Anything a view derives itself — per-index clip brushes, alpha-blended track fills — must
be dropped on `NotaPalette.Changed`.

## The rules

- **Colour.** Warm graphite neutrals plus one accent, brass. The accent is spent on
  primary actions, selection, focus, active state, and the time-domain data that is the
  point of a DAW — playhead, MIDI notes, waveform. Rec and destructive are danger red.
  Green / amber / red are for status only. Flat solid fills: **no gradients, no textures,
  no background images.**
- **Type.** `Font.UI` (Inter, standing in for Geist) for everything a person reads;
  `Font.Mono` for anything numeric that is compared or aligned — transport position, BPM,
  sample counts, latency. Sentence case everywhere. No emoji.
- **Spacing.** 4px grid. Panels pad 16; cards and track rows pad 10. There are no
  `Space.*` resources — this is convention only.
- **Geometry.** Radii scale with the element: clips 4, chips/wells/checkboxes 5, buttons
  and menus 7, pills 8, cards and panels 10, dialogs 14.
- **Structure over shadows.** Panels separate with 1px `Brush.BorderDefault` lines.
  Shadows belong only to floating layers — menus and dialogs.
- **States.** Hover `Brush.SurfaceHover`; pressed `Brush.SurfaceActive`; checked
  `Brush.AccentSubtle` fill + `Brush.Accent` border + `Brush.AccentBright` text; selected
  `Brush.SurfaceSelected`; focus a 1px `Brush.AccentBright` ring. **No scale or bounce
  transforms.**
- **Motion.** 100–240ms, ease-out, opacity plus ≤6px translate. Motion that delays a click
  is a bug.
- **Copy.** Calm, technical, precise. Imperative for actions, declarative for status
  (`Playing · 128 BPM`). Middle dot `·` as the meta separator. Empty states get one short
  line and one primary action.
- **Icons.** Lucide geometry, stroke ~1.75. No emoji, no unicode-as-icons except `·` and
  the transport glyphs already in use.

## Tokens

### Surfaces

| Key | Graphite | Paper | Use |
|---|---|---|---|
| `Brush.SurfaceAbyss` | `#0A0908` | `#DCD6C5` | Deepest recess — meter troughs, transport console |
| `Brush.BgSunken` | `#100F0D` | `#E4DFD1` | Wells, lanes, graph grounds, chrome |
| `Brush.BgApp` | `#171613` | `#EFEADE` | App ground |
| `Brush.LaneB` | `#191814` | `#E9E4D6` | Lane alternation |
| `Brush.SurfaceCard` | `#1E1C18` | `#F8F5EC` | Panels, cards |
| `Brush.SurfaceRaised` | `#26231E` | `#FFFEF8` | Transport, chips, device cards |
| `Brush.SurfaceHover` | `#2E2B24` | `#E7E1D0` | Hover |
| `Brush.SurfaceActive` | `#332F27` | `#DBD4C0` | Pressed |
| `Brush.SurfaceSelected` | `#24D8A03D` | `#2EA87415` | Selection (accent wash) |

Forward still means lighter in both variants, and a well is still darker than the surface
it sits in. Hover is the one role that flips direction: it lifts on graphite and settles
on paper.

### Text and borders

| Key | Graphite | Paper | Use |
|---|---|---|---|
| `Brush.TextPrimary` | `#E9E4D8` | `#241F17` | Warm off-white / warm near-black |
| `Brush.TextSecondary` | `#A39D8F` | `#5E5849` | Labels, captions |
| `Brush.TextTertiary` | `#6E6A5E` | `#8A8474` | Section labels |
| `Brush.TextDisabled` | `#4A463D` | `#ADA694` | Disabled |
| `Brush.TextOnAccent` | `#171613` | `#FFFBF2` | Ink on the accent fill |
| `Brush.BorderDefault` | `#2C2923` | `#D5CEBB` | Panel separation |
| `Brush.BorderStrong` | `#3A362D` | `#BDB5A0` | Control outlines |
| `Brush.GridBeat` | `#1F1D18` | `#DED8C8` | Beat lines |
| `Brush.GridBar` | `#2A2721` | `#CFC8B4` | Bar lines |

### Accent and semantic

| Key | Graphite | Paper | Use |
|---|---|---|---|
| `Brush.Accent` | `#D8A03D` | `#A87415` | Brass / bronze — the audio path |
| `Brush.AccentHover` | `#E8B24C` | `#8F6110` | Hover on solid fills |
| `Brush.AccentBright` | `#F0C060` | `#744D07` | Focus ring, playhead, selected note |
| `Brush.AccentSubtle` | `#24D8A03D` | `#24A87415` | Toggle-on wash |
| `Brush.Success` | `#58B368` | `#2C7A3E` | Signal present, Session play |
| `Brush.Warning` | `#D9C34C` | `#837010` | Meter caution zone |
| `Brush.Danger` | `#D95F4C` | `#B03A28` | Record, destructive |
| `Brush.DangerHover` | `#E4715F` | `#C64A36` | Hover |

The accent ramp reads "more prominent" downward on paper and upward on graphite:
`AccentBright` is the hottest mark in both, which means the *lightest* on graphite and the
*darkest* on paper.

Fluent's `SystemAccentColor` and its six ramps are retinted to brass, so stock Avalonia
controls match instead of shipping default blue.

### Geometry

`Radius.Clip` 4 · `Radius.Sm` 5 · `Radius.Md` 7 · `Radius.Pill` 8 · `Radius.Lg` 10 ·
`Radius.Xl` 14
`Control.Sm` 22 · `Control.Md` 26 · `Control.Lg` 28

### Track palette

Assigned round-robin, muted and equal-weight so no track outranks another. A track inside
a group takes its group's hue instead, varied across the three shades so siblings stay
apart inside one family; an explicitly coloured track keeps its own colour. Paper keeps
the eight hues and darkens them, so a clip's full-strength content still reads over a 16%
fill of itself:

| | rust | amber | olive | sage | teal | slate | mauve | rose |
|---|---|---|---|---|---|---|---|---|
| Graphite | `#C4756A` | `#C99C55` | `#9BA65D` | `#6FA383` | `#5B9E9C` | `#6D8FB5` | `#9B7FA6` | `#B57286` |
| Paper | `#9A4A3E` | `#97682A` | `#646E32` | `#42765A` | `#2F7472` | `#3E6288` | `#6A5176` | `#86465B` |

Returns `#7C88A0` / `#A08A7C` (paper `#4D5972` / `#705B4E`); master reuses the accent.

Track 5 (teal, `#5B9E9C`) doubles as the **modulation accent** — see below.

## Text roles

| Class | Spec | Colour |
|---|---|---|
| `TextBlock.Heading` | 13px SemiBold | TextPrimary |
| default control text | 11px Medium | TextPrimary |
| `TextBlock.Caption` | 10px | TextSecondary |
| `TextBlock.SectionLabel` | 10px Bold | TextTertiary |
| `TextBlock.Mono` | `Font.Mono` | — |

Inside device cards the scale is tighter: knob captions 8px uppercase tertiary, knob
values 8px mono, card titles 11px SemiBold.

## Device cards

Every built-in device and instrument editor is a card of fixed height **260px**
(`DeviceCardKit.CardH`). Width is declared per kind by the body strategy.

**Vertical budget:** header 26 · LIVE strip 34 · body 200.

- **Header (26px)** — power dot, device name (11px SemiBold), type tag (9px bold
  tertiary, .08em tracking), preset selector, A/B toggle, then right-aligned CPU %,
  stereo meter, peak dB, close and drag glyphs.
- **LIVE strip (34px)** — the two or three parameters worth reaching for mid-take, as
  inline 3px sliders with an 8×9 cap and a mono value.
- **Body (200px)** — optional 80px tab rail, main area, optional 100–150px output rail.

**Frame:** `Brush.SurfaceRaised` fill, 1px `Brush.BorderDefault`, radius 7, clipped.
Header has a 1px bottom border and 8px horizontal pad. Body inset is 10px unless the
strategy sets `FullBleed`.

### The two-colour rule

This carries the most meaning and is the easiest to get wrong:

- **Brass `Brush.Accent` = the audio path.** Anything the signal passes through — gain,
  frequency, drive, mix. On a knob: `Accent = true`.
- **Teal `NotaPalette.Teal` = modulation and detection.** Anything that *controls* rather than
  carries the signal — LFOs, envelopes, sidechain detectors, followers. Pass it as the
  knob's `ArcColor`, and group those parameters behind a 2px teal left border with a teal
  section label.
- **Output rail stays muted.** End-of-chain trim and pan fill with `Brush.BorderStrong`,
  not brass — housekeeping, not the sound.
- **Active tab takes a 2px left border** — brass for signal pages, teal for modulation
  pages.

## Control kit

Avalonia's stock controls are too chunky at this density, so the card layer is
custom-drawn. Builders live in `DeviceCardKit`; controls in `Controls/`.

### Interaction contract

Every value control behaves identically — match this exactly when adding one:

- **Vertical drag**, up increases; full range over ~140px.
- **Double-click** restores `Default` (`NaN` disables).
- **Left button only** — right-click bubbles so the CV-modulate menu still opens.
- **`GestureBegin` / `GestureEnd`** bracket `BeginAutomationWrite` / `EndAutomationWrite`,
  so a move made while playing is recorded.
- **`MidiLearn.Bind`** registers the control with its display name.
- **Live follow** re-reads engine state each UI tick and **skips while `Dragging`**, so
  automation never fights the hand.
- **Numeric readouts are mono**, so width does not jump as digits change.

### Inventory

| Control | Size | Role |
|---|---|---|
| `Knob` | 38×38, 270° from 135° | The workhorse. Sunken groove, value arc, pointer at 0.66 r. |
| `KnobCell` | 58 wide | Knob + 8px mono value + 8px uppercase caption. Under 52 wide both drop to 7px. |
| `ValueBar` | h 18, min-w 46 | Sunken field, value text over a horizontal fill. |
| `MiniFader` | h 12, min-w 40 | 3px track, 8×9 cap. For the 64px track header. |
| `VFader` | w 30, 0–1.5 | Mixer strip fader. |
| `PanKnob` | 26×26, −1…1 | Radial pan, 1.5px ring, 2px indicator. |
| `PanBar` | bipolar | Track-header pan, fills centre-out, reads `50L … C … 50R`. |
| `DragNumber` | mono | Drag to nudge, double-click to type, commits on Enter/blur. |
| `MeterBar` | v w10 / h 12×80 | Peak+RMS, ~30 Hz, −60…0 dB, green/amber/red, peak-hold tick. |
| `StereoMeter` | two 3px bars | L over R, output rail and card header. |
| `GrMeter` | hangs from top | Gain reduction — more compression reads as more bar. |
| `MixBar` | h 6 | Proportional source levels as amber segments. |
| `ChipRow` | radius 4, 9px | Single-select; on = brass fill + `TextOnAccent`. |
| `Labeled` | 8px uppercase | Centred caption above any control. |
| `TextButton` | radius 5, 11px | In-card action. |
| `BypassTag` | 8px bold | The `BYPASSED` marker. |
| `NaBadge` | h 16, radius 8 | `N/A` / `M7+` — feature not built. The control stays visible but disabled so nothing shifts later. |
| `Glyph` / `Hint` | 10px / 11px | Clickable text glyph; quiet inline hint. |

## Visualisers

~40 custom-drawn views in `Controls/`. The rule: **a device draws the thing it does**, not
a generic graph. Shared ground is `Brush.BgSunken` with a `NotaPalette.GraphBorder` inner border.

Families: transfer curves (`AmpCurve`, `CompTransfer`, `VintageViz`) · frequency response
(`AutoFilterCurve`, `DynamicEqCurve`, `EqCurve`) · spectra (`AmpHarmonics`,
`OperatorSpectrumViz`) · scrolling history (`AutoGainViz`, `CompGrHistory`,
`CeilingScope`) · waveform (`GrainWaveViz`, `StrataWave`, `AuroraStack`) · grids
(`ArpGrid`, `VoltMatrix`, `DelayTaps`) · envelopes (`SynthViz`, `ReverbTail`) ·
instrument-as-itself (`PendulumViz`, `FluxVectorPad`, `ChordKeysViz`) · icons
(`WaveIcon`, `GrainShapeIcon`).

Where possible these are **real**: the reverb tail is an actual decaying impulse response,
and the amp harmonics come from pushing a unit sine through the same waveshaper the audio
takes — not a drawing that resembles one.

## Layout skeleton

`36px title bar → 60px transport → body (on Brush.BgApp) → 22px status bar.`
Panels are `Brush.SurfaceCard` with a 1px bottom border.

The transport is **one row**, not the transport + toolbar pair it used to be. It reads
left to right as which view · what plays · the numbers you set · the switches you flip ·
then, pinned right, what the machine is doing. Transport, position and loop share a
single `Border.console` recess on `Brush.SurfaceAbyss` so playback reads as one object;
tempo, signature, grid and launch quantize use one `.cell` shape — mono value over an 8px
`.CellLabel` — so the row scans as a strip of readouts rather than a queue of pills.
The four switches — metronome, follow playhead, snap, automation — are icon-only 28px
`tp-icon` toggles that draw the thing they do (a metronome, a playhead on its ruler, a
horseshoe magnet, a breakpoint envelope) and carry their name in a tooltip; none sets a Foreground, so the engaged state turns the
glyph brass through the base `ToggleButton:checked`. Controls that belong to one context
appear only there: launch quantize in Session, "Re-enable" only while a lane is
overridden.

The body holds **islands**: the browser, the arrangement and the modular canvas are
`Radius.Md` cards with a 1px `Brush.BorderDefault` edge, clipped to their bounds, floating
on `Brush.BgApp` with an 8px gutter. The splitter between two islands carries no line of
its own — it is a 2px transparent grab strip inside that gutter.

Arrangement rows are not one pitch. A **track** row is 64px and its header carries every
control (name + kind · mute/solo/arm + input · fader + dB + pan, with a level rail on the
right edge). A **group** row is a 26px titled bar — disclosure, name, mute/solo, a 4px
level rail, kind tag — and opens to a full 64px row only while it is the selected track.
Header column 228px. Every y↔row conversion goes through `ArrangementView.RowTop` /
`RowAtY`; nothing multiplies by a row constant.

A **clip** is a tinted body (`Radius.Clip`) under a 2px band in the track colour, with the
waveform or notes across its full height. The name is drawn over the body — not in a strip
of its own — and only where a run of clips begins.

Transport buttons set `IsTabStop` and `Focusable` to false, so global hotkeys (Space, R,
L, Return) always reach the window instead of a focused control.

## Known drift

Things that are true today and should not surprise you:

- **The hex literals are gone.** The custom-drawn layer used to carry ~970 of them; they
  now route through `NotaPalette` (`#1B1916` → `SurfaceInset`, `#221F1A` → `GraphBorder`,
  and so on). What remains as a literal is *data*: the tag-colour swatches in
  `TagEditorWindow`, the layer hues in `StrataDeviceBody`, the kit-voice dots in
  `RhythmInstrumentCard` — each resolved through `NotaPalette.Ink()` at use.
- **Ink light values are derived, not authored.** A one-off device hue gets its paper
  counterpart from a lightness reflection. Most land well; pin the ones that don't in
  `NotaPalette.InkOverrides` rather than reaching for a literal.
- **Black stays black.** Drop shadows and the black-key row tint are alpha-over-black in
  both variants — that is correct, not drift.
- **The title bar is custom.** A 36px frameless bar (`Brush.ChromeBg`) with the macOS
  traffic lights in a left inset and the document name centred; Windows reserves 180px on
  the right for the native caption buttons.
- **Font substitution.** Mockups specify Geist / Geist Mono; the app ships Inter and the
  Cascadia → Menlo → Consolas stack, so weights sit slightly differently.
- **No `Space.*` tokens.** The 4px grid is honoured by convention only.
- **Two palette files** must be edited together and nothing enforces it — and each token
  now carries two values, so a change is four edits (`NotaTheme.axaml` Dark + Light,
  `NotaPalette.cs`, this file).
