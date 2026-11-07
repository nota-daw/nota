# Feature Specification: Live graphs for Nota Reverb and Nota Delay

**Feature Branch**: `feat/live-space-graphs` (based on `ui/style-guide`, nota-daw/nota#296)

**Created**: 2026-10-28

**Status**: Draft

**Input**: Egor's review of nota-daw/nota#296: "Reverb, Delay, Chamber: the visualization is
not informative. It might be worth considering a redesign." Chris picked the approach on
2026-10-28: live graphs, with the same card layout as the rearranged Compressor and Auto
Filter (nota-daw/nota#376): the graph fills the centre panel, and the right panel holds the
controls of whichever centre tab is open.

## Nota Context

**Feature area**: Engine (scope telemetry only) and UI (`src/managed/Nota.App`).

What the cards show today (ui/style-guide at 4dc27801):

- **Reverb.** Both centre tabs (Space, Tone · Mod) show the same `ReverbTailView`: the RT60
  formula drawn as a curve, with early-reflection ticks, replayed as a looping animation
  while the wet signal is above −74 dB. Nothing in it comes from the audio. The knobs sit
  under the graph, so the graph is about 100 px tall.
- **Delay.** `DelayTapsView` draws the taps as static dots at their delay times, with their
  heights set by the feedback. The second tab (Loop · Wow) shows the same picture.
- **Engine.** `Reverb::scopeRead` and `Delay::scopeRead` publish 11 meter values each and no
  history.

## User Scenarios

### US1: See the tail of my audio (Reverb, P1)

A producer plays a snare through Nota Reverb and wants to see how long the tail rings and
whether it matches the Decay setting. The Space tab shows the last six seconds of the input
and of the reverb's tail, live. A guide line falls from the latest peak of the tail at the
set RT60, so a tail that dies faster or slower than the setting is visible at once.

**Acceptance**: playing a drum loop, the tail fills between the hits; raising Decay moves the
guide and the tail together; with Freeze on, the tail holds flat and the graph says so.

### US2: See what the tone controls do (Reverb, P1)

The Tone tab shows the decay time across the frequency range (HF Damp shortens the highs),
the low-cut and high-cut filters on the tail, and the live spectrum of the tail under it.

**Acceptance**: raising HF Damp bends the decay curve down at the top; moving Low Cut and
High Cut moves the shaded cut regions, and the live spectrum drops there.

### US3: See my echoes (Delay, P1)

The Time tab shows the last few seconds of the input and the echoes, live, on the project's
beat grid when Sync is on. Markers show where the next repeats of the latest hit will land,
and how loud the feedback makes them.

**Acceptance**: a single hit shows repeats landing on the markers and fading; changing
Feedback changes how fast they fade; in Sync the markers sit on the beat grid.

### US4: See how the repeats change (Delay, P2)

The Loop · Wow tab shows the frequency response of the first several repeats: each repeat
passes the loop's low cut and high cut (and tape loss) once more, so each curve is darker and
narrower than the last. The wow depth and rate are shown as the pitch wobble they cause.

## Requirements

- **FR-001** `Reverb::scopeRead` and `Delay::scopeRead` append a level history after their
  existing values: input peak dB and wet peak dB (before the dry/wet mix) at 4 ms per point
  for 6.1 s (1536 points each), then the latest 2048 wet samples (mono). The existing
  values keep their indices.
- **FR-002** The history is written in `process()` with no allocation, no lock and one
  publish per block. While the device sleeps on silence, the history keeps scrolling with
  silence.
- **FR-003** Reverb and Delay cards keep their size (700 × 260) and every control they have
  today. The centre panel shows one graph per tab filling the panel; the right panel shows
  the controls of the open centre tab (as in #376), with the output controls in every
  panel's lower rows or a shared Output tab.
- **FR-004** The Reverb Space graph keeps today's gestures: drag to change the decay,
  double-click to reset.
- **FR-005** The graphs follow the style guide (`NotaGraph` window, grid, title, legend,
  axis labels; units with a thin space).
- **FR-006** No change to the sound, the parameters, presets or the project format.

## Out of scope

- Nota Chamber: its graphs already draw the real impulse response. It gets the same layout
  in a follow-up if Chris wants it after seeing Reverb and Delay.
- A pop-out window for the graphs (Chris picked rearranging the cards instead).

## Success criteria

- **SC-001** The graphs are at least 1.6 times as tall as today's.
- **SC-002** Every graph shows something that comes from the audio playing now.
- **SC-003** The build and smoke test pass; the Reverb and Delay output is bit-identical to
  before for the same input (the history only reads).
