# Feature Specification: Hear notes in the piano roll

**Feature Branch**: `024-piano-roll-audition`

**Created**: 2026-10-18

**Status**: Implemented

**Input**: User description: "Piano roll: note audition toggle — play notes when placing, selecting, dragging (nota-daw/nota#46) — and make the keys column playable: click a key to hear it, drag across keys for a glissando, modifier-click selects all notes of that pitch (nota-daw/nota#47)."

## Nota Context

**Feature area**: UI (Avalonia) — `PianoRollView`, `ClipEditorView`, `MainWindow.Detail`.
No engine change: audition goes through the existing `TrackNoteOn` / `TrackNoteOff` ABI (a
live note addressed to one track, armed or not).

## Clarifications

### Session 2026-10-18

Defaults chosen for the open points:

- Q: Where is the switch, and is it per clip? → A: A speaker chip in the clip editor's header, first in the right-hand group. One app-wide setting (`Settings.PianoRollAudition`), on by default (FL Studio's default); every open editor's chip follows it.
- Q: How long does an auditioned note sound? → A: While the mouse button is down. A drag to another pitch ends the old note and starts the new one.
- Q: Which note sounds when a group is dragged? → A: The note under the pointer (the grabbed one), at its own velocity.
- Q: What about recording? → A: No audition while the engine records: a track's live notes are recorded into its take.
- Q: The keys column already zooms on a vertical drag (specs/019 FR-004, which said a key drag must not audition). How do both live there? → A: A press plays the key; moving more than 4 px vertically turns the press into the zoom and lets the note go. ⇧ held at the press glides through the keys instead of zooming. This amends 019 FR-004: the press sounds the key until the drag starts.
- Q: Does the audition switch silence the keys? → A: No. The keys are an instrument; the switch is about notes being edited.
- Q: Which modifier selects a pitch? → A: ⌘-click (Ctrl-click on Windows / Linux), ⇧ added keeps the existing selection. It plays nothing.

## User Scenarios & Testing

### User Story 1 - Hear notes while editing (P1)

**Acceptance Scenarios**:

1. **Given** audition on, **When** the user double-clicks empty grid, **Then** the new note sounds until the button comes up.
2. **Given** audition on, **When** the user presses a note and drags it up, **Then** it sounds at its pitch, then at each new pitch, and stops on release.
3. **Given** audition off, **When** the user presses or drags a note, **Then** nothing sounds.
4. **Given** the switch was turned off, **When** Nota restarts, **Then** it is still off.

### User Story 2 - Play the keys (P2)

1. **Given** any clip open, **When** the user presses a key, **Then** that pitch sounds on the track's instrument and the key lights until release.
2. **Given** a press on a key, **When** the user drags vertically, **Then** the rows zoom as before and the note stops.
3. **Given** ⇧ held, **When** the user drags across keys, **Then** each key sounds in turn.
4. **Given** notes of C4 in the clip, **When** the user ⌘-clicks the C4 key, **Then** exactly those notes are selected.

## Requirements

- **FR-001**: The roll MUST play at most one audition note at a time and MUST end it on release, capture loss or when the editor is closed.
- **FR-002**: Audition MUST go to the clip's own track (arrangement clip or Session slot), armed or not.
- **FR-003**: Audition MUST NOT play while the engine is recording.
- **FR-004**: The switch MUST persist in Settings and stay in step across open editors.
- **FR-005**: Settings → Shortcuts MUST list the keys-column gestures.
