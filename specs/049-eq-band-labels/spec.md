# Feature Specification: EQ band labels and notes

**Feature Branch**: `feat/eq-band-labels`

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "Always-visible custom EQ band labels with expandable notes
(nota-daw/nota#135). Every EQ band gets an optional short label shown next to its node in the
graph, and clicking the label opens a larger multiline note. Labels and notes save with the
project and preset, stay with their band when it moves, avoid colliding with each other, can be
hidden all at once, and don't touch the DSP."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Name a band so its purpose is visible at a glance (Priority: P1)

A mixer cuts a narrow resonance at 2.4 kHz on a vocal EQ-8. They name band 4 "Resonance" and it
shows next to the node in the graph. Weeks later they open the project and know straight away
what that dip is for.

**Why this priority**: The label in the graph is the heart of the request; notes and the toggle
build on it.

**Independent Test**: Name a band, see the label next to its node, drag the node and see the
label follow, save, reopen, and see it again.

**Acceptance Scenarios**:

1. **Given** an EQ-8 with band 4 selected, **When** the user types "Resonance" into the band's
   Label field in the band panel, **Then** "Resonance" appears next to band 4's node in the graph
   and on band 4's chip in place of the band type.
2. **Given** a labelled band, **When** the user drags its node, changes its type, gain, Q, slope
   or channel, or switches it off, **Then** the label stays with that band (dimmed while the band
   is off) and follows the node.
3. **Given** a labelled band, **When** the project is saved and reopened, **Then** the label comes
   back on the same band.
4. **Given** a labelled band, **When** the user saves the EQ as a preset and loads that preset
   onto another EQ-8, **Then** the label comes with it. **When** they load a factory preset or
   Init, **Then** the labels are those of that preset (none for factory presets).
5. **Given** a labelled EQ-8, **When** the user duplicates the device or the track, or copies it
   to another track, **Then** the copy has the same labels.
6. **Given** a band label field, **When** the user clears it, **Then** the node shows no label and
   the chip shows the band type again.
7. **Given** a label longer than 24 characters, **Then** the field stops accepting more; leading
   and trailing spaces are trimmed and line breaks are not allowed.

---

### User Story 2 - Write a longer note on a band (Priority: P2)

The user wants to remember why: "Narrow cut for the ring on sustained notes. Don't go past −6 dB
or it gets hollow." They click the band's label in the graph and a note box opens beside it;
they type the note and click away.

**Why this priority**: Notes are the "expandable" half of the request; useful, but a label alone
already answers "what is this band?".

**Independent Test**: Click a label in the graph, type a multiline note, close it, reopen the
project and click the label again to read the note.

**Acceptance Scenarios**:

1. **Given** a labelled band, **When** the user clicks its label in the graph, **Then** a note
   popup opens next to the label with the band's label (editable) and a multiline note field, and
   the band becomes the selected band.
2. **Given** the note popup, **When** the user types and then presses Escape, clicks outside or
   presses Ctrl/Cmd+Enter, **Then** the popup closes and the text is kept.
3. **Given** a band with a note, **Then** its label in the graph shows a small note mark, and
   hovering the label shows the first lines of the note as a tooltip.
4. **Given** a band with a note but no label, **Then** the graph shows the note mark alone next to
   the node, so the note can still be opened.
5. **Given** the band panel, **Then** it has a "Note" button that opens the same popup for the
   selected band, with or without a label, so the popup can be reached with the keyboard.
6. **Given** a note up to 2000 characters, **Then** it is saved and restored exactly (line breaks
   included); the field stops accepting more past that.

---

### User Story 3 - Keep the graph readable (Priority: P2)

Two labelled bands sit close together around 200 Hz. Their labels don't overlap. When the user
is doing detailed curve work they hide all labels with one switch, and show them again later.

**Why this priority**: Without collision handling and a hide switch, labels get in the way on
busy curves.

**Independent Test**: Label three bands within half an octave of each other and see three
readable, non-overlapping labels; switch Labels off and see none; switch them on again.

**Acceptance Scenarios**:

1. **Given** labels that would overlap, **Then** the graph moves them apart vertically (above or
   below their nodes, then further out) so no two labels overlap and each one stays connected to
   its node by position or a thin leader line.
2. **Given** a label near the graph's edge, **Then** it stays fully inside the graph.
3. **Given** the Labels switch in the EQ-8's analyzer bar, **When** the user turns it off,
   **Then** no labels or note marks are drawn in the graph (chips and the band panel still show
   them); turning it on brings them back. The switch is saved with the device and is on by
   default.
4. **Given** labels shown, **Then** dragging a node, double-clicking a node or empty space, the
   wheel over a node and the right-click menu work exactly as before; only a click on a label
   itself opens the note.

### Edge Cases

- **New band from empty space**: double-clicking empty space turns on a free (off) band as a
  bell. If that band still has a label or note from earlier, they are cleared, since it is now a
  new band.
- **Reset / Init**: loading Init or a factory preset replaces labels and notes with that preset's
  (none). Resetting a single band's parameters (double-click on a knob) leaves its label alone.
- **Older projects and presets**: an EQ-8 saved before this change opens with no labels and
  Labels on; nothing else changes.
- **Undo**: a label or note edit is one undo step, committed when the field loses focus or the
  popup closes, not one step per keystroke.
- **Automation, MIDI learn, A/B**: labels are not parameters and can't be automated or learned.
  A/B compare swaps parameter values only and leaves labels as they are.
- **Text content**: any Unicode text is allowed, including Cyrillic; control characters other
  than line breaks (notes only) are dropped.
- **Narrow card / small graph**: when a label can't be placed without overlapping after moving
  it apart, it is shortened with an ellipsis; the full label is in the tooltip.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each of the EQ-8's eight bands MUST have an optional label (up to 24 characters,
  one line) and an optional note (up to 2000 characters, multiline), tied to the band's slot so
  they follow it through every parameter change.
- **FR-002**: Labels, notes and the Labels switch MUST be saved and restored with the project, with
  user presets, and carried by device copy, device duplicate and track duplicate.
- **FR-003**: Labels and notes MUST NOT change the audio, automation or latency, and MUST NOT be
  parameters; the EQ's processing MUST be identical with and without them.
- **FR-004**: The graph MUST draw each band's label next to its node, avoid overlaps between labels
  and keep them inside the graph, and draw a note mark for bands with a note.
- **FR-005**: Clicking a label or note mark MUST open a note popup for that band with editable
  label and note; the band panel MUST have a Label field and a Note button for the selected band.
- **FR-006**: A band chip MUST show the band's label (shortened to fit) in place of its type when
  it has one.
- **FR-007**: A Labels switch MUST hide or show all labels and note marks in the graph; it is a
  display-only device parameter like Peak (saved with the device, on by default).
- **FR-008**: A label or note edit MUST be a single undo step.
- **FR-009**: Turning on a free band from empty space MUST clear any label and note left on it.
- **FR-010**: The engine MUST expose the labels and notes to MCP through the EQ-8's device text,
  so an agent can read what each band is for.
- **FR-011**: The user guide (EN and RU) MUST describe band labels and notes.

### Key Entities

- **Band annotation**: label and note text for one EQ-8 band slot (1–8).
- **EQ-8 annotations**: the eight band annotations plus the Labels switch, saved with the device.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A project with labels and notes on all eight bands reopens with every label and note
  byte-identical.
- **SC-002**: Rendering a test signal through an EQ-8 with and without annotations gives
  bit-identical output.
- **SC-003**: With all eight bands labelled and packed within one octave, no two labels overlap in
  the graph at the card's normal size.
- **SC-004**: Naming a band takes no more than a click and typing (select the band, type the label);
  opening its note is one click on the label.

## Assumptions

- Scope is EQ-8 (device kind 0), the EQ with a node graph and eight fixed band slots. EQ-3 has no
  nodes, and Dynamic EQ-8 can reuse this later with the same design.
- An EQ-8 inside an Audio Effect Rack chain keeps its params only (like every chain device
  today), so its card shows no Label field, Note button or Labels switch.
- Bands are fixed slots in EQ-8, so "renumbering" doesn't happen; the slot is the band's identity.
- Labels are a device setting, not a parameter, so they ride on the device's saved state rather
  than the parameter list.
- Touch editing is whatever Avalonia gives text fields and taps on the platforms Nota ships on.
