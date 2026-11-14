# Implementation Plan: Keep held notes safe when changing a track's MIDI source

**Branch**: `093-midi-source-held-notes` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

## Summary

The instrument render remembers which pitches a track's MIDI In started and which source they
came from. A block that renders with another MIDI In, or with a MIDI In whose track is gone,
first sends those pitches' note-offs. The MIDI In pickers name a removed source as such.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10, Avalonia.

**Primary Dependencies**: none new.

**Audio-Thread Impact**: a few bit operations per routed event and, on a switch, at most 128
note-offs appended to the block's stack array (capacity 1024, as the rest of the render). The
state lives in `Track::LiveHeld`, which the track already shares across its versions (edits,
undo) and with no other track; it is touched only by the track's own render. No allocation, no
lock, no I/O.

**Testing**: `MidiSourceSwitchTests` in the smoke test (`-- --midi-source` alone, and in the
full run): switch to None and to another track, the new route playing, switch away and back
inside one block, the source removed and the removal undone, the destination's own note kept.

## Design

- `Track::LiveHeld` gains `routedBits[2]` (pitches the route started, not yet ended) and
  `routedFrom` (the MIDI In they came from).
- `Engine::renderInstrumentRaw`:
  - before gathering: when `routedFrom` differs from the track's current MIDI In, append the
    note-offs (`releaseRouted`) at offset 0 and take the new id. A route switched away and back
    between two renders matches and releases nothing.
  - while gathering the source's events: set and clear `routedBits` from its note-ons and offs.
  - after gathering: a MIDI In whose track wasn't in the graph releases its notes too.
  - the note-offs sit at offset 0; `midiEvLess` sorts offs before ons at a tie, so a new
    source's note-on on the same pitch in that block starts after the release.
- `Engine::releaseRouted` (static): appends and clears, like the disarm release (#366).
- UI: `MidiInputPick.Compact` lists a removed source as "From a removed track" and selects it;
  the track menu's MIDI from value, the inspector's MIDI from chip and the detail chip read
  "Removed track". New `en.json` keys: `midi_input.from_removed_track`, `common.removed_track`.

## Constitution Check

Engine change within the real-time rules (AR-6): fixed-size state, no allocation. No new ABI,
no format change (a removed source is still saved as no source, as before). Texts through `Loc`.
