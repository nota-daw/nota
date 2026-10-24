# Device layout audit: PR 296 at 884314b

Every built-in device (49) and every tab state reachable from its tab rows (136 states) was
rendered headlessly at 2× in Studio Dark and Studio Light. A geometric checker then flagged
overlaps, clipped text, and controls that cross a panel edge. Each flag was checked by eye.
Layout doesn't depend on the theme, so both themes give the same results; the light renders are
included for colour checks.

- `dark/<device>/NN-<tab>.png`, `light/<device>/NN-<tab>.png`: one card per tab state
- `audit-dark.md`: the checker's raw output per state, with coordinates in card pixels at 1×

## Overlapping or covered (must fix)

| Device | Tab | Problem |
|---|---|---|
| Operator | right panel · Env | The envelope graph (VoltEnv, y 98–157) starts 8 px under the panel top, so the "carrier → out" label and the A/B/C/D operator picker (y 86–109) sit inside the graph. The A/D/S/R knobs (y 146–171) overlap its bottom edge by 11 px. The graph needs its own row below the label/picker row, and the knobs need a row below the graph. |
| Operator | Algorithm | The routing diagram's A–D stack is drawn over the "drag an operator onto another…" caption, and "out" is cut off at the bottom of the box. |
| Auto Filter | right panel · Output | The Retrig / Analog buttons (y 215–236) are drawn over the Gain row (y 204–217) and over the Tempo sync / Stereo phase switches beneath them. The panel content is taller than the panel. |
| Delay | Loop · Wow | The "Filter" mini-curve (77×49) is drawn on top of the Feedback and Wow rate knobs and their value/label text ("38 %", "Diffuse", "0.23 Hz", "Wow rate"). "Feedba…" and "Di…" are trimmed. "Tape mode" is cut off at the card edge. |
| Auto Shift | MIDI | The "Oct lock" button sits on the "Key Source is not MIDI" note (37×12 px overlap). |
| Shutter | Pattern | The Complexity slider's thumb sits over the word "Complexity". |
| Valve | Amp (default) | The "no oversampling · aliasing −57 dB" readout runs into the "Presence" label. |
| Drum Rack | Mixer | The scroll bar sits on the "ch" column. The last row ("Snare Tight") is cut in half at the panel bottom. |
| Compressor | Sidechain | "no key signal" sits on the dashed key line in the graph. |
| Velocity | default | "Last 12 notes" sits right on top of the history graph, with no gap. |

## Content cut off at a panel's bottom edge (right-panel tabs taller than the panel)

| Device | Tab | What's cut |
|---|---|---|
| Compressor | right panel · Output | "External key" switch, half visible. Makeup / Mix / Range rows have about 2 px between them, and their slider tracks overlap by 2 px. |
| Prism | right panel · Output (also on Band detail and Time) | The "Listen" / "Auto makeup" switches are cut in half. |
| Utility | right panel · Output | The buttons under Match are cut off (only their tops show). |
| Shutter | right panel · Meters | "External sidechain" is cut in half. |
| Chamber | right panel · Output | The row under Quality is cut off. |
| Delay | right panel · Output | "Wet only (send)" is cut at the bottom. |
| Mosaic | Filter | The last knob ("50 % Slic…") is cut at the card's right edge. "Env → Cutoff" pushes it out. |

## Too tight or trimmed (should fix)

- **Labels and values on top of each other** (spacing between rows is 0 px or less):
  - Operator Env: "Key → lvl" and "Vel → lvl" sit right under the knob labels.
  - Random: the slider rows' labels touch the slider above (Velocity / Skip / Octave).
  - Valve: knob labels sit under value text with no gap.
  - Grain Pitch: "0.0 st" is trimmed.
- **Graph axis labels drawn on top of each other or on the graph's title:**
  - Ceiling Reduction: 0 / −0.8 / −1.5 / −2.3.
  - Ceiling Loudness: −10 / −14 / −18.
  - Utility Levels: −12 / −24 / −36.
  - Utility Field: 10k / 1k / 100.
  - Lens Waterfall: "10 kHz" is over "Waterfall".
  - Lens Scope: the top-right readout runs over itself ("holdoff 12 ms" ∩ "free").
  - Auto Shift: the pitch axis F4…G3 is 7 labels in about 90 px.
  - Chorus: "8.00 ms" is over the curve.
  - Flanger: "0 dB" is over "+12".
  - Phaser: "fc 20k" sits on the curve.
  - EQ-8 / Dynamic EQ-8: band chip values "300" / "800" / "200" lose 1–2 px.
- **Tab-row trailing readouts and tab labels trimmed or cut:**
  - Delay: "L 1/8 · R 1/8" cut.
  - Compressor Motion: "attack 10.0 ms · release 120 r…" cut.
  - Compressor Motion: "GR peak 0.0 dB · 0 hits / wind…" cut.
  - Auto Shift MIDI: two trimmed hints.
  - Ceiling Reduction: "reached the clip · 0 ti…".
  - Ceiling Detector: "latency 132 s…".
  - Beat Repeat Slices: the "Triplet" segment is cut to 5 px at the right of the Grid row.
  - Selected tab labels lose 3 px when they go bold: Beat Repeat "Slices", "Filter"; Auto Filter "Envelope"; Ceiling "Loudness". The width is measured from the regular weight, so the bold text no longer fits.
- **Value text 1 px narrower than its text:** about 60 knob/slider values ("0.0 dB", "20.0 ms", "0.05 ms", "0.0 st") in Compressor, Crush, Forge, Shutter, Utility, Valve, Vintage, Lens, Prism, Reverb, Sampler, Mosaic, Volt, Chamber, Keys and Instrument Rack. They render fine at 2× but clip on some rasterisers. The fix is one shared min-width in the value cell rather than per device.
- **Arp:** the rate row (1/8T 1/16 1/16T 1/32…) is very tight.
- **Scale:** the root header "Ro…" is trimmed.

## Clean

These devices showed no real problems in any tab:
- **Instruments:** Physical, Aurora, Volt, Pendulum, Flux, Rhythm, Monolith, Pentad, Consort, Keys, Sampler, Instrument Rack.
- **Audio effects:** EQ-3, Reverb, Level, Orbit, Vocoder, Strata, Audio Effect Rack, Forge (layout only).
- **MIDI effects:** Chord, Length.
