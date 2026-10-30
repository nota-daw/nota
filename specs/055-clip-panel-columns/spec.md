# Feature Specification: Two-column clip inspector and MIDI tools

**Feature Branch**: `cursor/clip-panels-two-columns-edc4`

**Created**: 2026-10-30

**Status**: Implemented

**Input**: nota-daw/nota#276: "The clip editor's side panels are a single narrow column that
scrolls vertically … Lay out the inspector and the MIDI tools panel in two columns, so most or all
sections fit without scrolling. Make the layout adaptive: two columns when there is room, one
column when the editor area is narrow, and vertical scrolling only as a last resort when even two
columns don't fit the height. Sections stay whole, keep a stable order, and the change applies to
the MIDI clip editor, the audio clip editor and MIDI tools in the same way."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reach every inspector control without scrolling (Priority: P1)

A producer opens a MIDI clip in the detail area, which is about 320 px tall. Today Clip and
Quantize are visible and Transpose and Selection sit below the fold. With this change the
inspector widens to two columns, Clip and Quantize on the left and Transpose and Selection on
the right, and nothing scrolls.

**Independent Test**: Open a MIDI clip in a short, wide editor: the inspector shows all four
sections in two columns and has no scrollbar. Make the editor tall: the inspector goes back to
one 232 px column.

**Acceptance Scenarios**:

1. **Given** the inspector's sections fit the editor's height in one column, **Then** it stays
   one 232 px column, as today.
2. **Given** they don't fit and the editor can give the inspector a second column while the piano
   roll / waveform keeps at least 480 px, **Then** the inspector shows two columns of the same
   width side by side (464 px in all).
3. **Given** they don't fit and the editor is too narrow for a second column, **Then** the
   inspector stays one column and scrolls.
4. **Given** two columns and they still don't fit the height, **Then** the inspector scrolls
   vertically (both columns together).
5. **Given** any layout, **Then** a section is never split across columns, the first column holds
   the leading sections and the second the rest in their usual order, and the split is the one
   that leaves the taller column shortest.
6. **Given** the inspector's width changes, **Then** the header's clip cell (colour bar, name,
   kind badge) keeps the inspector's width, so the hairline under it lines up.
7. **Given** the hint line, **Then** it stays pinned under the sections across the panel's full
   width.

### User Story 2 - The same for the audio editor and MIDI tools (Priority: P1)

The audio clip editor's inspector (Clip, Playback, Warp; ADSR in ADSR mode) and the MIDI tools
panel (the Generate / Transform tool list, the tool's parameters, Output) follow the same rules.
The tools panel's column is 272 px; its result line, Apply and Revert / Defaults stay pinned at
the foot across the panel's width.

**Acceptance Scenarios**:

1. **Given** the audio editor, **Then** its inspector behaves as in User Story 1. In ADSR mode
   its single section is one column.
2. **Given** the MIDI tools panel is open, **Then** it takes a second column under the same rules,
   after the inspector has chosen its own: the inspector leaves room for a one-column tools panel
   when it decides, and the tools panel decides from the width that remains.
3. **Given** a section changes height (the selection card appears, a tool with more parameters is
   picked), **Then** the layout is re-chosen.

### Edge Cases

- A panel with a single section never takes a second column.
- Infinite height (a panel measured without a height limit) is always one column.
- Hidden sections take no part in the split.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The clip inspector (MIDI and audio) and the MIDI tools panel MUST use one column
  while their sections fit the available height.
- **FR-002**: They MUST use two columns of the one-column width when one column would scroll and
  the width they are offered, less what the editor canvas keeps (480 px, plus a one-column tools
  panel for the inspector while Tools is open), holds two columns.
- **FR-003**: They MUST scroll vertically only when the chosen layout doesn't fit the height.
- **FR-004**: Sections MUST stay whole and in order; column one holds a leading run of them.
- **FR-005**: The split MUST minimise the taller column; on a tie the first column takes more.
- **FR-006**: The header's clip cell MUST keep the inspector's width.

## Success Criteria *(mandatory)*

- **SC-001**: In a MIDI editor 320 px tall (header included) and 1400 px wide, all four inspector
  sections are visible without scrolling. (Measured in the headless app: the two-column inspector
  needs an editor about 310 px tall; 300 px, the figure first written here, was too short.)
- **SC-002**: In a tall editor the panels are pixel-for-pixel today's single column.
- **SC-003**: The layout choice is covered by smoke-test cases (`--clipeditor`).

## Assumptions

- Two columns are used only when one would scroll: when everything already fits, the roll keeps
  the width. The 480 px canvas reserve is a tunable constant (`ClipEditorKit.MinCanvasW`).
- No persisted setting: the layout follows the editor's size.
