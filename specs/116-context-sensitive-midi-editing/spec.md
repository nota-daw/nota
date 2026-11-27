<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Feature Specification: Context-Sensitive MIDI Note Editing

**Feature Branch**: `116-context-sensitive-midi-editing`

**Created**: 2026-11-27

**Status**: Draft

**Input**: Approved GitHub issue [nota-daw/nota#1122](https://github.com/nota-daw/nota/issues/1122), refined by the existing-feature audit and the user's Piano Roll brief.

## Scope and existing behavior

Nota already supports note creation, selection and marquee selection, moving,
resizing, duplication, clipboard editing, keyboard nudges, semitone/octave
transposition, start quantization, velocity editing, expression/controller lanes,
audition, fit-to-notes, and trackpad/wheel zoom. These remain in place and are not
reimplemented by this feature.

This feature makes common note work accessible through a predictable,
context-sensitive pointer workflow, and fills the specific gaps in note mute,
split/join, precise selection editing, and navigation described below. MIDI clip
boundary dragging and automatic clip expansion are handled separately by approved
issue #1121 and are out of scope here.

## User Scenarios & Testing

### User Story 1 - Edit notes with the pointer (Priority: P1)

A user selects and edits notes directly in the Piano Roll without changing tools.
The pointer's position on a note indicates whether a drag moves it, adjusts its
start or end, or changes velocity. The same operation applies coherently to the
selected notes, preserving their relative timing, pitch and velocity differences
where the operation is a group adjustment.

**Why this priority**: This is the core of the approved issue: reduce tool switching
and make everyday note edits discoverable and predictable.

**Independent Test**: Create notes with varied pitches, starts, lengths and
velocities. Select one and several notes, then move, resize and adjust velocity from
their respective pointer zones. Confirm the expected notes change and unrelated
notes remain intact.

**Acceptance Scenarios**:

1. **Given** a note, **When** the user drags its center, **Then** it moves in time
   and pitch, obeying the active snap setting.
2. **Given** one or more selected notes, **When** the user drags a note's left or
   right edge, **Then** the corresponding boundaries change together and the notes
   remain valid.
3. **Given** one or more selected notes, **When** the user drags the velocity
   region, **Then** their velocities change by a shared delta and remain in the
   valid MIDI range.
4. **Given** a very short or narrow note, **When** the user hovers or clicks it,
   **Then** it remains selectable and movable, and ambiguous edit zones resolve
   consistently with visible cursor feedback.
5. **Given** an active drag, **When** the user presses Escape or the gesture loses
   capture, **Then** the edit is safely cancelled or settled according to whether
   the gesture was explicitly cancelled or completed by capture loss, with no
   partial conflicting operation.

### User Story 2 - Perform structural note edits safely (Priority: P2)

A user can mute/unmute a note without deleting it, split a note at a chosen musical
position, and join compatible adjacent notes. These operations are explicit and
undoable, so they do not happen accidentally during selection or ordinary drags.

**Why this priority**: These are named gaps in the requested context-sensitive
workflow, but they are less frequent and more destructive than moving, resizing or
adjusting velocity.

**Independent Test**: Mute and restore a note, split a note, join two compatible
adjacent notes, then undo and redo each operation and verify note playback and data.

**Acceptance Scenarios**:

1. **Given** a note, **When** the user invokes its mute action, **Then** it remains
   editable and visibly muted, and playback omits it until unmuted.
2. **Given** a note and an edit position inside it, **When** the user splits it,
   **Then** two notes meet at that position and retain the original pitch and
   applicable note properties.
3. **Given** adjacent notes that can be joined, **When** the user invokes Join,
   **Then** they become one note spanning their combined time; incompatible notes
   cannot be joined.
4. **Given** any mute, split or join operation, **When** the user undoes or redoes
   it, **Then** the complete operation is restored as one history step.

### User Story 3 - Edit selected note values precisely (Priority: P2)

A user can enter exact pitch, musical start, length and velocity values for the
selection from the Piano Roll settings area. A single selection shows editable
values; a multi-selection makes a committed value apply consistently to every
selected note, while mixed values are clearly represented before editing.

**Why this priority**: Numerical edits complement pointer editing for precise work
and are part of the approved issue, but are not required for basic note entry.

**Independent Test**: Select one note and edit each value numerically. Select notes
with different values, confirm mixed-value display, edit one field, and verify the
change, validation and undo behavior.

**Acceptance Scenarios**:

1. **Given** one selected note, **When** the user commits a valid pitch, start,
   length or velocity, **Then** the Piano Roll updates immediately and playback
   uses the committed values.
2. **Given** multiple selected notes with different values, **When** the inspector
   is displayed, **Then** mixed fields are distinguishable from a concrete value.
3. **Given** an invalid or out-of-range value, **When** the user commits it,
   **Then** the value is rejected or clamped according to the field's documented
   range and no invalid note is written.
4. **Given** a committed numerical edit, **When** the user undoes or redoes it,
   **Then** the full selection edit changes as one history step.

### User Story 4 - Navigate and snap predictably (Priority: P3)

A user can navigate and zoom the note grid without losing the musical position
under the pointer, pan with a dedicated pointer gesture, and use common straight,
triplet and dotted grid divisions. A temporary modifier bypasses snapping during a
gesture without changing the saved grid setting.

**Why this priority**: Nota already has grid, triplet, scrolling and cursor-anchored
zoom. This story covers the missing dotted divisions and explicit pan/snap-bypass
consistency requested in issue #1122.

**Independent Test**: Choose each grid family, create/move/resize notes, bypass snap
temporarily, and zoom or pan around a known beat. Confirm the saved grid is
unchanged by temporary bypass and the pointer anchor remains on the same beat.

**Acceptance Scenarios**:

1. **Given** the Piano Roll grid picker, **When** the user chooses a straight,
   triplet or dotted division, **Then** the drawn grid and note snapping agree.
2. **Given** snapping is enabled, **When** the user holds the temporary bypass
   modifier during a note gesture, **Then** that gesture follows unsnapped timing
   and the configured grid remains unchanged.
3. **Given** a pointer position over the grid, **When** the user zooms, **Then**
   the beat under the pointer stays at that position.
4. **Given** the user pans the grid, **When** the pointer gesture ends, **Then**
   the note data and active selection remain unchanged.

### Edge Cases

- Notes close together or overlapping must have deterministic hit-testing and cursor
  feedback; selecting one must not unexpectedly edit another.
- At small zoom or note dimensions, unavailable pointer zones fall back to a safe,
  discoverable operation without making the note unselectable.
- Group resizing must respect each note's minimum length, pitch range, MIDI velocity
  limits and clip limits without silently dropping notes.
- A split at or outside a note boundary, or a join of incompatible notes, is a safe
  no-op with no undo entry.
- Muted notes remain visible and selectable; their state survives copy, paste,
  duplicate, save, reload and undo/redo.
- A clip or selection change during a gesture cannot apply the edit to a different
  clip or leave auditioned notes sounding.
- Temporary snap bypass must work consistently across pointer-created, moved and
  resized notes while not modifying the user's grid preference.
- Note editing remains responsive during playback and with large note counts.

## Requirements

### Functional Requirements

- **FR-001**: The Piano Roll MUST retain its current selection,
  marquee, move, resize, duplicate, clipboard, nudge, quantize, velocity, expression,
  audition and fit-to-notes behavior.
- **FR-002**: The Piano Roll MUST provide a context-sensitive pointer workflow for
  selecting, moving, resizing and adjusting velocity without requiring persistent
  tool changes.
- **FR-003**: Pointer hit zones MUST adapt to note size and zoom, provide operation-
  appropriate cursors, and preserve access to short notes.
- **FR-004**: Group move and edit operations MUST preserve relative note properties
  except for the property being edited, and MUST respect valid note and clip limits.
- **FR-005**: Users MUST be able to explicitly mute and unmute notes; muted notes
  MUST remain visible, editable, persistable and silent during playback.
- **FR-006**: Users MUST be able to split a note at a valid edit position and join
  compatible adjacent notes through explicit, discoverable actions.
- **FR-007**: Mute, split, join and pointer gestures MUST each be atomic undoable
  operations; explicit cancellation MUST restore the pre-gesture state.
- **FR-008**: The Piano Roll settings area MUST support validated numerical editing
  of selected notes' pitch, start, length and velocity, including multi-selection
  and clear mixed-value display.
- **FR-009**: The Piano Roll grid MUST support straight, triplet and dotted timing
  divisions, and its visual grid MUST match the positions used for snapping.
- **FR-010**: Users MUST be able to bypass snapping temporarily during a pointer
  gesture without changing the configured grid setting.
- **FR-011**: Zoom MUST preserve the musical position beneath the pointer; a dedicated
  pan gesture MUST navigate without editing note data.
- **FR-012**: Note auditions MUST always receive note-off when a gesture ends, is
  cancelled, capture is lost, focus changes, or the active clip/editor changes.
- **FR-013**: MIDI clip boundary editing and automatic clip expansion are excluded
  from this feature and remain scoped to issue #1121.
- **FR-014**: New user-visible labels and gestures MUST follow Nota localization,
  shortcut catalog and design conventions.

### Key Entities

- **MIDI note**: Pitch, start, duration, velocity, mute state and any existing
  articulation or expression data.
- **Note selection**: The editable notes whose values or timing may be changed as a
  group.
- **Piano Roll grid setting**: The selected timing division, triplet/dotted family
  and snap-enabled state; temporary bypass is gesture-local.
- **Pointer gesture**: One captured editing action with a defined start state,
  operation, preview and commit/cancel outcome.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Users can select, move, resize and adjust the velocity of notes using
  the context-sensitive pointer without changing editing tools.
- **SC-002**: Split, join and mute operations can each be completed and reversed in
  one undo/redo step.
- **SC-003**: A user can enter exact values for pitch, start, length and velocity on
  one note or a multi-selection without producing invalid MIDI values.
- **SC-004**: Straight, triplet and dotted grid lines coincide with the timing
  positions used by note creation and editing.
- **SC-005**: Zooming leaves the beat under the pointer stationary, and panning does
  not alter note data.
- **SC-006**: All auditioned notes are released after normal completion, cancellation,
  focus loss and active-clip changes.
- **SC-007**: The interaction remains responsive during playback on a clip containing
  at least 10,000 notes.

## Assumptions

- The current Piano Roll remains the single editing surface; no separate editor or
  persistent tool palette is introduced for these operations.
- Existing selection and note-edit commands remain available alongside pointer
  gestures.
- A note is considered join-compatible when pitch and mute state match and the notes
  touch or overlap within the grid's smallest editable tolerance; articulation and
  expression handling must preserve data deterministically.
- Existing snap settings remain the source of truth; dotted divisions extend the
  grid choices without changing the behavior of existing projects.
- Issue #1121 owns MIDI clip boundary handles, trim semantics and automatic clip
  expansion.

### Modifier-drag duplication (US1)

**FR-015**: Holding a duplication modifier when dragging the center of a note MUST
copy the selected notes and move the copies as a group, preserving note properties
and relative timing and pitch. A press and release without a drag MUST create no
copies. Escape MUST discard the copies. A completed gesture MUST undo in one step.
The editor MUST keep duplication distinct from temporary snap bypass.

### P0: Proportional note time stretching (US1)

**FR-016**: Alt/Option + drag on either edge of an editable note MUST stretch
that note or the current selection if the grabbed note is selected. The opposite
outer edge of the selection stays fixed. The same positive scale applies to the
notes' starts relative to that anchor and to their durations. Pitch and velocity
stay unchanged. The editor MUST limit the scale to keep all notes inside their
clips and above the minimum valid duration.

**Acceptance**: With snap on, the moving edge of the selection follows the grid
and internal note boundaries retain their proportions. With snap off, movement
is free. Shift temporarily bypasses snap during the stretch. Notes update visibly
as the pointer moves. Escape restores the original notes. A completed stretch
takes one undo step and redo restores it. An edge drag started without Alt/Option
continues to resize normally.


### Single-click note creation (US1)

**FR-017**: A single left click on editable empty grid space MUST create one
note using the existing snap, pitch, length and velocity defaults. An empty-space
drag MUST still start marquee selection. Existing-note double-click deletion
remains available. A double-click on empty space MUST NOT create and immediately
delete the same note.
