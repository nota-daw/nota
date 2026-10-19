<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Nota design guideline — Studio

Nota's UI runs on **Studio**, a design language built around one idea: in a music app the
work carries the colour, and the interface steps back. Neutral greys stepped in a few exact
tones, flat panels butted against dark 1px seams, small crisp type, and one orange accent
that appears only where something is **live or chosen** — playing, selected, switched on.
Track colours, clips, waveforms and meters read first; the chrome around them stays quiet
until you touch it.

Colour is a **theme** (§ Colour themes): the structure — layout, type, geometry, which
element carries the accent — is Studio's in every theme; only the values change. Four
themes are built in:

- **Studio Dark** (the default) — a DAW is used in dark rooms for long sessions.
- **Studio Light** — the same roles in light greys. The accent goes a
  shade deeper (and its line variant darker still) so marks hold contrast on pale grey.
- **Ember Graphite** / **Ember Paper** — the warm browns and brass of Nota's earlier
  language, dark and light, on Studio's structure.

Preferences → Appearance picks a mode — **Dark**, **Light** or **Follow system** (a
dark-mode and a light-mode theme, switched with the OS) — then a theme of that kind, each
shown as a miniature Nota window in its own colours; every change is live.

This file is the guideline the app is held to: Studio's rules, the tests that enforce them,
and the few places where a screen's needs override a general rule (§ Accepted exceptions).
The change from the earlier Ember language is recorded in
`specs/010-studio-visual-style/` (decisions in `research.md`, the token map in
`data-model.md`, the visual contract in `contracts/ui-contract.md`).

**[→ Open `DESIGN.html`](DESIGN.html)** for the visual reference — every swatch, the
control specimens and the card anatomy at true size, with a Dark / Light toggle.

## Source of truth, in order

| Rank | File | Authority |
|---|---|---|
| 1 | `src/managed/Nota.App/Theme/Themes/*.json` | **Definitive for colour.** One file per colour theme, one value per token. |
| 1 | `src/managed/Nota.App/Theme/NotaTheme.axaml` | **Definitive for the rest:** geometry, type classes, control styles. It holds no colour. |
| 2 | `Theme/NotaPalette.cs` · `NotaColourTheme.cs` · `NotaThemeService.cs` · `NotaGeometry.cs` · `NotaFonts.cs` · `NotaText.cs` · `NotaNum.cs` | The C# side: the token slots (the catalogue), theme loading and switching, elevation geometry, radii/sizes/spacing, typefaces and type scale, sentence-case labels, number setting. |
| 3 | `DeviceCardKit.cs`, `Controls/` | The shared controls and card builders. |
| 4 | `tests/Nota.SmokeTest/DesignTokenCheck*.cs` | Enforces the theme files against the catalogue and the readability bar, the wiring, and the bans below. |
| 5 | `DESIGN.html` / this file | Documentation. If these disagree with 1–4, **these are wrong** — fix them. |

## How theming works

Colour has one home: the active theme's file. `NotaPalette` declares one **slot** per token
by name — one long-lived `SolidColorBrush` whose `Color` is re-pointed from the theme on
every switch, so views keep the same brush object and nothing is rebuilt.
`NotaThemeService` publishes those same brushes into `Application.Resources` under their
`Brush.*` keys (`NotaPalette.KeyMap`), and builds the `Shadow.*` elevations and Fluent's
`SystemAccentColor*` ramp from the theme's colours. So XAML **must** use
`{DynamicResource Brush.*}` / `{DynamicResource Shadow.*}`; a `StaticResource` cannot follow
a theme change. Geometry, fonts and sizes are theme-independent (`StaticResource`).

Ways to get a colour in code:

| Call | For |
|---|---|
| `NotaPalette.Accent` (any named slot) | a token |
| `NotaPalette.Wash(slot, 0x28)` | a translucent tint of a token; cached, follows its slot |
| `NotaPalette.InkOn(fill)` | text or a glyph on a solid fill — dark ink (`#17181A`) or white, whichever has the higher contrast on the fill's current colour. `ReadableInkOn(color)` for a fill computed at draw time |
| `NotaPalette.Ink("#6A9FD8")` | a one-off device hue or stored tag colour. A hex equal to a role token returns that token; a hex that was an Ember role (`#D8A03D` brass, `#5B9E9C`, `#B57286`, `#6D8FB5`, `#B8862E`, `#2A2317`) returns the slot that plays that role now (`LegacyRole`); any other gets a derived light value, or one pinned in `InkOverrides` |
| `NotaPalette.Derived(() => …)` | a colour a view computes from slots; register once and cache it |
| `ArrangementView.TrackBrush(index)` | a track colour |

**Nothing may snapshot a colour.** `new SolidColorBrush(slot.Color)`, a
`static readonly Color` taken from the palette, or a static array of palette colours all
freeze a view in the theme it was built in. Use the slot, a `Wash`, `InkOn`, a property
(`static Color X => NotaPalette.Accent.Color;`), or read `.Color` inside `Render`.

### Changing a token

1. Change its value in **every** theme file in `Theme/Themes/` that should change — at
   least both Studio themes. A new token is a new `T(nameof(X))` slot in `NotaPalette`
   plus a value in every theme file (the smoke test names any file that lacks it), plus a
   `KeyMap` entry if XAML uses it.
2. Run `python3 scripts/design-html.py` to regenerate `DESIGN.html`, and update the table here.
3. Look at it in every theme — the app (Preferences → Appearance) and `DESIGN.html`.

## Colour themes

A colour theme is one fixed look: an id, a name, a kind (**dark** or **light**) and one
value for every token — the format is `specs/011-colour-themes/contracts/theme-file.md`.
The kind sets the OS window chrome and Fluent's base, and decides which follow-system list
the theme appears in. A theme changes colour only: never geometry, type, layout, or which
element carries the accent.

| Theme | Kind | Values |
|---|---|---|
| Studio Dark | dark | The § Tokens tables below |
| Studio Light | light | The § Tokens tables below |
| Ember Graphite | dark | Nota 0.52.1's dark palette by token name; the tokens Studio added are derived by the role Studio gives them (seam = Ember's gutter, rule = its strong border, faint = its disabled ink, mute = its amber, solo = its steel blue, viz ink = 92 % primary over the well, viz grid = its well grid, master = its muted ink) |
| Ember Paper | light | The same, from 0.52.1's light palette |

**Appearance.** Preferences → Appearance stores one theme, or **Follow system** with a
dark-mode and a light-mode theme (each list offers only themes of that kind). Old
settings carry over: Dark → Studio Dark, Light → Studio Light, System → follow system with
the two Studio themes. An unknown or wrong-kind id falls back to the Studio theme of the
kind needed (`Nota.Application.Appearance`).

**Adding a theme.** Add `Theme/Themes/<id>.json` with every token and append its id to
`NotaThemes.BuiltInOrder`. Nothing else names a theme. The smoke test checks the file
against the catalogue and the readability bar.

### Readability bar

Every theme meets these (WCAG 2.x contrast ratio; ΔE = CIE76). They are calibrated on
Studio: WCAG where Studio meets it, Studio's own value where it does not.

| | Pairing | Minimum |
|---|---|---|
| T3.1 | `TextPrimary`, `TextSecondary`, `TextMuted` on `BgApp`, `Panel`, `SurfaceRaised`, `SurfaceHover`, `BgSunken` | 4.5 |
| T3.2 | `TextTertiary` on the same five | 3.0 |
| T3.3 | `TextFaint`, `TextDisabled`, `TextAxis` on the same five | 2.5 |
| T3.4 | `AccentBright` on `BgApp`, `Panel`, `BgSunken` | 2.3 |
| T3.5 | Derived ink on `Accent`, `Mute`, `Solo`, `Record` | 4.5 |
| T3.6 | Derived ink on the track and return colours | 3.0 |
| T3.7 | The nine track colours, pairwise | ΔE ≥ 12 |
| T3.8 | `Accent`, `Mute`, `Solo`, `Record`, pairwise | ΔE ≥ 15 |
| T3.9 | `RecordInk` on `Record`, `SuccessInk` on `Success` | 4.5 |

Track colours are not compared with state colours: solo blue is the Keys track blue by
design. **Known gap:** in Studio Light the accent line on the ground (2.39) and faint ink
(2.58) sit below WCAG's 3:1 for graphics and incidental text; T3.3 / T3.4 hold Studio at
those values until a Studio change raises them.

### Ember adjustments

Ember is 0.52.1's palette exactly, except these values, each moved the smallest step in
HSL lightness (hue kept) that passes the bar:

| Theme | Token | 0.52.1 / derived | Shipped | Failed |
|---|---|---|---|---|
| ember-graphite | `TextMuted` | `#8D8779` | `#8F897C` | T3.1 TextMuted/SurfaceHover 4.45 < 4.5 |
| ember-graphite | `TextTertiary` | `#6E6A5E` | `#716D60` | T3.2 TextTertiary/SurfaceHover 2.94 < 3.0 |
| ember-graphite | `TextFaint` | `#55514A` | `#646054` | T3.3 TextFaint/Panel 2.36 < 2.5; T3.3 TextFaint/SurfaceHover 2.31 < 2.5; T3.3 TextFaint/SurfaceRaised 2.41 < 2.5 |
| ember-graphite | `TextDisabled` | `#55514A` | `#646054` | T3.3 TextDisabled/Panel 2.36 < 2.5; T3.3 TextDisabled/SurfaceHover 2.31 < 2.5; T3.3 TextDisabled/SurfaceRaised 2.41 < 2.5 |
| ember-graphite | `TextAxis` | `#4A463D` | `#65614F` | T3.3 TextAxis/BgApp 2.10 < 2.5; T3.3 TextAxis/Panel 2.38 < 2.5; T3.3 TextAxis/SurfaceHover 2.33 < 2.5; T3.3 TextAxis/SurfaceRaised 2.44 < 2.5 |
| ember-graphite | `Record` | `#C25B44` | `#BE563E` | T3.5 Record 4.31 < 4.5 |
| ember-graphite | `RecordInk` | `#F4E3DC` | `#FFFFFF` | T3.9 RecordInk/Record 3.67 < 4.5 |
| ember-paper | `TextMuted` | `#746D5C` | `#625B50` | T3.1 TextMuted/BgApp 3.54 < 4.5 |
| ember-paper | `TextTertiary` | `#8A8474` | `#7E786C` | T3.2 TextTertiary/BgApp 2.57 < 3.0 |
| ember-paper | `TextFaint` | `#9E9786` | `#8D866F` | T3.3 TextFaint/BgApp 2.00 < 2.5 |
| ember-paper | `TextDisabled` | `#9E9786` | `#8D866F` | T3.3 TextDisabled/BgApp 2.00 < 2.5 |
| ember-paper | `TextAxis` | `#ADA694` | `#8D8669` | T3.3 TextAxis/BgApp 1.67 < 2.5 |
| ember-paper | `Accent` | `#A87415` | `#AD7716` | T3.5 Accent 4.38 < 4.5 |
| ember-paper | `Track9` | `#4D7A5D` | `#598C69` | T3.7 Track2/Track9 7.53 < 12 |
| ember-paper | `ReturnA` | `#4D7A5D` | `#598C69` | follows Track9 (returns draw from the ninth role) |


## The rules

1. **One accent: live or chosen.** Orange means playing, selected, the playhead, the loop,
   a toggle that is on, a hovered menu item, the device power switch, and a value control
   only while it is hovered or dragged. Nothing else. If an orange element is neither
   playing, selected, looping, toggled on nor under the pointer, it is wrong. There is no
   second accent.
2. **Resting values are neutral.** Knob arcs, fader and slider fills, value bars, graph
   curves and waveforms at rest are `VizInk`. A modulation arc keeps its source's chroma —
   that is data, not state.
3. **Semantics by purpose.** Mute on is amber (`Mute`), solo on is blue (`Solo`), arm and
   record are red (`Record`) — in the arrangement, mixer and Session alike. Red is also
   overload (`Danger`). Green is positive status and the meter's safe range (`Success`);
   amber is the meter's −6…0 dB zone (`Warning`).
4. **Derived ink on every solid fill.** Text or a glyph on an accent, mute, solo, arm or
   track-colour fill takes `InkOn(fill)` (or `TextOnAccent` / `RecordInk` in XAML) — never a
   hard-coded label colour.
5. **Seam between, rule within.** Panels meet on the dark 1px `Seam` and so do keys in a
   segmented group; items inside a panel are divided by the lighter 1px `Rule` (toolbar and
   menu separators, field edges, graph frames). A device card's sections are not boxes:
   they sit on the card and a rule divides neighbours (§ Device cards).
6. **Flat docked, blur only floating.** No gradients, glow, textures or skeuomorphism.
   Depth is a tone step plus a 1px seam, with a faint top bevel on raised controls. Only
   floating layers (the command palette, in-window overlays) cast a blurred shadow.
7. **Small, nearly square corners.** 2 for inner keys, rows, clips and menu items; 3 for
   controls, cards, graph windows and popovers; 4 only for modals. 0 for docked panels; 1
   for bars inside a plot.
8. **Two typefaces, one split.** Geist for names and words; Geist Mono for every number a
   user reads. Names are never mono. Nothing smaller than 7 px.
9. **No shouting.** Labels are sentence case at letter-spacing 0 (`NotaText.Label`);
   acronyms and units keep their case (`LFO rate`, `Gain dB`). No uppercase tracked labels,
   no decorative kicker text.
10. **One implementation per control**, differing only in size. A control's value is
    always visible next to it — never only in a tooltip.
11. **State changes colour, never size, border width or position.** Nothing moves on hover.
12. **Real time is not animated.** Meters, playhead and spectra jump to the value.
13. **Opacity is not a state.** Muted and disabled take the next ink step.
14. **Numbers are set, not printed:** point decimal, U+2212 minus, thin space before the
    unit, fixed precision, en-dash ranges.
15. **One icon set.** Every icon is a `Glyph` kind (§ Icons); nothing else draws one.

## Tokens

Values are Studio Dark / Studio Light, as `Theme/Themes/studio-dark.json` and
`studio-light.json` declare them. Several Ember-era keys now
alias one Studio role; the names stayed so the ~1,000 call sites kept working (§ Known
drift).

### Surfaces — far to near

Five steps: well · ground · panel · raised · raised hover.

| Key | Role | Dark | Light | Use |
|---|---|---|---|---|
| `Brush.BgSunken` (`ChromeBg`, `SurfaceActive`, `TrackOff`, `SurfaceAbyss`) | Well | `#17181A` | `#D6D8DB` | Graph windows, fields, readouts, slider tracks, the transport console; pressed keys sink into it; the track of an off switch |
| `Brush.BgApp` | Ground | `#1E1F22` | `#C9CBCF` | Window, title bar, arrangement, status line, detail-panel body, plugin body |
| `Brush.LaneB` | Ground, alternate | `#222326` | `#C4C6CA` | Alternate arrangement lane |
| `Brush.Panel` / `Brush.SurfaceCard` | Panel | `#2B2D31` | `#E4E5E7` | Transport row, browser, panel toolbars, inspector, device card body and its sections, mixer strips, dialog body |
| `Brush.SurfaceRaised` | Raised | `#34363B` | `#EEEFF1` | Buttons and keys at rest, device card header, dialog header, menus, tooltips |
| `Brush.SurfaceHover` / `Brush.SurfaceSelected` | Raised hover | `#393C41` | `#F7F8F9` | Hovered key or row; selected row (with an accent tick) |
| `Brush.SurfaceHoverClear` | Raised hover at alpha 0 | `#00393C41` | `#00F7F8F9` | Resting row and ghost button, so the hover fade never passes through white |
| `Brush.AccentSubtle` | Accent-soft (16 % accent over panel) | `#4D3F2E` | `#E6D6C4` | A soft accent ground; nothing at rest uses it |

`NotaPalette` adds `CanvasBg` (ground), `SurfaceDeep` `#1B1C1F` / `#CFD1D4` (step-sequencer
ground) and `SurfaceInset` `#232427` / `#DCDEE0` (rail, sunken chip) for custom drawing.

### Lines

| Key | Role | Dark | Light | Use |
|---|---|---|---|---|
| `Brush.Seam` (`BorderDefault`, `Gutter`) | Seam | `#141517` | `#9DA0A6` | Between docked panels, around raised controls, between segmented keys, menu ring |
| `Brush.Rule` (`BorderStrong`, `Hairline`) | Rule | `#45484E` | `#B3B6BB` | Within a panel: separators, field and readout edges, graph-window frame (`NotaPalette.GraphBorder`) |
| `Brush.BorderBrass` / `AccentEdge` | Accent line (70 % accent over panel) | `#BF7D22` | `#ECA54E` | An accent stroke where a fill is too heavy |
| `Brush.GridBeat` | Grid on ground | `#2A2C30` | `#BDC0C4` | Beat grid, zero axis |
| `Brush.GridBar` | — | `#3A3C41` | `#ADB0B5` | Bar lines |
| `Brush.VizGrid` (`NotaPalette.WellGrid`) | Viz grid | `#2E2F32` | `#C0C3C6` | Grid inside a graph window |

### Ink — by importance

| Key | Step | Dark | Light | Use |
|---|---|---|---|---|
| `Brush.TextHeading` | Ink 0 | `#F4F5F6` | `#0E0F11` | Modal and section titles. Rare. |
| `Brush.TextPrimary` | Ink 1 · text-primary | `#E8E9EB` | `#16171A` | Names, values, button text |
| `Brush.TextStrong` | Ink 2 | `#D4D6DA` | `#282A2E` | Toggle-key text, glyphs on transport keys |
| `Brush.TextSecondary` | Ink 3 · text-secondary | `#C0C4C9` | `#3A3D42` | Labels, idle icons, metadata, supporting copy |
| `Brush.TextMuted` | Ink 4 · text-muted | `#A4A9B0` | `#4F535A` | Section labels, units, counts, badges |
| `Brush.TextTertiary` | Ink 5 | `#92969D` | `#65696F` | Row prefixes and tags, placeholders, empty states |
| `Brush.TextFaint` (`TextDisabled`, `TextAxis`) | text-faint | `#82868C` | `#797C81` | Disabled controls; axis labels inside graphs |
| `Brush.VizInk` | viz-ink | `#D7D8DA` | `#252629` | A value at rest: knob arcs, fader fills, graph curves, waveforms |
| `Brush.TextOnAccent` | on-accent | `#17181A` | `#17181A` | Text on the accent (dark ink wins on both accents). For other fills use `InkOn` |

### Accent

| Key | Role | Dark | Light | Use |
|---|---|---|---|---|
| `Brush.Accent` | accent | `#FF9F1C` | `#EF8A0C` | Playing Play key, playhead, loop, selection ticks and bands, toggled-on keys, chosen segment, switch on, menu hover, a value control while live |
| `Brush.AccentBright` / `AccentHover` | accent-glow | `#FFB547` | `#C46A00` | The line / ink variant — the accent drawn as a stroke or text |
| `Brush.AccentDeep` | pressed | `#C77612` | `#D47800` | Pressed accent |
| `Brush.AccentDim` | accent-dim | `#C77612` | `#F7C27A` | The soft accent |

Fluent's `SystemAccentColor` ramps are retinted to the orange, so stock controls match.

### Semantics

| Key | Role | Dark | Light | Use |
|---|---|---|---|---|
| `Brush.Record` | error | `#F06A6E` | `#B3262C` | Record and arm — solid when on |
| `Brush.RecordInk` | — | `#17181A` | `#FFFFFF` | The disc on an engaged record key: dark on Dark's light red |
| `Brush.Danger` | error | `#F06A6E` | `#B3262C` | Overload, clipping, error text |
| `Brush.DangerBright` | — | `#F58F92` | `#9C1F24` | Overload readout text |
| `Brush.Mute` | warning amber | `#F2B53C` | `#7A4F00` | Mute on |
| `Brush.Warning` | warning amber | `#F2B53C` | `#7A4F00` | Meter −6…0 dB |
| `Brush.Solo` | track blue | `#6A9FD8` | `#3F78B8` | Solo on |
| `Brush.Success` | green | `#6CC673` | `#22692B` | Meter safe range, positive status; Play in Session |
| `Brush.SuccessInk` | — | `#17181A` | `#FFFFFF` | The glyph on a green key (Session play) |
| `Brush.SuccessDim` | green-dim | `#3F8A45` | `#79B880` | Transport input, metronome |

`DangerHover` and `DangerSubtle` complete the error family; `NotaPalette` adds `DangerDeep`
(over-threshold fill), `DangerPale` (live-take waveform) and `MuteTint` (the muted band).

### Graph chromas — inside graphs only

When two to four sources share a graph, each takes a chroma in this fixed order
(`NotaGraph.Chroma(n)`): **viz ink** (primary: left channel, mid band, main signal) ·
**Teal** `#5FB3A8` / `#3A8A7F` (lows, early reflections, input, modulation) · **Rose**
`#D77A8F` / `#B4566B` (right channel, highs, tail) · **Blue** (`ChromaSteel`) `#6A9FD8` /
`#3F78B8` (rare). Light pairs for text: `ChromaTealLight` `#86C9C0` / `#2E7067`,
`ChromaRoseLight` `#E49EAE` / `#93404F`. They never go on a button and never mean state —
the accent alone does.

### Track palette — nine roles

| | Drums | Perc | Bass | Keys | Texture | FX | Brass | Vox | Return |
|---|---|---|---|---|---|---|---|---|---|
| Dark | `#7FB87A` | `#5FB3A8` | `#5AA6C4` | `#6A9FD8` | `#E0A84A` | `#9D90C9` | `#D77A8F` | `#B3B45E` | `#8E9DB0` |
| Light | `#4A7F46` | `#3A8A7F` | `#357C99` | `#3F78B8` | `#B8862F` | `#7363A8` | `#B4566B` | `#7E7F2F` | `#5D6C80` |

Nine muted roles (teal, blue, amber, violet, rose and four more at matching chroma),
one lightness for all so no track is louder than another (`Brush.Track1…9`,
`NotaPalette.TrackBrushes`). Each role has three shades (base · 24 % toward white · 30 %
toward black) that tracks inside a group step through. Old projects keep their stored
index, which now points at the hue at that place in the table.

### Master and returns

| Key | Dark | Light | Use |
|---|---|---|---|
| `Brush.ReturnA` / `ReturnB` | `#8E9DB0` / `#7A889A` | `#5D6C80` / `#4E5B6D` | Return buses — the ninth role's slate |
| `Brush.Master` | `#A4A9B0` | `#4F535A` | The master strip and spine — muted grey, not a hue: it is the sum, not a track |

### Elevation

| Key | Dark | Light | Use |
|---|---|---|---|
| `Shadow.Raised` | `inset 0 1 0 0 #12FFFFFF` | `inset 0 1 0 0 #B3FFFFFF` | The top bevel on raised keys, the device card header, the dialog header. The seam ring is the control's own 1px `Seam` border |
| `Shadow.Sunken` | `inset 0 0 0 1 #45484E` | `inset 0 0 0 1 #B3B6BB` | The field edge on a recessed well |
| `Shadow.Popover` | `0 0 0 1 #141517, 0 10 28 0 #6B000000` | `0 0 0 1 #9DA0A6, 0 10 28 0 #381E2024` | A popover drawn inside the main window |
| `Shadow.Modal` | `0 0 0 1 #141517, 0 24 64 0 #80000000` | `0 0 0 1 #9DA0A6, 0 24 64 0 #401E2024` | The command palette |

Only `Shadow.Popover` and `Shadow.Modal` carry a blur; everything docked is flat. The
geometry is Studio's (built in `NotaThemeService`); a theme supplies the colours: the
bevel (`ShadowBevel`), the drop (`ShadowPopover`, `ShadowModal`), and the rings, which are
its `Seam` and `Rule`.

### Geometry

| Radius (`Radius.*` / `NotaRadius`) | px | For |
|---|---|---|
| `Docked` | 0 | A docked panel — it meets its neighbours on a seam |
| `Bar` | 1 | Spectrum and level bars inside a plot |
| `Clip` | 2 | Clip, row highlight, menu item, slider track, switch track |
| `Badge` | 2 | Inner key, badge inside a device |
| `Control` | 3 | Button, field, dropdown, graph window, popover, segmented group |
| `Tile` | 3 | Transport key, search field, tile |
| `Panel` | 3 | Device section |
| `Body` | 3 | Device card |
| `Pill` | 3 | Chip, switch — Studio has no pills; the name stays for its callers |
| `Modal` | 4 | Modal window, command palette, splash |

| Size (`Control.*` / `NotaSize`) | px |
|---|---|
| Device card | 700 × 260 |
| Transport row (`TransportBar`) · console (`Console`) | 44 · 30 |
| Transport key (`Transport`) · Play (`Play`, width) | 26 · 36 |
| Shell button / field / readout well (`Shell`) | 26 |
| Tab segment (`Seg`) · segment container (`SegGroup`) | 24 · 30 |
| Filter chip (`Chip`) | 20 |
| Title bar · panel toolbar · status line | 32 · 32 · 22 |
| Menu item | 24 |
| Switch | 18 × 10, knob 7, inset 1.5 |
| Switch, Settings rows | 28 × 16, knob 12, inset 2 (`SwitchTrack(large: true)`) |
| Knob (`KnobSecondary` / `KnobRegular` / `KnobMain`) | 34 / 36 / 44 · `KnobInline` 24 in a table row |
| Parameter cell (`ParamCell`) | 53 tall |
| Slider track | 3, handle 6 × 7 |
| Meter | 11 wide, 5 between channels |
| List row | 26 |

| Spacing (`Space.*` / `NotaSpace`) | px |
|---|---|
| In a device: hair · gap · inset · section inset | 2 · 5 · 6 · 8 |
| In the shell: tile · between docked panels (`Gutter`, the seam) · inside a panel | 8 · 1 · 20 |

**Depth:** well — `BgSunken`, a 1px `Rule` edge (`Shadow.Sunken` or a border) · ground —
`BgApp` · panel — `Panel`, docked on seams, flat · raised — `SurfaceRaised`, a 1px `Seam`
border and the `Shadow.Raised` bevel · floating — `Shadow.Popover` / `Shadow.Modal`.

## Type

`Font.UI` = **Geist** 400/500/600/700, `Font.Mono` = **Geist Mono** 400/500, both bundled
(`assets/fonts`, SIL OFL). `NotaFonts` holds the typefaces for custom drawing — never name
a font family in a view. Geist lacks the thin space; it falls back to the system font.
Nothing in the shell is larger than 13 except the position readout.

**Shell scale** (`NotaType`, `TextBlock.*` classes)

| Class | Spec | For |
|---|---|---|
| `Title` | 600 · 13, Ink 0 | Modal and section heading |
| `Heading` | 600 · 13 | Project name, dialog title |
| `Name` | 500 · 12 | Track, preset, file name |
| default | 500 · 12 | Buttons, tabs, chips, menu items |
| `Caption` | 400 · 11 / 16, secondary | Supporting copy, hint, status line |
| `SectionLabel` / `GroupLabel` | 600 · 11, muted, sentence case | Panel and browser group heading |
| `CellLabel` | 500 · 10, muted | The unit beside a readout (`BPM`, `Loop`, `Grid`) |
| `Readout` | Mono 400 · 13 | Transport numbers; the position is 14 with .02em |
| `Value` | Mono 400 · 11, secondary | Row values, metadata |
| `Eyebrow` | Mono 400 · 10, muted, as written | Format badge |

**Device scale** (inside the 700 × 260 card)

| Class | Spec | For |
|---|---|---|
| `DeviceName` | 600 · 12 | Device name in the header |
| `DeviceSection` | 600 · 10.5, muted | Section title |
| `RowLabel` | 500 · 9, secondary | Slider-row label, segments in a device |
| `KnobLabel` | 500 · 8, secondary | Name under a knob, switch word |
| `KnobValue` | Mono 400 · 8 | Parameter value |
| `Axis` | Mono 400 · 7.5, faint | Tick label inside a graph |

7 is the floor and is reached only by plot tick labels; a label is 8 or more. The
`NotaType.*Tracking` constants are 0 and stay only so older call sites compile — new code
sets no `LetterSpacing`.

### Numbers

`NotaNum.Install()` (called in `App.Initialize`) makes the display culture the default:
invariant with U+2212 as the negative sign — so every implicit `{v:0.0}` prints `−3.3`
with a point on any OS locale; typed input still accepts a hyphen. Project and preset
files are JSON and never go through it.

- The unit follows a **thin space**: `4.6 s`, `−3.3 dB`, `72 %` (` ` in source).
- **Minus is U+2212** on every number shown, scale and tick labels included (the mixer
  scale reads `+6 · 0 · −12 · −48`). A literal typed with a hyphen-minus (`"-12"`) fails
  `RunNumbers`.
- A plus only where the sign matters: `+0.8 dB`.
- **Ranges take an en dash** (U+2013): closed between plain numbers or note names
  (`1–127`, `1.1–5.1`, `C1–C3`), spaced when either endpoint contains a space
  (`0.20 – 1.50 s`, `20 Hz – 20 kHz`).
- Fixed precision: dB one decimal · tempo two (`120.00`) · percent whole · Hz whole up to
  999, then `3.2 k`. Helpers: `NotaNum.Db`, `Hz`, `Pct`, `Bpm`, `Time`, `Unit`, `F`.
- Width must not jump across 10 or 100 — mono plus fixed precision.
- Axis labels in graphs and clip lengths in beats may use variable precision.

### Labels and copy

- Parameter labels: English, sentence case, one word where it fits — `Decay`,
  `Pre-delay`, `Feedback`. Views may still pass caps literals (`"PRE-DELAY"`): every label
  builder routes them through `NotaText.Label`, which sentence-cases an all-caps string and
  keeps acronyms and units (`LFO`, `EQ`, `ADSR`, `MIDI`, `dB`, `Hz`, `kHz`, `ms`) in their
  own case (`NotaText.Fixed`, which also holds `LUFS`, `USB`, `OK`, `DAW`). A string with
  any lower-case letter is left as written. No colons, no "Value", no vowel-dropped
  contractions (`Fdbk`, `Atk`, `Lvl`); the established truncations `Freq`, `Reso`,
  `Thresh` are allowed — the full words do not fit a 53 px cell.
- **Every label is sentence case** — menu items, command titles, palette headings, window
  titles, tooltips, section heads, buttons: "Save as…", "Live freeze", "Export audio",
  "Recent", "Suggested · Arrangement". Capitals stay only on acronyms and units
  (`NotaText.Fixed`), product and device names ("Nota Aurora"), the view names
  (Arrangement, Session, Modular, Mixer — "Back to Arrangement", "Show Mixer") and other
  proper nouns (Finder), and a Settings page named after "Settings: " ("Settings: Audio").
  Data keeps its own case: preset names, engine parameter names, MIDI-learn names, user
  data. `RunLabels` holds the rule.
- Shell tooltips: short and verb-first — "Snap clips to the grid (hold Alt to drag
  freely)", "Clear the peak hold". Not "Click to…". The value stays on the control;
  a tooltip may add the unit or range.
- Browser category tags lowercase (`bass`, `analog`; acronyms stay `EQ`, `LFO`); the
  processing-type badge is mono, muted, sentence case (`Convolution`, `Dynamics`).
- Preset names describe a place or material (`Stone Vault`, `Tape Glue`), not an emotion.
  Device names are one word from the musical vocabulary (`Chamber`, `Prism`).
- Middle dot `·` separates meta. Sentence case. No emoji, no exclamation marks.

## Controls

Builders live in `DeviceCardKit`; controls in `Controls/`. Reuse before writing anything.

### Interaction contract

- **Vertical drag**, up increases, full range over ~140 px; Shift (or Ctrl/⌘) is fine.
  Cursor `ns-resize`. A click never jumps the value. The one horizontal drag is a horizontal
  slider (`SliderTrack`) along its own axis: right increases, the handle follows the pointer,
  cursor `ew-resize`; a vertical drag still works on it as on a knob.
- **Double-click** restores the default where one is supplied.
- **Left button only** — right-click bubbles to the CV-modulate / MIDI Learn menu.
- **`GestureBegin` / `GestureEnd`** bracket `BeginAutomationWrite` / `EndAutomationWrite`.
- **`MidiLearn.Bind`** registers the control with its display name.
- **Live follow** re-reads engine state each tick and skips while dragging.

### Inventory

| Control | Spec |
|---|---|
| `Knob` | 52-grid: groove r21 stroke 5 in Well, 270° from −135°, raised face r14 with a seam edge, pointer 2.4 in primary ink. Value arc: `VizInk` at rest on the audio path (`Accent = true`), `Rule` off the path or disabled, `Accent` while hovered or dragged; `ArcColor` for a modulation source. Sizes snap to 34 / 36 / 44; `Inline` pins 24 for a knob inside a table row. `IsModified`, `IsDim`. |
| `DeviceCardKit.KnobCell` | Knob → name 8/500 sentence case, secondary → value mono 8, no gap; 53 tall under a 34 knob. A modified or `emphasised` parameter lifts its name to primary ink — no accent, no dot. |
| `SwitchTrack` / `DeviceCardKit.Switch` | 18 × 10, knob 7, inset 1.5, radius 2, a squared knob; on = accent track + panel-coloured knob right, off = well track + Ink 5 knob left, on but inactive = `Rule`. Word to the right, 8 sentence case in a device, 12 in the shell (`CheckBox`). The Settings window uses the large 28 × 16 size (knob 12, inset 2). |
| `DeviceCardKit.Segments` | Well strip with a rule edge; chosen = solid accent with derived ink, 600. 9 px in a device; in the shell `ToggleButton.seg` (24, raised, 12 px, the weight unchanged when chosen so auto-width keys never shift) inside `Border.segmented` — keys butted on 1px seams, only the group's outer corners round. |
| `SliderTrack` / `DeviceCardKit.SliderRow` | Name 9/500 · 3 px well track · 6 × 7 handle · fixed-width mono value right. Fill `VizInk` and handle primary at rest, accent while hovered or dragged; teal under modulation. Bipolar fills from centre. Inactive drops to `Rule` and faint ink, keeps the number. |
| `MiniFader`, `PanBar`, `ValueBar`, `MixBar`, `VFader` | Track-header gain and pan, value bars, mixer fader — same drag contract, same rule: neutral at rest, accent while live. |
| `DragNumber` | Mono field: drag, or double-click to type. |
| Buttons (`Button`, `.primary`, `.ghost`, `.cell`, `.chip`, `.tp-icon`, `.tp-play`) | 26 tall, radius 3, raised with a seam border and the bevel at rest. A context's primary action is set apart by weight (600) and one tone step (raised hover), **not by colour**. A toggled-on key is solid accent with derived ink. A destructive action is an ordinary raised key; its label says what it does. |
| Readouts and fields (`Border.well`, `.cell`, `TextBox.field`, `ComboBox`, `.search`) | 26, well fill, 1 px rule edge, radius 3; a mono value with its muted unit on one line. Focus = the edge turns accent, no ring. |
| Filter chips | 20, radius 3, 9 side pad, raised with a seam border; a category-colour dot instead of an icon; active = solid accent with derived ink. The Favorites / tags strip is one line and overflows into `+N`; the device tabs' category row wraps instead, so every category stays in sight. |
| Track toggles (M / S / arm) | Raised keys at rest; on = solid `Mute` / `Solo` / `Record` with `InkOn` text. |
| `Glyph` | The one icon set (§ Icons): geometry on a 16-unit grid, a round-capped 1.5 px stroke at 16 px (scaled, min 1.2) or a solid fill. No icon font, no unicode glyphs. |
| `MeterBar`, `StereoMeter`, `GrMeter` | 11 wide, 5 apart, radius 2 on Well; green → −6 dB amber → 0 red; −60…+6; peak hold stays until clicked. |
| `Inactive.Set(root, on, interactive)` | Puts a subtree into the disabled look by colour (faint ink, accent → rule, controls `IsDim`). |
| `NaBadge` | Feature not built yet; the control stays so nothing shifts later. |

**Record:** at rest a raised key with a red disc; engaged, solid `Record` with a
`RecordInk` disc. The same rule arms tracks in the arrangement, mixer and Session.

### States

| State | Treatment |
|---|---|
| Rest (raised) | Raised · seam border · Ink 1–2 · `Shadow.Raised` bevel |
| Hover | Raised hover (120 ms) · nothing moves |
| Pressed | Well · bevel hidden — no 1 px shift |
| Toggled on / chosen option | Solid accent · derived ink (`TextOnAccent`) · bevel hidden |
| Selected row | Raised hover + 2 px accent tick on the left; name Ink 1 |
| Selected tab | Primary ink + 2 px accent underline |
| Focus (fields, readouts, keys) | 1 px accent edge, no ring, no glow |
| Disabled | Panel ground · faint ink — no opacity |
| Live value control | Arc / fill / handle turn accent while hovered or dragged |
| Modified parameter | Name lifts to primary ink; no accent, no dot |

**Motion:** hover and colour 120 ms ease-out (`BrushTransition`); nothing real-time is
animated. **Empty state:** one line of Ink 5, centred, no illustration or call-to-action
button; details go in a tooltip; the area keeps its size and frame.

### Icons

`Controls/Glyph.cs` is the app's only icon set: every icon is a `GlyphKind`, used as a
`Glyph` control or drawn with `Glyph.Draw` inside a `Render`. Nothing else parses path
geometry, and no icon is a typed character (`RunIcons`).

- **Grid:** a path kind is drawn on a 16-unit grid and scaled as a whole to the size asked
  for — never `Stretch.Uniform`, so a wide and a tall icon keep their optical balance. The
  natural size is 16; the transport shapes keep theirs (Stop 10, Record 11, Play 11 × 14).
- **Stroke:** stroked parts take the shared pen — round caps and joins,
  `max(1.2, size × 1.5/16)`, so 1.5 px at 16. Icons in the rail, browser, Preferences,
  palette, history and player match in weight.
- **Filled vs stroked:** a shape is outlined unless it needs mass to read. Filled: the
  transport shapes, the view keys Arrangement and Session, the OS marks (Apple, Windows,
  Linux), `StarFilled` (a favourite; `Star` is the outline), the disclosure carets and
  Snap. Metronome, Follow and Automation mix a stroked frame with a filled weight, flag or
  handle. Fills take the ink, never a second colour.
- **Kinds:** besides the transport and context-menu set — the views (`Arrangement`,
  `Session`, `Modular`); rail and Settings pages (`Keyboard`, `Sliders`, `Folder`,
  `Downloads`, `Star`, `Bookmark`, `Doc`, `Midi`, `Map`, `History`, `Sidebar`, `Options`,
  `Search`, `Gamepad`, `Phone`, `Plug`, `Library`, `Appearance`, `Shortcuts`, `MidiPort`);
  palette kinds (`Action`, `Track`, `Device`, `Preset`, `Patch`); the player (`Loop`,
  `Speaker` with `SpeakerLow` / `SpeakerHigh` / `SpeakerMute` layered on it, `Levels`);
  the transport toggles (`Metronome`, `Follow`, `Snap`, `Automation`); `ChainLink` (Live
  freeze); `CaretRight` / `CaretDown`. Each kind carries a one-line comment naming what it
  depicts.
- **A new icon** is a new kind in `Glyph.cs`, drawn on the grid with the shared pen — not a
  path in the view that needs it.

## Visualisers

About 45 custom-drawn views in `Controls/`. A device draws **the thing it does**, and where
possible draws it for real (the reverb tail is an actual impulse response; the amp
harmonics push a sine through the real waveshaper).

All live in one **graph window** (`Controls/NotaGraph.cs`):

- Well ground, 1 px rule frame (`GraphBorder`), radius 3, grid in `VizGrid`.
- Axis labels mono 7.5 in faint ink, **in the corners** — never full axes with ticks. A
  frequency graph is labelled only in its bottom corners (`20` · `20k Hz`).
- Title sentence case 8/500, muted, top-left; the legend lives **inside** the window, a
  drawn sample next to a value (`NotaGraph.Legend`).
- Primary curve 1.8 px `VizInk`; secondary 1.2–1.6 px in their chroma. **No fills under
  curves, no gradients.**
- Nodes 7 px with a 2 px ground-coloured ring; the active (selected or dragged) node is the
  accent, the rest secondary ink.
- Waveforms at rest in `VizInk`; second channel at 70 %; the trimmed part darkened, not
  hidden.
- Bars: 1–2 px gaps, radius 1, no outline; the accent only for a selected range; updates
  are discrete.
- Timeline: the playhead is 1 px accent with a 7 × 5 flag and no glow; the loop range is
  accent.

## Device cards

Every built-in device is a **700 × 260** card (`DeviceCardKit.CardH`, width from
`IDeviceBody.Width`): panel body, radius 3, a 1 px seam frame. A selected card carries a
1.5 px accent ring outside its frame.
Width exceptions: the parameter stubs and the generic "Open GUI" card are narrow; Nota Bass and
Nota Physical keep their wider layouts; **Nota Volt is 790** since it took over Nota Synth and
Nota Bass (specs/026) and its Osc rows carry Shape and Pulse Width.

- **Header 22** (`HeaderH`, built by `DeviceChainView.Shell.cs` `BuildCardShell`): a
  raised strip with the `Shadow.Raised` bevel and a seam under it, in **fixed slots, left to
  right: name · preset · badges · power**.
  - **Name** (12/600), in the left half of the header, trimmed before it reaches the
    picker. A compact card drops the "Nota " prefix.
  - **Preset** — the picker `‹ Name ⌄ ›`, an 18 px well field, **centred in the header**
    between two equal halves: the name opens the factory presets, the chevrons step through
    them, wrapping. Only when the device has presets.
  - **Badges**, right-aligned before the power slot: the processing-type badge (mono 10,
    muted, sentence case; instruments add live voices, `Subtractive · 3/16`), then the
    card's own accessories (Learn, the S / L size toggle).
  - **Power** — the bypass switch, accent while the device is on. The slot
    (`SwitchTrack.W` wide) is reserved on every card, empty when the device cannot be
    bypassed (instruments), so right edges line up down a chain.

  A/B, move and delete live in the header's right-click menu; drag the card by its whole
  header; Delete removes it. Every card uses this header — effects, instruments, MIDI
  effects, racks (the Instrument Rack's "Rack · N chains" is a badge), the generic
  instrument card, hosted plug-ins — and the Sends card, a `Utility` card with no device
  menu, selection or presets.
- **Body 238**, inset 6 (`NotaSpace.DeviceInset`) unless the body is `FullBleed`. A knob
  row is 52–54 tall.
- **Sections are divided by rules, not boxed.** `DeviceCardKit.Section(body, edges)`
  (`Controls/RuleSection.cs`) has no fill and no frame — it sits on the card surface — and
  draws a 1 px `Rule` only on a top or left edge that faces a neighbouring section. By
  default (`SectionEdge.Auto`) it finds those from the layout: a section within 16 px that
  overlaps the edge by at least 4 px; a caller may name the edges instead. It keeps the
  1 px inset of the old frame, so content does not move. Sections keep gap 5 and section
  inset 6–8, titled in `DeviceSection` (muted 600). The cards' local section helpers
  delegate to it. **Not sections:** graph windows (`NotaGraph`) and wells keep their frame; chips,
  selectable tiles, rows and pads, and readouts keep theirs.
- **Three columns, left to right: choice → work → output** — source or preset on the left,
  graph and parameters in the middle, levels and mix on the right.
- **Values at rest are neutral**; the accent marks only what is live or chosen (a knob in
  the hand, a chosen segment, a switch that is on, the playing step). Modulation shows its
  source chroma (teal for LFOs and envelopes) on the arc.
- A bypassed card keeps its layout and turns to the disabled look via `Inactive`.
- **Add device** — the end of a chain is a 28 px-wide key with a `Plus` glyph and a
  tooltip; its dashed outline (`BorderStrong`, radius 3) shows only while a drag is over
  the chain, so an empty dock stays quiet. A rack's chain ends the same way.
- **The chain** sits tight in the Devices panel: a 6 px inset all round and 6 px between
  cards (`DeviceChainView.ChainInset` — just room for the selection ring), and the panel
  opens at exactly the cards' height (seam + 32 px toolbar + insets + 260), so nothing
  floats above or below the cards.

Width exceptions, by decision: **Bass 1060, Physical 720** (squeezing them would be a
redesign) and the parameter-list stub (190). A hosted plug-in (VST3 / AU) is a two-size
card like the S / L instruments: **S 260** names it (name, vendor, format, kind, params,
latency) over *Open editor* and *Save preset*; **L 700** adds its parameters as a paged,
filterable knob grid (and the sidechain source when it has a bus). Rhythm moved onto the
700 frame with its redesign.

## Shell

```
┌ title 32 ─ "Nota", centred, on ground ─────────────────────────────────┐
├ transport 44 ─ project · view switch · console · tempo · …  CPU master │
├─────────────┬──────────────────────────────────────────────────────────┤
│ browser     │ canvas (arrangement · session · modular)                 │
├─────────────┴──────────────────────────────────────────────────────────┤
│ toolbar 32 ─ Devices · Pattern · Clip · track · Freeze · ⧉ ×           │
│ detail body ─ cards                                                    │
├────────────────────────────────────────────────────────────────────────┤
└ status 22 ─ caption text on ground ────────────────────────────────────┘
```

Every region is **docked**: radius 0, no gutter, separated by 1 px seams (the window's
`Seam`-coloured ground showing through a 1 px margin).

- **Title bar 32** — frameless, on ground, a seam below; macOS traffic lights in a left
  inset, "Nota" (`Heading`) centred — the project's name lives in the transport; Windows
  reserves 180 px for the caption buttons.
- **Transport 44, under the title bar** — a flat full-width row on `Panel` with a seam
  below (`Border.transport`). Stop / play / record, position and loop share the console,
  one 30 px recessed group on the well with a rule edge (`Border.console`), keys butted on
  1 px seams. It reads left to right: the project name (13 semibold, Ink 1) · view switch
  (three 28 × 24 icon keys — `Glyph` Arrangement · Session · Modular — in a segmented
  group, the current one solid accent; the tooltip and accessible name are the view name)
  · the console · tempo `120.00 BPM`, signature, key,
  grid — each a 26 px readout well with its muted unit beside it · the switches
  (metronome, follow, snap, automation) — then, pinned right, MIDI, CPU and master.
  Transport keys are 26, Play 36 wide; Play is raised at rest and solid accent only while
  the transport runs. There is no add-track button — tracks come from the browser. Launch
  quantize lives in the Session toolbar, not here. Transport buttons are not focusable, so
  Space / R / L / Return always reach the window. `MainWindow.TransportFit.cs` sheds the
  least-needed parts first when the window is narrow.
- **Body** — the browser on the left and the canvas meet on a seam. The splitter is a
  transparent 6 px grab strip centred on the seam, with the same limits as before.
- **Detail panel** — docked under the canvas on a seam; its body is the ground. It opens
  with a 32 px panel toolbar on `Panel` with a seam under it: the mode segments, the track
  (colour square, name 12 semibold, routing summary), raised 22 px Freeze / Live Freeze, a
  rule, then pop-out and close as ghost icons.
- **Status 22** — one line of `Caption` text on the ground, a seam above it, aligned 16 px
  in.
- **List rows** are 26 and one line: a colour dot or bar, the name, mono metadata right;
  hover raised hover, selected raised hover with a 2 px accent tick. A built-in device's
  browser row shows its short name ("Aurora", not "Nota Aurora" — search still matches the
  full name); the type tag on the right is `TextFaint` mono 10.
- **Category chips** — the Instruments, Audio effects and MIDI effects tabs carry a row of
  category chips (the filter chip: 20 tall pill, sentence case) between the search row and
  the Favorites / tags row. It wraps rather than overflowing into "+N", because every
  category should stay in sight. One chip is picked per tab, and a picked chip narrows the
  list, stacking with search and the Favorites / tag chip. `Other` comes last and appears
  only when an unrecognised plug-in lands there. "Category chips" in ⋮ hides the row. The
  categories live in `DeviceCategories.cs`.
- **Mixer strips** are panel-tone columns on seams, each with a 3 px top bar in the track
  colour (muted grey for master).
- **Arrangement** (geometry unchanged): track rows 64 (the header carries every control),
  group rows 26 (open to 64 while selected), header column 228, default zoom 28 px per
  beat. Every y ↔ row conversion goes through `ArrangementView.RowTop` / `RowAtY`. A
  group's rows step their 3 px colour spine 10 px further in per level (`GroupIndent`),
  the name just past it. Above the tracks: Overview 34, Sections 22 (spans alternate two
  neutral tone steps), Zoom + ruler 24. The horizontal scrollbar is a 12 px well with a
  5 px thumb (`ThinScrollBar`). Audio clips draw their waveform as spaced bars by default —
  a 1 px stroke every 3 px, mirrored about the centre; Settings → Appearance → Waveform
  switches to Solid (a column per pixel). The pinned Return / Master band meets the tracks
  on a 1 px seam; below the last track the lane side keeps the lane colour and the header
  side the header colour. A new track is scrolled fully into view
  (`ArrangementView.RevealTrack`).
- **Clips** are flat 2 px-radius rectangles: a full-colour body in the track colour, a
  12 px title band across the top in the track colour mixed 30 % toward black (the accent
  when the clip is selected) with the name in 9.5/600 derived ink, and the content — notes
  or waveform — in a deeper shade (58 % toward black). A row too short for the band keeps
  a 2 px band.
- **Clip editor** (MIDI and audio share one frame, `ClipEditorKit`): header 38 whose first
  232 is the clip cell (3 px track-colour bar, name, mono kind badge), modes as shell
  segments after it; inspector 232 on the left, sections split by rules and ending in one
  Ink 5 hint line; the canvas sits in the well. MIDI: keys 48, rows 12, a beats-only
  ruler 24 with the loop as a 3 px accent strip, notes in the track colour quieter by
  velocity, selected notes accent, scale rows a neutral lift; the tools panel is 272 and
  Apply is its primary action. Audio: a ruler 24, a 22 px warp-marker strip, and
  everything outside the played region darkened. Envelopes and expression curves are
  neutral viz ink; only the point under the pointer is the accent.

- **Session** — the inspector column is 260 and can be hidden: an Inspector key (`Glyph` Sidebar, its tooltip names it) at
  the right end of the toolbar, remembered (`SessionInspectorOpen`, on by default). When
  the toolbar is narrow its least-used controls fold, one at a time, into a "⋯" menu that
  lists whatever is hidden — the position readout first, then Back to Arrangement, Capture
  scene, the section toggles, Follow — so every control stays reachable.
- **Modular** — the sidebar always lists the tracks; opening the view with nothing
  selected selects the first track that is neither a group nor a return. Empty states:
  "No tracks yet" in the sidebar, "Pick a track to see its signal graph" on the canvas.

### Menus, popovers, dialogs

- **Menus, context menus, flyouts, drop-downs, tooltips:** raised, radius 3, a 1 px seam
  ring. Menu items are 24 tall, radius 2; hover is a full accent fill with derived ink;
  the shortcut on the right is muted ink (derived ink while highlighted); separators are
  rules. **No blurred shadow:** Avalonia draws each popup in a window of its own, sized to
  its content, so a blur would be clipped at the popup's edge.
- **Command palette** — the in-window modal: panel body, radius 4, seam ring and
  `Shadow.Modal`.
- **Dialog windows** (`NotaWindow`): panel body; on macOS and Windows a 32 px raised header
  with the bevel and a seam below, the title in `Heading`. The OS draws the window shadow.

### Floating windows

The mixer and the export dialog are OS windows (`NotaWindow`), not in-window overlays, so
the mixer can live on a second screen. The command palette is the only in-window overlay.

- **Escape** closes a floating window, as it closes the palette: the mixer closes (unless a
  text field has focus — it owns Escape then), Export cancels (an open drop-down takes its
  own Escape first).
- **The mixer sizes to its strips** (`MixerWindow.FitWidth`): the window is as wide as the
  strips and master plus padding, clamped to [560 (`MinWidth`), the screen's working
  width], so there is no dead area right of the master. It refits when a track is added
  or removed while it is open, and a window that grows past the screen's right edge moves
  back onto it. The height stays 460.
- **Titles are sentence case:** "Mixer", "Export audio", "Edit tags", "What's new in
  Nota", "Connect phone".

### Nota Remote

The phone page (`src/web/remote`) follows the same system (spec 014). It holds no colour
value: Nota sends the theme it wears and the user's dark-mode and light-mode picks with all
their tokens (the `theme` message), and the page sets each token as a custom property named
after it — `BgApp` → `--bg-app`, `SurfaceRaised` → `--surface-raised` — plus the derived
inks `--ink-on-accent` / `-mute` / `-solo` / `-record` / `-success` (`NotaPalette.ReadableInkOn`'s
pair, which Nota sends). **Follow Nota** wears Nota's theme, **Follow phone** a pick by the
phone's own scheme. Before Nota answers, the page wears the last theme it stored (a head
script paints it before the app loads), or the Studio Dark fallback that `theme-plugin.ts`
builds from the theme files at `npm run build`. Elevation is built the same way:
`--bevel` (`ShadowBevel`), `--sunken` (`Rule`), `--popover` (`Seam` + `ShadowPopover`),
`--scrim` (`ShadowModal`).

Structure is Studio's — seams, the accent for live or chosen only, solid semantic fills with
derived ink, sentence case, radii 0 / 1 / 2 / 3 — at phone sizes: touch targets stay
36–56 px and words 11–16 px (the big position and the count-in larger). The phone's icons
are inline SVG drawn with the shared stroke (1.5, round caps and joins), since `Glyph` is
native. `npm run mock` serves the built page against a stand-in Nota with a demo project and
the real theme messages, for screenshots without the desktop app.

## Enforced by tests

`tests/Nota.SmokeTest/DesignTokenCheck*.cs` runs in the smoke test (the `-- design: … --`
sections) and fails the build on:

| Check | Holds |
|---|---|
| `Run` | `NotaTheme.axaml` holds no colour; every referenced `Brush.*` key is published (`KeyMap`); every `Shadow.*` key is built from the theme |
| `RunThemes` | Every theme file: shape, id, kind, exactly the token catalogue, valid opaque colours (shadows may be translucent), and every readability pairing (§ Readability bar); `BuiltInOrder` lists exactly the files |
| `RunAppearance` | Old settings migrate (Dark / Light / System); unknown and wrong-kind ids fall back; storing keeps the legacy setting in step |
| `RunGeometry` | XAML ↔ `NotaGeometry` values; no undefined `Radius`/`Control`/`Space` keys; no `CornerRadius` literals, drop shadows or gradients in views; `Rectangle` `RadiusX` / `RadiusY` literals on the 0/1/2/3/4 scale (`RadiusAllowList.txt`) |
| `RunType` | Fonts bundled and matched; no font names in views; nothing under 7 px |
| `RunControls` | No unicode icons typed as text; Knob / Switch / Slider sizes |
| `RunVisualisers` | Graph windows via `NotaGraph`; no fills under curves; no meter ballistics; meter 11/5; playhead 1 px without glow |
| `RunLayout` | Device header 22; cards 700 except the listed exceptions; transport a docked row at the top at `Control.TransportBar` with its recessed console; browser rows 26–28 |
| `RunNumbers` | Display culture installed; no hyphen minus in a negative format section or before ∞; no hyphen-minus before a digit in a view literal (`"-12"`, `" -3"`; `NumberAllowList.txt`); no invariant culture on readouts; thin space before units; no vowel-dropped labels |
| `RunBans` | No hex outside `Ink()` (bar three data tables), no RGB typed as numbers, no named system colours, `Opacity` only on drag ghosts, no brush snapshotting a theme colour |
| `RunLabels` | No all-caps or Title Case label literal in a label context (`Text`, `Content`, `Header`, `Title`, tooltips, menu headers, command titles, palette and Settings headings, local label builders), unless it is `NotaText.Fixed` acronyms, goes through `NotaText.Label`, or names a view, product or proper noun; interpolated strings are skipped. `LabelAllowList.txt` holds only data (preset, mode and window-tab names, proper names) |
| `RunIcons` | No path geometry outside `Controls/Glyph.cs`: no `Geometry.Parse` / `StreamGeometry.Parse` / `PathGeometry.Parse`, no axaml `Data="M…"`, no SVG path string. `IconAllowList.txt` is empty |
| `RunBrowser` | Every built-in device sits in exactly one category of its own tab (`DeviceCategories.cs`); no category names a missing device or is empty; names in sentence case; 18 / 26 / 6 devices shelved |
| `RunStudio` | Every `Radius.*` on the 0/1/2/3/4 scale; only `Shadow.Popover` / `Shadow.Modal` blurred, no literal `BoxShadow` in views; no positive `LetterSpacing` in the theme, `NotaType` tracking 0, no upper-casing in the shared label builders; knob arcs, slider fills and graph curves neutral at rest (`VizInk`); transport 44 · console 30 · seam 1; no Ember value in the Studio themes |
| `RunRemote` | Nota Remote's page: no colour literal (hex, `rgb()`, `hsl()`) in its sources or `index.html`; the first-paint fallback built from the theme files; no letter-spacing, `uppercase`, `.toUpperCase()` in a component or `.caps`; radii 0 / 1 / 2 / 3 / 4 px or 50 %; every custom property it reads is a theme token or one it defines |

When a check fails, fix the view — or, for a genuine exception, add it to the check's
allow-list with a comment saying why. The ratchet lists in `tests/Nota.SmokeTest/`
(`LabelAllowList.txt`, `IconAllowList.txt`, `NumberAllowList.txt`, `RadiusAllowList.txt`)
only shrink: a new offender fails, and so does a line that no longer matches anything.

## Accepted exceptions

Places where a screen's needs override a general rule, kept on purpose:

- **Device cards keep the 700 × 260 frame** (and the width exceptions), so device text sits
  at 7.5–10.5 px, the densest type in the app. Sentence case is narrower than tracked caps,
  so 8 px fits where 7 px caps did.
- **Device header 22**, not 24; the power switch sits at the right of the header, not as a
  key leading it.
- **Nine track roles**, so stored track indices keep their meaning: five core clip colours
  plus four at matching chroma.
- **Transport 44, not 40**, and it keeps Nota's layout and order (view switch leading,
  console of transport · position · loop, master pinned right). 44 keeps the console's
  position and loop legible without reflow.
- **No in-window menubar row.** macOS uses the system menu; Windows / Linux keep the native
  menu bar. The title bar is 32.
- **Menu popups without a blur** (see § Menus) — a seam ring only.
- **Primary actions by weight, not colour** — the accent stays reserved for state; Nota has
  no solid-accent action button.
- **Play turns green in Session** (`tp-play.session`) while clips run.
- **Data hues by `Ink()`** — Strata layer `#C99C55`, tag swatch `#C8A24B`, Random
  `#D0603F`, the Rhythm kit dots — are data colours, not accents; their light values are
  derived or pinned.
- **Segments with more than four options** stay segments where the choice is frequent:
  Amp Model (7) and Cabinet (5), Compressor Character (5), Vintage Character (6), Delay
  Division (8), Arp Rate (8) and Order (8), Aurora Warp (5) and Filter (5), Bass LFO wave (5),
  Monolith Glide range (6), Pendulum Division (5) and Wave (5).
- **Inline knobs (24)** inside table rows — the oscillator rows and ADSR strips of Consort,
  Pentad and Monolith (and Monolith's unison pair); a 34 knob cannot fit those rows.
- **Preset picker in the device header**; switching presets is frequent enough to stay one
  click away.
- **Zoom 28 px per beat** by default; arrangement rows are 64 and the track header 228.
- **`Freq`, `Reso`, `Thresh`** as labels (see § Labels and copy).
- **Hero readouts** larger than the device scale — Strata bar number, Shutter Open/Shut,
  Auto Shift note, Level value.
- **Filter chips 20**, not 24 — the browser's chip row is dense.

## Known drift

True today, not yet fixed:

- **Slot names still say Brass.** `BorderBrass`, `AccentSubtle` (the old "Brass Wash"),
  `DeviceCardKit.Brass` (an alias of `Accent`), `Inactive.IsBrass`, and several aliases
  (`Gutter`, `BorderDefault`, `Hairline`, `TextDisabled`, `TextAxis`, `ChromaSteel`) name
  their Ember role, not their Studio one. Comments in a few views (`Inactive.cs`,
  `BrowserView.cs`, `DeviceChainView.Shell.cs`, `QrView.cs`, `BrandMark.cs`,
  `ScaleNoteMap.cs`, `ChordKeysViz.cs`) still say Graphite, Paper, brass or almanac. A
  rename pass can follow.
- **Accent judgement calls in device views.** Each device's visualisers were audited for
  the live-or-chosen rule; a few accent uses were kept as "chosen" and are worth a second
  look: the last-struck ball in Pendulum, in-scale note chips (Scale map, Auto Shift), the
  live pitch readout in Auto Shift, the step strip wash in Rhythm, the zone chip in Mosaic,
  the macro-mapped chip in a rack (teal might suit it better).
- **The user manual** (`../nota-docs`) screenshots still show Ember; they are not re-shot.
- **Column order** choice → work → output is applied in Utility; the other cards have not
  been audited card by card.
- **Slider reset** on double-click works only where the card passes a default.
- **Names:** presets with emotional names (Screaming Lead, VHS Fever, Gentle Master,
  Transparent Safety, Dive Bomb, Laser Zap…) and multi-word device names (Auto Filter,
  Beat Repeat, Dynamic EQ-8, EQ-3, the "Nota " prefix — dropped only from browser rows and
  compact card headers) remain; renaming touches MCP names, the browser and saved projects.
