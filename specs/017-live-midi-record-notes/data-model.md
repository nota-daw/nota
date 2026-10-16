# Data Model: Live MIDI notes while recording

Phase 1 of [plan.md](plan.md). Nothing here is persisted except the clip's notes.

## Capture clock (engine, audio thread)

The beat the capture code stamps events with. Derived from `Transport` (AR-9) and the
count-in counters; never stored.

| State | Value |
|-------|-------|
| Transport rolling (arrangement) | block start beat (`playheadSamples / spb`) |
| Count-in running | `takeStartBeat − (countInTotal − countInPos)` — below `takeStartBeat`, may be < 0 |
| Session slot take | slot-local beat, `fmod(localBeats, slotLen)` |
| Stopped, no count-in | not capturing |

## Held live key (engine, audio thread)

One per pitch per source (live input, routed MIDI). Tracked while the transport is rolling
or a count-in runs, whether or not recording is on; used to seed the take on the record
rising edge (research R3).

| Field | Type | Notes |
|-------|------|-------|
| down | bool | key currently held |
| pressBeat | double | capture clock at note-on |
| velocity | float | 0..1 |
| trackId | int32 | Remote-addressed track, −1 = ordinary live input |

## Held recorded note (engine → UI)

An entry of the take's held set: a note that will be written into the take on release.
Exists from note-on (or from the record edge, when seeded) until note-off, recording
stop or transport stop.

| Field | Type | Rule |
|-------|------|------|
| pitch | int32 | 0..127 |
| source | int32 | 0 live input, 1 routed MIDI |
| startBeat | double | capture clock at start. Arrangement: absolute timeline beat, may be before the take's clip start and below 0 (count-in before 1.1). Session: slot-local |
| velocity | float | 0..1 |

Lifecycle: `none → held` (note-on while capturing, or seeded at record edge) →
`stored` (note-off: becomes a `RecordedNote`, leaves the held set in the same block) or
`dropped` (recording/transport stops first — as today, not stored).

Invariant: a pitch/source pair is held at most once; a re-trigger closes the previous
note first (today's `pendingActive_` semantics).

## Held-note snapshot (engine → UI, published per block)

| Field | Type | Notes |
|-------|------|-------|
| seq | atomic uint32 | odd while writing; readers retry on odd or changed |
| trackId | int32 | take's track; 0 when no MIDI take |
| sessionScene | int32 | −1 for arrangement takes |
| nowBeat | double | capture clock at the end of the block (right edge of held notes) |
| count | int32 | 0..256 |
| entries | Held recorded note × 256 | live (128) + routed (128) |

## Stored early note (clip data)

Ordinary `Note` in the take's `MidiClip`; `startBeat` is clip-relative.

| Case | Clip start | Note startBeat |
|------|-----------|----------------|
| Early note after beat 0 | moved back to the note's start | 0 (or ≥ 0 for later notes; existing notes shifted by the delta) |
| Early note before beat 0 (take at 1.1) | 0 | negative; the part `[0, start+length)` sounds from the clip start |

Render rule: a note with `startBeat < 0` and `startBeat + lengthBeats > 0` sounds from
the clip start for the remaining length; notes with `startBeat ≥ lengthBeats` stay muted.

## Appear-animation entry (UI only)

| Field | Type | Notes |
|-------|------|-------|
| key | (pitch, startBeat rounded to 1e-6) | absolute beat for arrangement, slot-local for Session |
| firstSeen | timestamp | first tick the note was held or newly stored |

Dropped 150 ms after `firstSeen`. A key already present never restarts the animation.
