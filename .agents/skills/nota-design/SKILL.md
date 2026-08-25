---
name: nota-design
description: Nota's UI design language — "Ember Graphite", a dark-only warm-graphite palette with a brass accent, used across the Avalonia/C# desktop app. Use whenever building or restyling any Nota UI (windows, panels, controls, device cards, custom-drawn views like the piano roll / waveform / arrangement) so everything stays visually consistent. Points at the authoritative token files and the full guideline rather than restating values.
user-invocable: true
---

# Nota design language — Ember Graphite

Warm graphite neutrals (hue ~40°) lit by a single **brass** accent `#D8A03D`.
Dark-only by intent: a DAW is used in dark rooms for long sessions.

## Read these first — do not work from memory

| File | What it gives you |
|---|---|
| `DESIGN.md` | **The guideline.** Rules, every token value, device-card conventions, the control kit, known drift. Start here. |
| `DESIGN.html` | The same thing rendered — swatches, live control specimens, card anatomy at true size. Open it when the question is "what should this look like". |
| `src/managed/Nota.App/Theme/NotaTheme.axaml` | **Definitive** source of colour, geometry and control styles. |
| `src/managed/Nota.App/Theme/NotaPalette.cs` | Mirror of the above for custom-drawn views. Kept in sync by hand. |
| `src/managed/Nota.App/DeviceCardKit.cs` | Palette aliases + atomic builders for device cards. |

If `DESIGN.md` / `DESIGN.html` disagree with the theme files, **the theme files win** and
the docs need fixing. Say so rather than propagating the drift.

> This skill deliberately does **not** restate token values. An earlier version did, and
> it drifted a whole palette out of date — it still described a cool-slate scheme with a
> cyan accent long after the app moved to Ember Graphite. Read the files.

## Non-negotiables

- **Never hardcode a colour or size in a view.** XAML: `{DynamicResource Brush.*}` /
  `{StaticResource Radius.*}`. Custom-drawn C#: read from `NotaPalette` or `DeviceCardKit`.
  (The card layer currently breaks this in ~628 places — do not add to it.)
- **One accent, spent sparingly.** Brass marks primary actions, selection, focus, active
  state, and the time-domain data that is the point of a DAW. Everything else is neutral.
  Green / amber / red are status only.
- **Flat fills.** No gradients, no textures, no background images, no glow.
- **Structure over shadows.** 1px `Brush.BorderDefault` lines separate panels. Shadows
  only for floating layers.
- **No scale or bounce.** Motion is 100–240ms ease-out, opacity + ≤6px translate.
- **Mono means data.** Numbers that are read, compared or aligned use `Font.Mono`;
  prose never does.
- **Sentence case, no emoji.** The only glyphs allowed as text are `·` and the transport
  symbols already in use.

## Device cards — the two-colour rule

Cards are a fixed 260px tall (`DeviceCardKit.CardH`): header 26 · LIVE strip 34 · body 200.

- **Brass = the audio path.** Anything the signal passes through. On a knob: `Accent = true`.
- **Teal `#5B9E9C` = modulation and detection.** LFOs, envelopes, sidechain detectors,
  followers. Pass as the knob's `ArcColor`; group behind a 2px teal left border with a
  teal section label.
- **Output rail stays muted** — `Brush.BorderStrong`, not brass.

Full anatomy, frame specs and rail dimensions: see `DESIGN.md` § Device cards.

## Adding a control

Reuse the kit before writing anything new — `Knob`, `KnobCell`, `ValueBar`, `MiniFader`,
`VFader`, `PanKnob`, `DragNumber`, `MeterBar`, `ChipRow`, `Labeled`, `TextButton`,
`NaBadge`. The inventory with sizes is in `DESIGN.md` § Control kit.

If you do add one, it **must** match the shared interaction contract:

- Vertical drag, up increases, full range over ~140px
- Double-click restores the default
- Left button only — right-click must bubble to the CV-modulate menu
- `GestureBegin` / `GestureEnd` bracket `BeginAutomationWrite` / `EndAutomationWrite`
- Register with `MidiLearn.Bind`
- Live-follow refresher that **skips while dragging**
- Mono readout so width does not jump

## Visualisers

A device draws **the thing it does**, not a generic graph — and where possible draws it
for real (the reverb tail is an actual impulse response; amp harmonics come from pushing
a sine through the real waveshaper). Ground is `Brush.BgSunken` with a `#221F1A` inner
border. Families and existing views: `DESIGN.md` § Visualisers.

## Applying it (Avalonia)

- `NotaTheme.axaml` is merged in `App.axaml`; the app runs `RequestedThemeVariant="Dark"`.
- Button classes: `primary`, `secondary` (default), `ghost`, `danger`. Transport chrome:
  `tp-icon`, `tp-play`, `chip`, `seg`. Containers: `Border.Card`, `Border.Panel`,
  `Border.well`.
- Text classes: `Heading`, `Caption`, `SectionLabel`, `Mono`.
- Custom-drawn views may cache token colours as fields, but the values must match the
  theme exactly.

## When you change a colour

1. Edit `NotaTheme.axaml`.
2. Mirror it into `NotaPalette.cs` — nothing enforces this.
3. Update `DESIGN.md` **and** `DESIGN.html` so the docs stay true.
