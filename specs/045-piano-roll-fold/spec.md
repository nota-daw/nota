# Feature Specification: Piano roll Fold

**Feature Branch**: `feat/piano-roll-fold`

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "Piano roll Fold (nota-daw/nota#344, #345): one Fold toggle in the
piano roll. On a melodic track with a scale on, it hides every out-of-scale row. On a Drum Rack
track, it shows only the pads, each row named after its pad. Notes on hidden pitches stay visible.
Drawing, dragging and arrow-key transposing move between visible rows only. Remembered per
track."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fold a melodic clip to its scale (Priority: P1)

A producer writes a bassline in A minor. With the scale on, out-of-scale rows are only dimmed, so
most of the keyboard is rows they never use. They press **Fold**. The keyboard now shows only the
scale's notes, stacked without gaps, and every note they draw or drag lands in key.

**Why this priority**: #344 is the request with the widest audience, since every melodic track can
use it. It also builds the row mapping that the Drum Rack mode reuses.

**Independent Test**: Turn on a scale, press Fold, and check that only in-scale rows are drawn.
Then check that a drawn note, a dragged note and an arrow-key transpose all land on in-scale
pitches.

**Acceptance Scenarios**:

1. **Given** a MIDI clip on an instrument track with the scale set to A minor, **When** the user
   presses Fold, **Then** the keyboard shows only A, B, C, D, E, F and G rows across the range,
   with no gaps, and the scroll position keeps the notes that were on screen in view.
2. **Given** Fold on, **When** the user drags a note up by one row, **Then** it moves to the next
   in-scale pitch (for example E to F, a semitone, and F to G, a whole tone).
3. **Given** Fold on, **When** the user presses ↑ with notes selected, **Then** each note moves to
   the next visible row. **When** they press ⇧↑, **Then** each moves up an octave (same scale
   degree).
4. **Given** a clip with an F♯ note and the scale set to A minor, **When** Fold is on, **Then** the
   F♯ row stays visible and is marked as out of scale, so no note is hidden.
5. **Given** Fold on, **When** the user turns the scale off or presses Fold again, **Then** the full
   chromatic keyboard comes back with the same notes in view.

---

### User Story 2 - Fold a Drum Rack clip to its pads (Priority: P1)

On a Drum Rack track, the piano roll shows a full keyboard (C1, C♯1, D1…), and the user has to
remember which note plays which pad. They press **Fold**. The keyboard shows one row per pad that
holds a sound, each named after its pad ("Kick", "Snare", "Hat closed"). Clicking a row's key plays
that pad.

**Why this priority**: #345 makes the piano roll usable for drums. It shares the button and the
row mapping with Story 1.

**Independent Test**: On a Drum Rack track with a kit loaded, press Fold. Check that the rows equal
the kit's pads, each named after its pad, and that clicking a key auditions it.

**Acceptance Scenarios**:

1. **Given** a Drum Rack track with a 16-pad kit, **When** the user presses Fold, **Then** the
   piano roll shows 16 rows, lowest note at the bottom, each key labelled with the pad's name.
2. **Given** Fold on, **When** the user clicks a row's key, **Then** that pad sounds, as with an
   unfolded key.
3. **Given** a clip with a note on a pitch that has no pad, **When** Fold is on, **Then** that
   pitch keeps an unnamed row (its note name, marked as empty), so the note stays visible.
4. **Given** Fold on, **When** the user renames a pad or drops a new sample on it, **Then** the row
   label updates without reopening the clip.
5. **Given** Fold on, **When** a pad is added to or removed from the rack, **Then** the rows follow
   the rack.

---

### User Story 3 - Fold is remembered (Priority: P2)

The user folds a drum track, edits, switches to another clip on the same track, and later reopens
the project. The drum track's clips open folded; a melodic track they never folded opens
unfolded.

**Why this priority**: Without memory, Fold has to be pressed again on every clip, which is the
friction #344 and #345 ask to remove. It's still usable without it, so it comes after the two
modes.

**Independent Test**: Fold one track, open another clip on that track, and check it opens folded.
Save, reopen, and check again. Check that a clip on another track opens unfolded.

**Acceptance Scenarios**:

1. **Given** Fold on for a track, **When** the user opens another clip on that track, **Then** it
   opens folded.
2. **Given** Fold on for a track, **When** the project is saved and reopened, **Then** that track's
   clips open folded.
3. **Given** a project saved before this feature, **When** it is opened, **Then** every track
   opens unfolded.

---

### Edge Cases

- **Fold with no scale on a melodic track**: Fold folds to the pitches the clip already uses (each
  row a used pitch). This is useful for editing a finished part and matches Ableton's Fold. A new
  note can still only be drawn on an existing row.
- **Fold on with an empty clip and no scale**: There are no rows to show. Fold shows the scale rows
  if a scale is on. Otherwise the button is available but folding shows the full keyboard with a
  hint ("Set a scale or add notes to fold"). [Assumption: the hint wording follows `nota-design`
  copy rules.]
- **Drum Rack with no pads**: same as an empty clip; the full keyboard stays with a hint.
- **Two pads on the same note**: one row, named "Kick / Kick 2" (both names), since one note
  triggers both.
- **Instrument Rack (not Drum Rack)**: treated as melodic (scale folding), as its chains share a
  key range rather than one note each.
- **A note dragged past the top or bottom visible row**: it stops at the last visible row, as an
  unfolded drag stops at the pitch range.
- **Velocity, expression and clip-strip lanes**: unaffected; they are drawn per note, not per row.
- **Selection rectangle and lasso**: select by visible rows; hidden pitches have no notes, so
  nothing hidden can be selected.
- **Scale changed while folded**: the rows re-fold to the new scale, keeping the view centred on
  the same notes.
- **MIDI recording while folded**: recorded notes land on their played pitch; an out-of-scale
  recorded pitch gets its own marked row, like any other out-of-scale note.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The piano roll toolbar MUST have one **Fold** toggle, shown for every MIDI clip.
- **FR-002**: On a track whose instrument is a Drum Rack, Fold MUST show one row per note that
  has a pad, each labelled with the pad's name. On any other instrument track, Fold MUST show one
  row per in-scale pitch when a scale is on, and one row per used pitch when no scale is on.
- **FR-003**: With Fold on, every pitch that has a note in the clip MUST keep a row, even when it is
  out of scale or has no pad. Such rows MUST be marked (out of scale, or no pad) so they read
  differently from the folded set.
- **FR-004**: Drawing, moving, resizing-then-moving, pasting, and the arrow-key transpose (↑/↓ one
  row, ⇧↑/⇧↓ an octave) MUST move between visible rows only while Fold is on.
- **FR-005**: Clicking a key MUST audition that row's pitch, folded or not.
- **FR-006**: Folded rows MUST follow changes live: scale or key changed, pads renamed, added or
  removed, and notes added on a new pitch.
- **FR-007**: Turning Fold off MUST restore the full keyboard with the same notes in view.
- **FR-008**: The Fold state MUST be remembered per track and saved with the project. Projects
  saved before this feature MUST open unfolded, without a project format bump.
- **FR-009**: Undo MUST be unaffected by Fold itself. Toggling Fold is a view change, not an edit,
  and an edit made while folded undoes exactly as it would unfolded.
- **FR-010**: The note grid, playhead, loop markers and the time ruler MUST behave the same folded
  and unfolded; only the vertical layout changes.
- **FR-011**: Fold MUST be reachable from the keyboard. [Assumption: an unused shortcut is picked
  in the plan and added to the shortcuts pane through the `nota-shortcuts` workflow.]
- **FR-012**: The MCP server MUST be able to read and set a track's Fold state, as it can for other
  per-track view settings. [Assumption: `get_piano_fold` / `set_piano_fold`, or a field on an
  existing per-track tool.]

### Key Entities

- **Fold state**: on or off, one per track, stored with the project.
- **Row map**: the ordered list of visible pitches for the clip on screen, each with a label (note
  name or pad name) and a mark (in set, out of scale, no pad). It is derived from the scale, the
  rack's pads and the clip's notes, and is never stored.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a 7-note scale on the piano roll's 88-key range, Fold shows no more than 52 rows,
  plus one row per out-of-scale pitch used in the clip.
- **SC-002**: With a 16-pad kit, Fold shows exactly 16 rows, plus one per used pitch with no pad.
- **SC-003**: In a folded clip, 100% of notes drawn, dragged or arrow-transposed land on a visible
  row's pitch, which smoke tests check against the row map.
- **SC-004**: Toggling Fold or changing the scale while folded keeps every note that was on screen
  still on screen.
- **SC-005**: Folding a clip of 2,000 notes redraws in under one frame (16 ms) on the CI machines.

## Assumptions

- "Remembered per clip / per track" in #344 is read as **per track**, the reading Chris was given in
  the milestone plan. Every clip on a drum track opens folded.
- Fold with no scale folds to the used pitches (Ableton's behaviour), rather than greying out the
  button.
- The pad name is the rack chain's own name when it has one (a kit voice or a dropped sample),
  otherwise its instrument's name, as the Drum pattern view already shows.
- Only the piano roll folds. The Drum pattern view already shows pads, and the arrangement's clip
  previews stay as they are.
- No engine change is needed: the rows are a view mapping, and pad names and trigger notes are
  already exposed.
- The existing global scale setting (shared across clips) stays as it is; only Fold is per track.
