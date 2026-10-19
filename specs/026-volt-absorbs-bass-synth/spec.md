# Feature Specification: Fold Nota Bass and Nota Synth into Nota Volt

**Feature Branch**: `feat/volt-absorbs-bass-synth`

**Created**: 2026-10-19

**Status**: Implemented

**Input**: nota-daw/nota#195. Nota Bass and Nota Synth are one-oscillator, one-filter
versions of Nota Volt: the same oscillators, state-variable filter, envelopes, unison and
glide. Give Volt the few things only they have, move their factory presets into Volt, and
take them out of the browser. Old projects that use them must still open and sound the same.

## Nota Context

**Feature area**: engine (`VoltSynth.h`), Volt device card, factory presets, browser /
palette / rack menus / MCP, the default instrument of a new track.

**Audio thread**: Volt's render gains a few per-block branches; the new paths (drive,
pulse width, shape morph, the unison stack) are allocation-free and only cost when used.
A patch that leaves the new parameters at their defaults renders sample-for-sample as before.

**Persistence**: Volt's parameters are appended (133 → 143), so older Volt state loads with
the new ones at neutral defaults. Projects store Nota Synth (kind 0) and Nota Bass (kind 7)
tracks by kind; both engines stay, so those tracks load and play unchanged. No format bump.

## Clarifications

### Session 2026-10-19

- Q: Hide or convert? → A: Hide. Bass and Synth leave the browser, palette, rack / pad
  menus and MCP's instrument list; their engines, cards and presets stay for old tracks.
  Converting saved projects on load is a later step if the code is worth removing.
- Q: How does a Synth/Bass preset become a Volt preset? → A: Through one converter
  (`VoltPort`) that turns each instrument's normalized parameters into Volt's, matching the
  ranges (cutoff, envelope times, glide, octave, reso, loudness) rather than copying numbers.
- Q: Bass's continuous oscillator shape? → A: A per-oscillator **Shape** knob on Volt that
  morphs from the chosen wave toward the next one in sine → triangle → saw → square order.
  0 = the wave chip exactly as before.
- Q: Bass's sub oscillator? → A: Osc 2 an octave (or two) down, sine / square / triangle.
- Q: Synth's 1 / 2 / 4 / 7 unison? → A: A **Voices** choice on Volt: 1 keeps Volt's single
  detuned partner (the Unison knob, as before); 2 / 4 / 7 stack copies of each oscillator
  across ±(Unison × 50) cents, panned by a new **Spread** knob (stereo through the filters).
- Q: Synth's filter Off? → A: Ported as Volt's low-pass fully open, no resonance, no
  envelope. No new filter type.
- Q: Saw LFO? → A: Not added: no Bass preset uses it.
- Q: Mono behaviour? → A: Volt's Mono keeps a held-note stack (releasing the top key falls
  back to the one under it) and gains a **Legato** choice: Poly / Mono / Legato. Legato only
  slides while another key is held and the voice is still sounding. Existing mono Volt
  patches load as Legato, which is what they did; a note struck on a releasing voice now
  retriggers instead of fading out.
- Q: Default instrument? → A: Nota Volt for every new instrument track (toolbar, new project,
  MIDI import, audio-to-MIDI, the fallback when a plug-in or sample can't be reloaded) and the
  first chain of a new Instrument Rack. Loading a saved Nota Synth track still makes a Nota Synth.
- Q: Card width? → A: 790 (a documented exception to the 700 frame, like Bass and Physical):
  the Osc rows need two more knob columns.

## Requirements

- **FR-001**: Volt MUST add Shape (per oscillator), Pulse Width (per oscillator), Filter
  Drive (before both filters), Drive (output), Voices (1/2/4/7), Spread, Legato and
  Wheel → LFO (the mod wheel opens each filter's LFO onto its cutoff, up to ±2 octaves).
- **FR-002**: A Volt patch saved before this change MUST render the same in Poly (checked
  sample for sample against the previous engine). Mono changes on purpose: a note struck on a
  releasing voice retriggers, releasing the top key falls back to a key still held, and a
  legato note's key tracking follows the new note.
- **FR-003**: Every Nota Synth and Nota Bass factory preset MUST exist as a Volt preset.
- **FR-004**: Nota Synth and Nota Bass MUST NOT appear in the browser, the command palette,
  rack chain / drum pad menus or `list_instrument_kinds`; projects and presets that use them
  MUST still load, play and show their own card.
- **FR-005**: New instrument tracks MUST be Nota Volt.
- **FR-006**: The Volt card MUST show the new controls without overflowing.

## Out of scope

Converting saved Synth/Bass tracks to Volt; removing `Synth.h` / `BassSynth.h`; a saw LFO.
