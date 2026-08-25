<!-- SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms. -->

# Nota design guideline — Ember Graphite

Nota's UI runs on **Ember Graphite**: warm graphite neutrals (hue ~40°) lit by a single
brass accent. It is dark-only by intent — a DAW is used in dark rooms for long sessions.

**[→ Open `DESIGN.html`](DESIGN.html) for the visual reference.** That page renders every
swatch, control specimen and card layout at true size in the real palette, and is the
faster way to answer "what should this look like". This file is the text version: the
rules, the token values, and what to do when they disagree.

## Source of truth, in order

| Rank | File | Authority |
|---|---|---|
| 1 | `src/managed/Nota.App/Theme/NotaTheme.axaml` | **Definitive.** Colour, geometry and control styles. |
| 2 | `src/managed/Nota.App/Theme/NotaPalette.cs` | Mirror of (1) for custom-drawn views that cannot resolve XAML resources cheaply. Must stay in sync by hand. |
| 3 | `src/managed/Nota.App/DeviceCardKit.cs` | Palette aliases and the atomic builders for device cards. |
| 4 | `DESIGN.html` / this file | Documentation. If these disagree with 1–3, **these are wrong** — fix them. |

**Never hardcode a colour or size in a view.** In XAML use `{DynamicResource Brush.*}` /
`{StaticResource Radius.*}`. In custom-drawn C# read from `NotaPalette` or `DeviceCardKit`.

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

| Key | Value | Use |
|---|---|---|
| `Brush.BgSunken` | `#100F0D` | Wells, lanes, graph grounds, chrome |
| `Brush.BgApp` | `#171613` | App ground |
| `Brush.LaneB` | `#191814` | Lane alternation |
| `Brush.SurfaceCard` | `#1E1C18` | Panels, cards |
| `Brush.SurfaceRaised` | `#26231E` | Transport, chips, device cards |
| `Brush.SurfaceHover` | `#2E2B24` | Hover |
| `Brush.SurfaceActive` | `#332F27` | Pressed |
| `Brush.SurfaceSelected` | `#24D8A03D` | Selection (14% brass) |

### Text and borders

| Key | Value | Use |
|---|---|---|
| `Brush.TextPrimary` | `#E9E4D8` | Warm off-white |
| `Brush.TextSecondary` | `#A39D8F` | Labels, captions |
| `Brush.TextTertiary` | `#6E6A5E` | Section labels |
| `Brush.TextDisabled` | `#4A463D` | Disabled |
| `Brush.TextOnAccent` | `#171613` | Ink on brass |
| `Brush.BorderDefault` | `#2C2923` | Panel separation |
| `Brush.BorderStrong` | `#3A362D` | Control outlines |
| `Brush.GridBeat` | `#1F1D18` | Beat lines |
| `Brush.GridBar` | `#2A2721` | Bar lines |

### Accent and semantic

| Key | Value | Use |
|---|---|---|
| `Brush.Accent` | `#D8A03D` | Brass — the audio path |
| `Brush.AccentHover` | `#E8B24C` | Hover on solid fills |
| `Brush.AccentBright` | `#F0C060` | Focus ring, playhead, selected note |
| `Brush.AccentSubtle` | `#24D8A03D` | Toggle-on wash |
| `Brush.Success` | `#58B368` | Signal present, Session play |
| `Brush.Warning` | `#D9C34C` | Meter caution zone |
| `Brush.Danger` | `#D95F4C` | Record, destructive |
| `Brush.DangerHover` | `#E4715F` | Hover |

Fluent's `SystemAccentColor` and its six ramps are retinted to brass, so stock Avalonia
controls match instead of shipping default blue.

### Geometry

`Radius.Clip` 4 · `Radius.Sm` 5 · `Radius.Md` 7 · `Radius.Pill` 8 · `Radius.Lg` 10 ·
`Radius.Xl` 14
`Control.Sm` 22 · `Control.Md` 26 · `Control.Lg` 28

### Track palette

Assigned round-robin, muted and equal-weight so no track outranks another:
`#C4756A` rust · `#C99C55` amber · `#9BA65D` olive · `#6FA383` sage · `#5B9E9C` teal ·
`#6D8FB5` slate · `#9B7FA6` mauve · `#B57286` rose.
Returns `#7C88A0` / `#A08A7C`; master reuses brass.

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

- **Brass `#D8A03D` = the audio path.** Anything the signal passes through — gain,
  frequency, drive, mix. On a knob: `Accent = true`.
- **Teal `#5B9E9C` = modulation and detection.** Anything that *controls* rather than
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
a generic graph. Shared ground is `Brush.BgSunken` with a `#221F1A` inner border.

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

`Transport bar → toolbar → track area (scrolls, on Brush.BgApp) → 26px status bar.`
Panels are `Brush.SurfaceCard` with a 1px bottom border. Track rows are cards: radius 10,
1px border, 10px pad.

Transport buttons set `IsTabStop` and `Focusable` to false, so global hotkeys (Space, R,
L, Return) always reach the window instead of a focused control.

## Known drift

Things that are true today and should not surprise you:

- **628 hardcoded hex literals** across `DeviceCards/` and `Controls/`. The "never
  hardcode" rule holds in XAML but has largely not held in the custom-drawn layer.
- **`#1B1916`** (rail background, 36 files) and **`#221F1A`** (graph border, 22 files)
  behave like tokens but are declared nowhere. They should be promoted.
- **`AccentSubtle` disagrees with itself** — `#24D8A03D` in the theme, `#28D8A03D` in
  `DeviceCardKit.AccentSubtleB`.
- **No custom title bar.** The OS title bar is still in use; the status bar carries the
  chrome identity.
- **Font substitution.** Mockups specify Geist / Geist Mono; the app ships Inter and the
  Cascadia → Menlo → Consolas stack, so weights sit slightly differently.
- **No `Space.*` tokens.** The 4px grid is honoured by convention only.
- **Two palette files** must be edited together and nothing enforces it.
