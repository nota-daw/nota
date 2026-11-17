# Feature Specification: Keep held notes safe when changing a track's MIDI source

**Feature Branch**: `093-midi-source-held-notes`

**Created**: 2026-11-11

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#504: "Change which internal instrument track feeds another
track, or turn that route off, without leaving a previously routed note sounding. […] Notes
already started through the old route should either receive their corresponding releases at the
original receiver or be deliberately released as part of the switch. New notes should follow the
newly selected route once the change takes effect. Avoid a blanket reset that cuts unrelated
notes already playing on the receiving track. […] Repeated changes should have a defined
outcome, and the UI should show the applied source. If a referenced track no longer exists,
report that state rather than silently selecting a different instrument."

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — the instrument render's routed MIDI
- [x] Managed app (C#, `src/managed/*`) — the MIDI In selectors
- [x] UI (Avalonia) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: an instrument track's **MIDI In** (`midiFromTrackId`)
names another instrument track whose post-FX MIDI it plays. `setTrackMidiSource` changes the id;
each block `renderInstrumentRaw` reads the source's events from the MIDI pre-pass. Nothing
remembers what the old route started, so a note-on that came through the old route never gets
its note-off once the route changes: the old source's note-off now goes nowhere. Removing the
source track leaves the id in place (undo restores the track), with the same result. Disarming a
track while a key is held already releases its live notes (#366); a track whose MIDI input
device changes still takes the note-off of a note it holds (specs/086).

## User Scenarios & Testing

### User Story 1 - Switch the route while a note is held (Priority: P1)

Track D plays track A's MIDI. The performer holds a note on A and switches D's MIDI In to None,
or to track B. D's note ends at the switch; A keeps sounding its own note; once the key comes up
nothing is left on D. With B as the new source, B's notes play on D from then on.

**Independent Test**: D ← A, A armed and muted, hold a key; switch D to None (or B); after the
release time the master is silent with the key still down, and stays silent after the key-up.
Notes played on B then sound through D and end with their note-off.

**Acceptance Scenarios**:

1. **Given** D ← A and a key held on A, **When** D's MIDI In changes to None or B, **Then** D's
   note from A ends at the next block.
2. **Given** the switch to B, **When** B plays a note, **Then** D plays it and ends it with B's
   note-off.
3. **Given** the switch, **When** the key on A comes up, **Then** nothing is left sounding on D.

### User Story 2 - Repeated changes (Priority: P1)

The performer flicks D from A to B and back to A faster than an audio block. Nothing changed for
D, so its note keeps sounding and ends with the key, as if no switch had happened.

**Acceptance Scenarios**:

1. **Given** D ← A with a key held, **When** D goes to B and back to A before the next block,
   **Then** the note keeps sounding and ends with A's note-off.
2. **Given** several switches across blocks, **Then** each block that renders with a new source
   ends the notes the previous one started; notes are never ended twice by the route.

### User Story 3 - The source track is removed (Priority: P2)

The source track A is deleted while its key is held. D's note ends. D's MIDI In still names A,
and the pickers say **Removed track** (**From a removed track** in the compact selectors)
instead of None or All inputs. Undo restores A, and the route plays again.

**Acceptance Scenarios**:

1. **Given** D ← A and a key held, **When** A is removed, **Then** D's note ends.
2. **Given** A removed, **Then** the track menu's MIDI from item, the inspector's MIDI from row,
   the detail chip and the header / mixer / Session mixer selectors show the source as removed.
3. **When** the removal is undone, **Then** D plays A's notes again.

### Edge Cases

- **The track's own notes** (its clips, its live input, Nota Remote on it): untouched; only the
  pitches the route started and hasn't ended are released.
- **A pitch the track also plays itself**: a note-off ends a pitch, so a routed note on the same
  pitch as one of the track's own notes ends it too. That is the instrument's note model, and
  the same as a note-off from the source would do.
- **The destination muted or playing a Session slot at the switch**: it renders no
  routed MIDI then; the release goes out the next time it renders routed MIDI.
- **The source muted**: it still forwards MIDI (as before), so its notes are tracked and
  released like any other.
- **Sustain pedal held on the destination**: the release waits for the pedal, like any
  note-off.

## Requirements

### Functional Requirements

- **FR-001**: The engine MUST remember, per instrument track, the pitches its MIDI In started
  and hasn't ended, and the source they came from.
- **FR-002**: When a track renders with a MIDI In other than the one those notes came from, it
  MUST send their note-offs first, at the start of the block, and forget them.
- **FR-003**: When MIDI In names a track that is not in the graph, its notes MUST end.
- **FR-004**: Notes the track did not get through its MIDI In MUST NOT be released by the
  switch.
- **FR-005**: The audio thread MUST NOT allocate, lock or do I/O for this (AR-6).
- **FR-006**: The MIDI In pickers MUST show a source that no longer exists as removed, not as
  None or another input.

## Success Criteria

- **SC-001**: After any MIDI In switch, no voice the old route started keeps sounding past the
  instrument's release.

## Assumptions

- Releasing at the switch (rather than delivering the old source's later note-off) is the
  "deliberately released as part of the switch" option of the issue: it ends the note when the
  performer changes the route, which is when they expect the old layer to stop.
- The issue's first version covers the routing control itself; MIDI Learn / Action switching and
  external MIDI output are out of scope, as it says.
- Controllers routed from the old source (a held sustain pedal on it) are not part of this
  change.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header
- [x] No new dependency
- [x] Audio-thread impact: two bit masks and an id per track, in the track's existing
  audio-thread state (`Track::LiveHeld`); no allocation, lock or I/O
- [x] No new managed/native crossings
- [x] Smoke-test impact: `MidiSourceSwitchTests` (`-- --midi-source`)
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` (existing pickers, texts through `Loc`)
