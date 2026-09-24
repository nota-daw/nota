<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Nota design guideline — Ember

Nota's UI runs on **Ember**: warm neutrals (hue ~40°) lit by exactly one accent, brass.
It comes in two variants of one system:

- **Ember Graphite** (dark, the default) — a DAW is used in dark rooms for long sessions.
- **Ember Paper** (light) — the same roles on a warm paper ground. Brass darkens to
  bronze: against paper a mark needs *less* lightness to carry the emphasis it had
  against graphite.

Preferences → Appearance offers Graphite / Paper / System; the switch is live.

The system comes from the **Nota Design Almanac** (`Nota.html` in the sibling
`nota-design` folder, next to the mockups — it stays a design artifact there, not a second
requirement in this repo), which was distilled from the accepted designs — Consort, Pentad, Chamber, and
the Browser / Arrangement / Transport shell. The almanac describes Graphite only; this
file is the guideline the app is held to: the almanac's rules as implemented, the Paper
values, and the few places where the app deliberately departs from the almanac
(§ Accepted departures).

**[→ Open `DESIGN.html`](DESIGN.html)** for the visual reference — every swatch, the
control specimens and the card anatomy at true size, with a Graphite / Paper toggle.

## Source of truth, in order

| Rank | File | Authority |
|---|---|---|
| 1 | `src/managed/Nota.App/Theme/NotaTheme.axaml` | **Definitive.** Colour (both variants), geometry, type classes, control styles. |
| 2 | `Theme/NotaPalette.cs` · `NotaGeometry.cs` · `NotaFonts.cs` · `NotaNum.cs` | The C# side for custom-drawn views: colour slots, radii/sizes/spacing, typefaces and type scale, number setting. |
| 3 | `DeviceCardKit.cs`, `Controls/` | The shared controls and card builders. |
| 4 | `tests/Nota.SmokeTest/DesignTokenCheck.cs` | Enforces (1) ↔ (2) sync and the bans below. |
| 5 | `DESIGN.html` / this file | Documentation. If these disagree with 1–4, **these are wrong** — fix them. |

## How theming works

Colour tokens live in `NotaTheme.axaml` under `ThemeDictionaries` → `Dark` / `Light`, so
XAML **must** use `{DynamicResource Brush.*}`; a `StaticResource` cannot follow the
variant. Geometry, fonts and sizes are variant-independent (`StaticResource`).

`NotaPalette` serves the custom-drawn layer. Every token is a **slot**: one long-lived
`SolidColorBrush` whose `Color` is re-pointed when the variant changes, so views keep the
same brush object and nothing is rebuilt. Ways to get a colour:

| Call | For |
|---|---|
| `NotaPalette.Accent` (any named slot) | a token |
| `NotaPalette.Wash(slot, 0x28)` | a translucent tint of a token; cached, follows its slot |
| `NotaPalette.Ink("#6D8FB5")` | a one-off device hue or stored tag colour. A hex equal to a role token returns that token; any other gets a derived Paper value, or one pinned in `InkOverrides` |
| `NotaPalette.Derived(() => …)` | a colour a view computes from slots; register once and cache it |
| `ArrangementView.TrackBrush(index)` | a track colour |

**Nothing may snapshot a colour.** `new SolidColorBrush(slot.Color)`, a
`static readonly Color` taken from the palette, or a static array of palette colours all
freeze a view in the variant it was built in. Use the slot, a `Wash`, a property
(`static Color X => NotaPalette.Accent.Color;`), or read `.Color` inside `Render`.

### Changing a token

1. Edit **both** branches in `NotaTheme.axaml` — `Dark` and `Light`.
2. Mirror the pair into the `NotaPalette` slot (the smoke test fails until you do).
3. Run `python3 scripts/design-html.py` to regenerate `DESIGN.html`, and update the table here.
4. Look at it in both variants — the app (Preferences → Appearance) and `DESIGN.html`
   (the toggle in its masthead, or `DESIGN.html?variant=paper`).

## The rules

1. **One accent.** Brass means exactly "active, selected, changed". Everything else is a
   surface, text or semantics. There is no second accent.
2. **Semantics only where they apply.** Red belongs to *active recording* (`Record`) and to
   overload (`Danger`/Alert). Green and yellow are meter zones and live signal.
3. **Role chromas live only inside graphs** — never on buttons, never as state.
4. **Flat material.** No gradients, no glow, no textures, no skeuomorphism. Depth is
   lightness plus a hairline; three levels at most.
5. **Two typefaces, one split.** Geist for names and words; Geist Mono for anything that is
   a measurement. Nothing smaller than 7 px.
6. **One implementation per control**, differing only in size. A control's value is
   always visible next to it — never only in a tooltip.
7. **State changes colour, never size, border width or position.** Nothing moves on hover.
8. **Real time is not animated.** Meters, playhead and spectra jump to the value.
9. **Muted is the next Ink step, not opacity.** Opacity is not a state.
10. **Numbers are set, not printed:** point decimal, U+2212 minus, thin space before the
    unit, fixed precision.

## Tokens

Values are Graphite / Paper. Paper values were derived from Graphite by lightness
reflection and pinned by hand where the reflection read poorly.

### Surfaces — nine steps, far to near

| Key | Almanac | Graphite | Paper | Use |
|---|---|---|---|---|
| `Brush.SurfaceAbyss` | Void | `#0A0908` | `#D8D1BE` | Deepest recess — the transport strip |
| `Brush.BgApp` | App | `#0B0A09` | `#DCD6C5` | Window ground, plugin body |
| `Brush.Gutter` | Gutter | `#0C0B09` | `#E0DAC9` | The gaps panels float in |
| `Brush.BgSunken` | Well | `#100F0D` | `#E4DFD1` | Graph windows, fields, slider tracks (`ChromeBg`, `SurfaceActive` alias it) |
| `Brush.Panel` | Panel | `#141310` | `#EAE5D7` | Browser, headers, inspector; disabled button |
| `Brush.SurfaceCard` | Card | `#171613` | `#EFEADE` | A section inside a device |
| `Brush.SurfaceRaised` | Raised | `#1C1A16` | `#F8F5EC` | Button at rest |
| `Brush.SurfaceHover` | Hover | `#252219` | `#E7E1D0` | Hovered button or row |
| `Brush.TrackOff` | Track off | `#26231E` | `#DBD4C0` | Track of an off switch |
| `Brush.LaneB` | — | `#131210` | `#E7E2D4` | Alternate arrangement lane |
| `Brush.SurfaceSelected` / `AccentSubtle` | Brass Wash | `#241F17` | `#F2E4C4` | Selected row, engaged button ground |

### Lines

| Key | Almanac | Graphite | Paper | Use |
|---|---|---|---|---|
| `Brush.Hairline` (`NotaPalette.GraphBorder`) | Hairline | `#221F1A` | `#D5CFBE` | Row dividers, graph frame |
| `Brush.BorderDefault` | Border | `#2C2923` | `#D5CEBB` | Panel, button, field |
| `Brush.BorderStrong` | Border strong | `#3A362D` | `#BDB5A0` | Knob cap, hovered border, inactive fill |
| `Brush.BorderBrass` | Border brass | `#6B5326` | `#C9A254` | Engaged button, focus |
| `Brush.GridBeat` | Grid | `#1E1C18` | `#DED8C8` | Grid inside graphs, zero axis |
| `Brush.GridBar` | — | `#2A2721` | `#CFC8B4` | Bar lines |

### Ink — eight steps, strictly by importance

| Key | Step | Graphite | Paper | Use |
|---|---|---|---|---|
| `Brush.TextHeading` | Ink 0 | `#F2EDE1` | `#1A150E` | Page title, project name. Rare. |
| `Brush.TextPrimary` | Ink 1 | `#E9E4D8` | `#241F17` | Device, track, preset names; values |
| `Brush.TextStrong` | Ink 2 | `#C7C0B0` | `#3E382C` | Button text, transport readouts |
| `Brush.TextSecondary` | Ink 3 | `#A39D8F` | `#5E5849` | Inactive but readable, metadata |
| `Brush.TextMuted` | Ink 4 | `#8D8779` | `#746D5C` | Explanations, units, hints |
| `Brush.TextTertiary` | Ink 5 | `#6E6A5E` | `#8A8474` | Caps labels over parameters; empty states |
| `Brush.TextDisabled` | Ink 6 | `#55514A` | `#9E9786` | Placeholder, disabled action |
| `Brush.TextAxis` | Ink 7 | `#4A463D` | `#ADA694` | Axis labels inside graphs only |
| `Brush.TextOnAccent` | — | `#171613` | `#FFFBF2` | Text on solid brass |

### Brass

| Key | Almanac | Graphite | Paper | Use |
|---|---|---|---|---|
| `Brush.Accent` | Brass | `#D8A03D` | `#A87415` | Arc, fill, playhead, the one solid button |
| `Brush.AccentBright` | Brass Light | `#F0C060` | `#744D07` | Knob pointer, modified label, handle |
| `Brush.AccentHover` | Brass Hover | `#E9BE6A` | `#8F6110` | Engaged text, brass link hover |
| `Brush.AccentDeep` | Brass Deep | `#B08536` | `#7A5008` | Pressed |
| `Brush.AccentDim` | Brass Dim | `#8A6B2E` | `#9C8034` | Eyebrows, mono section marks |
| `Brush.AccentEdge` | Brass Edge | `#5E4A22` | `#D3B173` | Brass chip border |

`AccentBright` is the hottest mark in both variants — the lightest on graphite, the darkest
on paper. Fluent's `SystemAccentColor` ramps are retinted to brass.

### Semantics

| Key | Almanac | Graphite | Paper | Use |
|---|---|---|---|---|
| `Brush.Record` | Record | `#C25B44` | `#B54A32` | Active recording — the only claim on red |
| `Brush.RecordInk` | — | `#F4E3DC` | `#FFF4EE` | The disc on an engaged record button |
| `Brush.Danger` | Alert | `#C2554A` | `#B13E33` | Overload, clipping |
| `Brush.DangerBright` | Alert Light | `#E08A72` | `#A3391B` | Overload readout text |
| `Brush.Warning` | Caution | `#D9C34C` | `#837010` | Meter −6…0 dB |
| `Brush.Success` | Signal | `#58B368` | `#2C7A3E` | Working meter zone, live signal |
| `Brush.SuccessDim` | Signal Dim | `#7FB069` | `#609548` | Transport input, metronome |

### Role chromas — inside graphs only

When two to four sources share a graph, each takes a chroma in this fixed order
(`NotaGraph.Chroma(n)`): **Brass** (primary: left channel, mid band, main signal) ·
**Teal** `#5B9E9C` / `#2F7472` (lows, early reflections, input, modulation) · **Rose**
`#B57286` / `#86465B` (right channel, highs, tail) · **Steel** `#6D8FB5` / `#3E6288` (rare).
Light pairs for text: `ChromaTealLight` `#7FC0BE`, `ChromaRoseLight` `#D79BAB`.

A modulated knob's arc takes the chroma of its source; its label stays neutral.

### Track palette — nine roles

| Drums | Perc | Bass | Keys | Texture | FX | Brass | Vox | Return |
|---|---|---|---|---|---|---|---|---|
| `#58B368` | `#4E9E7A` | `#3E8E8E` | `#5AA0B8` | `#C77F55` | `#7A6FB0` | `#B05A7A` | `#9AA64A` | `#7FA88E` |
| `#328140` | `#306F53` | `#256464` | `#317187` | `#985127` | `#4B4082` | `#7F3450` | `#6C752C` | `#4D7A5D` |

One lightness for all, so no track is louder than another. Each role has three shades
(base · light · dark) that tracks inside a group step through. Return buses draw from the
ninth role (`ReturnA/B`); master is brass. Old projects keep their stored index, which
now points at the new hue.

### Geometry

| Radius (`Radius.*` / `NotaRadius`) | px | For |
|---|---|---|
| `Bar` | 1 | Spectrum bars, stop square |
| `Clip` | 2 | Clip, meter, slider track |
| `Badge` | 3 | Small segment, badge in a device |
| `Control` | 4 | Graph window, button, dropdown |
| `Tile` | 5 | Transport button, search, tile |
| `Panel` | 6 | Panel, device section, transport module |
| `Body` | 8 | Device body, window frame |
| `Pill` | ½ height | Filter chip, switch |

| Size (`Control.*` / `NotaSize`) | px |
|---|---|
| Device card | 700 × 260 |
| Transport strip (`Console`) | 42 · buttons 34 · Play 46 |
| Shell button / field (`Shell`) | 26 |
| Tab segment (`Seg`) in container (`SegGroup`) | 24 in 30 |
| Filter chip (`Chip`) | 20 |
| Switch | 18 × 10, knob 7, inset 1.5 |
| Knob (`KnobSecondary` / `KnobRegular` / `KnobMain`) | 34 / 36 / 44 |
| Parameter cell (`ParamCell`) | 53 tall |
| Slider track | 3, handle 6 × 7 |
| Meter | 11 wide, 5 between channels |
| List row | 26 |

| Spacing (`Space.*` / `NotaSpace`) | px |
|---|---|
| In a device: hair · gap · inset · section inset | 2 · 5 · 6 · 8 |
| In the shell: tile · gutter between panels · inside a panel | 8 · 12 · 20 |

**Depth, exactly three levels:** sunken — Well, Hairline, `Shadow.Sunken` (inner top line
`#80000000`) · flat — Card, Border, no shadow · raised — Raised, Border, `Shadow.Raised`
(inner top line `#08FFFFFF`). No drop shadows except a floating plugin window.

## Type

`Font.UI` = **Geist** 400/500/600/700, `Font.Mono` = **Geist Mono** 400/500, both bundled
(`assets/fonts`, SIL OFL). `NotaFonts` holds the typefaces for custom drawing — never name
a font family in a view. Geist lacks the thin space; it falls back to the system font.

**Shell scale** (`NotaType`, `TextBlock.*` classes)

| Class | Spec | For |
|---|---|---|
| `Title` | 600 · 26 | Section title |
| `Heading` | 600 · 13 | Project name in the header |
| `Name` | 500 · 12 | Track, preset, file name |
| default | 500 · 11 | Buttons, tabs, chips |
| `Caption` | 400 · 11, Ink 4 | Explanation, hint |
| `SectionLabel` / `GroupLabel` | 700 · 10 caps, .14em | Panel group header |
| `Readout` | Mono 500 · 13 | Position and tempo |
| `Value` | Mono 400 · 10 | Row values, metadata |
| `Eyebrow` | Mono 500 · 9 caps, .18em | Eyebrow, format badge |

**Device scale**

| Class | Spec | For |
|---|---|---|
| `DeviceName` | 600 · 12 | Device name in the header |
| `DeviceSection` | 700 · 9 caps, .12em | Section title |
| `RowLabel` | 700 · 8 caps, .1em | Slider-row label |
| `KnobLabel` | 700 · 7 caps, .08em | Label under a knob |
| `KnobValue` | Mono 400 · 7 | Parameter value |
| `Axis` | Mono 400 · 7 | Axis label inside a graph |

### Numbers

`NotaNum.Install()` (called in `App.Initialize`) makes the display culture the default:
invariant with U+2212 as the negative sign — so every implicit `{v:0.0}` prints `−3.3`
with a point on any OS locale; typed input still accepts a hyphen. Project and preset
files are JSON and never go through it.

- The unit follows a **thin space**: `4.6 s`, `−3.3 dB`, `72 %` (`\u2009` in source).
- A plus only where the sign matters: `+0.8 dB`.
- Fixed precision: dB one decimal · tempo two (`120.00`) · percent whole · Hz whole up to
  999, then `3.2 k`. Helpers: `NotaNum.Db`, `Hz`, `Pct`, `Bpm`, `Time`, `Unit`, `F`.
- Width must not jump across 10 or 100 — mono plus fixed precision.
- Axis labels in graphs and clip lengths in beats may use variable precision.

### Labels and copy

- Parameter labels: English caps, one word — `DECAY`, `PRE-DELAY`, `FEEDBACK`. No colons,
  no "Value", no vowel-dropped contractions (`FDBK`, `ATK`, `LVL`). The established
  truncations `FREQ`, `RESO`, `THRESH` are allowed: the full words do not fit a 53 px cell.
- Shell tooltips: short and verb-first — "Snap clips to the grid (hold Alt to drag
  freely)", "Clear the peak hold". Not "Click to…". The value stays on the control;
  a tooltip may add the unit or range.
- Browser category tags lowercase (`bass`, `analog`; acronyms stay `EQ`, `LFO`); the
  processing-type badge is mono caps (`CONVOLUTION`, `DYNAMICS`).
- Preset names describe a place or material (`Stone Vault`, `Tape Glue`), not an emotion.
  Device names are one word from the musical vocabulary (`Chamber`, `Prism`).
- Middle dot `·` separates meta. Sentence case. No emoji, no exclamation marks.

## Controls

Builders live in `DeviceCardKit`; controls in `Controls/`. Reuse before writing anything.

### Interaction contract

- **Vertical drag**, up increases, full range over ~140 px; Shift (or Ctrl/⌘) is fine.
  Cursor `ns-resize`. A click never jumps the value. No horizontal drag anywhere.
- **Double-click** restores the default where one is supplied.
- **Left button only** — right-click bubbles to the CV-modulate / MIDI Learn menu.
- **`GestureBegin` / `GestureEnd`** bracket `BeginAutomationWrite` / `EndAutomationWrite`.
- **`MidiLearn.Bind`** registers the control with its display name.
- **Live follow** re-reads engine state each tick and skips while dragging.

### Inventory

| Control | Spec |
|---|---|
| `Knob` | 52-grid: groove r21 stroke 5, 270° from −135°, cap r14, pointer 2.4 Brass Light. Sizes snap to 34 / 36 / 44. `IsModified`, `IsDim`, `ArcColor` for a modulation source. |
| `DeviceCardKit.KnobCell` | Knob → label 7/700 caps → value mono 7, no gap; 53 tall under a 34 knob. Label and value go Brass Light when modified or `emphasised`. |
| `SwitchTrack` / `DeviceCardKit.Switch` | 18 × 10, knob 7, inset 1.5; on = brass + panel-coloured knob right, off = Track off + Ink 5 knob left. Word to the right, caps 7 in a device, 11 in the shell. |
| `DeviceCardKit.Segments` | Sunken container; selected = solid brass with dark text. 9 px in a device, 11 in the shell (`ToggleButton.seg` in `Border.segmented`). |
| `SliderTrack` / `DeviceCardKit.SliderRow` | Label · 3 px well track · 6 × 7 handle · fixed-width mono value right. Bipolar fills from centre. Inactive loses brass, keeps the number. |
| `MiniFader`, `PanBar`, `VFader` | Track-header gain and pan, mixer fader — same drag contract. |
| `DragNumber` | Mono field: drag, or double-click to type. |
| Buttons (`Button`, `.primary`, `.ghost`, `.cell`, `.chip`, `.tp-icon`, `.tp-play`) | 26 tall; raised at rest. **One** solid-brass action per context; every other engaged button is Brass Wash + `BorderBrass` + `AccentHover` text. |
| Fields (`TextBox.field`, `.search`) | 26, sunken; focus = `BorderBrass`, no ring. |
| Filter chips | 20, pill, 9 side pad, a category-colour dot instead of an icon; one line, overflow folds into `+N`. |
| `Glyph` | Icons drawn as geometry: stop 10 r1, record 11 disc, play 11 × 14 triangle; the rest a round stroke of ~15 % of the size (min 1.2). No icon font, no unicode glyphs. |
| `MeterBar`, `StereoMeter`, `GrMeter` | 11 wide, 5 apart, radius 2 on Well; Signal → −6 dB Caution → 0 Alert; −60…+6; peak hold stays until clicked. |
| `Inactive.Set(root, on, interactive)` | Puts a subtree into the disabled look by colour (Ink 6, brass → Border strong, controls `IsDim`). |
| `NaBadge` | Feature not built yet; the control stays so nothing shifts later. |

**Record:** at rest a neutral button with a red disc; engaged, solid `Record` with a
`RecordInk` disc. The same rule arms tracks in the arrangement, mixer and Session.

### States

| State | Ground · border · text |
|---|---|
| Rest | Raised · Border · Ink 2 |
| Hover | Hover · Border strong · Ink 1 (120 ms) |
| Pressed | Well · Brass Deep — no 1 px shift |
| Engaged | Brass Wash · Border brass · Brass Hover |
| Selected | Brass Wash + 2 px brass bar on the left; name Ink 1 |
| Focus | Border brass, no outline |
| Disabled | Panel · Hairline · Ink 6 — no opacity |
| Modified | label and value → Brass Light; no dot |

**Motion:** hover and colour 120 ms ease-out (`BrushTransition`); nothing real-time is
animated. **Empty state:** one line of Ink 5, centred, no illustration or call-to-action
button; details go in a tooltip; the area keeps its size and frame.

## Visualisers

About 45 custom-drawn views in `Controls/`. A device draws **the thing it does**, and where
possible draws it for real (the reverb tail is an actual impulse response; the amp
harmonics push a sine through the real waveshaper).

All live in one **graph window** (`Controls/NotaGraph.cs`):

- Well ground, 1 px Hairline frame, radius 4, grid in `GridBeat`.
- Axis labels mono 7–8 in Ink 7, **in the corners** — never full axes with ticks. A
  frequency graph is labelled only in its bottom corners (`20` · `20k Hz`).
- Title caps 8 top-left; the legend lives **inside** the window, a drawn sample next to a
  value (`NotaGraph.Legend`).
- Primary curve 1.8 px brass; secondary 1.2–1.6 px in their chroma. **No fills under
  curves, no gradients.**
- Nodes 7 px with a 2 px ground-coloured ring; active brass, the rest Ink 3.
- Waveforms: second channel at 70 %; the trimmed part darkened by 72 %, not hidden; the
  boundary a 1 px brass line with its value.
- Bars: 1–2 px gaps, radius 1, no outline; brass only for a selected range, else Border
  strong; updates are discrete.
- Timeline: a clip is a flat rectangle in its track colour, radius 2, no outline; the
  playhead is 1 px brass with a 7 × 5 flag and no glow.

## Device cards

Every built-in device is a **700 × 260** card (`DeviceCardKit.CardH`, width from
`IDeviceBody.Width`).

- **Header 22** (`HeaderH`): device name (12/600) on the left; on the right the **preset
  picker** `‹ Name ⌄ ›` (an 18 px sunken field, radius 4: the name opens the factory
  presets, the chevrons step through them, wrapping), the processing-type badge (mono caps;
  instruments add live voices, `SUBTRACTIVE · 3/16`) and the bypass switch. The picker
  appears only when the device has factory presets. A/B, move and delete live in the
  header's right-click menu; drag the card by its whole header; Delete removes it.
- **Body 238**, inset 6 (`NotaSpace.DeviceInset`) unless the body is `FullBleed`.
  Sections radius 6, gap 5, section inset 6–8; a knob row is 52–54 tall.
- **Three columns, left to right: choice → work → output** — source or preset on the left,
  graph and parameters in the middle, levels and mix on the right.
- **Brass** marks the audio path and active state; modulation shows its source chroma
  (teal for LFOs and envelopes) on the arc; end-of-chain trim fills with Border strong.
- A bypassed card keeps its layout and turns to the disabled look via `Inactive`.

Width exceptions, by decision: **Bass 1060, Physical 720** (squeezing them would be a
redesign) and the host-plugin / parameter-list stubs (230, 190). Rhythm moved onto the
700 frame with its redesign.

## Shell

```
┌ header 36 ─ project name, centred ─────────────────────────────────────┐
│ transport 42 ─ view switch · console · tempo · signature · grid · …  CPU│
├─────────────┬──────────────────────────────────────────────────────────┤
│ browser     │ canvas (arrangement · session · modular)                 │
├─────────────┴──────────────────────────────────────────────────────────┤
└ status 22 ─────────────────────────────────────────────────────────────┘
```

- **Header 36** — frameless; macOS traffic lights in a left inset, the project name
  (`Heading`) centred; Windows reserves 180 px for the caption buttons.
- **Transport 42, under the header** (`Border.transport` on Void, hairline below). It reads
  left to right: view switch (Arrangement · Session · Modular) · the console (stop / play /
  record, position, loop) · tempo `120.00`, signature, grid · the switches (metronome,
  follow, snap, automation) · `+ Track` — then, pinned right, MIDI, CPU and master. Launch
  quantize appears only in Session. Transport buttons are not focusable, so Space / R / L /
  Return always reach the window.
- **Body** — the browser on the left and the canvas float as panels on `Gutter` with 12 px
  gaps; the canvas is always sunken relative to panels. The splitter is a transparent grab
  strip inside the gap.
- **List rows** are 26 and one line: a colour dot or bar, the name, mono metadata right.
- **Arrangement:** track rows 64 (the header carries every control), group rows 26 (open to
  64 while selected), header column 228, default zoom 28 px per beat. Every y ↔ row
  conversion goes through `ArrangementView.RowTop` / `RowAtY`.

## Enforced by tests

`tests/Nota.SmokeTest/DesignTokenCheck.cs` runs in the smoke test and fails the build on:

| Check | Holds |
|---|---|
| `Run` | Dark/Light key parity; every `NotaPalette` slot matches its XAML pair; `KeyMap` keys exist |
| `RunGeometry` | XAML ↔ `NotaGeometry` values; no undefined keys; no `CornerRadius` literals, drop shadows or gradients in views |
| `RunType` | Fonts bundled and matched; no font names in views; nothing under 7 px |
| `RunControls` | No unicode icons typed as text; Knob / Switch / Slider sizes |
| `RunVisualisers` | Graph windows via `NotaGraph`; no fills under curves; no meter ballistics; meter 11/5; playhead without glow |
| `RunLayout` | Header 22; cards 700 except the listed exceptions; transport 42 at the top; rows 26–28 |
| `RunNumbers` | Display culture installed; no hyphen minus; no invariant culture on readouts; thin space before units; no vowel-dropped labels |
| `RunBans` | No hex outside `Ink()` (bar three data tables), no RGB typed as numbers, no named system colours, `Opacity` only on drag ghosts, no brush snapshotting a theme colour |

When a check fails, fix the view — or, for a genuine exception, add it to the check's
allow-list with a comment saying why.

## Accepted departures from the almanac

Decisions taken while aligning the app, kept on purpose:

- **Transport at the top, view switch inside it.** The almanac puts the transport at the
  bottom and the modes in the header.
- **Shell language is English.** The almanac asks for Russian infinitive tooltips; the app
  is English throughout, so the rule became "short, verb-first".
- **Segments with more than four options** stay segments where the choice is frequent:
  Amp Model (7) and Cabinet (5), Compressor Character (5), Vintage Character (6), Delay
  Division (8), Arp Rate (8) and Order (8), Aurora Warp (5) and Filter (5), Bass LFO wave (5),
  Monolith Glide range (6), Pendulum Division (5) and Wave (5).
- **Wide instruments** — Bass, Physical (see § Device cards).
- **Preset picker in the device header.** The almanac allows only name, badge and bypass
  there; switching presets is frequent enough to stay one click away.
- **Zoom 28 px per beat** by default, not a 16 px bar; the ruler labels every bar once a bar
  is wider than 40 px.
- **`FREQ`, `RESO`, `THRESH`** as labels (see § Labels and copy).
- **Hero readouts** larger than the device scale — Strata bar number, Shutter OPEN/SHUT,
  Auto Shift note, Level value.
- **Play turns green in Session** (`tp-play.session`) while clips run.
- **Data hues close to a chroma** — Strata layer `#C99C55`, tag swatch `#C8A24B`, Random
  `#D0603F`, the Rhythm kit dots (`#C77F55` Kick … `#5AA0B8` Tom) — are data colours, not accents.

## Known drift

True today, not yet fixed:

- **Column order** choice → work → output is applied in Utility; the other cards have not
  been audited card by card.
- **Panel expand** is not animated (the almanac asks for 180 ms).
- **`LOOKAHEAD`** in Compressor truncates with an ellipsis.
- **Slider reset** on double-click works only where the card passes a default.
- **Names:** presets with emotional names (Screaming Lead, VHS Fever, Gentle Master,
  Transparent Safety, Dive Bomb, Laser Zap…) and multi-word device names (Auto Filter,
  Beat Repeat, Dynamic EQ-8, EQ-3, the "Nota " prefix) remain; renaming touches MCP names,
  the browser and saved projects.
- **Paper values are derived**, not designed by the almanac; pin any that read poorly in
  `NotaPalette.InkOverrides` or the theme.
- **The "Add device" tile** at the end of a chain keeps a plus and two lines — it is a drop
  target rather than an empty state.
