# Feature Specification: Shape a note while placing it

**Feature Branch**: `066-piano-roll-place-drag`

**Created**: 2026-10-30

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#45 — "Place a note and shape it in one gesture, instead
of placing it and then grabbing it again. While the mouse button is still held after placing
a note, dragging does something useful (choice by modifier or setting): drag horizontally to
set the length, drag to move the note (pitch / time) before releasing, with a modifier drag
vertically to set velocity. Today placement (double-click on empty space) adds the note and
returns; the held mouse does nothing until the next press. Look at FL Studio's piano roll."

## Nota Context

**Feature area**: UI (Avalonia, `src/managed/Nota.App/PianoRollView.cs`) — managed only, no
engine or ABI change.

**Existing behaviour this builds on**: double-click on empty grid adds a note with the length
and velocity of the last note touched (#44), auditions it until the button comes up, and
marks it as a pending placement that overwrites same-pitch notes once let go (specs/007).
Dragging an existing note moves it, dragging its edge resizes it, ⌥ during a drag ignores the
grid (FR-009 of the arrangement), and drags stream to the engine on the live path with one
undo step seeded at the gesture start.

## Clarifications

### Session 2026-10-30

- Q: Modifier or setting to pick what the held drag does? → A: Modifiers, read live, no
  setting. A plain drag moves the note — FL Studio's default, which the reporter points to;
  ⇧ sets the length (the end follows the drag, as dragging the edge does); ⌘ / Ctrl sets
  velocity with a vertical drag. ⌥ keeps meaning "off the grid".
- Q: What happens when the modifier changes mid-drag? → A: The drag switches what it does and
  continues from the note as it is at that moment — nothing jumps.

## User Scenarios & Testing

### User Story 1 - Place and move (P1)

A user double-clicks to add a note, keeps the button down and drags: the note follows the
pointer in pitch and time on the grid (⌥ off it), re-sounding at each new pitch, and lands
where the button comes up.

### User Story 2 - Place and set the length (P1)

Holding ⇧ while dragging after the double-click, the note's end follows the pointer
horizontally (snapped, never shorter than the minimum length); start and pitch stay.

### User Story 3 - Place and set the velocity (P2)

Holding ⌘ (macOS) / Ctrl (Windows, Linux) while dragging after the double-click, dragging up
makes the note louder and down quieter, one MIDI step per 2 px, clamped to 1..127; the note's
shade and the velocity lane follow.

### Edge Cases

- Release without moving: exactly today's placement.
- Losing the pointer (released outside the window) ends the placement drag. Hovering back over the grid does not keep moving the note.
- Switching into length or move mid-drag keeps the note as it is until the pointer actually moves; an off-grid end is not snapped by the switch itself.
- Velocity drag near the viewport edge does not auto-scroll the rows.
- Double-click on a note still deletes it; no drag follows.

## Requirements

- **FR-001**: After a double-click adds a note, while the button stays down, a drag shapes
  that note; the press does not need to be repeated.
- **FR-002**: Plain drag moves the note (pitch and time), snapped unless ⌥ is held.
- **FR-003**: ⇧ drag sets the length by moving the note's end; ⌘/Ctrl drag sets the velocity
  vertically. ⌘/Ctrl wins when both are held. Modifiers are read on every move.
- **FR-004**: Switching modifiers mid-drag re-anchors on the note's current state.
- **FR-005**: The placement stays pending (overwrites on let-go) exactly as before; the
  shaped note becomes the "last touched" note, so the next one drawn takes its length and
  velocity.
- **FR-006**: In the arrangement's editor the placement and its drag are one undo step
  (the live path); the Shortcuts pane and FEATURES.md list the gesture.

## Success Criteria

- **SC-001**: A note can be placed and given a different pitch, length or velocity in one
  press–drag–release.
- **SC-002**: `scripts/build-linux.sh` passes, including the new place-drag checks in the
  note-editing smoke tests.
