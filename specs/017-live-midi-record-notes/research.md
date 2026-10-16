# Research: Live MIDI notes while recording

Phase 0 of [plan.md](plan.md). Each section records a decision, why, and what else was
considered. File references are to the state of `develop` at 0.55.1.

## R1 — How the held notes leave the audio thread

**Decision**: The audio thread publishes a compact snapshot of the held captured notes
once per block behind a sequence counter (a seqlock: one writer, readers retry). Each
slot is made of relaxed `std::atomic` fields so the read is not a data race. The snapshot
holds the take's track id, the capture clock's "now" beat and up to 256 entries (128 pitches × live and routed)
(pitch, source, start beat, velocity). A new C ABI call copies it out
(see [contracts/c-abi.md](contracts/c-abi.md)).

**Rationale**: The capture state (`pendingActive_` / `pendingStart_` / `pendingVel_` and
`recRoutedActive_` / … in `Engine.h`) is plain audio-thread memory; the UI must not read
it directly. The writer never waits, allocates or locks (AR-6); at most 256 small stores
per block. Readers are the UI tick at ~30 Hz; a torn read is retried a few times and
otherwise returns the previous count of 0 — a dropped frame of preview, never a stall.
The "now" beat travels in the same snapshot so the note's right edge and its start come
from one consistent instant, including during the count-in, when the transport playhead
does not move.

**Alternatives considered**:
- *Per-pitch atomics only* (like `liveHeld_` bitmask, `Engine.h:1520`): a bitmask works for
  "which keys are down" but not for a start beat + velocity per note without tearing.
- *SPSC ring of on/off events to the UI* (like `recorded_`): the UI would have to rebuild
  state and resynchronise after a missed event or a stop; the snapshot is self-healing.
- *Reading `recorded_` earlier / writing open notes into the clip*: would turn the preview
  into stored data and churn undo/republish every tick; rejected.

## R2 — Capturing notes during the count-in (FR-012)

**Decision**: Give the capture code a *capture clock* instead of the transport beat:
- transport rolling → the block's start beat (today's `blockStartBeat`);
- count-in running → `takeStartBeat − (countInTotal_ − countInPos_)`, i.e. negative
  offsets before the take start (beats before 1.1 when the take starts at 1.1);
- Session slot → the slot's loop-local clock (unchanged).

`drainLiveMidi` and `captureRoutedNotes` capture when `recording && (playing ||
countInActive_)` (today only `playing`, `Engine_Render.cpp:459` and `:682`).

**Rationale**: During the count-in the transport is stopped (`Transport::advanceBy`
only moves when playing) and `drainLiveMidi` gates on `playing`, which is why count-in
notes are lost today. `recording_` is already true from the moment Record is pressed
(`Engine_Recording.cpp`, `setRecording`), so only the gate and the clock change.

**Alternatives considered**: A dedicated pre-roll buffer replayed at take start — more
state, same result.

## R3 — Keys already held when recording starts (FR-012)

**Decision**: Track every live (and routed-into-the-record-track) note-on with its press
beat on the capture clock *whenever the transport is rolling or a count-in runs*, in
audio-thread arrays separate from the record pending state. On the record rising edge
(`prevRecording_` flip, `Engine_Render.cpp:417`), instead of only clearing the pending
arrays, seed them from the keys still down for the take's track:
- pressed while rolling → their real press beat;
- pressed while stopped, then Record with count-in → the count-in's first beat;
- pressed while stopped, Record without count-in → the take start beat.

Notes pressed and released before Record while rolling are not captured (clarified).

**Rationale**: Today the rising edge wipes `pendingActive_`, dropping those keys. The press
beat is only knowable on the audio thread, where events are timed.

## R4 — Storing early notes: moving the clip's left edge (FR-013)

**Decision**: In `Engine::poll` (`Engine_Render.cpp:276–292`), when a materialised note's
absolute start is before `clip.startBeat`, move the clip's start back to
`max(0, noteStart)` and shift the clip's existing notes and length by the same delta
(as `trimClip` does, `Engine_Tracks.cpp:1903`). A note still starting before the new
clip start (only possible before 1.1) is stored with a negative `startBeat`.

Render change: a note whose `startBeat < 0` but whose end is inside the clip sounds from
the clip start with the remaining length. Today every gating site skips it
(`Engine_Render.cpp:524`, `:749`, `:765`, `Engine_Tracks.cpp:1807`); each gets the same
rule. Notes starting at or after the clip end stay skipped.

The take's undo step is unchanged: the left move is part of the live take
(`republishWithTrackRaw`, no extra undo step).

**Rationale**: Matches the clarification; `trimClip` already keeps notes in place when the
left edge moves, so a stored negative head becomes audible and visible as soon as the
left edge is extended — after moving the clip right if it sits at 1.1. Clips cannot start
before beat 0 (`trimClip` clamps), so negative note starts are the only way to keep the
head.

**Alternatives considered**: Clamping the note to the clip start (rejected by the user);
allowing negative clip starts (touches every timeline consumer, export, ruler).

## R5 — Session slots

**Decision**: Session slots keep today's behaviour for early notes: `std::fmod(r.startBeat
< 0 ? 0 : …)` (`Engine_Render.cpp:266`) — an early note starts at the slot start. The held
preview is slot-local and is drawn in the slot being recorded. Notes crossing the loop
end are previewed wrapped, the same way `drainLiveMidi` computes `len += slotLen`.

**Rationale**: Clarified in the spec's assumptions; a slot has no timeline before its
start.

## R6 — Where and how often the UI redraws

**Decision**: Draw the preview from the existing recording tick: `MainWindowViewModel.OnTick`
(33 ms) raises `RecordingTick` while `Engine.IsRecording`, which already calls
`Timeline.Refresh(rebuildHeaders: false)` and `ReloadEditorNotes()`
(`MainWindow.axaml.cs:318`). The tick reads the snapshot once and hands it to the
arrangement lanes, the Session grid and the piano roll. No new timer; the 30 Hz rate stays.

**Rationale**: The lane surface already repaints on every recording tick (full clip-model
refresh), so the preview adds only a few rectangles to work that already happens.
`RecordingTick` fires only while recording (count-in included, since `recording_` is set
at Record), so the idle repaint rate is untouched (FR-010, SC-004). Limiting the lane
redraw to the take's rectangle would be an improvement to the *existing* recording
refresh as well; it is not needed for this feature and is left as a follow-up.

**Alternatives considered**: A separate 60 Hz animation timer — only if 30 Hz looks choppy
in the quickstart check (5 frames over 150 ms with ease-out is expected to be smooth).

## R7 — The appear animation

**Decision**: A UI-side tracker, shared by the views, maps each recorded note in the take —
held (from the snapshot) or just materialised (new in the record clip since the last
tick) — keyed by (pitch, absolute start beat), to the time it was first seen. Draw
progress `p = clamp((now − firstSeen) / 150 ms)`: opacity `0.35 → 1` and a blend from the
accent colour to the track's note colour, ease-out. Entries older than 150 ms are dropped
from the tracker; growth and release keep the key, so they never re-animate. Notes that
were pressed and released between two ticks (never held in a snapshot) still animate,
because they appear as new materialised notes.

**Rationale**: Covers FR-008 and SC-006 including staccato; the colour use follows the
Studio rule that the orange accent marks what is live (`DESIGN.md`).

## R8 — Smoke test

**Decision**: Extend `tests/Nota.SmokeTest` (a new `LiveRecordTests.cs`, registered in
`Program.cs`) driving the engine offline like `MpeTests.Recording`:
held snapshot appears on note-on and grows with the clock; disappears on note-off and the
stored note matches; count-in pickup note stored before the take start; key held across
Record seeded at its press beat; take at 1.1 keeps a negative head and the in-clip part
renders; routed MIDI previewed; Session slot preview is slot-local.

**Rationale**: Constitution IV and FR-011: engine behaviour changes and a new C ABI read
need smoke coverage.
