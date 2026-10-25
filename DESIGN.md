<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Nota design guideline

This file sets out how every part of Nota is drawn, so new screens and devices match the
rest of the app. It is the rule set the app is held to: the principles, every colour,
size and corner, how each control and card looks, the tests that enforce some of it, the
few accepted exceptions, and what is known to be out of step.

The look comes from the **Nota style guide** agreed with the owner in October 2026
(`specs/038-style-guide/`: the requirements in `spec.md`, decisions in `research.md`). It
redraws the app without moving or removing a control. In short: a flat grey shell so the
track colours carry the content, depth only inside device cards, orange in three clearly
different strengths, and one typeface with fixed-width digits. It replaces the earlier
"Studio" rules (spec 010) where they differ; the colour themes, the token mechanism and
the interaction contract carry over.

The design was built on the `ui/style-guide` branch: the tokens, the typeface, the corner
scale and the shared sizes first, then the controls, the shell, the editors, every device
card and the Modular view. This file describes what the app draws; § Known drift lists the
few places that still fall short, and § Enforced by tests says what a test holds.

Colour is a **theme** (§ Colour themes). Layout, type, shapes and where orange goes are the
same in every theme; only the values change. Four themes are built in: **Studio Dark**
(the default), **Studio Light**, and **Ember Graphite** / **Ember Paper**, the warm browns
and brass of Nota's earlier look on the same structure.

**[→ Open `DESIGN.html`](DESIGN.html)** for the visual reference: every colour, the
controls and cards drawn live from the theme files, with a theme switch.

Sizes are in pixels at 100 % interface scale. Text sizes are written as size / weight:
13 / 600 is 13 px semibold, 400 is regular.

## Source of truth, in order

| Rank | File | Authority |
|---|---|---|
| 1 | `src/managed/Nota.App/Theme/Themes/*.json` | **Definitive for colour.** One file per colour theme, one value per token. |
| 1 | `src/managed/Nota.App/Theme/NotaTheme.axaml` | **Definitive for the rest:** corner scale, sizes, spacing, type classes, control styles. It holds no colour. |
| 2 | `Theme/NotaPalette.cs` · `NotaColourTheme.cs` · `NotaThemeService.cs` · `NotaGeometry.cs` · `NotaFonts.cs` · `NotaText.cs` · `NotaNum.cs` | The C# side: the token slots (the catalogue), theme loading and switching, the corner scale, sizes and spacing, the typeface with tabular figures and the type scale, sentence-case labels, number setting. |
| 3 | `DeviceCardKit.cs`, `Controls/` | The shared controls and card builders. |
| 4 | `tests/Nota.SmokeTest/DesignTokenCheck*.cs` | Enforces the theme files against the catalogue and the readability bar, the wiring, and the bans in § Enforced by tests. |
| 5 | `DESIGN.html` / this file | Documentation. If these disagree with 1–4 on a value, **these are wrong**: fix them. Where the code has not reached the target yet, § Known drift says so. |

## How theming works

Colour has one home: the active theme's file. `NotaPalette` declares one **slot** per token
by name: one long-lived `SolidColorBrush` whose `Color` is re-pointed from the theme on
every switch, so views keep the same brush object and nothing is rebuilt.
`NotaThemeService` publishes those same brushes into `Application.Resources` under their
`Brush.*` keys (`NotaPalette.KeyMap`), and builds the `Shadow.*` elevations and Fluent's
`SystemAccentColor*` ramp from the theme's colours. So XAML **must** use
`{DynamicResource Brush.*}` / `{DynamicResource Shadow.*}`; a `StaticResource` cannot follow
a theme change. Corners, fonts and sizes are theme-independent (`StaticResource`).

A slot without a `Brush.*` key (`SurfaceDeep`, `AccentPale` today) is reachable from code
only; give it a `KeyMap` entry before XAML uses it.

Ways to get a colour in code:

| Call | For |
|---|---|
| `NotaPalette.Accent` (any named slot) | a token |
| `NotaPalette.Wash(slot, 0x28)` | a translucent tint of a token; cached, follows its slot |
| `NotaPalette.InkOn(fill)` | text or a glyph on a solid fill: dark ink (`#17181A`) or white, whichever has the higher contrast on the fill's current colour. `ReadableInkOn(color)` for a fill computed at draw time |
| `NotaPalette.Ink("#6A9FD8")` | a one-off data hue or stored tag colour. A hex equal to a role token returns that token; a hex that was an Ember role (`#D8A03D` brass, `#5B9E9C`, `#B57286`, `#6D8FB5`, `#B8862E`, `#2A2317`) returns the slot that plays that role now (`LegacyRole`); any other gets a derived light value, or one pinned in `InkOverrides` |
| `NotaPalette.Derived(() => …)` | a colour a view computes from slots (a 20 % track tint over the lane, say); register once and cache it |
| `ArrangementView.TrackBrush(index)` | a track colour |

**Nothing may snapshot a colour.** `new SolidColorBrush(slot.Color)`, a
`static readonly Color` taken from the palette, or a static array of palette colours all
freeze a view in the theme it was built in. Use the slot, a `Wash`, `InkOn`, a property
(`static Color X => NotaPalette.Accent.Color;`), or read `.Color` inside `Render`.

### Changing a token

1. Change its value in **every** theme file in `Theme/Themes/` that should change, at
   least both Studio themes. A new token is a new `T(nameof(X))` slot in `NotaPalette`
   plus a value in every theme file (the smoke test names any file that lacks it), plus a
   `KeyMap` entry if XAML uses it. A see-through token also goes in
   `NotaThemes.AlphaTokens` and the smoke test's copy of that list.
2. Every theme must still pass the readability bar (§ Readability bar).
3. Run `python3 scripts/design-html.py` to regenerate `DESIGN.html`, and update the
   tables in § Colour.
4. Look at it in every theme: the app (Preferences → Appearance) and `DESIGN.html`.

## Colour themes

A colour theme is one fixed look: an id, a name, a kind (**dark** or **light**) and one
value for every token. The format is `specs/011-colour-themes/contracts/theme-file.md`.
The kind sets the OS window chrome and Fluent's base, and decides which follow-system list
the theme appears in. A theme changes colour only: never shapes, type, layout, or which
element carries orange.

| Theme | Kind | Values |
|---|---|---|
| Studio Dark | dark | The § Colour tables |
| Studio Light | light | The § Colour tables |
| Ember Graphite | dark | Nota 0.52.1's dark palette by token name. Tokens added since take the role's value worked out from Ember's own grounds (seam = Ember's gutter, rule = its strong border, mute = its amber, solo = its steel blue…); the style guide's brushes take the guide's Ember starting values, kept by the step each colour has from Ember's backgrounds |
| Ember Paper | light | The same, from 0.52.1's light palette |

**Appearance.** Preferences → Appearance picks a mode (**Dark**, **Light** or **Follow
system**, a dark-mode and a light-mode theme switched with the OS), then a theme of that
kind, each shown as a miniature Nota window in its own colours; every change is live. Old
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

Track colours are not compared with state colours: solo blue is the blue track's colour by
design. The bar does not cover the see-through wells or the device greys (they carry no
text of their own); check those by eye in every theme. **Known gap:** in Studio Light the
orange line on the ground (`AccentBright` on `BgApp`, 2.39) and ink 4 (2.58) sit below
WCAG's 3:1 for graphics and incidental text; T3.3 / T3.4 hold Studio at those values until
a change raises them. Orange text on the soft tint is not in the bar either; in Studio
Light it is `AccentPale` (5.52 on `AccentSoft`) rather than `AccentBright` (3.12) for that
reason (§ Orange).

### Adjustments

Where a value from the guide or from Ember's 0.52.1 palette failed the bar, it was moved
the smallest step in HSL lightness (hue kept) that passes:

| Theme | Token | From | Shipped | Failed |
|---|---|---|---|---|
| studio-dark | `TextDisabled` | `#6E727A` (the guide's ink 4) | `#757981` | T3.3 TextDisabled/SurfaceHover 2.29 < 2.5 (now 2.54) |
| ember-graphite | `TextMuted` | `#8D8779` | `#8F897C` | T3.1 TextMuted/SurfaceHover 4.45 < 4.5 |
| ember-graphite | `TextTertiary` | `#6E6A5E` | `#716D60` | T3.2 TextTertiary/SurfaceHover 2.94 < 3.0 |
| ember-graphite | `TextFaint` | `#55514A` | `#646054` | T3.3 TextFaint/Panel 2.36 < 2.5; TextFaint/SurfaceHover 2.31; TextFaint/SurfaceRaised 2.41 |
| ember-graphite | `TextDisabled` | `#55514A` | `#646054` | T3.3 TextDisabled/Panel 2.36 < 2.5; /SurfaceHover 2.31; /SurfaceRaised 2.41 |
| ember-graphite | `TextAxis` | `#4A463D` | `#65614F` | T3.3 TextAxis/BgApp 2.10 < 2.5; /Panel 2.38; /SurfaceHover 2.33; /SurfaceRaised 2.44 |
| ember-graphite | `Record` | `#C25B44` | `#BE563E` | T3.5 Record 4.31 < 4.5 |
| ember-graphite | `RecordInk` | `#F4E3DC` | `#FFFFFF` | T3.9 RecordInk/Record 3.67 < 4.5 |
| ember-paper | `TextMuted` | `#746D5C` | `#625B50` | T3.1 TextMuted/BgApp 3.54 < 4.5 |
| ember-paper | `TextTertiary` | `#8A8474` | `#7E786C` | T3.2 TextTertiary/BgApp 2.57 < 3.0 |
| ember-paper | `TextFaint` | `#9E9786` | `#8D866F` | T3.3 TextFaint/BgApp 2.00 < 2.5 |
| ember-paper | `TextDisabled` | `#9E9786` | `#8D866F` | T3.3 TextDisabled/BgApp 2.00 < 2.5 |
| ember-paper | `TextAxis` | `#ADA694` | `#8D8669` | T3.3 TextAxis/BgApp 1.67 < 2.5 |
| ember-paper | `Accent` | `#A87415` | `#AD7716` | T3.5 Accent 4.38 < 4.5 |
| ember-paper | `Track9` | `#4D7A5D` | `#598C69` | T3.7 Track2/Track9 7.53 < 12 |
| ember-paper | `ReturnA` | `#4D7A5D` | `#598C69` | follows Track9 (returns draw from the ninth colour) |

The fifteen style-guide brushes (§ Colour) shipped in both Ember themes at the guide's
starting values unchanged; none is in the bar. Look at them by eye when an Ember ground
changes.

## Principles

1. **Keep every control.** The layout and every control stay where they are; this guide
   sets only how they are drawn. A device keeps every control, label and value it had in
   0.56.1, in the same groups. A control's value is always visible next to it, never only
   in a tooltip.
2. **A flat, grey shell.** Toolbar, browser, track headers, mixer and editors are flat and
   grey, so the track colours carry the content. Panels meet on a 1 px seam. Controls in
   the shell have no border: they sit on a faint well.
3. **Depth only in devices.** The device card is raised, its sections are pressed in and
   their controls sit in grooves; the picked option rises out of its groove. Modular nodes
   are raised like device cards. Nothing else looks raised. Depth comes from slightly
   different greys, thin lines and 1–2 px shadows: no gradients, no blur, no glow, no
   texture.
4. **Orange has three jobs.** Solid for what is live, a soft tint with orange text for what
   you picked, orange lines for values inside a device. Nothing else is orange (§ Orange).
5. **One typeface.** Geist for every word and number, with tabular figures so a value can
   change without the text around it jumping. No monospace face, no capital-letter labels.
6. **Never see-through.** An unavailable option is drawn in ink 4 with nothing picked,
   never faded with opacity. The see-through wells are fills, not a state.

These carry over from the earlier rules and still hold:

- **Semantics by purpose.** Mute on is amber (`Mute`), solo on is blue (`Solo`), arm and
  record are red (`Record`), in the arrangement, mixer and Session alike. Red is also
  overload (`Danger`). Meters run green, amber, red (`MeterSafe`, `MeterHot`, `MeterOver`).
- **Derived ink on every solid fill.** Text or a glyph on an orange, mute, solo, arm or
  track-colour fill takes `InkOn(fill)` (or `TextOnAccent` / `RecordInk` in XAML), never a
  hard-coded label colour.
- **Seam between, rule within.** Panels meet on the dark `Seam`; inside a panel, items are
  split by the lighter `Rule` (toolbar group separators, menu separators, tab rows).
- **State changes colour, never size or position.** Nothing moves on hover. Hover and
  colour changes take 120 ms ease-out (`BrushTransition`).
- **Real time is not animated.** Meters, playhead and spectra jump to the value.
- **Numbers are set, not printed** (§ Numbers).
- **One implementation per control**, with a device variant and a shell variant where this
  guide gives one.
- **One icon set** (§ Icons).

### Words this guide uses

| Word | Means |
|---|---|
| Seam | The thin dark line where two panels meet (`Seam`). |
| Rule | The lighter thin line that splits things inside a panel (`Rule`). |
| Well | The faint fill behind a button, field or menu at rest (`Well`). It takes the place of a border. |
| Soft tint | A pale orange fill (`AccentSoft`) with orange text. It marks the thing you picked. |
| Solid orange | Something live (`Accent`): playing, recording, a switch that is on inside a device. |
| Raised | Lifted off the surface, with a light top edge (`Bevel`). Used inside devices only. |
| Groove | Pressed into the surface (`Groove`), also called a trough. Device controls sit in one. |
| Ink 1 to 4 | The four text colours, from strongest (names, values) to faintest (hints): `TextPrimary`, `TextSecondary`, `TextTertiary`, `TextDisabled`. |
| Shell | Everything outside a device card. Controls there stay flat and grey. |

## Colour

Every colour has one job. Values are as the theme files declare them; the Ember values of
the style-guide brushes are the guide's starting values (§ Colour themes).
See-through values are `#AARRGGBB`.

### Backgrounds

| Use | Brush | Studio Dark | Studio Light | Ember Graphite | Ember Paper |
|---|---|---|---|---|---|
| Line between panels | `Brush.Seam` | `#141517` | `#9DA0A6` | `#0C0B09` | `#E0DAC9` |
| Behind the arrangement; window, title bar | `Brush.BgApp` | `#1E1F22` | `#C9CBCF` | `#0B0A09` | `#DCD6C5` |
| Toolbar, left rail, pinned Return / Master rows, device panel (its toolbar and body), status line | `SurfaceDeep` (slot only) | `#1B1C1F` | `#CFD1D4` | `#131210` | `#E7E2D4` |
| Browser, track headers, mixer strips | `Brush.Panel` | `#26282C` | `#E4E5E7` | `#141310` | `#EAE5D7` |
| Cards in the shell; a bypassed Modular node's header | `Brush.SurfaceCard` | `#2B2D31` | `#E4E5E7` | `#171613` | `#EFEADE` |
| A raised control inside a device (picked segment, preset menu, number buttons); tooltip | `Brush.SurfaceHover` | `#393C41` | `#F7F8F9` | `#252219` | `#E7E1D0` |
| Behind graphs | `Brush.SurfaceInset` | `#232427` | `#DCDEE0` | `#1A1815` | `#E9E4D6` |
| Track lane behind clips | `Brush.LaneB` | `#222326` | `#C4C6CA` | `#131210` | `#E7E2D4` |
| Sunken: a shell slider's body, a wheel | `Brush.BgSunken` | `#17181A` | `#D6D8DB` | `#100F0D` | `#E4DFD1` |

### Wells, grooves and raised parts

| Use | Brush | Studio Dark | Studio Light | Ember Graphite | Ember Paper |
|---|---|---|---|---|---|
| Well behind a button, field or menu at rest | `Brush.Well` | `#0EFFFFFF` | `#0F000000` | `#0EFFFFFF` | `#0F000000` |
| Switch and slider track | `Brush.WellTrack` | `#17FFFFFF` | `#1C000000` | `#17FFFFFF` | `#1C000000` |
| Knob track | `Brush.KnobTrack` | `#1AFFFFFF` | `#21000000` | `#1AFFFFFF` | `#21000000` |
| Groove inside a device | `Brush.Groove` | `#1A1B1E` | `#C9CCD0` | `#0F0E0B` | `#E1DCCE` |
| A pressed-in section inside a device | `Brush.DeviceSection` | `#222327` | `#DCDEE1` | `#13120F` | `#E8E3D5` |
| That section's inner edge | `Brush.DeviceSectionEdge` | `#0BFFFFFF` | `#17000000` | `#0BFFFFFF` | `#17000000` |
| Device card body and Modular node | `Brush.SurfaceRaised` | `#303237` | `#EEEFF1` | `#1C1A16` | `#F8F5EC` |
| Device card header | `Brush.DeviceHeader` | `#303237` | `#F3F4F5` | `#1A1916` | `#F0EBDD` |
| Light top edge on raised parts | `Brush.Bevel` | `#14FFFFFF` | `#FFFFFFFF` | `#14FFFFFF` | `#FFFFFFFF` |
| Knob centre cap | `Brush.KnobCap` | `#44474D` | `#FFFFFF` | `#242320` | `#FFFFFF` |

The knob cap has a 1 px `Seam` edge.

### Lines

| Use | Brush | Studio Dark | Studio Light | Ember Graphite | Ember Paper |
|---|---|---|---|---|---|
| Inside a panel: separators, tab row, menu rules, graph frame | `Brush.Rule` | `#45484E` | `#B3B6BB` | `#3A362D` | `#BDB5A0` |
| Beat grid, zero axis | `Brush.GridBeat` | `#2A2C30` | `#BDC0C4` | `#1E1C18` | `#DED8C8` |
| Bar lines | `Brush.GridBar` | `#3A3C41` | `#ADB0B5` | `#2A2721` | `#CFC8B4` |
| Grid inside a graph | `Brush.VizGrid` | `#2E2F32` | `#C0C3C6` | `#232019` | `#DDD7C6` |
| An orange stroke where a fill is too heavy (focus outline uses `Accent`) | `Brush.BorderBrass` / `AccentEdge` | `#BF7D22` | `#ECA54E` | `#6B5326` | `#C9A254` |

### Ink

| Use | Brush | Studio Dark | Studio Light | Ember Graphite | Ember Paper |
|---|---|---|---|---|---|
| **Ink 1**: names, values | `Brush.TextPrimary` | `#E8E9EB` | `#16171A` | `#E9E4D8` | `#241F17` |
| **Ink 2**: secondary labels, icons at rest, a switch that is on in the shell | `Brush.TextSecondary` | `#C0C4C9` | `#3A3D42` | `#A39D8F` | `#5E5849` |
| **Ink 3**: labels, units, tags, placeholders | `Brush.TextTertiary` | `#92969D` | `#65696F` | `#716D60` | `#7E786C` |
| **Ink 4**: axes, column heads, hints, anything unavailable | `Brush.TextDisabled` | `#757981` | `#797C81` | `#646054` | `#8D866F` |
| Modal and section titles (rare) | `Brush.TextHeading` | `#F4F5F6` | `#0E0F11` | `#F2EDE1` | `#1A150E` |
| Glyphs on transport keys | `Brush.TextStrong` | `#D4D6DA` | `#282A2E` | `#C7C0B0` | `#3E382C` |
| Section labels, counts, badges in the shell | `Brush.TextMuted` | `#A4A9B0` | `#4F535A` | `#8F897C` | `#625B50` |
| Tick labels inside graphs | `Brush.TextAxis` | `#82868C` | `#797C81` | `#65614F` | `#8D8669` |
| Older faint ink; new code uses ink 4 | `Brush.TextFaint` | `#82868C` | `#797C81` | `#646054` | `#8D866F` |
| A curve in a graph; a value at rest outside devices | `Brush.VizInk` | `#D6D8DC` | `#252629` | `#D8D3C8` | `#332E26` |
| Text on solid orange | `Brush.TextOnAccent` | `#17181A` | `#17181A` | `#171613` | `#FFFBF2` |

Text is never faded: a quieter line takes the next ink.

### Orange

| Use | Brush | Studio Dark | Studio Light | Ember Graphite | Ember Paper |
|---|---|---|---|---|---|
| Solid: playing, playhead, loop, power, device values | `Brush.Accent` | `#FF9F1C` | `#EF8A0C` | `#D8A03D` | `#AD7716` |
| Soft tint behind something picked | `Brush.AccentSoft` | `#4A3B28` | `#FBE3C4` | `#2F281B` | `#E8DDCA` |
| Orange text or line on the ground; orange text on the soft tint in dark themes | `Brush.AccentBright` | `#FFB547` | `#C46A00` | `#F0C060` | `#744D07` |
| Orange text on the soft tint in light themes | `AccentPale` (slot only) | `#FFD08A` | `#8A4A00` | `#FCE5B8` | `#5C3C04` |
| Pressed orange | `Brush.AccentDeep` | `#C77612` | `#D47800` | `#B08536` | `#7A5008` |

Fluent's `SystemAccentColor` ramps are retinted to the theme's orange, so stock controls
match. `AccentSubtle`, `AccentDim`, `AccentMute` and `AccentHover` are older shades; new
code uses the three above.

### Track states and meters

| Use | Brush | Studio Dark | Studio Light | Ember Graphite | Ember Paper |
|---|---|---|---|---|---|
| Record and arm | `Brush.Record` | `#F06A6E` | `#B3262C` | `#BE563E` | `#B54A32` |
| The disc on an engaged record key | `Brush.RecordInk` | `#17181A` | `#FFFFFF` | `#FFFFFF` | `#FFF4EE` |
| Mute | `Brush.Mute` | `#F2B53C` | `#E0A030` | `#D9C34C` | `#837010` |
| Solo | `Brush.Solo` | `#6A9FD8` | `#3F78B8` | `#6D8FB5` | `#3E6288` |
| Meter, safe (to 70 %) | `Brush.MeterSafe` | `#6CC673` | `#3E9E4A` | `#58B368` | `#2C7A3E` |
| Meter, hot (70–88 %) | `Brush.MeterHot` | `#F2B53C` | `#C98A12` | `#D9C34C` | `#837010` |
| Meter, over (above 88 %) | `Brush.MeterOver` | `#F06A6E` | `#C8343A` | `#BE563E` | `#B54A32` |
| Selected track header | `Brush.TrackHeaderSelected` | `#2E3034` | `#F2F3F4` | `#191815` | `#F0EBDD` |
| Selected track lane | `Brush.LaneSelected` | `#24262A` | `#D2D4D7` | `#141310` | `#E4DFD1` |
| Overload, clipping, error text | `Brush.Danger` | `#F06A6E` | `#B3262C` | `#C2554A` | `#B13E33` |
| Positive status; Play in Session | `Brush.Success` | `#6CC673` | `#22692B` | `#58B368` | `#2C7A3E` |
| The glyph on a green key | `Brush.SuccessInk` | `#17181A` | `#FFFFFF` | `#171613` | `#FFFBF2` |
| Warning text | `Brush.Warning` | `#F2B53C` | `#7A4F00` | `#D9C34C` | `#837010` |

`DangerBright`, `DangerHover` and `DangerSubtle` complete the error family; `NotaPalette`
adds `DangerDeep` (over-threshold fill), `DangerPale` (live-take waveform) and `MuteTint`
(the muted band).

### Track colours

Nine colours at one lightness, so no track is louder than another (`Brush.Track1…9`,
`NotaPalette.TrackBrushes`). Each has three shades (base · 24 % toward white · 30 % toward
black) that tracks inside a group step through.

| Slot | Colour | Studio Dark | Studio Light | Ember Graphite | Ember Paper |
|---|---|---|---|---|---|
| `Track1` | Green | `#7FB87A` | `#4A7F46` | `#58B368` | `#328140` |
| `Track2` | Teal | `#5FB3A8` | `#3A8A7F` | `#4E9E7A` | `#306F53` |
| `Track3` | Sky | `#5AA6C4` | `#357C99` | `#3E8E8E` | `#256464` |
| `Track4` | Blue | `#6A9FD8` | `#3F78B8` | `#5AA0B8` | `#317187` |
| `Track5` | Amber | `#E0A84A` | `#B8862F` | `#C77F55` | `#985127` |
| `Track6` | Violet | `#9D90C9` | `#7363A8` | `#7A6FB0` | `#4B4082` |
| `Track7` | Rose | `#D77A8F` | `#B4566B` | `#B05A7A` | `#7F3450` |
| `Track8` | Olive | `#B3B45E` | `#7E7F2F` | `#9AA64A` | `#6C752C` |
| `Track9` | Slate | `#8E9DB0` | `#5D6C80` | `#7FA88E` | `#598C69` |

**New tracks take the colours in this order:** green, blue, amber, violet, rose, teal,
slate, olive, sky, then green again, so each track contrasts with the one before (and the
tenth, green, with the ninth, sky). The order lives in `ArrangementView.AutoTrackColorIndex`
as a table of slots (1, 4, 5, 6, 7, 2, 9, 8, 3). The slot table itself does not change:
projects store the slot index, so a saved project opens with every track in its old
colour.

| | Studio Dark | Studio Light | Ember Graphite | Ember Paper | Use |
|---|---|---|---|---|---|
| `Brush.ReturnA` / `ReturnB` | `#8E9DB0` / `#7A889A` | `#5D6C80` / `#4E5B6D` | `#7FA88E` / `#6B907B` | `#598C69` / `#3E6A4D` | Return buses: the ninth colour |
| `Brush.Master` | `#A4A9B0` | `#4F535A` | `#8D8779` | `#746D5C` | The master strip: a grey, because it is the sum, not a track |

### Inside graphs

A graph's own curve is `VizInk`. When two to four sources share a graph they are told
apart by shade first (a second curve in a darker grey, `TextTertiary`), and by chroma only
where shade cannot carry it (`NotaGraph.Chroma(n)`: viz ink · teal `ChromaTeal` · rose
`ChromaRose` · blue `ChromaSteel`). The style guide removes the teal, olive and rose
*identity* colours devices used for a right channel, a band or a modulation source
(§ Devices); a chroma never goes on a button and never means state.

## Orange

Orange comes in three strengths, so you can tell at a glance what is playing, what you
picked and what a device is set to. Everywhere else values stay grey: knobs on track
headers and in the mixer, faders and meters.

| Strength | Looks like | Used for |
|---|---|---|
| **Live: solid** | `Accent` fill, derived ink on it | Playing (Play while the transport runs), recording, the playhead, the loop range, a switch that is on inside a device, a device's power switch, the current device tab's underline, a live icon button |
| **Picked: soft tint** | `AccentSoft` fill with orange text or icon (`AccentBright` in dark themes, `AccentPale` in light ones) | The option you chose: a segment, the current view, a tool that is on (Metronome, Follow, Snap, Automation), the left rail's current page (with a 2 × 14 orange tick), a filter chip that is on, Loop and Replace on. Never solid |
| **Values in devices: orange lines** | `Accent` line on a quiet fill | Only inside a device card: the arc of a knob, the 3 px line on a value bar (its fill is the soft tint), the selected EQ point, an upright value bar |

A shell slider under the pointer or being dragged borrows the value bar's colours (soft
tint fill, orange line) while it is live, then goes back to grey. A hovered menu item keeps
its full orange fill (§ Menus; § Accepted exceptions). A field with keyboard focus gets a
1 px orange outline.

**Do**

- Use solid orange only for something live.
- Mark a picked option with the soft tint: a pale fill and orange text.
- Keep orange values (knob arcs, value bars, EQ points) inside device cards.

**Don't**

- Fill a picked option with solid orange.
- Turn a selected clip or Session slot orange: it gets an outline in ink 1.
- Colour knobs, faders or meters orange outside a device.

## Type

**One typeface: Geist.** `Font.UI` = Geist 400 / 500 / 600 / 700, bundled from
`assets/fonts` (SIL OFL 1.1, `LICENSES/OFL-1.1-Geist.txt`); `NotaFonts.Sans`,
`SansMedium`, `SansSemiBold`, `SansBold` hold the typefaces for custom drawing. Never name
a font family in a view. Geist Mono is no longer shipped. Glyphs Geist lacks (the thin
space, ■ ▾ ✓ ★) fall back to the system font per character.

**Fixed-width digits.** Geist's digits are proportional, so the app turns on the font's
own tabular figures (OpenType `tnum`) everywhere: `NotaFonts.Tabular` is the feature set,
`NotaFonts.InstallTabular()` makes it the default of the inherited
`TextElement.FontFeatures` on every top level, templated control, `TextBlock` and text
presenter (called once before the XAML loads), and custom-drawn text is built with
`NotaFormattedText`, a `FormattedText` with the same features. "Geist" is a Reserved Font
Name, so the files stay unmodified and the feature is applied at shaping time
(`research.md` R1). A new `FormattedText` in a view is a `NotaFormattedText`.

**The scale.** Every word and number uses one of these:

| Role | Size / weight | Ink | Where |
|---|---|---|---|
| Readout | 17 / 500 | 1 | Position, tempo, signature, key and grid in the toolbar; the unit beside it in ink 3 |
| Device name | 12 / 600 | 1 | Device card header, the inspector's clip name |
| Track name | 13 / 580 | 1 | Track headers, mixer strips, browser rows |
| Body | 12 / 400 | 1–2 | Chips, tabs, search, menu items, buttons |
| Value | 11.5 / 400 | 1 | Numbers on a button or in a row; segment cells |
| Label | 11 / 400 | 3 | Tags, units and row labels |
| Device label | 9.5 / 400 | 3 | Knob names inside a device; section titles and row labels |
| Column head | 10 / 400 | 4 | Column heads and axes |
| Key letter | 10 / 650 | 3 | M, S and arm letters on track buttons |

Geist ships as static 400 / 500 / 600 / 700 files, so the guide's in-between weights round
to the nearest one the app has: 580 and 650 draw as 600 (SemiBold), 550 as 500 (Medium).

Inside a device the whole scale sits one step lower so the 260 card keeps its room for
controls: a knob's value 10, switch words and captions 10, a value on a button or in a row
10.5, titles and tabs inside a device 11, graph axes 9 (`NotaType.Device*`).

Others the guide fixes: a field 12.5, the browser search 12.5, a clip's
name 10.5 / 600, a Modular node's name 12.5 / 600, the current tab 12 / 580, a tooltip
10.5, a device number button 9.5 / 550. `NotaType` and the `TextBlock.*` role classes in
`NotaTheme.axaml` carry the scale for code and XAML. Tags and units are Geist in sentence
case, so nothing looks like code.

**Do:** set every word and number in Geist · write tags in sentence case · put a unit in
ink 3 beside its value. **Don't:** use a monospace face, even for numbers · fade text with
opacity (use ink 4) · set `LetterSpacing`.

### Numbers

`NotaNum.Install()` (called in `App.Initialize`) makes the display culture the default:
invariant with U+2212 as the negative sign, so every implicit `{v:0.0}` prints `−3.3`
with a point on any OS locale; typed input still accepts a hyphen. Project and preset
files are JSON and never go through it.

- The unit follows a **thin space**: `4.6 s`, `−3.3 dB`, `72 %` (` ` in source).
- **Minus is U+2212** on every number shown, scale and tick labels included (the mixer
  scale reads `+6 · 0 · −12 · −48`). A literal typed with a hyphen-minus (`"-12"`) fails
  `RunNumbers`.
- A plus only where the sign matters: `+0.8 dB`.
- **Ranges take an en dash** (U+2013): closed between plain numbers or note names
  (`1–127`, `1.1–5.1`, `C1–C3`), spaced when either endpoint contains a space
  (`0.20 – 1.50 s`, `20 Hz – 20 kHz`).
- Fixed precision: dB one decimal · tempo two (`120.00`) · percent whole · Hz whole up to
  999, then `3.2 k`. Helpers: `NotaNum.Db`, `Hz`, `Pct`, `Bpm`, `Time`, `Unit`, `F`.
- Width must not jump across 10 or 100: tabular figures plus fixed precision.
- Axis labels in graphs and clip lengths in beats may use variable precision.

### Labels and copy

- Parameter labels: English, sentence case, one word where it fits: `Decay`,
  `Pre-delay`, `Feedback`. Views may still pass caps literals (`"PRE-DELAY"`): every label
  builder routes them through `NotaText.Label`, which sentence-cases an all-caps string and
  keeps acronyms and units (`LFO`, `EQ`, `ADSR`, `MIDI`, `dB`, `Hz`, `kHz`, `ms`) in their
  own case (`NotaText.Fixed`, which also holds `LUFS`, `USB`, `OK`, `DAW`). A string with
  any lower-case letter is left as written. No colons, no "Value", no vowel-dropped
  contractions (`Fdbk`, `Atk`, `Lvl`); the established truncations `Freq`, `Reso`,
  `Thresh` are allowed.
- **Every label is sentence case**: menu items, command titles, palette headings, window
  titles, tooltips, section heads, buttons, tags: "Save as…", "Live freeze", "Export
  audio", "Recent", "Suggested · Arrangement", "Wavetable synth". Capitals stay only on
  acronyms and units (`NotaText.Fixed`), product and device names ("Nota Aurora"), the
  view names (Arrangement, Session, Modular, Mixer: "Back to Arrangement", "Show Mixer")
  and other proper nouns (Finder), and a Settings page named after "Settings: "
  ("Settings: Audio"). Data keeps its own case: preset names, engine parameter names,
  MIDI-learn names, user data. `RunLabels` holds the rule.
- Shell tooltips: short and verb-first: "Snap clips to the grid (hold Alt to drag
  freely)", "Scroll the arrangement to keep the playhead in view". Not "Click to…". The
  value stays on the control; a tooltip may add the unit or range.
- A device's type tag in its header is Geist 11 in ink 3, sentence case ("Analog poly",
  "Convolution"); instruments add live voices ("Analog poly · 0/5").
- Preset names describe a place or material (`Stone Vault`, `Tape Glue`), not an emotion.
  Device names are one word from the musical vocabulary (`Chamber`, `Prism`).
- Middle dot `·` separates meta. No emoji, no exclamation marks.

## Shapes

### Corner scale

Corners run from 2 on small parts up to 9 on device cards; docked panels have none.
`Radius.*` in `NotaTheme.axaml`, `NotaRadius` in `Theme/NotaGeometry.cs` (`research.md`
R2). Each site picks the name for what it draws.

| Name | px | For |
|---|---|---|
| `Docked` | 0 | A docked panel: it meets its neighbours on a seam |
| `Card` | 9 | Device card, toolbar view group, the well around an icon-button group, modal windows |
| `Group` | 7 | Icon button, chip, segment group, device section, search, menus, Modular node |
| `Field` | 6 | Field, dropdown, clip, toggle pill, row highlight, graph window |
| `Cell` | 5 | One segment cell, track button, EQ band toggle, tooltip, inner key, Session slot |
| `ValueBar` | 4 | Value bar, shell slider, fader cap, overview strip |
| `Switch` | 3 | Switch track |
| `Fine` | 2 | Switch knob, notes, bars inside a plot, a meter channel |

`NotaRadius.Top(r)` rounds only the top corners (a header strip on a body).

### Sizes

| Part | Size |
|---|---|
| Toolbar | 44 high, 10 padding; icon buttons 28 in a 2 px well |
| Left rail | 40 wide, buttons 28, 6 apart; the current page gets a 2 × 14 orange tick and the soft tint |
| Browser | rows 30, search 30, chips 24, footer 30 |
| Track header | 229 wide, 54 high, two rows: name row 18, controls row 20 |
| Device card | always 260 high; the width fits the device (§ Devices); header 24 |
| Device sections | 4 from the card edge, 6 apart, 8 padding inside; tab rows 22; rows spread to fill the height |
| Knob | 26 with a centre cap in a device; 22 when its label sits beside it (Glide, an EQ band); 28 and flat outside devices |
| Switch | 20 × 12, knob 8, square corners |
| Track buttons | M, S and arm 18 × 18; device number buttons 14 × 16 |
| Segment cell | 20 high in a 2 px well |
| Field · dropdown · search | 26 · 22 · 30 high |
| Value bar · shell slider | 16 high |
| Tab row | 26 high |
| Meter channel | 3 wide, 1.5 apart |
| Modular node | 214 wide, header 34, parameter rows 18 |
| Title bar · panel toolbar · menu item | 32 · 32 · 24 |
| Audio editor column | 232 wide |

`Control.*` in `NotaTheme.axaml` and `NotaSize` hold the shared sizes; `Space.*` /
`NotaSpace` the spacing (the seam between docked panels is `Space.Gutter`, 1).

### Depth

Depth exists only inside a device card and on a Modular node:

- **The card** is lifted off the background: a thin outline in `Seam` and a light top edge
  (`Bevel`, 1 px inset) on `SurfaceRaised`; its header is `DeviceHeader` with the same
  top edge and a seam under it.
- **Each section** inside it is pressed in: `DeviceSection` with a 1 px inner edge in
  `DeviceSectionEdge` and a 1–2 px inner shadow, corner 7.
- **Controls sit in grooves**: segment groups, value bars and fields inside a device sit in
  `Groove` with a 1–2 px inner shadow.
- **The picked option rises** out of its groove: `SurfaceHover` (or the soft tint) with
  the bevel and a 1 px drop.
- **The round wheels are the exception**: they keep their 0.56.1 look and never sit in a
  groove (§ Wheels).

Depth comes only from slightly different greys, thin lines and 1–2 px shadows. No
gradients, no blur, no glow. In the shell nothing is raised: no raised buttons, no raised
panels.

Elevation and depth tokens, built by `NotaThemeService` from the theme's colours
(`x y blur spread colour`; the custom-drawn views take the same ones through `NotaDepth`):

| Key | Studio Dark | Studio Light | Use |
|---|---|---|---|
| `Shadow.Raised` | `inset 0 1 0 0 #12FFFFFF` | `inset 0 1 0 0 #B3FFFFFF` | The top bevel on a raised part; the dialog header |
| `Shadow.Sunken` | `inset 0 0 0 1 #45484E` | `inset 0 0 0 1 #B3B6BB` | A rule edge around a recess, where a groove is not used |
| `Shadow.Popover` | `0 0 0 1 #141517, 0 10 28 0 #6B000000` | `0 0 0 1 #9DA0A6, 0 10 28 0 #381E2024` | A popover drawn inside the main window |
| `Shadow.Modal` | `0 0 0 1 #141517, 0 24 64 0 #80000000` | `0 0 0 1 #9DA0A6, 0 24 64 0 #401E2024` | The command palette |
| `Shadow.Card` | `0 0 0 1 #141517, inset 0 1 0 0 #14FFFFFF` | `0 0 0 1 #9DA0A6, inset 0 1 0 0 #FFFFFFFF` | A device card or Modular node: seam outline and light top edge |
| `Shadow.Section` | `inset 0 0 0 1 #0BFFFFFF, inset 0 1 2 0 #40000000` | `inset 0 0 0 1 #17000000, inset 0 1 1 0 #0F000000` | A pressed-in section inside a device |
| `Shadow.Groove` | `inset 0 1 2 0 #73000000` | `inset 0 1 2 0 #2E000000` | A groove: segment groups, value bars and fields inside a device |
| `Shadow.Lifted` | `inset 0 1 0 0 #14FFFFFF, 0 1 2 0 #73000000` | `0 0 0 1 #1A000000, 0 1 2 0 #26000000` | The picked option raised out of its groove; raised keys in a device |
| `Shadow.SwitchInset` | `inset 0 1 2 0 #80000000` | `inset 0 1 2 0 #26000000` | The slight inner shadow of a switch track |

Only `Shadow.Popover` and `Shadow.Modal` blur more than 2 px: they float. The theme
supplies the colours (`ShadowBevel`, `ShadowPopover`, `ShadowModal`, `Bevel`,
`DeviceSectionEdge`, and its `Seam` and `Rule`); the inner shadows of the device depth are
black at a strength set by the theme's kind.

**Do:** keep depth inside device cards · build depth from greys, thin lines and 1–2 px
shadows · keep every device card 260 high and let the width fit. **Don't:** raise buttons
or panels in the shell · use gradients or blur · put the pitch and mod wheels in a groove.

## Buttons

Buttons have no border: they sit on the faint well. What you picked gets the soft tint;
what is live gets solid orange. Inside a device, controls sit in grooves and the picked
option is raised.

### Toolbar

The bar across the top: views, transport, tempo, grid and tools.

- **Groups:** views, transport and timing, grid, tools, master. The views and the transport
  well sit 12 apart with no line between them (both are wells already); the later groups
  are split by short thin lines.
- **Values** are plain numbers at Readout 17 / 500 with the unit in ink 3 (`124.00 BPM`,
  `1.1.00 bars`); no box.
- **Toggles:** Loop and the loop-record mode (Overdub: two layers · Replace: one layer and a
  turn arrow) are icon buttons beside the position; the loop range is on the ruler. Tap is
  a small pill and keeps its four beat dots.
- **Tools:** Metronome, Follow, Snap and Automation are icon buttons; on is the soft tint.
- **Right:** MIDI and Remote with small status dots, then Master as a flat slider with its
  dB value inside (§ Sliders).
- 44 high on `SurfaceDeep`, a seam below. The view switch (Arrangement · Session ·
  Modular) is an icon-button group with corner 9; the current view has the soft tint; the
  tooltip and accessible name are the view name.
- There is no add-track button (tracks come from the browser). Launch quantize lives in
  the Session toolbar. Transport buttons are not focusable, so Space / R / L / Return
  always reach the window. `MainWindow.TransportFit.cs` sheds the least-needed parts
  first when the window is narrow.

### Icon button

A square button with just an icon, used in the toolbar and the left rail. 28 × 28, corner
7, icon 16.

| State | Drawn |
|---|---|
| Rest | No fill, ink 2 icon |
| Hover | The well appears |
| On (chosen) | Soft tint, orange icon |
| Live | Solid orange with derived ink (Play while running) |
| In a group | A 2 px well with corner 9 around the buttons |

### Segment buttons

A row of options where exactly one is picked (Notes / Envelopes, Pre / Post / Off).

- **Group:** a well with 2 padding and corner 7; inside a device, a groove.
- **Cell:** 20 high, 8 padding (6 in a device), corner 5, Value 11.5 in ink 3.
- **Picked:** soft tint with orange text; inside a device it is also raised.
- **Disabled cell:** ink 4 text, never faded.
- The picked cell keeps its weight and width, so auto-width cells never shift.

### Chips and pills

- **Chip** (browser filters, the category row): 24 high, 10 padding, corner 7, the well,
  Body 12 in ink 2; on is the soft tint with orange text. A category chip may carry its
  colour dot. The Favorites / tags strip is one line and overflows into `+N`; the device
  tabs' category row wraps instead, so every category stays in sight.
- **Pill** (Tap in the toolbar): a label-size toggle, 3 × 7 padding, corner 6,
  Label 11; on is the soft tint.

### Track buttons

Mute, solo and record-arm on each track, and the numbered voice buttons in devices.

- 18 × 18, corner 5, Key letter 10 / 650.
- **Rest:** the well, ink 3; arm shows a red dot.
- **On:** solid `Mute` amber, `Solo` blue, `Record` red, with `InkOn` ink.
- **Device number buttons** (Voices 1 to 5): 14 × 16, raised (`SurfaceHover` with the
  bevel), 9.5 / 550 in ink 2.
- The same rule arms tracks in the arrangement, mixer and Session.

### Switch

An on/off toggle, including a device's power switch.

- 20 × 12, corner 3; knob 8 × 8, corner 2. Square, not round.
- **Off:** a dark track (`WellTrack`) with a slight inner shadow, the knob in ink 3 at the
  left.
- **On:** light grey (ink 2) with a `SurfaceDeep` knob outside devices; orange with a
  `TextOnAccent` knob inside a device and for power; the knob moves right.
- **Label:** 6 after the switch, Label 11 in ink 2.
- The Settings window's larger switch keeps the same look at its own size.

### Tabs

Switch between pages of a panel (a device's Oscillators and Filter pages, the detail
panel's Devices / Pattern / Clip).

- A 26 high row with a thin line under it. Inside a device tabs sit 16 apart; elsewhere
  each tab has 10 padding on both sides, so the underline is wider than the word.
- **Current:** ink 1, 12 / 580, a 2 px underline: orange inside a device, ink 1 elsewhere.
- **Rest:** ink 3.

### Primary and destructive actions

A context's primary action is set apart by weight (600) and one tone step, **not by
colour**: there is no solid-orange action button. A destructive action is an ordinary
button; its label says what it does.

### States

| State | Treatment |
|---|---|
| Rest | The well (or no fill for an icon button); ink 2 |
| Hover | The well appears or steps up one tone (120 ms); nothing moves |
| Pressed | One step darker; no 1 px shift |
| Picked | Soft tint, orange text or icon |
| Live | Solid orange, derived ink |
| Inside a device, picked | Raised out of its groove, soft tint or orange text |
| Selected row | The well with a 2 px orange tick on the left; name ink 1 |
| Selected clip or Session slot | A 1 px outline in ink 1 |
| Focus (fields) | A 1 px orange outline, no ring, no glow |
| Disabled | Ink 4, nothing picked; no opacity |
| Modified parameter | Its name lifts to ink 1; no orange, no dot |

**Empty state:** one line of ink 3, centred, no illustration or call-to-action button;
details go in a tooltip; the area keeps its size and frame.

**Do:** show the well at rest, or on hover for icon buttons · draw a disabled cell in ink 4.
**Don't:** draw a border around a button · box toolbar values; they are plain text with a
quieter unit.

## Knobs, sliders, meters and wheels

Inside devices, knobs have a centre cap and sliders are wide value bars. Outside devices
the controls stay flat: sliders are flat bars with the value inside, and knobs stay small
and grey.

### Knob

- **Arc** 270° (from −135°), 2.4 stroke with round ends on the knob track (`KnobTrack`);
  a bipolar knob fills from 12 o'clock.
- **Marker** a live reading can sit on the track as a short ink-2 tick across it (Dynamic
  EQ-8's band level on its Thresh knob); none while the knob is inactive.
- **Cap** a small disc in the centre (`KnobCap`, a 1 px `Seam` edge); devices only. (The
  guide edges it in `BgSunken` in dark themes; `Seam` is within a step of it.)
- **Pointer** a thin line from the centre in ink 1.
- **Colour** orange (`Accent`) inside a device. Outside devices a knob is flat: grey
  (`TextTertiary`) at rest and orange only while it is being set, like the shell slider:
  the pan knob on a track header, sends and pan in the mixer.
- **Size** (`NotaSize`) 26 in a device with the value (10) and label (Device label 9.5)
  under it; 22 with the label and value beside it (Glide, an EQ band's gain); a flat knob
  outside devices 28, with no cap. `Knob` snaps to these three sizes.
- An inactive knob draws its arc in ink 4. A modulation arc keeps its source's colour:
  that is data, not state.

### Value bar

The slider inside devices (`ValueBar`). Every slider inside a device is one; a vertical
fader inside a device is an upright value bar.

- 16 high, corner 4, in a groove.
- **Fill:** the soft tint up to the value; **value:** a 3 px orange line. Bipolar bars
  (Pan, Balance, Feedback) fill from the centre. While dragged the fill deepens toward
  orange.
- Drag anywhere on the bar; a click never jumps the value; double-click resets.
- Why: a thin slider inside a device is hard to grab and read.

### Slider

The slider outside devices: track volume, Master, mixer sends, the A–B crossfader,
quantize strength. A flat bar with its value written inside.

- 16 high, corner 4, flat on `BgSunken`: no groove outside devices.
- **Fill:** a quiet grey up to the value (`TextTertiary` mixed 40 % into `BgSunken`).
- **Value:** a 2 px line in ink 2; the number sits inside the bar at the left, 10.5 / 400,
  or just past the line when the fill is too short to hold it.
- **Live:** under the pointer or while dragged it takes the value bar's colours: the soft
  tint and an orange line.
- **Size:** the value is inside the bar, so the bar gets the whole width the row allows.
- Drag up and down to change, Shift for fine, a click never jumps, double-click resets
  (track volume to 0 dB).

### Wheels

The pitch and mod wheels on instruments. Every instrument, Pentad included, uses the round
wheel (`PerformWheel`, from 0.56.1's Aurora, Bass, Operator and Volt); Pentad's flat
wheels go. Never put in a groove.

- 16 wide and fully round, `BgSunken` with a 1 px `Seam` edge; a raised grip
  (`SurfaceRaised`) over the middle third with three tick lines in `Rule`; a 2 px bar
  across.
- **Bar colour:** ink 3 at rest, `VizInk` once moved off its default, orange while
  dragged.
- **Height** fills the wheels column (about 147 on a 260 card).
- **Panel:** one shared panel (`InstrumentCardKit.WheelsPanel`), identical in every
  instrument: 76 wide, 4 / 9 / 4 / 6 padding, the wheels, captions and values on a 60 px
  centred pair.
- **Text:** "Wheels" above, "Pitch" and "Mod" under each wheel, and the two values side by
  side below (`InstrumentCardKit.SetWheelValues`): the pitch wheel's range (`±2 st`) at rest
  and the bend in semitones while moved, the mod wheel in %; ink 2 at rest, ink 1 once moved.
- 140 px of drag for the full range, ×0.25 with Shift, never jumps to the pointer;
  double-click resets; the pitch wheel springs back to the centre.

### Meter

The level meter on each track (`MeterBar`, `StereoMeter`; `GrMeter` for gain reduction).

- Channels 3 wide, corner 2, 1.5 apart, on the well.
- Zones: green (`MeterSafe`) to 70 %, amber (`MeterHot`) to 88 %, red (`MeterOver`) above.
- Down the right edge of the track header. Peak hold stays until clicked; no release
  ballistics.

### Graphs

About 45 custom-drawn views in `Controls/` draw what a device does, for real where
possible (the reverb tail is an actual impulse response). All of them live in one graph
window (`Controls/NotaGraph.cs`):

- `SurfaceInset` ground, a 1 px `Rule` frame, corner 6, grid in `VizGrid`.
- Axis labels Column head 10 in ink 4, **in the corners**: never full axes with ticks. A
  frequency graph is labelled only in its bottom corners (`20` · `20k Hz`).
- Title sentence case in ink 3, top left; the legend lives inside the window, a drawn
  sample next to a value (`NotaGraph.Legend`).
- The primary curve 1.8 px `VizInk`; a second source a darker grey. **No fills under
  curves, no gradients.**
- Nodes 7 px with a 2 px ground-coloured ring; the selected or dragged node is orange; a
  picked but switched-off node is an orange ring.
- Waveforms at rest in `VizInk`; second channel a darker grey; the trimmed part darkened,
  not hidden.
- Bars: 1–2 px gaps, corner 2, no outline; orange only for a selected range; updates are
  discrete.
- Timeline: the playhead is a 1 px orange line with a 7 × 5 flag and no glow; the loop
  range is orange.
- **Room for the words.** The plot starts under the title row (`NotaGraph.PlotTop`, 22) and
  stops above the bottom corner labels (`NotaGraph.PlotFloor`, 18), so no curve, node or
  peak runs through the title or a corner label. Edge labels go through
  `NotaGraph.EdgeLabels`, which drops one that would sit on another label, the title row or
  the bottom corners; a label that has to sit over a curve goes on a ground plate
  (`NotaGraph.PlateText`).
- A graph does not repeat a value its knobs already show; its title carries a name, not the
  knob values. A short window keeps its two end labels and drops the middle ones.

**Do:** use value bars for sliders inside a device · write a shell slider's value inside
the bar · use the round wheel for pitch and mod on every instrument. **Don't:** use a thin
slider inside a device · put a groove under a slider outside a device · use a flat or
restyled pitch and mod wheel.

### Interaction contract

Every value control, in a device or the shell:

- **Vertical drag**, up increases, full range over ~140 px; Shift (or Ctrl/⌘) is fine.
  Cursor `ns-resize`. A click never jumps the value. A horizontal value bar or slider also
  follows a horizontal drag along its own axis (cursor `ew-resize`).
- **Double-click on the control** restores the default (or clears a route).
- **⌥/Alt-click, or a double-click on the read-out**, types a value: the read-out itself
  turns editable (`ValueEntry` / `ValueText`: no box, its own font, size and alignment, a
  1 px orange line under it, the number selected and its unit left alone; laid on
  `SurfaceRaised` over a control without a read-out), read in the units shown. Enter or a
  click away commits, Esc cancels, Tab / ⇧Tab move to the next / previous value on the
  card. A commit is one `GestureBegin` / `GestureEnd`, so one undo step and one
  automation gesture, like a drag. Return outside the box stays Stop.
- **Right-click** opens the parameter menu (Type value…, Reset to default), merged into
  the CV-modulate / macro menu where the control has one.
- **Drag with the left button only.**
- **`GestureBegin` / `GestureEnd`** bracket `BeginAutomationWrite` / `EndAutomationWrite`.
- **`MidiLearn.Bind`** registers the control with its display name.
- **Live follow** re-reads engine state each tick and skips while dragging.

`Inactive.Set(root, on, interactive)` puts a subtree into the unavailable look by colour
(ink 4, orange lines to ink 4, controls `IsDim`). `NaBadge` marks a feature not built yet;
the control stays so nothing shifts later.

## Fields

Fields and menus have no border; they sit on the well like buttons. Inside a device, a
field sits in a groove and a preset menu is raised.

- **Field** (a project name, a clip start): 26 high, 9 padding, corner 6, 12.5; the well;
  a groove inside a device. Placeholder in ink 3. **Focus:** a 1 px orange outline.
- **Dropdown** (an input, a mode): 22 high, 8 padding, corner 6, Value 11.5; a 5 px
  chevron with a 1.3 stroke in ink 3. With a label, the label sits before it in ink 3.
- **Device preset:** a raised dropdown (`SurfaceHover`, the bevel), with previous and next
  arrows either side of the preset name.
- **Search** (the top of the browser): 30 high, corner 7, the well; a 16 px icon,
  placeholder in ink 3, the result count on the right in ink 3, a filter icon button beside
  it.
- **Readouts** in the toolbar are plain text, not fields (§ Toolbar).

### Menus, popovers, dialogs

- **Menus, context menus, flyouts, drop-down lists, tooltips:** a raised popover
  (`SurfaceRaised`; a tooltip `TooltipBg`), corner 7 (a tooltip 5), a 1 px seam ring,
  padding 4, at least 200 wide. **No blurred shadow:** Avalonia draws each popup in a
  window of its own, sized to its content, so a blur would be clipped at the popup's edge.
- **Menu items** (the Nota `MenuItem` theme in `NotaTheme.axaml`, spec 034) are 24 tall,
  corner 6, in fixed columns that line up across a menu: icon (14, ink 3, never a tinted
  icon) · label · value · shortcut · caret. Hover or keyboard focus is a full orange fill
  with the derived ink on every part; disabled takes ink 4 and no fill. A chosen choice is
  ticked with an orange check in the icon column (radio and checkbox items), not a
  coloured dot. The **value** column shows a submenu's current choice in ink 3 ("Record
  input  Ext ›"), or a switch ("Live freeze"), or a data colour chip (a tag's colour).
  **Shortcuts** are Geist 11 in ink 3, spelled the platform's way: `⌘G`, `⌘⇧G`, `⌫` on
  macOS (no "+"), `Ctrl+G` elsewhere (`MenuKit.Spell`). The **submenu caret** is a small
  chevron glyph set in from the edge.
- **Destructive items** (delete, remove, clear, discard, flatten) sit in the menu's last
  group in the danger ink (label and icon), `MenuKit.Danger`; their hover is the same
  orange fill as every item. Danger is a semantic ink, not a second accent.
- **The object header.** A menu for something with a name (track, clip, device, chain,
  pad, macro, scene, Session slot, section, band, browser item, version) opens with a
  non-interactive header row: its colour chip when it has one, the name in 600, and a
  badge in ink 3 with its type and position or count ("Audio · 3", "3 tracks", "MIDI
  clip"). Items below don't repeat the noun ("Copy", not "Copy track"). A menu on empty
  space or a control (lane background, snap chip, metronome) has none.
- **Inline rows** put the most common property edit in the menu itself, one click, no
  submenu: the **colour grid** (the 9 track colours × base / light / dark, plus Auto; the
  current colour ringed in ink 1, the pointer or keyboard cursor in ink 3, orange on the current colour) and **segment
  buttons** ("Height: Compact · Standard · Large", Option-click for every track; the
  chosen one in the soft tint). They never highlight as a whole; arrows move inside them,
  Enter applies, and a pick closes the menu. Small **captions** in ink 3 ("Recent") head a
  group inside a menu.
- **Group order**, rules between groups: header → inline rows → naming and settings →
  the object's own actions → create / group → clipboard (Copy ⌘C · Cut ⌘X · Paste ⌘V ·
  Duplicate ⌘D, always in this order, `MenuKit.ClipboardGroup`) → destructive.
  `DesignTokenCheck.RunMenus` keeps menus on `MenuKit`: icons through `MenuKit.Icon`,
  destructive items through `MenuKit.Danger`, no Color / Track height submenus.
- **Command palette:** the in-window modal: panel body, corner 9, seam ring and
  `Shadow.Modal`.
- **Dialog windows** (`NotaWindow`): panel body; on macOS and Windows a 32 px header with
  the bevel and a seam below, the title in 13 / 600. The OS draws the window shadow.

### Floating windows

The mixer and the export dialog are OS windows (`NotaWindow`), not in-window overlays, so
the mixer can live on a second screen. The command palette is the only in-window overlay.

- **Escape** closes a floating window, as it closes the palette: the mixer closes (unless a
  text field has focus, which owns Escape then), Export cancels (an open drop-down takes
  its own Escape first).
- **The mixer sizes to its strips** (`MixerWindow.FitWidth`): the window is as wide as the
  strips and master plus padding, clamped to [560 (`MinWidth`), the screen's working
  width], so there is no dead area right of the master. It refits when a track is added
  or removed while it is open, and a window that grows past the screen's right edge moves
  back onto it. The height stays 460.
- **Titles are sentence case:** "Mixer", "Export audio", "Edit tags", "What's new in
  Nota", "Connect phone".

## Cards

The device card is the one place with depth. Everywhere else, panels meet on seams and the
colour comes from the tracks.

### The window

```
┌ title 32 · "Nota", centred, on BgApp ──────────────────────────────────┐
├ toolbar 44 · views · transport · tempo · grid · tools · MIDI Remote Master │
├──┬──────────┬──────────────────────────────────────────────────────────┤
│r │ browser  │ canvas (arrangement · session · modular)                 │
│a │          │                                                          │
│i ├──────────┴──────────────────────────────────────────────────────────┤
│l │ panel toolbar 32 · Devices · Pattern · Clip · track · Freeze · ⧉ ×   │
│  │ detail body · device cards                                          │
├──┴─────────────────────────────────────────────────────────────────────┤
└ status 22 · caption text on SurfaceDeep ────────────────────────────────┘
```

Every region is **docked**: corner 0, no gutter, separated by 1 px seams.

- **Title bar 32**, frameless, on `BgApp`, a seam below; macOS traffic lights in a left
  inset, the project's name centred ("Untitled" until it is saved); Windows reserves 180 px
  for the caption buttons. There is no in-window menubar row.
- **Toolbar 44** (§ Toolbar).
- **Left rail and browser:** the rail 40 wide on `SurfaceDeep`; the browser on `Panel`,
  rows 30 and one line (a colour dot or bar, the name, metadata in ink 3 on the right);
  hover the well, selected the well with a 2 px orange tick. A built-in device's row shows
  its short name ("Aurora", not "Nota Aurora"; search still matches the full name). The
  Instruments, Audio effects and MIDI effects tabs carry a row of category chips between
  the search row and the Favorites / tags row (`DeviceCategories.cs`); one chip is picked
  per tab and narrows the list; `Other` comes last and appears only when an unrecognised
  plug-in lands there; "Category chips" in ⋮ hides the row. The splitter is a transparent
  6 px grab strip centred on the seam.
- **Detail panel**, docked under the canvas on a seam; its body is `SurfaceDeep`, like the
  toolbar, and so is the popped-out window. It opens with a 32 px panel toolbar, also on
  `SurfaceDeep`: the mode tabs, the track (colour square, name, routing summary),
  Freeze / Live freeze, a rule, then pop-out and close as icon buttons.
- **Status 22**: one line of caption text (Label 11, ink 2) on `SurfaceDeep`, a seam above it,
  16 px in. This is the window's status line; device cards have none.
- **Mixer strips** are `Panel` columns on seams, each with a 3 px top bar in the track
  colour (grey for master); sliders and knobs are the flat shell kind, sends with their
  amount written inside the bar. The footer's Sends and I/O toggles are chips (shown is
  picked: the soft tint), and the A–B crossfader placeholder is a flat slider.

### Device card

The card each instrument or effect lives in, in the device panel.

- **Card:** corner 9, raised: a thin outline and a light top edge; always 260 high
  (`DeviceCardKit.CardH`), the width fits the device (`IDeviceBody.Width`, § Devices).
- **Header 24:** name (Device name 12 / 600), preset menu (20 high) (where the device has presets),
  type tag in ink 3, power switch at the right, on every device, instruments and racks
  included.
- **Sections** are pressed in, corner 7; 4 from the card edge, 6 apart, 8 padding
  inside; rows spread to fill the height. The header and the padding stay thin on purpose:
  the room goes to graphs and controls (0.57 had a 22 header and almost no padding).
- **No status line:** nothing sits under the card. A control or a device's own readout that
  lived there moves into the card (§ Devices). The engine readouts every status line ended
  with (sample rate, latency, voices, CPU) go, as the style guide's Changes section lists
  for each device.

### Track header

The controls at the left of each track in the arrangement.

- **229 × 54, two rows.** Name row (18): name (Track name 13 / 580), the MIDI or Audio tag
  in ink 3, M, S, arm (monitor on audio tracks). Controls row (20): volume (a flat slider
  with its value inside), the pan knob and its value, the input picker. Every control the
  header had stays.
- **Colour:** a 3 px bar in the track colour down the left edge. Inside a group the member
  is indented 14: the group's 4 px bar runs down through it, then its own 3 px bar.
- **Keys:** M, S and arm are 18 × 18, 4 apart; the type tag is Label 11 in ink 3; the
  input picker is 66 wide (a custom picker, not a ComboBox, so its text never truncates),
  and the monitor key goes ink 4 while Master is the input.
- **Selected:** the header takes `TrackHeaderSelected` (background only, no outline) and its
  lane `LaneSelected`.
- **Meter** down the right edge.
- **Rows** default to 54; below 50 a row is compact (the name row only), from 100 large.
  The pinned Return / Master rows are one 30 px line each (name, flat slider 122, meter).

### Clip

A block of notes or audio on a track lane.

- **Fill:** 20 % of the track colour over the lane, corner 6.
- **Edge:** a thin line in the track colour; **selected:** a 1.5 px outline in ink 1,
  never orange.
- **Content:** notes, waveform and the name (10.5 / 600) in the full track colour, moved toward
  dark ink (or white) just enough to keep 3 : 1 on the lane. Only a light shade on Studio Light
  needs it.
- Audio clips draw their waveform as spaced bars by default (a 1 px stroke every 3 px,
  mirrored about the centre); Settings → Appearance → Waveform switches to Solid.

### Group

A group track and the tracks inside it.

- **Row:** tinted 16 % in the group colour; a 4 px bar runs down from the group row through
  every member and ends at the last one.
- **Members:** a 6 % wash; their own colour on an inner bar.
- **Session and Mixer:** one bar spans the group and its tracks; members get a colour dot
  beside the name.

### Session slot

A clip slot in the Session view grid.

- **Clip:** the track tint (20 %, a step deeper while playing) with the name (10.5 / 600)
  in the track colour and the length in ink 3; corner 5.
- **Selected:** a 1.5 px outline in ink 1, not orange; the selected column's header takes
  `SurfaceSelected` with no outline.
- **Empty:** fades back to the well (`Well`).
- **Mixer rows** under the grid use the flat shell slider with the value inside (sends and
  volume) and the 14 px pan knob with its value beside it.
- The inspector column is 260 and can be hidden (an Inspector icon button at the right of
  the Session toolbar, remembered in `SessionInspectorOpen`). When the toolbar is narrow
  its least-used controls fold, one at a time, into a "⋯" menu that lists whatever is
  hidden.

### Modular node

A device as a card in the Modular view, with its inputs and outputs for cables.

- **Card:** 214 wide, corner 7, raised like a device card.
- **Header 34**, tinted by category: blue for instruments, green for effects, mauve (24 %)
  for MIDI effects, an orange wash (16 %) for modulators, a faint grey for the track's
  audio in and out; plain grey (`SurfaceCard`) when bypassed.
- **On / off:** a power switch on every device, the instrument included; the track output
  shows a green dot because it cannot be switched off.
- **Actions:** expand and delete (a bin icon) in the header; the output keeps its Master
  label.
- **Ports** on the card edges, inputs left and outputs right; the first row lines up on
  every node so the main cable runs straight; round for audio, square for MIDI, diamond for
  CV, in the port colours of 0.56.1.
- **Expanded:** the parameters as 18 px rows with soft value bars; the list scrolls after
  eight.
- **Selected:** a thin orange outline.
- The sidebar always lists the tracks; opening the view with nothing selected selects the
  first track that is neither a group nor a return. Empty states: "No tracks yet" in the
  sidebar, "Pick a track to see its signal graph" on the canvas.

### Editor settings strip

The settings row above the notes in the piano roll (`ClipPropsView`): every clip setting in
one row above the notes, so the notes get the full width. It costs 40 px of note height
(`ClipEditorKit.StripH`); the help hint sits next to Notes and Envelopes in the header.

- **Groups** split by short thin lines: Clip (Start, Length, the Loop switch and its
  length), Quantize (grid menu, Quantize button, Strength as a flat slider with its value
  inside), Transpose (−, the value, +), Selection (one line of text).
- **Type:** group title 13 / 600 ink 1, labels 12 ink 3; fields 26 high on the well,
  corner 6, no border. Inactive parts (Loop off, its length) in ink 4.
- **Header row:** the clip cell (colour bar, name 13 / 600, kind in Label 11 ink 3 right
  after the name), the tabs, then flat buttons (Fit, the snap chip, audition, Tools,
  "Remove out of scale"): no well at rest, the well on hover, the soft tint when on. MIDI and audio share one editor
frame (`ClipEditorKit`); the canvas sits on the ground, notes in the track colour quieter by
velocity, selected notes orange, scale rows a neutral lift; envelopes and expression curves
are `VizInk`, only the point under the pointer is orange.

### Audio editor

A single column, 232 wide (`AudioClipEditorView`), rearranged so nothing scrolls: Start and
Length side by side; Gain as a flat slider with its value inside; the Warp title row holds
the switch, its state ("off" or the marker count, Label 11 ink 3) and the stretch-mode menu;
Detect tempo and To transients side by side; then the Detected row. Section titles are
13 / 600 ink 1, row labels Label 11 ink 3, sections 14 apart on thin rules. The clip cell in
the header puts the kind ("Audio") right after the name. The canvas keeps its ruler 24 and
22 px warp-marker strip, and everything outside the played region is darkened.

### Arrangement

Geometry kept unless this guide changes it: header column 229, group rows 26, default zoom
28 px per beat. Every y ↔ row conversion goes through `ArrangementView.RowTop` / `RowAtY`.
Above the tracks: Overview 34 (its strip corner 4), Sections 22, Zoom + ruler 24. The
horizontal scrollbar is a 12 px well with a 5 px thumb (`ThinScrollBar`). The pinned Return
/ Master band meets the tracks on a 1 px seam. A new track is scrolled fully into view
(`ArrangementView.RevealTrack`).

**Do:** keep every control a device or track already has · outline a selected clip or slot
in ink 1 · tint a Modular node's header by category. **Don't:** add a status line under a
device card · fill a selected clip with orange.

## Icons

`Controls/Glyph.cs` is the app's only icon set: every icon is a `GlyphKind`, used as a
`Glyph` control or drawn with `Glyph.Draw` inside a `Render`. Nothing else parses path
geometry, and no icon is a typed character (`RunIcons`).

- **Grid:** every icon is drawn on a 24-unit grid and scaled as a whole to the size asked
  for (never `Stretch.Uniform`, so a wide and a tall icon keep their balance). In a button
  an icon is 16.
- **Line:** stroked parts take the shared pen, 1.6 units on the 24 grid with round caps
  and joins, so every icon in the rail, browser, Preferences, palette, history and player
  matches in weight.
- **Filled vs stroked:** a shape is outlined unless it needs mass to read. Filled: the
  transport shapes, the OS marks (Apple, Windows, Linux), `StarFilled` (a favourite; `Star`
  is the outline), the disclosure carets. Fills take the ink, never a second colour.
- **Shapes:** every icon keeps its 0.56.1 shape, redrawn on the grid, except **Follow
  playhead**, which becomes a playhead with a short arrow pointing right (tooltip "Scroll
  the arrangement to keep the playhead in view"). **Show automation** keeps its shape
  (tooltip "Show parameter-automation lanes (⌘⇧A)").
- **Colour:** ink 2 at rest, orange on the soft tint when on, derived ink on solid orange
  when live.
- **A new icon** is a new kind in `Glyph.cs` with a one-line comment naming what it
  depicts, drawn on the grid with the shared pen, not a path in the view that needs it.
  No unicode symbols (`■ ▶ ● ✓ ★ ▾`…) and no emoji as text; `·` separates meta; `→` in
  prose is fine.

## Devices

All 50 devices in 0.56.1 (16 instruments, 25 audio effects, 6 MIDI effects, 3 racks) are
redrawn with this guide, and Nota Vocoder, added since, follows the same rules. Every
control, label and value stays.

**Do**

- Keep every control, in the same groups as 0.56.1.
- Use pressed-in sections, value bars, capped knobs and orange values inside the card.
- Put the on/off switch at the right of every header, instruments and racks included.
- Fit every tab of a panel in the 190 px under its tab row: rows on a fixed grid, at least
  3 px between a control and a rule, nothing clipped at the bottom or the right edge.
- Say a reading once. A readout under the knobs carries what the knobs add up to (a
  result, a derived time), not their own values again; an in · out readout is one line.
- Give a control one home. When it shows on two tabs, keep the one where it does its
  work; never drop it outright.

**Don't**

- Add a status line under the card; a control that lived there moves into the card.
- Use thin sliders, monospace readouts or capital-letter labels.
- Fade anything out; draw it in ink 4 with nothing picked.
- Wrap a note onto a second line in a narrow column; it goes in the control's tooltip.

### The card shell

- **Every card uses one shell** (`DeviceChainView.Shell.cs` `BuildCardShell`): effects,
  instruments, MIDI effects, racks (the Instrument Rack's "Rack · N chains" is its type
  tag), the generic instrument card, hosted plug-ins, and the Sends card (a `Utility` card
  with no device menu, selection or presets). Never build a header of your own.
- **The Sends card** sizes to its returns: one pressed-in section holding a 60 px column per
  return track (its colour square and letter, the knob, the amount, the return's name), so
  it is narrow with one return and grows a column for each return added.
- **Header 32** (`DeviceCardKit.HeaderH`), in fixed slots, left to right: name · preset
  menu · type tag and the card's accessories (MIDI learn, the S / L size toggle) · power.
  The name trims before it reaches the preset menu; a compact card drops the "Nota "
  prefix. The preset menu (‹ Name ⌄ ›) opens the factory presets and its arrows step
  through them, wrapping. A/B, move and delete live in the header's right-click menu; drag
  the card by its whole header; Delete removes it.
- **Body:** sections 4 from the card edge, 6 apart, 8 padding (`DeviceCardKit.Section`).
  Graph windows keep their frame inside a section. Columns run choice → work → output:
  source or preset on the left, graph and parameters in the middle, levels and mix on the
  right.
- **Values** are orange lines (knob arcs, value bars, the selected point); the identity
  colours some devices had (teal, olive, rose for a channel, band or modulation) become
  greys told apart by shade.
- A **bypassed** card keeps its layout and takes the unavailable look via `Inactive`.
- A **selected** card carries a 1.5 px orange ring outside its outline.
- **Add device:** the end of a chain is a 28 px-wide key with a `Plus` glyph and a
  tooltip; its dashed outline shows only while a drag is over the chain. A rack's chain
  ends the same way.
- **The chain** sits tight in the Devices panel: a 6 px inset all round and 6 px between
  cards (`DeviceChainView.ChainInset`, room for the selection ring), and the panel opens at
  exactly the cards' height (seam + 32 px toolbar + insets + 260). A card wider than the
  panel scrolls sideways.
- **Hosted plug-ins** (VST3 / AU / CLAP) use the same shell and keep their own parameter
  list: S 260 wide names it over *Open editor* and *Save preset*; L 700 adds the parameters
  as a paged, filterable grid.
- **Widths.** Three fixed sizes, all 260 high (`DeviceCardKit.CardS / CardL / CardWide`):
  **S 260** (mini and folded cards), **L 700** (every device unless listed below) and
  **Wide 1050** for the densest devices. A card never takes a width in between: a device
  that outgrows 700 is repacked to fit or moves up to 1050 and uses the room. The
  parameter-list stubs (190) and the generic "Open GUI" card (170) are the exceptions; EQ-8
  and Dynamic EQ-8 also fold to a narrower strip.

### Instruments (16)

| Device | Width | What changes |
|---|---|---|
| Aurora | 1050 | Status line removed. Rail sliders become value bars; the teal modulation colour goes, so Position, Fine, Detune and Spread read like the rest. Wide so the oscillator strip fits on one line. |
| Bass | 1050 | Status line removed. Rail sliders become value bars; filter type in one row of four instead of a 2 × 2 grid. Wide so the filter graph keeps its frequency labels. |
| Consort | 1050 | Status line removed. The patch-bay row under the card goes: its cable count and Open menu move to the header, its "no cables" note is dropped. Mixer, glide and drift sliders become value bars. |
| Flux | 700 | Status line removed. With no source the React controls (Listen, Target, the source menu) are drawn in ink 4; the four world names lose their colours. Glide, tune and gain become value bars; React keeps its header line. |
| Grain | 700 | Status line removed. Read sliders become value bars; Scan speed is inactive in Freeze, so its row is ink 4. |
| Keys | 1050 | Status line removed. MIDI learn and the S / L switch stay in the header; Play sliders become value bars; the teal goes. Wide so the pickup window keeps its size and the pedal sits beside its label. |
| Monolith | 700 | No status line before either. Mixer, Ext, Feedback and Drift sliders become value bars. |
| Mosaic | 700 | Status line removed. MIDI learn stays in the header; Volume, Pan and Glide become value bars; the teal Vel → Vol arc turns orange like the rest. |
| Operator | 700 | Status line removed. Level and Global sliders become value bars; the Volume knob sits beside the meter. The teal modulator marks become grey (carrier D stays in ink 1). |
| Pendulum | 1050 | Status line removed. The Rate slider becomes a value bar on its own row under Free / Sync, division and Motion. The teal leads and times become grey; the ball striking a wall stays orange. |
| Pentad | 700 | Status line removed; the Voice menu moves up beside the Mixer and Output tabs. Mixer sliders become value bars; the flat wheels become the round wheels. |
| Physical | 700 | Status line removed. Tune, Fine and Note off become value bars; the teal "env → freq off" readout becomes grey. |
| Rhythm | 1050 | Status line removed. The Step strip sits under the Voice panel so the eight kit voices keep full-height rows. Swing, Human and Accent become value bars; the teal trigger line, playing bank and velocity lane become grey or a picked segment. |
| Sampler | 700 | Status line removed. Volume, Pan and Glide become value bars; Crossfade is ink 4 while the loop is off. |
| Synth | 700 | Status line removed. MIDI learn and the S / L toggle stay in the header after the type tag; Volume, Pan and Glide become value bars. Pulse W is ink 4 while the wave is not Square. |
| Volt | 1050 | Status line removed. Cutoff, Reso and Glide become value bars. The repeated knob and Route labels of the two oscillator rows become one row of column heads (Noise keeps its Color label); in the Global rail the meter stands at the right. |

### Audio effects (25, plus Vocoder)

| Device | Width | What changes |
|---|---|---|
| Auto Filter | 700 | Status line removed. Shape faders become upright value bars; Mod-tab sliders become value bars. |
| Auto Shift | 700 | Status line removed. Shift-tab sliders become value bars; the In and Corr pitch columns stay as empty readout columns until something is sung. |
| Beat Repeat | 700 | Status line removed. Character-tab sliders become value bars; Repeat filter and Latch share one row. |
| Ceiling | 700 | Status line removed. Look and Link become value bars; the Limit title stays orange while the limiter is reducing. |
| Chamber | 700 | Status line removed. Blend and Wet faders become upright value bars; Levels-tab sliders become value bars and the wet meter a thin level bar. The IR, decay and LFO views lose their teal and mauve (greys by shade). On EQ · Mod the EQ position switch sits beside Tail EQ, so Modulation and Shimmer fit; the shimmer toggle reads Feed back. |
| Chorus | 700 | Status line removed. Rate, Offset, HPF and the Voice sliders become value bars; Detune and Delay stay read-only range bars. |
| Compressor | 700 | Status line removed. Thr and Mkp faders become upright value bars; Ratio, Mix and Range become value bars. |
| Crush | 700 | Status line removed. Grit-tab sliders become value bars; the In and Out level columns stay as empty readout columns with no signal. |
| Delay | 700 | Status line removed. The two loop faders become upright value bars; the level sliders become value bars. |
| Dynamic EQ-8 | 1050; 584 folded | The EQ Eight layout (below), graph 473 × 169. Each band chip carries a dynamics activity bar along its bottom. The panel is drawn like EQ-8's, in two columns on one set of rows so their parts line up: the band column (head, shapes, Freq / Gain / Q, then Key Self / Ext and Solo) beside the dynamics column (title and fold arrow, Static / Duck / Lift beside the shapes, Thresh / Range / Attack / Release as knobs beside Freq / Gain / Q, then the Dynamic switch and an inline Output knob, as EQ-8's Scale and Output). Range is a bipolar knob (left cuts, right boosts); Thresh carries the band's level as a marker. A picked but switched-off band has a grey toggle and an orange-ring graph point; the dynamics knobs are ink 4 while the band is Static. The right panel folds like EQ-8's (below). |
| EQ-3 | 700 | Status line removed. Band faders become upright value bars around 0 dB; the band colours (rose, white, teal) become greys. |
| EQ-8 | 700; 580 folded | The EQ Eight layout (below), graph 470 × 169, with the right panel one narrow column (200) as before. Slope and Channel sit side by side under the picked band's knobs, each named underneath like a knob; Scale and Output close the column as small knobs. Analyzer and Auto gain sit over the graph's top-left corner, the auto gain in effect on the switch's word. Channel uses the short names St, M, S, L, R. The status line's draggable Out readout is the same control as the Output knob, which stays. The right column folds (below). |
| Flanger | 700 | Status line removed. Sliders become value bars (Feedback fills from the centre); the right channel draws in a darker grey instead of teal. |
| Forge | 700 | Status line removed. The three stage boxes become rows split by rules: each stage dot becomes an on switch, the selected stage shows a tinted number. Amount, Wet and Out stack as value-bar rows above the transfer graph; switched-off stages are ink 4. |
| Lens | 700 | Status line removed. The Top and Range rail faders become upright value bars; Peak hold and Mid / Side share one row. |
| Level | 700 | Status line removed. The correction fader becomes an upright value bar around 0 dB; target and peak marks are grey instead of teal. |
| Orbit | 700 | Status line removed. Rate, Shape, Amount, Phase and Mix become value bars. The teal goes: the right channel (curve, legend, playhead dot, R tags) is a darker grey, L the curve colour. |
| Phaser | 700 | Status line removed. Rate, Stereo, Center, Depth, Feedback and Mix become value bars (Feedback from the middle). The teal goes as in Orbit. |
| Prism | 700 | Status line removed. Amount and Out faders become upright value bars; the band threshold sliders become value bars (an off band is grey). The band colours (olive, teal) go: band marks and crossovers are greys told apart by shade. |
| Reverb | 700 | Status line removed. Decay and Damp faders in the Tail column become upright value bars; the Levels sliders become value bars. |
| Shutter | 700 | Status line removed. Detector filter and Peak hold share one row. Thresh, Return and Floor become value bars. |
| Strata | 700 | No status line before either; the live strip (bar counter, cycle ticks, the four transport buttons) heads the Layers section. Feedback, Input gain and Speed become value bars; Record keeps its red. |
| Utility | 700 | Status line removed. Width, Balance and Gain become value bars (Balance and Gain from the middle). The Mono knob is grey while Mono below freq is off. |
| Valve | 1050 | Status line removed. Wide so five knobs and their readouts fit side by side and the five cab sizes fit in one row; Cabinet sim and Noise gate share one row. Gain and Out faders become upright value bars; Axis and Gate become value bars. |
| Vintage | 1050 | Status line removed. Wide so the knobs and their two readout lines fit side by side. Drv and Wear faders become upright value bars; Low, High (from the middle) and Mix become value bars. |
| Vocoder | 700 | Added after 0.56.1, so the guide has no picture of it yet. It follows the same rules: the card shell, pressed-in sections, value bars, capped knobs, no status line. |

**The EQ Eight layout** (EQ-8 and Dynamic EQ-8), the classic EQ Eight arrangement drawn in
this guide: the graph leads at the left under a slim row of eight band chips (20 high), and one
panel at the right edits the picked band. Each chip reads on one line: the band's number (ink 1),
its filter shape and its frequency (ink 2), all ink 4 while the band is off; the band being edited
has an orange outline and an orange number. A click picks the band, a double-click switches it on
or off. The panel's band
column opens with the toggle, the type's name and an On switch, then the six filter shapes in a
groove (Low cut, Low shelf, Bell, Notch, High shelf, High cut; the picked one tinted, its name
in the tooltip), then Freq, Gain and Q side by side. A row that does not apply (Slope on a shelf band) is ink 4
with nothing picked. The card stays 260 high; the graph grows sideways only.

**The EQ fold.** EQ-8's and Dynamic EQ-8's right panel folds to a narrow strip (60) with an
arrow to bring it back over the picked band's Freq, Gain and Q knobs, stacked and centred in the space under it; the card narrows from 720 to 580
(Dynamic EQ-8 940 to 584); the band tabs and the graph keep
working (a node's right-click menu still sets type, slope and channel, or type, mode and solo). Folding hides nothing that unfolding doesn't bring back, and changes
no parameter. The fold is kept per device for the session (like Prism's picked band), not
saved in the project.

### MIDI effects (6)

| Device | Width | What changes |
|---|---|---|
| Arp | 1050 | Status line removed. Play sliders become value bars; Transpose and Steps share one row. Wide so the rate row fits. |
| Chord | 700 | Status line removed. Shift rulers, Strum and Spread become value bars; the shift on/off dots become switches. |
| Length | 700 | Status line removed. Length and the three modifier sliders become value bars; each modifier puts its value on the bar row with its hint under it. |
| Random | 700 | Status line removed. The five amount sliders and Chance become value bars. |
| Scale | 700 | Status line removed. The root stepper (‹ C ›) stays on the Root section's title row. |
| Velocity | 700 | Status line removed. Drive and Random become value bars, the out range a two-ended bar; Random off is ink 4. Last 12 notes stays empty until notes are played. |

### Racks (3)

| Device | Width | What changes |
|---|---|---|
| Audio Effect Rack | 700 | No preset menu (the header holds the name, "Built-in" and power). The macro strip becomes its own section across the top; the eight macro sliders and Gain become value bars. The rail is regrouped so all of it fits: Mode, then Dry / wet beside Gain and PDC, then Macro map, Fold and Save on one row; Macro map is a plain button. |
| Drum Rack | 700 | Status line removed. Swing, Humanize and the pad sliders become value bars; Pan and Tune are centred bars. |
| Instrument Rack | 700 | No preset menu; the header holds the name, "Rack · 1 chain" and power. The macro strip becomes its own section across the top; the eight macro sliders and Glide become value bars. Rack out puts Volume beside Glide, with Macro map, Fold and Save on one row; Macro map is a plain button. |

## Nota Remote

The phone page (`src/web/remote`, spec 014) **keeps its own styling for now**: its radii
(0 / 1 / 2 / 3 / 4 px), its type (Geist for words and Geist Mono for numbers, bundled with
the page) and its layout are not redrawn by this guide. Only the shared colour tokens it
reads change, and its guard test (`RunRemote`) keeps passing.

It holds no colour value: Nota sends the theme it wears and the user's dark-mode and
light-mode picks with all their tokens (the `theme` message), and the page sets each token
as a custom property named after it (`BgApp` → `--bg-app`, `SurfaceRaised` →
`--surface-raised`) plus the derived inks `--ink-on-accent` / `-mute` / `-solo` /
`-record` / `-success` (`NotaPalette.ReadableInkOn`'s pair, which Nota sends). **Follow
Nota** wears Nota's theme, **Follow phone** a pick by the phone's own scheme. Before Nota
answers, the page wears the last theme it stored (a head script paints it before the app
loads), or the Studio Dark fallback that `theme-plugin.ts` builds from the theme files at
`npm run build`. Elevation is built the same way: `--bevel` (`ShadowBevel`), `--sunken`
(`Rule`), `--popover` (`Seam` + `ShadowPopover`), `--scrim` (`ShadowModal`).

Its structure: seams, orange for live or chosen only, solid semantic fills with derived
ink, sentence case, at phone sizes: touch targets 36–56 px and words 11–16 px (the big
position and the count-in larger). Its icons are inline SVG with the shared stroke, since
`Glyph` is native. `npm run mock` serves the built page against a stand-in Nota with a
demo project and the real theme messages, for screenshots without the desktop app.

## Enforced by tests

`tests/Nota.SmokeTest/DesignTokenCheck*.cs` runs in the smoke test (the `-- design: … --`
sections) and fails the build on what is listed here, as the checks stand today. Some
still hold the earlier sizes; they change in the commit that redraws that control (§ Known
drift).

| Check | Holds |
|---|---|
| `Run` | `NotaTheme.axaml` holds no colour; every referenced `Brush.*` key is published (`KeyMap`); every `Shadow.*` key a style names is one `NotaThemeService` builds |
| `RunThemes` | Every theme file: shape, id, kind, exactly the token catalogue, valid colours, opaque except the shadows and the see-through `Well`, `WellTrack`, `KnobTrack`, `DeviceSectionEdge`, `Bevel`; every readability pairing (§ Readability bar); `BuiltInOrder` lists exactly the files; no view names a theme id; the theme preview draws the theme's own colours (`ColorIn`) |
| `RunAppearance` | Old settings migrate (Dark / Light / System); unknown and wrong-kind ids fall back; picks per mode; storing keeps the legacy setting in step |
| `RunInterfaceScale` | Scale presets 75 / 100 / 125 / 150 / 200 %; anything else reads as 100 % |
| `RunGeometry` | `Radius.*` / `Control.*` / `Space.*` in XAML equal `NotaGeometry`; no undefined keys; no `CornerRadius` literals, drop shadows or gradients in views; `Rectangle` `RadiusX` / `RadiusY` literals on the 0 / 2 / 3 / 4 / 5 / 6 / 7 / 9 scale (`RadiusAllowList.txt`) |
| `RunType` | `Font.UI` matches `NotaFonts.UiUri`; it is the only font family (no Mono URI); `+tnum` is on (`NotaFonts.Tabular`); the four Geist weights are bundled and no other `.ttf`; the Geist OFL licence ships; no font names or `FontFamily.Default` in views; nothing under 7 px |
| `RunControls` | No unicode icons typed as text; `Knob` snaps to its three sizes (26 device, 22 label beside, 28 flat); `SwitchTrack` is 20 × 12 with an 8 px knob inset 2; `SliderTrack` is a 16 px value bar in a groove with a 3 px line |
| `RunVisualisers` | Graph windows via `NotaGraph`; no fills under curves; no meter or spectrum ballistics; meter channels 3 wide, 1.5 apart, green to 70 %, amber to 88 %; the arrangement playhead 1 px without glow |
| `RunLayout` | `DeviceCardKit.HeaderH` 32 and cards 260 high; every card width between 260 and 940; the transport a docked row at the top at `Control.TransportBar` with its console; browser rows 30 |
| `RunNumbers` | Display culture installed; no hyphen in a negative format section or before ∞; no hyphen-minus before a digit in a view literal (`NumberAllowList.txt`); no invariant culture on readouts; thin space before units; no vowel-dropped labels |
| `RunBans` | No hex outside `Ink()` (bar three data tables), no RGB typed as numbers, no named system colours, `Opacity` only on drag ghosts, no brush snapshotting a theme colour |
| `RunStudio` | Every `Radius.*` on the 0 / 2 / 3 / 4 / 5 / 6 / 7 / 9 scale; only `Shadow.Popover` / `Shadow.Modal` blur more than 2 px, no literal `BoxShadow` in views; no positive `LetterSpacing` in the theme or views (bar the Remote pairing code), `NotaType` tracking 0, no upper-casing in the shared label builders; device knob arcs and value-bar lines `Accent`, flat knobs `TextTertiary` at rest, graph curves `VizInk`; toolbar 44, transport well 32, seam 1; no Ember value in the Studio themes |
| `RunLabels` | No all-caps or Title Case label literal in a label context (`Text`, `Content`, `Header`, `Title`, tooltips, menu headers, command titles, palette and Settings headings, local label builders), unless it is `NotaText.Fixed` acronyms, goes through `NotaText.Label`, or names a view, product or proper noun; interpolated strings are skipped. `LabelAllowList.txt` holds only data |
| `RunIcons` | No path geometry outside `Controls/Glyph.cs`: no `Geometry.Parse` / `StreamGeometry.Parse` / `PathGeometry.Parse`, no axaml `Data="M…"`, no SVG path string (`IconAllowList.txt`, a per-file count) |
| `RunMenus` | Menu icons through `MenuKit.Icon`, destructive items through `MenuKit.Danger`, no Color / Track height submenus (`MenuAllowList.txt`) |
| `RunBrowser` | Every built-in device sits in exactly one category of its own tab (`DeviceCategories.cs`); no stale or empty category; names in sentence case; 16 / 27 / 6 devices shelved |
| `RunRemote` | Nota Remote's page: no colour literal in its sources or `index.html`; the first-paint fallback built from the theme files; no letter-spacing, `uppercase`, `.toUpperCase()` or `.caps`; radii 0 / 1 / 2 / 3 / 4 px or 50 %; every custom property it reads is a theme token or one it defines |

Not enforced by a test yet: the type scale, the sizes in § Shapes, the 24 header and the
card widths in § Devices, where orange may appear, the absence of depth in the shell, the
track colour order, and every per-device note. Those are held by review against this file
and the guide.

When a check fails, fix the view, or, for a genuine exception, add it to the check's
allow-list with a comment saying why. The ratchet lists in `tests/Nota.SmokeTest/`
(`LabelAllowList.txt`, `IconAllowList.txt`, `NumberAllowList.txt`, `RadiusAllowList.txt`,
`MenuAllowList.txt`) only shrink: a new offender fails, and so does a line that no longer
matches anything.

## Accepted exceptions

Places where a screen's needs override a general rule, kept on purpose:

- **The round wheels keep their 0.56.1 look** inside device cards and never sit in a
  groove.
- **Every card stays 260 high**, EQ cards included; a device that needs room grows
  sideways (and the EQ cards fold).
- **Menu hover is a full orange fill.** The guide's three strengths don't cover menus; a
  hovered item counts as live (§ Menus).
- **Menu popups without a blur**, a seam ring only (§ Menus).
- **Play turns green in Session** (`tp-play.session`) while clips run.
- **Primary actions by weight, not colour**: Nota has no solid-orange action button.
- **No in-window menubar row.** macOS uses the system menu; Windows / Linux keep the native
  menu bar. The title bar is 32.
- **Nine track colours**, so stored track indices keep their meaning; only the order new
  tracks take them changes.
- **Data hues by `Ink()`**: the Strata layer colour `#C99C55`, tag swatches `#C8A24B`,
  Random `#D0603F`, the Rhythm kit dots. They are data, not orange and not state; their
  light values are derived or pinned.
- **Segments with more than four options** stay segments where the choice is frequent:
  Amp Model (7) and Cabinet (5), Compressor Character (5), Vintage Character (6), Delay
  Division (8), Arp Rate (8) and Order (8), Aurora Warp (5) and Filter (5), Bass LFO wave
  (5), Monolith Glide range (6), Pendulum Division (5) and Wave (5).
- **Knobs in table rows** (the oscillator rows and envelope strips of Consort, Pentad and
  Monolith) take the 22 size with the label beside or above, as the row allows.
- **`Freq`, `Reso`, `Thresh`** as labels (§ Labels and copy).
- **Hero readouts** larger than the device scale: Strata's bar number, Shutter Open/Shut,
  Auto Shift's note, Level's value.
- **Zoom 28 px per beat** by default.
- **Nota Remote keeps its own styling and Geist Mono** for now (§ Nota Remote).

## Known drift

True today, not yet fixed:

- **No single brush for orange text on the soft tint.** The guide uses `AccentBright` in
  Studio Dark and `AccentPale` in Studio Light; `AccentPale` has no `Brush.*` key yet, and
  neither has `SurfaceDeep`. Add the keys (or one derived "on soft tint" brush) when the
  first view needs them.
- **The guide's colour table names `Brush.Panel` for both "panel" and "card".** The theme
  files keep two tokens: `Panel` `#26282C` and `SurfaceCard` `#2B2D31` in Studio Dark (the
  same `#E4E5E7` in Studio Light); this file uses `SurfaceCard` for cards.
- **The guide's EQ-8 sizes were stale** ("690", graph "592 × 156"); EQ-8 is 720 wide, 580
  folded, graph 470 × 169. The guide is to be corrected, and it has no picture of Nota
  Vocoder yet.
- **Ember-era token names.** Several tokens hold the same value as a Studio role and stay
  only so their call sites keep working: `SurfaceAbyss`, `TrackOff`, `SurfaceActive`
  (= `BgSunken`); `Gutter`, `BorderDefault` (= `Seam`); `BorderStrong`, `GraphBorder` /
  `Brush.Hairline` (= `Rule`); `BorderBrass` / `AccentEdge`; `AccentSubtle`, `AccentDim`,
  `AccentMute`; `TextFaint` beside `TextDisabled`; `ChromaSteel`. `DeviceCardKit.Brass`
  (an alias of `Accent`) and `Inactive.IsBrass` still say brass, and comments in a few
  views say Graphite, Paper, brass or almanac. New code uses the names in § Colour; a
  rename pass can follow.
- **Column order** choice → work → output is applied in Utility; the other cards have not
  been audited card by card.
- **Graph controls** (curves, pads, envelope and matrix handles) reset on double-click but
  do not take typed values yet (specs/035, later PRs).
- **Names:** presets with emotional names (Screaming Lead, VHS Fever, Gentle Master,
  Transparent Safety, Dive Bomb, Laser Zap…) and multi-word device names (Auto Filter,
  Beat Repeat, Dynamic EQ-8, EQ-3, the "Nota " prefix, dropped only from browser rows and
  compact card headers) remain; renaming touches MCP names, the browser and saved
  projects.
