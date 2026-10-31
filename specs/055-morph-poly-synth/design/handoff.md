<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Nota Twin card — design handoff

The visual reference is [`twin-card.html`](twin-card.html). Open it in a browser. The card in it
is live: tabs, A / B, Single / Layer / Split, S / L, the morph demo, the delay on/off state, the
theme (Studio Dark / Light), and knobs you can drag. At 1× scale, one CSS px is one Avalonia
logical px. Token values in it come from `Theme/Themes/studio-*.json`. If the HTML and a theme
file disagree, the theme file is right.

Scope: `src/managed/Nota.App/DeviceCards/Instruments/TwinInstrumentCard.cs` and
`Controls/TwinViews.cs`. No engine or parameter changes. Every control drives the same param id it
drives now.

## Frame

| Part | Now | Target |
|---|---|---|
| Card | 700 × 260, S 260 | unchanged |
| Status line | 18 px at the bottom | **removed**. It repeated the header badge and the side switch. Its 18 px go to `BodyH`. |
| Morph column | 64, Morph knob 40, A/B, three raised buttons | 70 (64 + Rule). Morph `KnobMain` 44, value mono 9, **morph strip** 56 × 3 with `A` and `B` at its ends (the end being edited takes the accent), “Edit” A / B segments (fill, 18 tall), A → B / B → A / Swap as **ghost** buttons 56 × 16 |
| Tab row | 20, tabs + Parts + Single/Layer/Split (clipped) | 22, Rule under it. Tabs 10.5/600, padding 7; the chosen tab gets Ink 0 and a 2 px accent underline. Right side: Lower / Upper (only when the layout is Layer or Split), then Single / Layer / Split, both 18 tall. **“Parts” button removed** |
| Tab body | ~186, content top-aligned, bottom 40 % empty | 612 × 204. Sections via `DeviceCardKit.Section` (padding 5/8, gap 4), each with a 16 px header row. The header row carries the section's switch or chooser on its right |
| Section chevrons “›” | between every island | **removed**. The Rule alone divides sections |

**Row rule.** Sections that sit side by side use the same row order: header 16, then graph or
chooser, then a knob row (53 tall for a 34 knob, 62 for a 44 knob), then a second knob row, then
slider rows of 14. This keeps knob rows on a shared baseline across the tab.

## Per tab

| Tab | Changes |
|---|---|
| **Osc** | Osc 1 212 · Osc 2 212 · Noise rest. Each source gets a full-width wave graph (196 × 34, `TwinWaveView`) where the 56 × 16 thumbnail was. Row 1: Wave, Width, Metal, Level. Row 2: Coarse, Fine (+ FM 1, FM cut in teal on Osc 2). `o*bal` / `noisebal` become a **slider row “To filter”** with end words *multi … ladder* (it was a knob reading “Filter · ladder”). Noise gets a small colour-tilt graph |
| **Filter** | Multimode 144 · Route 132 · Ladder 144 · Response rest. `mmmode` is continuous: it becomes a **slider with four marks** LP · BP · HP · notch. Cutoff and Resonance are 44 in both filters; Key and Env are 34 teal. Route: `route` becomes a series ↔ parallel slider plus a **route diagram** (new read-only view, 116 × 62), then Brute and Drive. Ladder: 12 / 24 in the header. Response 176 × 172: combined curve in `VizInk`, multimode `Teal`, ladder `Steel`, the active node in accent |
| **Env** | Three equal sections, 204 each. Graph 186 × 70. Under it, **four `DragNumber` cells** labelled Attack / Decay / Sustain / Release, replacing the unlabelled “1 ms · 91 ms · 50 % · 39 ms” string. Then Velocity (teal) + Curve, or Delay + Velocity on Mod env. Loop stays in the Mod env header |
| **LFO** | LFO 500 · Vibrato rest. LFO 1 / 2 / 3 moves into the section header. Left column (206): shape segments full width; then Sync, Retrig and Voice / Global on one row; then Rate (Division while synced), Fade and Amount (teal) as 44 knobs. LFO 3 adds Symmetry and Curve to that row at 34. Graph 258 × 166 with the live phase line. Vibrato: Depth 44 teal, caption “LFO 1 × mod wheel” |
| **Matrix** | Routes 412 · Macros rest. The header shows `7 / 64` and **+ Route**. Column heads read Source · Destination · Depth A (or B). **Rows are 20 tall** (7 visible, previously 5): number, source dropdown, arrow glyph, destination dropdown, **centre-zero teal depth bar** (new small view, the other side's depth as a faint tick, drags horizontally, double-click resets to 0), mono value, delete. Macros: 2 × 4 knobs 34, plus a one-line hint |
| **FX** | Mod FX 178 · Delay 178 · Reverb 178 · All rest. Each effect gets a 162 × 40 read-only preview (sweep · taps L/R · tail) and 44 knobs. **An effect that is off puts its body in `Inactive`**, so its Sync / Ping-pong switches stop showing the accent. The old “Section · Effects” column becomes **All** with a switch and the hint “Off bypasses all three, to use Nota's effect devices instead.” |
| **Seq** | Header: Plays (Layer/Split only), Record, To clip, On, Seq / Arp. Toolbar: Pattern 1–8, Length, Rate, Swing, Transpose, Latch. Step grid 594 × 78: chords, a velocity foot, the playing step in `AccentSubtle`, the selected step with an accent outline. Motion row: lane 1–4, target dropdown, Smooth, Arm, Clear, then Steps pages on the right. Motion lane 594 × 26 in teal. Last row: the selected step readout, Rest, Tie, and the hint that used to sit in the status line. **Arp:** Mode, Rate, Octaves, Hold on one row; arp preview graph 420 × 120; Gate and Swing at 44 |
| **Voice** | Voices 200 · Stereo 132 (118 in Layer/Split) · Output 140 / Part 160 · Pad rest. The voice mode chooser moves into the Voices header. **Detune is `Inactive` outside Unison**, with a hint saying why. Stereo: Spread, Drift at 44, then “Spread by” segments. Output: Volume, Pan at 44, then **Bend range as segments** ±2 · ±5 · ±12 · ±24 (`bendrange` has 4 steps; it used to be a knob). In Layer / Split this column becomes **Lower part / Upper part**: Volume, Pan, Octave, then Split key + Learn (Split only), Voices share, Bend. Pad 118 × 108 with the X / Y / Z readout and the legend “X cutoff · Y morph · Z vibrato” |
| **S size** | Morph column (no copy actions) · pad 172 × 124 · Cutoff, Reso, Attack, Release in a row |

## New or extended pieces

1. `DeviceCardKit.SliderRow` overload: end words or N marks under the track, with an optional
   hidden label and value. Used for To filter, Route and Mode.
2. `TwinViews.cs`: `TwinRouteView` (series/parallel diagram), `TwinDepthBar` (matrix depth),
   and three read-only previews `TwinModFxView`, `TwinDelayView`, `TwinReverbView`. All of them go
   through `NotaGraph`: well, Rule frame, radius 3, curves in `VizInk`, no fills under curves.
3. A morph strip with A/B end labels (extends `TwinMorphStrip`).
4. ADSR `DragNumber` cells in a 4-column grid (an existing control used in a new layout).

## Unchanged

- Param ids, ranges and formatters (`Attack`, `Cutoff`, `Wave` …).
- The knob's other-side tick and the morph dot that shows where the sound is now.
- The header from `BuildCardShell`: name · preset · badge · S/L · power.
- Interaction contract: vertical drag, Shift for fine steps, double-click resets, left button only,
  automation gestures, MIDI Learn.

## As built (2026-10-31)

Implemented in `TwinInstrumentCard.cs` and `TwinViews.cs`. In four places the build differs from the mockup:

- **Parts menu.** The menu still exists. It holds copy and swap of the parts and *Load into Lower / Upper*, which also turns a Single patch into Layer. It is now a button in the header of Voice → Output (Part).
- **Motion lane.** Motion values are still drawn as teal ticks inside the step grid, not in a separate strip. The grid is taller (92 px) to give them room.
- **Arp preview.** No arp preview graph was built. The arp page has the mode row, Gate and Swing at 44, and a hint.
- **Filter graph colour.** The ladder curve stays rose (`TwinFilterView`), as before, not blue.

The matrix depth uses the existing `SliderTrack` in bipolar, teal mode. The other side's depth is shown as its `Marker`. Kit additions: `SliderRow(…, withTrack:)` and `DeviceCardKit.SliderMarks`.

## Checks

- [ ] `scripts/build.sh` passes, including `DesignTokenCheck` (`RunStudio`, `RunLabels`, `RunIcons`).
- [ ] Nothing is clipped at 700 in Single, Layer or Split (tab row, Voices header, Seq toolbar).
- [ ] With side B being edited, B is the only accent in the morph column; at morph 35 % the knobs show the “now” dot.
- [ ] With Delay off, nothing inside the Delay section is orange.
- [ ] Every tab in all four themes: Studio Dark, Studio Light, Ember Graphite, Ember Paper.
- [ ] Re-shoot the docs screenshots of the card (`nota-docs`).
