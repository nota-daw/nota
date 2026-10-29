# Contract: the shared rack card (UI)

Source of layout: `nota-design/Nota Racks.html`, frames 1a–1f. Source of colour, type,
geometry, header and icons: `DESIGN.md` + `Theme/Themes/*.json` (they win any conflict).

## Frame

- Standard device card, `DeviceCardKit.CardH` tall, 700 wide, inside `BuildCardShell`:
  name · preset picker (when the rack has presets) · type badge "Rack" · power switch.
  Fold and A/B: where the shell already offers them (right-click menu / fold strip).
- Body (inset `NotaSpace.DeviceInset`), three columns + status line:

| Region | Width | Content |
|---|---|---|
| Macro column | ~116 | Section title "Macros", Map key (on = accent key; mapping in progress), 2 × 4 macro cells: `Knob` 22 px, name, value (mono, "—" when unmapped), target-count badge (teal wash) |
| Centre | fill | Tab strip (`segmented`): Chains · Zones (Audio: Select) · Macros, right-aligned mono summary; tab body |
| Right panel | ~160 | Tab strip: Chain · Rack out (MIDI: Output); panel body |
| Status line | 18 | One `Caption` line, left aligned; nothing on the right |

Columns are `DeviceCardKit.Section`s (rule on the facing edge), not framed boxes.

## Chains tab

- Chain list (124): rows 34 px — colour stripe (chain palette colour), name (accent ink
  when chosen), M / S keys (`Mute` / `Solo` fills + `InkOn` when on), summary (mono:
  `C-1–A#4 · v96+` / `sel 0–63` / `2 devices`), level bar (green safe range of the meter
  scale; MIDI: activity). Chosen row: accent seam + `Wash(Accent)`. "+ Chain" dashed key;
  one `Caption` hint at the bottom ("Drop an instrument to add a layer." /
  "Drop an effect to add a chain." / "Drop MIDI effects here.").
- Device strip: chosen chain's devices left → right; for Instrument Rack chains in three
  sections — MIDI effects (own dashed drop target, "MIDI" kind badge) · instrument · audio
  effects (issue #329); MIDI effects drag only within their section. 96 wide compact cards (power dot,
  name, kind badge, up to three key params as `SliderTrack` rows with mono values,
  "Open" + `Glyph.Grip`), chevron glyphs between cards, a dashed drop target at the end.
- While mapping: params of every compact card show a teal dashed (not mapped to this
  macro) or teal solid (mapped to it) frame, the cursor is "copy", the card scrolls through
  all params; a header line under the tabs reads "Mapping · Macro N · Name — click to add
  or remove". Resting slider fill = `VizInk`; mapped param = teal fill + "M n" tag.

## Zones / Select tab

- Ruler: Instrument/MIDI — note names at C0, C2, C4, C6, C8; Audio — 0, 32, 64, 96, 127.
- One 24 px row per chain: name, key-zone bar with drag edges (whole notes), velocity-zone
  bar (Instrument/MIDI); Audio: select-range bar. MIDI: an extra Selector row on top
  (and per-chain select bars) — shown inactive (next ink step) outside Select mode.
- Hover probe (Instrument/MIDI): 1 px teal line at the hovered key on every row; chains
  that would play at velocity 100 get a teal wash and teal name. Footer: hint left,
  result right (`C4 · vel 100 → Volt + Pentad` / `nothing`; Audio: `selector 30 → Crunch`).

## Macros tab

- Header: slot ("Macro 1"), name (double-click rename), value (mono), curve segmented
  Lin · Exp · Log · S (no key chosen = Mixed, with a "Mixed" caption), 30 × 22 curve
  preview via `NotaGraph` (VizInk curve, accent dot at the current value; Mixed → no
  curve, "Mixed" text), Map key.
- Rows: colour stripe, `chain › device ›` + param name, range bar (150) with min/max
  handles and a current-position marker, range text in the param's units, unmap
  (`Glyph.Close`). Empty state: one Ink-5 line.

## Right panel

- Chain: colour dot, name (double-click rename), "n / N"; Volume (dB; MIDI: velocity %),
  Pan (L/C/R; MIDI: Transpose, st), Keys / Vel or Select rows (click → Zones/Select tab),
  Duplicate · Delete (Delete disabled with one chain).
- Rack out — Instrument: Volume, Glide, switch "Mono (last note)". Audio: "Chains run"
  Parallel · Serial · Select + one-line explanation, Dry/wet, Gain, switch "Latency
  compensation". MIDI (Output): "Chains run" Parallel · Select + explanation, switches
  "Merge duplicate notes", "Pass through outside zones".

## Interaction contract

- All value controls: `Knob` / `SliderTrack` contract (no jump on click, double-click
  default, fine modifier, `GestureBegin/End` automation write, `MidiLearn.Bind`,
  live-follow skips while dragging).
- Esc ends mapping (registered shortcut). Switching away from Chains ends mapping.
- Drag-and-drop from the Browser: allowed kinds per rack (research D11); refused drops
  show a tooltip. Device reorder by `Grip` drag inside a chain.
- Theme switch recolours the card without a rebuild-induced state loss (tabs, chosen chain
  and macro persist).

## Labels (sentence case)

"Macros", "Map", "Chains", "Zones", "Select", "Chain", "Rack out", "Output", "Volume",
"Pan", "Transpose", "Keys", "Vel", "Duplicate", "Delete", "+ Chain", "Chains run",
"Parallel", "Serial", "Dry/wet", "Gain", "Glide", "Mono (last note)", "Latency
compensation", "Merge duplicate notes", "Pass through outside zones", "Curve", "Lin",
"Exp", "Log", "S", "Mixed", "Target", "Range", "Selector", "Open".
