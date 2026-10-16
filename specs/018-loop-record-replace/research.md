# Research: Loop record mode — Overdub / Replace

## R1 — Where a "pass" is known

- **Decision**: The audio thread counts passes. `Engine::onLoopWrap` (added by the seam fix) increments `recordPass_` while an Arrangement MIDI take is recording. Every `RecordedNote` carries the `pass` it started in.
- **Rationale**: The wrap happens on the audio thread, block-accurately, and `onLoopWrap` already splits held notes there. The head of a split note keeps the old pass, and the continuation (restarted at the loop start) gets the new pass. That is exactly spec AS-1.3.
- **Alternatives**: Derive the pass on the message thread from note start beats plus the loop length. Rejected: a pass is a time span, not a beat range (beats repeat every pass), so start beats cannot tell passes apart.

## R2 — What triggers a replace

- **Decision**: The trigger is a *fresh* note-on of the take (live keyboard on the record track, or routed MIDI captured into it) whose pass is greater than the last replaced pass, while the pass's latched mode is Replace. It sets `replaceUpTo_` (an atomic with release semantics) to that pass. The continuation of a note held across the seam is not a fresh note-on, so it does not trigger.
- **Rationale**: Clarification Q1 says whole earlier passes are removed at the first note of the new pass. FR-004 says a silent pass removes nothing. If a held continuation triggered the replace, holding a final chord over the seam would wipe the pass that chord belongs to.
- **Alternatives**: Trigger on the first *released* note. Rejected because the removal would lag the press by the note's length, and FR-010 wants the take view to update as you play.

## R3 — Mode switch timing

- **Decision**: The message thread writes the mode to an atomic (`loopRecordReplace_`). The audio thread latches it into `passReplace_` at take start and at every wrap. Triggers use the latched value.
- **Rationale**: FR-007 says a switch applies from the next pass. Latching at the wrap gives that with one relaxed load per wrap.

## R4 — Removing earlier passes on the message thread

- **Decision**: `collectRecorded` (message thread, `Poll`) first loads `replaceUpTo_` with acquire semantics, then drains `recorded_`, so every note of an earlier pass that was pushed before the trigger is already in the batch. It appends the batch. Then, if `replaceUpTo_` has grown, it removes from the take clip each note recorded by this take with `pass < replaceUpTo_`, and drops any late arrival with `pass < replaceUpTo_`. The take's notes are tracked in a message-thread list `takeNotes_` of `{pitch, absStartBeat, pass}`. Clip notes are matched by pitch and absolute start (ε = 1e-6 beat), so the matching survives the clip's left edge moving (count-in pickups) and other notes being reordered.
- **Rationale**: FR-005 says notes in the clip before the take must stay. Matching by identity (not by index) keeps those notes untouched. The edit goes through the same `republishWithTrackRaw` as the growing take, so the whole take stays one undo step (spec edge case) and stays AR-5 compliant: an immutable snapshot published from the message thread.
- **Alternatives**: A parallel per-index pass array. Rejected because it is fragile when the clip shifts or the user edits notes during recording.

## R5 — Held-note preview

- **Decision**: No change to the snapshot ABI. The preview shows held (pending) notes, and those always belong to the current pass. The replaced notes disappear when the next `Poll` republishes the clip, at most one UI tick (~33 ms) after the trigger.

## R6 — Scope boundaries

- Session slot recording (`recordSlotPlayer_` set) never counts passes and never replaces (spec edge case).
- Loop off: there are no wraps, so `recordPass_` stays 0 and nothing is ever replaced.
- Audio loop punch is unchanged.

## R7 — UI and shortcut

- **Decision**: A `cell` ToggleButton in the transport console, right after the Loop cell, labelled **Overdub** / **Replace** (sentence case, per DESIGN.md). The accent shows only while it reads Replace, since Overdub is the default resting state. A new command `transport.loopRecordMode`, "Loop record: overdub / replace", bound to **⌘⇧R** (free in `CommandCatalog`: ⌘R is Record, and the shift variant reads as "record mode"). A menu item goes under Transport next to Loop and Metronome. The setting persists as `Settings.LoopRecordReplace` (bool, default false), mirroring `CountInBars`.
- **Alternatives**: The right-click menu on Record (Q3 option B) and menu-only (Q3 option C) were rejected by the user.

## R8 — MCP

- **Decision**: `get_transport` gains `LoopRecordReplace`. A new tool `set_loop_record_mode(replace: bool)` sets the engine and the app setting through the same transport view-model path the UI uses (FR-012).
