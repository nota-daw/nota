# Feature Specification: Tempo-synced scope in Nota Lens

**Feature Branch**: `feat/lens-tempo-scope`

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "Native tempo-synced oscilloscope and audio visualizer
(nota-daw/nota#119): beat, bar and waveform views synchronized to the project tempo, main and
sidechain overlays, mono / stereo / left / right viewing, visual-only scaling, inspection and
freeze, bounded buffering with no added audio latency." Scope agreed with Chris (Oct 28): the
analysis core, built into Nota Lens (as ambertape suggested on the issue): a beat/bar-synced
scope window, a sidechain overlay, and a larger pop-out view. Themes, glow, logo overlays and a
presentation mode stay open on the issue.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See a bar of audio lined up with the beat (Priority: P1)

A producer has a kick and a bass that feel loose. On the bass track's Lens they open the Scope
view, turn **Sync** on and pick **1 bar**. The scope now shows exactly one bar of the bass's
waveform from the bar line to the next, with the beats marked, redrawn as the playhead sweeps
across it, so each note's attack lines up visibly with a beat line, or doesn't.

**Why this priority**: Tempo-synced display is the core of the request; the rest builds on it.

**Independent Test**: Play a project at 120 BPM with a click on every beat through Lens, Sync on
at 1 bar: four spikes sit on the four beat lines and stay put from bar to bar.

**Acceptance Scenarios**:

1. **Given** the Scope view, **Then** it has a **Sync** switch and, while Sync is on, a span
   picker: 1/4 beat, 1/2 beat, 1 beat, 2 beats, 1 bar, 2 bars, 4 bars (a bar is the project's
   beats per bar). Off, the scope works exactly as today (time/div, trigger, cursors).
2. **Given** Sync on and the transport playing, **Then** the scope's left edge is a multiple of
   the span on the song's beat grid (a span of 1 bar starts on a bar line), the right edge is one
   span later, and a sweep line at the playhead's position in the span divides this pass (left of
   it) from the previous one (right of it), which fades.
3. **Given** Sync on, **Then** the scope draws beat lines (bar lines stronger) and labels the span
   ("1 bar · 120 BPM").
4. **Given** a span longer than the raw audio Lens keeps, **Then** the trace is drawn from a
   min/max history, so a one-sample transient still shows at its full height at any span.
5. **Given** Sync on and the transport stopped on a track that still sounds (an instrument, or a
   monitored input), **Then** the scope rolls freely: the newest audio at the right, the span
   long at the current tempo, and a "free" mark in the header. A stopped audio track runs no
   devices, so its scope holds the last pass.
6. **Given** a tempo change, a seek, a loop wrap or a time-signature change, **Then** the next
   frame follows the new grid: the pass restarts cleanly at the new position instead of drawing
   audio from the old position under the new beat lines.
7. **Given** Freeze, **Then** the synced trace holds as it is (both passes), as today's trace does.

---

### User Story 2 - Compare the main signal with its sidechain (Priority: P1)

The producer routes the kick track to the bass track's Lens as its key (the same sidechain
picker the Compressor uses). The kick's waveform appears over the bass's in a second colour, on
the same synced bar, so they can see whether the bass ducks where the kick hits.

**Why this priority**: Main and sidechain comparison is the second half of the core request.

**Independent Test**: Route a click track as Lens's key, Sync at 1 bar, and see the key trace in
teal over the main trace with its spikes on the beats; change the key's display gain and see only
the key trace grow.

**Acceptance Scenarios**:

1. **Given** Lens on a track, **Then** it accepts a sidechain source like the Compressor does
   (the device header's key picker); the routed track's post-FX audio is the key.
2. **Given** a key routed, **Then** the scope draws the key trace in its own colour (teal) with
   the main trace (the graph's neutral ink, as today), labelled "main" and the key track's name.
3. **Given** a key routed, **Then** **Key** gain (−24 to +24 dB, display only, default 0) scales
   only the key trace, and **Layout** picks **Overlay** (both on one axis) or **Stacked** (main on
   top, key below, each with half the height).
4. **Given** a key routed, **Then** a **Key** switch hides or shows the key trace; with no key
   routed the key controls are disabled and say "no key".
5. **Given** a key and Sync off, **Then** the key trace follows the main trace's trigger and time
   base, so the two line up sample for sample.
6. **Given** Source and Mid/Side, **Then** they choose the channel for both traces (L+R, L, R,
   Mid, Side), as they do for the main trace today.

---

### User Story 3 - A bigger view (Priority: P2)

The 700 × 260 card is too small to read one bar of a dense mix. The user clicks Lens's expand
button and gets the same graph in its own resizable window, with the same controls, which they
can put on a second display.

**Why this priority**: The issue and ambertape's comment both ask for more room, but the synced
scope and overlay are useful in the card already.

**Independent Test**: Open the pop-out, resize it to full screen, and see the same live view
(spectrum, scope or waterfall) and controls at the new size; delete the device and the window
closes.

**Acceptance Scenarios**:

1. **Given** a Lens card, **When** the user clicks its expand button, **Then** a separate
   resizable window opens titled after the track and device, showing the current view with the
   view tabs and the scope controls, live.
2. **Given** the pop-out, **When** the device or the track is removed, or the project is closed,
   **Then** the window closes. Closing it changes nothing on the device.
3. **Given** a change in the pop-out (a view, Sync, span, key gain), **Then** the card follows,
   since both edit the same device params.

### Edge Cases

- **No tempo**: the engine always has a tempo; "free" (story 1, scenario 5) covers the stopped
  transport.
- **Span longer than history**: the min/max history covers at least 4 bars at 40 BPM at 192 kHz;
  anything older than the history is drawn empty.
- **Cost when unused**: the history is recorded only while Sync is on (and the key ring only
  while a key is routed), so a plain Lens costs what it did before; turning Sync on starts the
  history there, and the previous pass appears after one pass.
- **Key timing**: the engine hands every sidechain consumer its source's previous block. With a
  key routed, Lens captures one block behind (the held block with the key that belongs to it),
  so the two traces line up sample for sample; the display lags by one block (about 10 ms).
- **Key track removed**: the key trace disappears and the controls say "no key".
- **Mono source**: L and R are the same; the trace is drawn once.
- **Older projects**: a Lens saved before this change opens with Sync off, 1 bar, key shown at
  0 dB, Overlay, and no key routed; nothing else changes.
- **Audio**: Lens still passes the audio through untouched (bit-identical, zero latency).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Lens MUST record a min/max history of the analysis channel for the main signal and
  for the key, with each history bin stamped with the transport's beat position and whether the
  transport was playing, written only by the audio thread with no allocation, and read lock-free
  (or under the existing analysis lock) by the UI.
- **FR-002**: Lens MUST accept a sidechain source and record its audio into the key history.
- **FR-003**: Lens MUST gain these params, appended: Sync (0/1, default 0), Span (7 steps, default
  1 bar), Key (show, default 1), Key Gain (−24..+24 dB, default 0), Layout (Overlay / Stacked).
- **FR-004**: The scope MUST draw the synced window as specified in story 1 (grid-aligned span,
  sweep, previous pass fading, beat and bar lines), from the min/max history.
- **FR-005**: The scope MUST draw the key trace as specified in story 2, in both the synced and
  the triggered modes.
- **FR-006**: The Lens card MUST have an expand button that opens the pop-out of story 3.
- **FR-007**: Lens's MCP text (get_device_text / read_analyzer) MUST report Sync, the span, the
  key source and whether a key is arriving.
- **FR-008**: The audio MUST pass through bit-identical and with zero latency.
- **FR-009**: The user guide (EN and RU) MUST describe the synced scope, the key overlay and the
  pop-out.

### Key Entities

- **History bin**: min and max of the analysis signal over a short run of samples (main and key),
  with the beat position at its start and a playing flag.
- **Synced window**: the span on the beat grid containing the playhead, plus the previous pass.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a click on every beat at 120 BPM and Sync at 1 bar, each click's peak is drawn
  within 1 % of the span from its beat line.
- **SC-002**: A single-sample spike shows at its full height at every span.
- **SC-003**: Lens adds no audio-thread allocation and its per-block work stays within the perf
  gate (no override).
- **SC-004**: Rendering through Lens with and without the new features gives bit-identical
  output.

## Assumptions

- The issue's presentation features (themes, glow, logo overlay, presentation mode, theme files)
  are out of scope here and stay open on #119.
- "Combined" waveform colouring and pre-FX taps are left out: Lens sits where it is in the chain
  (pre/post is where you put it), and the key is the source track's post-FX audio, as for the
  Compressor.
- The pop-out is a plain resizable window; docking and a full-screen presentation mode are not
  part of this.
- GPU-specific rendering is not needed: the min/max history keeps the drawing to a few thousand
  points per frame, like today's scope.
