# Contract: Aurora Osc tab (700 × 260 default)

The card shell, the wheels (56), the rail (186), the tab row and the other five tabs are
unchanged (`AuroraInstrumentCard.cs:51-53, 849-876`). The Osc tab body is about 438 × 166.

```text
┌──────────────── Osc 1 (216) ────────────────┐ 6 ┌──────────────── Osc 2 (216) ────────────────┐
│ Osc 1 [on]  [Formant      ⌄] [Sync ⌄]   20   │   │ Osc 2 [on]  [vox_aa.wav   ⌄] [Off  ⌄]   20   │
│ ┌ window 216 × 60 ───────────────────────┐   │   │ ┌ window 216 × 60 ───────────────────────┐   │
│ │ Formant · 5/16 · lfo 1 → pos            │   │   │ │ vox_aa.wav · 9/16 · no modulation       │   │
│ │      (stack: ≤ 16 frames + lit)         │   │   │ │                                        │   │
│ └────────────────────────────────────────┘   │   │ └────────────────────────────────────────┘   │
│ POS  WARP  LEVEL  OCT  SEMI  FINE  (34 px)  54│   │ POS  WARP  LEVEL  OCT  SEMI  FINE         54 │
└──────────────────────────────────────────────┘   └──────────────────────────────────────────────┘
 SUB [sin sqr tri] LEVEL  OCT │ UNI VOICES DETUNE SPREAD                                       22
```

## Elements per oscillator

- **Name** "Osc 1" / "Osc 2", in section type. It is no longer a picker.
- **On switch**, 18 × 10.
- **Type drop-down** (22 high), listing:
  - `Analog`, `Pulse`, `Formant`, `Chroma`;
  - a separator, then the imported table's name when present (a check mark on the active
    source), `Import…`, and `Remove imported table` (only when present).
  When the source is imported and the built-in parameter is automated, the built-in row
  shows its value in ink 4 (FR-010).
- **Warp drop-down**: `Off`, `Sync`, `Bend`, `PWM`, `Fold`.
- **Window**:
  - a `NotaGraph` window with the title row (name · frame n/N · position modulation);
  - the stack drawn under `PlotTop`, at most 16 frames plus the lit frame;
  - the lit frame in `VizInk` 1.7 px, the rest in depth washes, as today
    (`AuroraStack.cs:27-43`);
  - when Osc is off, the `Inactive` look.
- **Knobs**: Position, Warp, Level, Oct, Semi, Fine; 34 px; the interaction contract as
  today (`AuroraInstrumentCard.cs:198-222`).

## States of the window

| State | What shows |
|---|---|
| Built-in | the bank's stack, lit at the set or played position |
| Imported | the preview stack. The caption says "1 frame" for single cycles, and notes "shortened to 60 s" / "first 256 frames" for 4 s after import |
| Drag over | a 1.5 px orange inner ring, and the caption "Drop to load into Osc n" |
| Loading | the caption "Loading…". The old stack stays, and the oscillator keeps playing |
| Error | a one-line reason for 4 s ("Couldn't read this file — use WAV, FLAC or MP3", "This file is silent"), then the previous state |
| Missing | the caption "Missing: <name>". The type drop-down reads the same; the built-in stack is shown |

## Live follow

On each 30 Hz tick, read the scope:

- if the played position ≥ 0, the lit frame follows it;
- otherwise the lit frame follows the set Position.

Repaint only on a lit-index change (a 64-step grid), a source change, dim, theme or resize.

## Drop targets

The whole oscillator column accepts a file from the browser (`BrowserView.IsAcceptableDrag`
/ `DroppedItems`) or from the file manager. MIDI files and presets are ignored. The file
picker filter is `*.wav;*.flac;*.mp3`.

## Removed

`oscPick`, `osel`, `applyOsc`, the shared strip `oscStrip` / `oscTableHosts` / `oscWarpHosts`
(`AuroraInstrumentCard.cs:423-442, 480-487`) and the single 128 px `tableWindow` (`:534`).
`Hint()` and `Summary()` stop naming "the picked oscillator".

## At 1050 (only if FR-005 is answered "Wide")

- Columns are about 391 px.
- The type and warp selectors become inline segment chips (4 + 5, plus an `Import…` chip
  and the import's name chip).
- The window is about 150 × 86, to the left of a 3 × 2 knob grid at 36 px.
- The sub / unison row is unchanged.
