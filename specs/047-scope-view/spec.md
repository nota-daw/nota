# Feature Specification: Scope view

**Feature Branch**: `feat/scope-view`

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "Scope: resizable / pop-out view with basic controls
(nota-daw/nota#266). The Modular Scope node is too small to be useful and can't be made bigger.
It should be resizable inside the card, with a pop-out / full-width view, and have basic
controls: time window (ms or beat-synced), vertical zoom, trigger (free / rising edge), freeze."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See a modulator's shape at a useful size (Priority: P1)

A sound designer patches an LFO into a Scope node to see what it does. Today the trace is a
76 px strip that rolls past in about a second, whatever the LFO's rate. They drag the bottom
edge of the scope to make it taller, or open it in its own window, and set the time window so
one or two cycles fill it.

**Why this priority**: The issue's first complaint is size; without it the other controls have
nowhere to live.

**Independent Test**: Add a Scope, patch an LFO in, drag the scope's bottom edge down, and open
the pop-out. Both show the same live trace at the new size.

**Acceptance Scenarios**:

1. **Given** a Scope node, **When** the user drags the grip under its trace, **Then** the trace
   grows or shrinks between 76 px and 320 px tall, and the node's card grows with it.
2. **Given** a resized Scope, **When** the project is saved and reopened, **Then** it keeps its
   height.
3. **Given** a Scope node, **When** the user clicks its expand button, **Then** a separate,
   resizable window opens with the same live trace and the same controls, titled after the
   track and the node; closing it leaves the node as it was.
4. **Given** the pop-out is open, **When** the node is deleted or the track is removed, **Then**
   the window closes.

---

### User Story 2 - Time window, zoom, trigger, freeze (Priority: P1)

The user sets the window to 1 bar so an LFO synced to 1/4 shows four cycles that line up with
the beat. They raise the zoom on a shallow envelope, turn the trigger on so a repeating shape
holds still instead of rolling, and freeze the trace to look at one moment.

**Why this priority**: These are the "basic controls" the issue lists.

**Independent Test**: With an LFO at 1/4 synced, set the window to 1 bar and see four cycles;
turn Trigger on and see the trace stop rolling; Freeze and see it hold while the LFO keeps
running.

**Acceptance Scenarios**:

1. **Given** the time window in **ms**, **When** the user sets it between 50 ms and 8 s,
   **Then** the trace spans exactly that much time, newest at the right.
2. **Given** the time window in **beats** (**Sync** on), **When** the user picks 1/4 beat to
   8 bars, **Then** the trace spans that many beats at the current tempo, and follows a tempo
   change.
3. **Given** a zoom of 1×, 2×, 4× or 8×, **Then** the vertical scale is ±1.15 divided by the zoom
   (a trace that leaves the scale is clipped at the edge).
4. **Given** Trigger on (rising edge), **When** the input crosses the trigger level (0) going up,
   **Then** the trace starts at the latest such crossing that leaves a full window after it, so
   a periodic signal holds still. With no crossing in the history the trace rolls as with
   Trigger off.
5. **Given** Freeze on, **Then** the trace stops updating while the input keeps running; Freeze
   off resumes the live trace.
6. **Given** these settings, **When** the project is saved and reopened, **Then** the window,
   Sync, zoom, trigger and trace height come back; Freeze always starts off.

### Edge Cases

- **Not enough history**: a window longer than what the scope has recorded (right after it is
  added, or at a very long window) draws what there is, right-aligned.
- **Transport stopped**: the scope keeps recording (modulators run while stopped), so an LFO
  stays visible.
- **Block-rate CV**: the scope records one value per audio block (a few ms). At the shortest
  window the trace is a few dozen points; it is drawn as straight segments between them.
- **Unwired scope**: the hint text stays as today; the controls stay usable.
- **Older projects**: a Scope saved before this change opens at 76 px, a 1 s window, 1×, free
  running.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The engine MUST record a Scope's input with each value's time step, at least 4096
  values, written only by the audio thread and read lock-free by the UI. Only Scope nodes hold
  the buffer.
- **FR-002**: Reading the scope MUST return the newest values (oldest → newest) with their time
  steps.
- **FR-003**: A Scope MUST have saved settings: window in ms (50–8000, default 1000), Sync
  (off by default), window in beats (0.25–32, default 4), zoom (1, 2, 4 or 8, default 1) and
  trigger (free or rising, default free), readable and writable like its other fields.
- **FR-004**: The Scope node MUST show controls for window (ms or beats with a Sync switch),
  zoom, trigger and freeze, under the trace.
- **FR-005**: The trace MUST be resizable by a grip, 76–320 px, saved with the Scope (field 18).
- **FR-006**: An expand button MUST open the scope in its own resizable window with the same
  trace and controls.
- **FR-007**: Freeze MUST be a view state only (not saved).

### Key Entities

- **Scope history**: a ring of (value, time step) pairs owned by a Scope modulator.
- **Scope settings**: window (ms / beats, Sync), zoom, trigger — modulator fields saved with the
  project.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a 1 Hz LFO and a 2 s window, the trace shows two cycles (within 5 %).
- **SC-002**: With a synced LFO and Trigger on, the trace's start moves by less than one block
  between frames.
- **SC-003**: A Scope adds no audio-thread allocation and less than 1 % to the engine's
  per-block work (the perf gate passes without an override).
- **SC-004**: An older project with a Scope opens with the defaults and its wiring unchanged.

## Assumptions

- The trace is the Scope's CV input, as today; audio stays Lens's job (#261).
- There are no MCP modulator tools yet, so the Scope settings have no MCP surface; Duplicate
  Track does not copy modulators today (separate gap), so it does not copy Scope settings either.
- The trigger level is fixed at 0 (the CV centre); a level control can come later.
- #119 (tempo-synced oscilloscope and visualizer) builds on this.
