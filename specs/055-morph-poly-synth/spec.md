# Feature Specification: Nota Twin — a morphing two-filter analog poly

**Feature Branch**: `feat/morph-poly-synth`

**Created**: 2026-10-30

**Status**: Draft

**Input**: User description: "Built-in Nota synth modelled on the Arturia PolyBrute 12: a
polyphonic analog-modelling synth (2 VCOs with waveform morphing, Steiner-Parker and ladder
filters, morphing between two sides of a patch, a modulation matrix, MPE / polyphonic
aftertouch, effects)."

*"Nota Twin" is a working name. "PolyBrute", "Morphée" and "FullTouch" are Arturia
trademarks and appear in no user-facing text, preset, or file name; the instrument is
inspired by the PolyBrute's architecture, not a clone of its sound or its factory library.*

## Clarifications

### Session 2026-10-30

- Q: Which side does automation / MIDI-learn of a parameter write to? → A: the side being
  edited, as on the hardware; lanes are labelled with their side (FR-024).
- Q: Own sequencer / arpeggiator, or rely on Nota Arp and clips? → A: Twin gets its own
  polyphonic step sequencer and arpeggiator with motion recording (FR-060 – FR-067, US8).
- Q: Split / Layer built in, or via an Instrument Rack? → A: built in, two parts sharing the
  12 voices, 6 + 6 by default (FR-070 – FR-075, US9).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md). Built the way the `nota-instrument` skill describes;
Nota Volt, Aurora, Pentad and Consort are the closest existing instruments.

**Where it sits among the existing synths**: Volt is the everyday subtractive default, Pentad
the Prophet-style 5-voice poly, Consort the paraphonic ladder synth, Flux the XY "timbre
worlds" morph. Twin's own ground is the **whole-patch A↔B morph** over a **two-filter
(multimode + ladder) analog voice** with **per-note expression everywhere** — a "big poly"
for evolving pads, expressive leads and keys that change character under the hand.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Play a rich analog poly (Priority: P1)

A producer adds Nota Twin to an instrument track and plays chords. Each voice has two
oscillators whose waveform sweeps continuously (saw ↔ square ↔ triangle, with a wavefolding
"metal" colour on the triangle), noise, and two filters — a multimode (LP/BP/HP/Notch, swept
continuously) and a 24/12 dB ladder low-pass — that can run in series, in parallel, or anywhere
between. Three envelopes (filter, amp, mod) and three LFOs shape the sound. Up to 12 voices
play at once, with Poly, Mono and Unison modes, glide and a per-voice analog drift.

**Why this priority**: Without a good-sounding voice nothing else matters; this alone is a
usable new instrument.

**Independent Test**: Add Twin, play a held 8-note chord from Init, sweep each oscillator's
Wave, the filter mode, the series/parallel balance and both cutoffs; every move is heard
smoothly, without clicks or zipper noise, and no voice drops.

**Acceptance Scenarios**:

1. **Given** a new Twin at Init, **When** the user plays a note, **Then** a plain, in-tune
   saw-through-ladder sound plays, and its level matches Volt's Init within ±1.5 dB.
2. **Given** a held chord, **When** the user sweeps any oscillator Wave, the multimode filter's
   mode, or the filter routing balance, **Then** the timbre changes continuously with no step,
   click or level jump greater than 1 dB.
3. **Given** 13 notes held in Poly with 12 voices, **When** the 13th note arrives, **Then** the
   quietest / oldest voice is taken over without a click and the other 11 keep sounding.
4. **Given** oscillator sync, cross-FM (Osc 2 → Osc 1), filter FM from an oscillator and the
   "brute" feedback/drive control, **When** each is turned up, **Then** it adds the expected
   harshness (sync sweep, metallic sidebands, growl) and stays bounded — no runaway level.
5. **Given** Unison, **When** one key is played, **Then** all available voices stack on it,
   detuned and spread across the stereo field by the Spread control.

---

### User Story 2 - Morph between two sides of a patch (Priority: P1)

Every patch has an **A side** and a **B side**. The user dials a sound on A, switches the
editor to B, turns knobs to make the second sound, and then the **Morph** control sweeps the
whole patch between them — every continuous parameter interpolates at once (oscillators,
filters, envelopes, LFOs, effect amounts, modulation depths). Morph is an ordinary parameter:
it can be automated, mapped to a macro, MIDI-learned, CV-modulated in the Modular View, and is
itself a modulation-matrix destination (so an LFO, the mod wheel or a note's pressure can
morph).

**Why this priority**: It is the instrument's defining idea; without it this is "another
two-oscillator poly".

**Independent Test**: Make A a dark pad and B a bright pluck, automate Morph 0 → 1 over 4
bars, render: the sound travels continuously from one to the other, and Morph = 0 and = 1
render identically to A and B played alone.

**Acceptance Scenarios**:

1. **Given** the editor showing A, **When** the user turns a knob, **Then** only A's value
   changes; switching to B shows B's own value, and each knob also shows where the morphed
   (current) value sits between A and B.
2. **Given** Morph at 0.5, **When** a note plays, **Then** every continuous parameter sounds
   halfway between its A and B values (on the parameter's musical scale — cutoff in pitch, times
   logarithmically), and stepped parameters (wave-type switches, voice mode, LFO sync on/off)
   switch at the midpoint without a click.
3. **Given** a side, **When** the user picks "Copy A → B" (or B → A), **Then** both sides
   become equal and the morph is silent until something is changed — one undo step.
4. **Given** a modulation route whose depth differs between A and B, **When** Morph moves,
   **Then** the depth moves too.
5. **Given** Morph driven from the matrix by pressure, **When** each note is pressed harder,
   **Then** that note alone morphs toward B (per-voice morph), the other held notes don't.

---

### User Story 3 - Play it expressively (Priority: P1)

The instrument responds to everything a player's hands produce: velocity, channel
aftertouch, polyphonic aftertouch, MPE (per-note bend, pressure, slide), the mod wheel, pitch
bend, and the sustain pedal — and a three-axis **expression pad** on the card (X, Y and a
pressure axis Z) gives mouse/touch players the same expression. Each of these is a source in
the modulation matrix, and sensible defaults are routed out of the box (pressure → morph or
cutoff, slide → multimode cutoff, mod wheel → LFO 1 vibrato).

**Why this priority**: The PolyBrute is played, not programmed; per-note expression is what
makes the morph musical. Nota already carries MPE and poly aftertouch end-to-end, so the cost
is in routing, not plumbing.

**Independent Test**: With an MPE controller (or the Arturia KeyLab's poly aftertouch / the
phone's Expressive keys), hold a chord and press one key harder — only that note opens; slide
one finger — only that note's filter moves; recording keeps the per-note curves and the
playback sounds the same.

**Acceptance Scenarios**:

1. **Given** an MPE controller, **When** the user bends one note of a chord, **Then** only that
   voice's pitch moves, over the controller's announced bend range.
2. **Given** an ordinary keyboard with polyphonic aftertouch, **When** one key is pressed
   harder, **Then** only that voice receives the pressure; with channel aftertouch, all voices do.
3. **Given** the card's expression pad, **When** the user drags across it and holds the mouse
   (or presses harder on a touch surface), **Then** X, Y and Z move their matrix routes live
   and can be recorded as automation.
4. **Given** a recorded clip with per-note expression, **When** it plays back, **Then** Twin
   sounds the same as during the take.

---

### User Story 4 - Route modulation in a matrix (Priority: P2)

A sound designer opens the Matrix tab: a list of routes, each a source (3 LFOs, mod envelope,
filter and amp envelopes, velocity, key, pressure, slide, per-note bend, mod wheel, expression
pad X/Y/Z, random-per-note, macros) and a destination (any oscillator, mixer, filter, envelope,
LFO, morph and effect amount, picked from grouped menus). "+ Route" creates one; a knob sets its
bipolar depth on the side being edited. (A grid of every source against every destination —
29 × 77 — does not read on a 260 px card, so the routes are rows.) At
least 64 simultaneous routes are possible. A small number of hard-wired routes (LFO → pitch,
env → cutoff, velocity → amp) remain as panel knobs so common patches need no matrix work.

**Why this priority**: Deep modulation is the second pillar of the instrument, but US1–US3
already play well with the fixed routes.

**Independent Test**: Route LFO 3 to Osc 1 Wave and to the multimode cutoff with opposite
depths, verify both move in opposite directions; remove one route, verify only it stops.

**Acceptance Scenarios**:

1. **Given** the matrix, **When** the user creates a route, sets its depth and deletes it, **Then**
   each step is one undo step and the sound follows immediately.
2. **Given** 64 routes in use, **When** the user tries to add one more, **Then** the editor says
   the matrix is full instead of silently dropping it.
3. **Given** a route depth, **Then** it is itself automatable and differs per A/B side (US2).

---

### User Story 5 - Finish the sound with its own effects (Priority: P2)

A built-in, switchable effects section follows the voices: a **modulation** effect (chorus,
flanger, phaser, ensemble), a **delay** (tempo-syncable, ping-pong) and a **reverb**, each with
its own on/off, mix and two or three character controls, and each part of the A/B morph. The
section can also be bypassed entirely so Nota's own effect devices can be used instead.

**Why this priority**: Presets need effects to sound finished, but Nota's effect devices
already cover the need, so this is polish over the instrument, not its core.

**Independent Test**: Turn each effect on and off on a held chord; bypass the whole section;
morph between an A side with a dry sound and a B side with a long reverb.

**Acceptance Scenarios**:

1. **Given** the effects section off, **Then** the output equals the voice sum alone.
2. **Given** a tempo-synced delay, **When** the project tempo changes, **Then** the delay time
   follows.
3. **Given** a long reverb or delay tail, **When** notes stop, **Then** the tail rings out and the
   instrument only goes to sleep once the tail is silent.

---

### User Story 6 - Start from presets (Priority: P2)

Twin ships with a factory library of at least 40 presets across folders (Pads, Keys, Leads,
Bass, Brass, Strings, Plucks, Sequence, FX, Morph showcases), each using A/B morph meaningfully
and assigning the macros. User presets save and load both sides, the matrix and the effects, and
the preset name / "modified" mark work as on every other device (specs/048).

**Why this priority**: Most users will meet the instrument through its presets.

**Independent Test**: Load each factory preset, play a C-major chord across the morph range;
every preset makes sound, none clips beyond 0 dBFS at velocity 100, none is silent on either side.

---

### User Story 7 - See and edit it on a device card (Priority: P2)

The device card follows the Studio design language and the signal-chain layout of Volt and
Aurora: a large size with tabs (Osc · Filter · Env · LFO · Matrix · FX · Seq), a Lower / Upper part switch when the patch is a Split or Layer, and a small size with the
Morph control, the A/B switch, the expression pad and the key sound knobs. Graphs are
interactive: the waveform of each oscillator, the two filters' combined response, envelope
shapes and LFO shapes can be dragged. The A/B side being edited is always obvious.

**Independent Test**: Edit a full patch using only the card; resize S ↔ L; verify the A/B side
indicator, live morph position and per-knob A/B markers in both sizes, in all four colour themes.

---

### User Story 8 - Sequence it from the instrument itself (Priority: P3)

A player enters a 16-step chord pattern by holding chords on the keyboard in step-record, hits
play on the transport and the pattern runs in time; holding a key transposes it live. They
arm motion recording, sweep Morph and the multimode cutoff over one pass — the moves now
repeat with the pattern. Switching between the patch's 8 patterns happens at the pattern end.
When they like it, "To clip" writes the pattern into a MIDI clip on the track. The
arpeggiator is the quick alternative: hold a chord, pick Up/Down, 2 octaves, 1/16.

**Why this priority**: It is the PolyBrute's performance workflow and makes presets come
alive, but Nota's clips, automation and Nota Arp already let users sequence Twin, so it
follows the sound engine.

**Independent Test**: Step-record a 4-chord pattern, play it with the transport at two tempos,
transpose by a held key, record one motion lane, convert to a clip, switch the sequencer off:
the clip plays the same notes.

**Acceptance Scenarios**:

1. **Given** step-record armed, **When** the user holds a three-note chord and releases it,
   **Then** that step holds the three notes and the cursor moves to the next step.
2. **Given** a running pattern, **When** the transport tempo changes or the transport stops,
   **Then** the sequencer follows / stops and no note hangs.
3. **Given** a motion lane recorded on Morph, **When** the pattern repeats, **Then** Morph
   follows the recorded moves (stepped or smooth per the lane's setting), and the user's own
   move of the knob takes over until they let go.
4. **Given** a pattern, **When** the user picks "To clip", **Then** a MIDI clip with the
   pattern's notes and velocities appears (motion lanes as automation) — one undo step.
5. **Given** the arpeggiator in Hold, **When** the user plays a new chord, **Then** the
   arpeggio switches to it on the next step.

---

### User Story 9 - Split or layer two sounds (Priority: P3)

A keyboardist puts a bass on the lower half and a pad on the upper half (Split at C3), or
stacks a string patch with a bell patch on every key (Layer), each part with its own morph,
and balances the 12 voices between them (e.g. 4 for the bass in Mono, 8 for the pad). They
load two existing presets into the two parts rather than building both from scratch.

**Why this priority**: Valuable for live playing and for "combination" presets, but an
Instrument Rack with two Twins already gives a workable split, so it comes last.

**Independent Test**: Make a Split at C3 with two different presets loaded into Lower and
Upper; play across the split; change the share to 4 + 8 and verify each half's polyphony.

**Acceptance Scenarios**:

1. **Given** Split at C3, **When** the user plays B2 and C3, **Then** B2 sounds the Lower part
   and C3 the Upper.
2. **Given** a 4 + 8 share, **When** 6 keys are held in the Lower half, **Then** only 4 sound
   (oldest stolen within Lower) and the Upper part keeps its 8.
3. **Given** Layer, **When** one key is played, **Then** both parts sound it, each through its
   own morph, matrix and effects.
4. **Given** a Single patch, **When** the user loads a preset into Upper, **Then** the patch
   becomes Layer with the current sound as Lower — one undo step.
5. **Given** the mode changes while notes are held, **Then** held notes release normally; no
   note hangs.

---

### Edge Cases

- **Morph between stepped values** (e.g. A uses ladder 24 dB, B 12 dB; A Poly, B Unison): they
  switch at the midpoint; the voice-mode switch never cuts held notes off.
- **Voice count changes while notes are held** (12 → 6): the extra voices release, not cut.
- **Self-oscillating filters with high resonance on both sides of the morph**: level stays
  bounded (soft-limited) through the whole morph range.
- **Extreme cross-FM / sync / feedback at high notes**: aliasing is kept inaudible at 44.1 kHz
  and above; the output never contains NaN or inf (it recovers if an input does).
- **MPE and a regular keyboard on the same track at once**: channel-wide controls move all
  voices, per-note ones only their note, as on Nota's other MPE instruments.
- **Sample-rate change or offline render at another rate**: the patch sounds the same, drift
  included (seeded, so a freeze and a render are identical).
- **Loading a project saved by a newer Twin with more parameters**: unknown values are ignored,
  missing ones take their defaults, the patch still loads.
- **Cloning the track, copy/paste of the device, freeze / bounce, Instrument Rack chains**: both
  sides, the matrix and the effects come along.
- **Idle**: with no notes and no tail, the instrument costs (near) nothing — a stopped
  sequencer included.
- **Automation recorded on side B while Morph = 0**: it is stored and plays, but is not heard
  until Morph moves toward B; the lane name ("· B") makes this explainable.
- **Sequencer running while the user also plays keys**: played notes sound alongside the
  pattern (unless a key is used for transpose with latch off); both share the part's voices.
- **Loop / jump in the transport**: the sequencer re-aligns to the new position at the next
  step without a stuck note.
- **Split with Mono on one part**: legato and glide work within that part only.
- **A preset saved as Single loaded into Upper of a Split**: only its part is taken; its own
  sequencer pattern is kept with it.
- **Voice share changed while both parts are held**: extra voices release, as in voice-count
  changes.

## Requirements *(mandatory)*

### Functional Requirements

**Voice**

- **FR-001**: The instrument MUST provide up to 12 voices per instance (default 12, selectable
  lower to save CPU; shared between parts in Split / Layer, FR-071), with Poly, Mono, Legato and Unison modes, glide (with legato-only option),
  and voice stealing that never clicks.
- **FR-002**: Each voice MUST have two oscillators. Each oscillator's **Wave** control MUST
  morph continuously between triangle, sawtooth and square/pulse, with pulse width and a
  wavefolding **Metal** amount; each MUST have coarse (±2 octaves in semitones) and fine tune.
- **FR-003**: Oscillator 2 MUST be able to hard-sync to oscillator 1, and frequency-modulate
  oscillator 1; an oscillator MUST be able to frequency-modulate the multimode filter's cutoff.
  Oscillator 2 MUST have a low-frequency / keyboard-off option so it can act as a modulator.
- **FR-004**: Each voice MUST have a noise source with a colour control (white ↔ pink/red).
- **FR-005**: A mixer MUST set each source's level and, per source, its balance between the two
  filters' inputs.
- **FR-006**: The **multimode filter** MUST sweep continuously through low-pass, band-pass,
  high-pass and notch (in the Steiner-Parker character: aggressive, dirty resonance), with
  cutoff, resonance up to self-oscillation, key tracking and envelope amount.
- **FR-007**: The **ladder filter** MUST be a low-pass with 12 and 24 dB/oct slopes, cutoff,
  resonance up to self-oscillation (with the ladder's characteristic bass loss), key tracking
  and envelope amount.
- **FR-008**: A routing control MUST blend continuously between series (multimode → ladder) and
  parallel filter routing.
- **FR-009**: A **Brute**/feedback control MUST feed the output back into the filters for
  growl and overdrive, and an output drive MUST saturate softly; both bounded at maximum.
- **FR-010**: There MUST be three envelopes — filter (ADSR), amp (ADSR) and a mod envelope
  with delay and a loop option — each with velocity sensitivity and curves.
- **FR-011**: There MUST be three LFOs with selectable shapes (sine, triangle, saw, square,
  sample & hold, smooth random), rate free or tempo-synced, per-voice or global, retrigger and
  fade-in; LFO 3 MUST additionally offer a drawable curve/symmetry shape.
- **FR-012**: Each voice MUST carry a small, seeded analog drift (pitch, cutoff, envelope
  times) with an amount control, so renders and freezes of the same project are identical.
- **FR-013**: Stereo: voices MUST be spreadable across the stereo field (by voice, by key or
  in unison), with an overall pan and volume.

**Morph**

- **FR-020**: Every sound parameter MUST hold an A value and a B value. A **Morph** control (0 =
  A, 1 = B) MUST set the sounding value of every continuous parameter by interpolating on its
  musical scale; stepped parameters MUST switch at the midpoint.
- **FR-021**: The editor MUST let the user choose which side (A or B) knobs edit, and MUST show
  for each knob both side values and the current morphed position.
- **FR-022**: The user MUST be able to copy A → B, B → A, and swap A ↔ B, each as one undo step.
- **FR-023**: Morph MUST be automatable, MIDI-learnable, macro-assignable, a Modular View CV
  target, and a modulation-matrix destination evaluated per voice.
- **FR-024**: Automation, MIDI-learn, macro and CV moves of an ordinary sound parameter MUST
  write to the **side being edited** when the move is made (as on the hardware), and the
  automation lane MUST say which side it drives ("Cutoff · A" / "Cutoff · B"); a lane on the
  side the morph is away from is heard only in proportion to the morph. Morph itself and the
  expression pad are side-less.

**Expression and modulation**

- **FR-030**: The instrument MUST respond to velocity, release velocity, channel aftertouch,
  polyphonic aftertouch, MPE per-note bend / pressure / slide, pitch bend (selectable range),
  mod wheel and the sustain pedal, honouring Nota's existing MPE conventions.
- **FR-031**: The card MUST offer a three-axis expression pad (X, Y, Z = pressure / hold) whose
  values are automatable parameters and matrix sources.
- **FR-032**: A modulation matrix MUST allow at least 64 simultaneous routes from the sources
  listed in US4 to any continuous sound parameter, with a bipolar depth per route that is itself
  per-side (morphs) and automatable. Per-note sources MUST act per voice.
- **FR-033**: Default routes MUST exist on Init (mod wheel → vibrato, pressure → cutoff, slide →
  multimode cutoff, velocity → amp) and be removable.
- **FR-034**: Eight macros MUST be available, each driving several destinations, as on Volt and
  Aurora.

**Effects**

- **FR-040**: A built-in effects section MUST provide a modulation effect (chorus / flanger /
  phaser / ensemble), a delay (free or tempo-synced, ping-pong) and a reverb, each switchable,
  with mix and character controls that are part of the A/B morph; the whole section MUST be
  bypassable.

**Integration**

- **FR-050**: The instrument MUST appear in the Browser and the command palette like every
  built-in instrument (with role, aliases and a description), be usable on instrument tracks,
  in Instrument Rack chains and through the "Replace…" command.
- **FR-051**: All parameters (both sides, matrix depths, macros, effects) MUST be automatable,
  saved with the project, kept by clone / copy / paste / freeze, and restored by undo.
- **FR-052**: Projects saved with an older Twin MUST load in a newer Twin (parameters are only
  ever appended).
- **FR-053**: A factory library of at least 40 presets (US6) MUST ship; user presets MUST work
  as on other built-in instruments, including the preset name and "modified" mark.
- **FR-054**: The instrument MUST be reachable from the MCP surface at least as far as other
  built-in instruments are (read / set parameters, load a preset).

**Sequencer and arpeggiator**

- **FR-060**: Twin MUST have its own **polyphonic step sequencer**: patterns of 1–64 steps,
  up to 6 notes per step, per-step velocity, gate length and tie, rest steps, rate (1/4 … 1/32
  with triplets), swing, and a pattern length independent of the bar.
- **FR-061**: The sequencer MUST follow the Nota transport (start, stop, position, tempo); when
  the transport stops it stops and its notes release. A held key MUST be able to **transpose**
  the running pattern (root = the pattern's first note), with a latch option.
- **FR-062**: Patterns MUST be enterable by step recording from the keyboard (notes held
  together become one chord step) and by editing steps on the card; one undo step per edit.
- **FR-063**: **Motion recording**: each pattern MUST hold up to 4 motion lanes; recording
  captures a knob's (or Morph's, or the expression pad's) movement per step, played back
  stepped or smoothed. Motion lanes act on top of the patch, per side as in FR-024.
- **FR-064**: An **arpeggiator** MUST offer Up, Down, Up/Down, Random, As Played and Chord
  modes, 1–4 octaves, rate (synced), gate, swing and Hold; the sequencer and the arpeggiator
  are alternatives, not both at once.
- **FR-065**: Each patch MUST store its pattern(s) and arp settings; at least 8 patterns per
  patch MUST be selectable, switching at the next pattern end.
- **FR-066**: The user MUST be able to turn the current pattern into an ordinary MIDI clip on
  the track (notes with velocity; motion lanes as automation), after which Twin's own
  sequencer can be switched off.
- **FR-067**: Sequencer and arp output MUST behave like keyboard notes everywhere downstream
  (voice allocation, Split / Layer routing, per-note expression sources, macros); a MIDI
  effect in front of Twin (e.g. Nota Arp) sees only the incoming notes, not the sequencer's.

**Split / Layer**

- **FR-070**: A patch MUST be able to hold **two parts** (Lower / Upper), each a complete
  sound with its own A/B sides, Morph, matrix, macros and effects section, in one of three
  modes: **Single** (one part, the default), **Layer** (both parts on every key) and **Split**
  (Lower below a split key, Upper from it up).
- **FR-071**: The 12 voices MUST be divided between the parts — default 6 + 6, adjustable in
  steps of 2 (2 + 10 … 10 + 2); each part steals only within its own share. Voice mode
  (Poly / Mono / Unison) and glide are per part.
- **FR-072**: Split MUST have a split key (set by a key press or a value) and a per-part
  octave transpose; Layer MUST have a per-part volume and pan so the balance can be set.
- **FR-073**: The editor MUST make the edited part obvious (Lower / Upper switch alongside
  the A/B switch); automation lanes and parameter names MUST include the part
  ("Upper · Cutoff · A").
- **FR-074**: Performance controls (pitch bend, mod wheel, sustain, aftertouch, MPE, the
  expression pad) MUST reach both parts; the sequencer / arp MUST target Lower, Upper or both.
- **FR-075**: Copy part Lower → Upper (and back) and "load preset into Lower / Upper" MUST
  exist, so two existing presets can be combined into a split or layer.

### Key Entities

- **Patch**: the whole instrument state — mode (Single / Layer / Split), split key, voice
  share, one or two parts, the sequencer patterns and arp settings. Saved as a preset or with
  the track.
- **Part (Lower / Upper)**: a complete sound — side A, side B, the morph position, matrix,
  macros, effects, voice settings.
- **Side (A / B)**: one value for every sound parameter.
- **Modulation route**: source, destination, depth on A, depth on B.
- **Expression state per voice**: velocity, pressure, slide, bend, random seed — the per-note
  inputs that the matrix reads.
- **Factory preset**: a named patch in a folder, with its macro assignments.
- **Pattern**: steps (each: up to 6 notes, velocity, gate, tie / rest), length, rate, swing,
  and up to 4 motion lanes (target parameter and side, one value per step, stepped / smooth).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A 12-note chord with both filters, all three LFOs, 16 matrix routes and all
  effects on plays without a single dropout at a 128-sample buffer on the reference Mac, and
  one instance at full polyphony costs no more than 2× Nota Pentad at its maximum polyphony in
  the engine perf gate.
- **SC-002**: Morph = 0 and Morph = 1 renders are identical (bit-exact with drift seeded) to
  renders of a patch holding only side A or only side B.
- **SC-003**: A full Morph sweep (0 → 1 in one beat) on any factory preset produces no click
  or level jump above 1 dB that is not present in A or B themselves.
- **SC-004**: Every factory preset produces sound on both sides, peaks under 0 dBFS at
  velocity 100 with an 8-note chord, and loads in under 50 ms.
- **SC-005**: Per-note expression: pressing one note of a held 6-note chord changes only that
  note's spectrum (others within 0.5 dB of before), verified by an automated render test.
- **SC-006**: With no notes and no effect tail, an instance's CPU cost is indistinguishable
  from an empty track.
- **SC-007**: A user who knows Volt can build a two-sided morphing pad from Init using only the
  card in under 5 minutes (checked in the docs walkthrough).
- **SC-008**: Rendering the same project twice gives identical output; a project round-trips
  through save / load with an identical render.
- **SC-009**: A sequencer pattern stays sample-accurate to the transport: over 64 bars at any
  tempo from 60 to 200 BPM its step onsets drift by no more than one sample from the grid,
  and a pattern converted to a clip renders identically to the running pattern.
- **SC-010**: In Split / Layer at full polyphony (6 + 6) the CPU cost stays within SC-001's
  budget for one instance.
- **SC-011**: At least 8 of the factory presets use the sequencer or arpeggiator and at least
  6 use Split or Layer.

## Assumptions

- **Inspired by, not cloned**: the target is the PolyBrute's *architecture and playing feel*,
  not a measured match of its circuits or factory sounds; no Arturia names, presets or
  artwork are used. Sound quality is judged by ear and by the success criteria above.
- **No new dependency**: the DSP is built from Nota's own primitives (oscillators, SVF, ladder,
  oversampler, delay, reverb); the Steiner-Parker character is a new in-house filter model.
- **Hardware-only features are out of scope**: the Morphée / ribbon hardware is replaced by
  the on-card expression pad plus the existing MPE / aftertouch / Nota Remote inputs; the
  PolyBrute's front-panel "Panel" mode and its MIDI SysEx patch format are not supported.
- **12 voices** match the original; the voice cap is a CPU choice, not a design one, and can
  rise later.
- **Standalone VST3** (as Pentad has) is a follow-up, not part of this feature.
- The **matrix size** of 64 routes is a target from the original; the plan may confirm a
  different number if per-voice cost requires it, with the spec updated.
- **Delivered in slices**: the scope is large, so the plan delivers it in priority order —
  P1 (voice, morph, expression) is a playable release on its own; P2 (matrix, effects,
  presets, full card) next; P3 (sequencer / arp, Split / Layer) last. Each slice keeps the
  persisted state append-only so patches from an earlier slice load in a later one.
- Nota Remote, the shortcuts list and the user docs are updated as for any new instrument.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — the matrix and A/B sides must be pre-allocated; route edits go through the command queue
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` (new instrument kind, morph endpoints, per-note expression render)
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
