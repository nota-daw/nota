# Feature Specification: Multi-Clip Editing in the Clip Editor

**Feature Branch**: `feat/multi-clip-editing`

**Created**: 2026-10-20

**Status**: Draft

**Input**: User description: "Clip editor: edit several clips at once (GitHub issue nota-daw/nota#48). Users want to edit notes across several MIDI clips at once — e.g. fix a bass line split into several clips, or write a melody against the chords in another clip. Select several MIDI clips (on one track or several) and open them together in the clip editor: all their notes are shown and editable on the shared timeline, with the active clip in full colour and the others either editable or dimmed for reference (as in Ableton Live 11+ multi-clip editing or Bitwig). Today the clip editor shows exactly one clip. Related: #49 (track mode)."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The engine is expected to be untouched: each clip's notes are still committed through the
existing per-clip note path. Whether several per-clip commits can form one undo step is a
plan-phase question (FR-011).

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Current state (for reference)**:

- The arrangement already supports selecting several clips (click, Shift/Cmd-click,
  rubber band). Double-clicking a MIDI clip opens the Clip editor for **that one clip**,
  whatever else is selected.
- The piano roll works in the clip's own beats (0 … clip length): the ruler, grid, notes,
  velocity lane, expression lanes, clipboard, overwrite rule (spec 007), length/velocity
  keys and the clip-tools panel all act on one note list. The inspector shows that clip's
  start, length and loop.
- Session-view slots open the same editor for one slot.

## Clarifications

### Session 2026-10-20

- Q: How do open clips other than the active one behave by default? → A: All open clips
  are editable; the active one is full colour, the others subdued; any non-active clip can
  be switched to reference (dimmed, read-only).
- Q: What happens when a note is dragged past its clip's edge into another open clip of
  the same track? → A: A note whose start lands inside another open, editable clip of the
  same track moves into that clip (one undo step); with no clip there it stops at the
  edge as today. Notes never move between tracks.
- Q: How does a set of clips get into the editor, and does it follow later arrangement
  selection changes? → A: Fixed at opening (double-click on a selected clip, or "Edit
  selected clips"); later selection changes leave the editor as it is until it is opened
  again. *(Reversed after review on 2026-10-20 — see below.)*
- Q: Where does the user see the open clips and switch the active clip or reference
  mode? → A: A clip strip above the note grid (under the ruler): one bar per clip in its
  track colour over its time span; click makes it active, a lock icon toggles reference.

### Review 2026-10-20 (bug06.nota)

- Q: Should the editor follow the arrangement selection after all? → A: Yes. While the
  Clip editor is on screen, selecting two or more MIDI clips shows them together at once
  (the clicked clip active when it is one of them); narrowing to one clip shows it alone.
  This replaces the "fixed at opening" answer above.
- Q: Where do ⌘D copies go when the selection's span runs past its clip's end? → A: Where
  the shift puts them: copies that start in the next open clip of the track land in it (the
  move rule, FR-006); copies past every open clip are dropped, never piled up at the edge.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open several clips together and see them on one timeline (Priority: P1)

A user selects three consecutive bass clips on one track and opens them in the Clip
editor. The piano roll shows all their notes laid out on the arrangement's timeline, each
clip's span marked, so the bass line reads as one continuous part. One clip is the
active clip (the one the user double-clicked) and is drawn in full colour.

**Why this priority**: Seeing the clips together is the foundation of every other story
and already answers the "write against the chords" use case visually.

**Independent Test**: Select two MIDI clips at different positions, double-click one;
the editor shows both clips' notes at their arrangement positions, the ruler shows
arrangement bars, and the double-clicked clip is the active one.

**Acceptance Scenarios**:

1. **Given** clips A (bars 1–4) and B (bars 5–8) on one track are selected, **When** the
   user double-clicks B, **Then** the editor shows A's and B's notes on a shared
   timeline covering bars 1–8, B is the active clip, and the view scrolls to B.
2. **Given** clips on two different tracks are selected, **When** the editor opens,
   **Then** each clip's notes are drawn in its own track's colour and the clip spans are
   visible behind the grid.
3. **Given** several clips are selected, including audio clips, **When** the user
   double-clicks a selected MIDI clip, **Then** only the MIDI clips open; audio clips are
   ignored.
4. **Given** one clip is selected, or the user double-clicks a clip that is not part of
   the selection, **When** the editor opens, **Then** it behaves exactly as today
   (single clip) — no change for users who never select several clips.
5. **Given** the Clip editor is on screen, **When** the user selects two or more MIDI
   clips in the arrangement (⇧-click, ⌘-click, rubber band, select all), **Then** the editor
   shows them together at once; **When** the selection narrows to one clip, **Then** it
   shows that clip alone.
6. **Given** a multi-clip editor is open, **When** the editor header is read, **Then** it
   names the active clip and shows how many clips are open (e.g. "Bass 2 · +2 clips").

---

### User Story 2 - Edit notes across all open clips (Priority: P1)

The user fixes a bass line split over several clips without switching clips: they select
notes in different clips with one rubber band, transpose, nudge, quantize or delete them
together, and draw new notes wherever a clip is.

**Why this priority**: The core request — editing, not just viewing, across clips.

**Independent Test**: Open two clips together, rubber-band notes spanning both, press ↑;
every selected note in both clips moves up a semitone, and one Undo reverts all of them.

**Acceptance Scenarios**:

1. **Given** two open clips, **When** the user rubber-bands across both, **Then** notes
   from both clips are selected, and Select All selects the notes of every editable clip.
2. **Given** a selection spanning two clips, **When** the user moves, resizes, transposes,
   nudges, changes velocity, quantizes or deletes it, **Then** each note is changed in its
   own clip, and a single Undo reverts the whole edit in every clip.
3. **Given** the user draws a note inside clip B's span, **Then** the note is added to
   clip B and B becomes the active clip; drawing where no open clip of the active
   track lies does nothing.
4. **Given** clips A (bars 1–4) and B (bars 5–8) on one track, **When** a note of A is
   dragged so that it starts in bar 5, **Then** it moves into clip B (keeping its
   arrangement position), and one Undo puts it back in A.
4a. **Given** a note of clip A is dragged past A's end where no open clip of that track
   lies (a gap, or the next clip is not open or is a reference), **Then** it stays in A
   and stops at A's edge as today.
4b. **Given** clips on two tracks, **When** a note is dragged vertically or over the other
   track's clip span, **Then** it never moves to the other track's clip.
5. **Given** two open clips on the same track overlap in time, **When** a note is drawn in
   the overlapping area, **Then** it goes to the active clip.
6. **Given** clicking a note of a non-active clip, **Then** that clip becomes the active
   clip (full colour, inspector and header follow) and the note is selected.
7. **Given** notes were copied in a multi-clip editor, **When** pasted, **Then** each copy
   lands on top of its original in the original's clip (spec 007 paste rule); notes
   copied from a clip that is not open land in the active clip at their clip-relative
   beats.
8. **Given** a placed note overlaps a same-pitch note **of the same clip**, **Then** the
   spec 007 overwrite rule applies; notes of other clips are never cut or removed by it.

---

### User Story 3 - Keep some clips as read-only reference (Priority: P2)

Writing a melody against the chords, the user wants the chord clip visible but not
accidentally edited. They mark a clip as reference: its notes are drawn dimmed, cannot be
selected or changed, and are skipped by Select All and rubber band.

**Why this priority**: The issue asks for "editable or dimmed for reference"; editing all
clips (Story 2) delivers value on its own, the reference mode refines it.

**Independent Test**: Open a melody clip and a chord clip, mark the chord clip as
reference, rubber-band over everything; only melody notes are selected, and chord notes
are drawn dimmed.

**Acceptance Scenarios**:

1. **Given** a multi-clip editor, **When** the user clicks the lock on a clip's bar in
   the clip strip, **Then** the clip becomes a reference:
   its notes are drawn dimmed and ignore clicks, rubber band, Select All and every edit.
2. **Given** the active clip, **When** the user tries to mark it as reference, **Then**
   it is not allowed (the active clip is always editable); marking another clip active
   first is required.
3. **Given** a reference clip, **When** the user toggles it back, **Then** its notes are
   editable again.
4. **Given** reference clips, **Then** they still play back normally — reference is only
   an editing lock, not a mute.

---

### User Story 4 - Inspector, envelopes and tools in multi-clip mode (Priority: P3)

The panels around the roll keep working: the inspector shows the active clip's start,
length and loop; the Envelopes tab edits the active clip's envelope; clip-tools and the
Selection read-out act on the selected notes wherever they are; the velocity and
expression lanes show the notes of all editable clips.

**Why this priority**: Needed for a complete, unsurprising editor, but each panel already
works on one clip; extending them is polish over Stories 1–2.

**Independent Test**: With two clips open, switch the active clip by clicking a note and
check that the inspector's Start/Length change to that clip; change velocities in the
velocity lane for notes of both clips.

**Acceptance Scenarios**:

1. **Given** the active clip changes, **Then** the inspector (Start, Length, Loop) and the
   Envelopes tab switch to that clip.
2. **Given** selected notes in two clips, **When** a clip-tool (e.g. quantize, humanize)
   is applied, **Then** it acts on the selected notes in both clips as one undo step.
3. **Given** the velocity lane, **Then** it shows a bar for every note of every editable
   clip, in its track colour, and editing a bar changes that note in its own clip.
4. **Given** the Scale overlay, **Then** it applies to the editor as a whole (one key and
   mode), as today.

### Edge Cases

- An open clip is deleted, moved, resized or undone away while the editor is open: that
  clip drops out of the editor (or follows its new position); if the active clip goes,
  the next open clip becomes active; if none remain, the editor closes as today.
- An open clip is moved in the arrangement: its notes follow it on the shared timeline.
- Two open clips of one track overlap in time and a moved note starts in the overlap: it
  stays in its current clip if that clip contains the start, otherwise it goes to the
  active clip, otherwise to the clip that starts later.
- A selection whose notes would move into several different clips is allowed; each note
  lands in the clip containing its new start, and notes that would land in a gap stop the
  whole selection at the edge, keeping its shape (as moves stop at the clip edge today).
- Clips far apart (e.g. bar 1 and bar 200): the timeline covers the full span; zoom-to-fit
  fits all open clips; gaps between clips are drawn as outside-clip area.
- Clips on several tracks with the same pitches at the same time: notes overlap visually;
  the active clip's notes are drawn on top.
- Note audition plays into the track of the note being placed or clicked.
- The ruler seeks in arrangement beats (no clip-local conversion needed).
- Deactivated clips open like any other; their notes are drawn as deactivated clips are
  today.
- Session-view slots cannot be combined with arrangement clips; multi-clip applies to
  the arrangement only.
- Recording into a track while its clip is open in a multi-clip editor behaves like the
  single-clip editor does today.
- The placement settle rule (spec 007) still applies: switching the active clip, closing
  the editor or saving settles the pending placement in every clip it touches.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Double-clicking a MIDI clip that is part of a selection of two or more MIDI
  clips MUST open all selected MIDI clips together in the Clip editor, with the
  double-clicked clip active. A menu/command "Edit selected clips" MUST do the same, with
  the first selected clip in time active.
- **FR-001a**: While the Clip editor is on screen, it MUST follow the arrangement
  selection: two or more selected MIDI clips open together (the clicked clip active when
  it is one of them, else the first in time); a selection of one MIDI clip shows that clip
  alone; a selection without MIDI clips leaves the editor as it is.
- **FR-005a**: Duplicate (⌘D) MUST shift the copies by the selection's span and place each
  where the shift lands it — in its own clip or, past the seam, in the next open clip of
  its track (as FR-006); a copy beyond every open clip MUST be dropped, never clamped to
  the clip's edge. This also applies to the single-clip editor.
- **FR-002**: Opening a single clip, or a clip outside the selection, MUST behave exactly
  as the current single-clip editor.
- **FR-003**: In a multi-clip editor the roll's horizontal axis MUST be arrangement time:
  ruler, grid, bar numbers and seek use arrangement beats; each open clip's span MUST be
  drawn, the area outside all spans drawn as outside-clip.
- **FR-004**: The active clip's notes MUST be drawn in full colour; other editable clips'
  notes MUST be drawn in their track colour but visibly subdued; reference clips' notes
  MUST be drawn dimmed further. The active clip MUST be distinguishable in the header.
- **FR-005**: Selection, rubber band, Select All, move, resize, transpose, nudge,
  length/velocity keys, delete, duplicate, quantize and the clip-tools MUST act on
  selected notes across all editable clips; each changed note stays in its own clip.
- **FR-006**: A note belongs to the clip that contains its start. When a move puts a
  note's start inside another open, editable clip of the same track, the note MUST move
  into that clip, keeping its arrangement position; this is part of the same undo step as
  the move. Where no such clip lies, the move MUST stop at the note's clip edge as today.
  Note ends follow the same limits as in the single-clip editor, measured against the
  clip that contains the note; resizing never moves a note between clips; notes never
  move between tracks. While a moved selection is still unsettled
  (spec 007), the move into another clip MAY be shown live but the overwrite rule in the
  destination clip applies only on settle.
- **FR-007**: New notes (draw, paste of foreign notes) MUST go to the active clip, or —
  when drawn inside the span of another open clip of the active clip's track — to that
  clip, which then becomes active. Drawing outside every open clip of that track MUST do
  nothing.
- **FR-008**: A multi-clip editor MUST show a clip strip between the ruler and the note
  grid: one bar per open clip, in its track colour, spanning the clip's time range and
  labelled with the clip name; the active clip's bar is marked as chosen. Clicking a bar
  MUST make that clip active; clicking or selecting a single note of a non-active clip
  MUST do the same. The header, inspector and Envelopes tab MUST follow the active clip.
  Overlapping clips of different tracks stack in the strip. The strip is not shown in
  the single-clip editor.
- **FR-009**: The user MUST be able to mark any non-active open clip as reference
  (read-only) and back with a lock icon on its bar in the clip strip; reference clips' notes cannot be selected or edited and are
  excluded from Select All and rubber band. Reference state lasts for the editor session
  and is not saved in the project.
- **FR-010**: The overwrite rule (spec 007) MUST only apply between notes of the same
  clip.
- **FR-011**: Any single edit that changes notes in several clips MUST be one undo step,
  and one Undo MUST restore every affected clip.
- **FR-012**: Copy/paste MUST keep each copied note's clip: copies paste on top of their
  originals in the original clip when that clip is open; notes from a clip that is not
  open paste into the active clip at their clip-relative beats.
- **FR-013**: The editor MUST follow arrangement changes to its open clips (move, resize,
  delete, undo) without pushing edits to clips that no longer exist.
- **FR-014**: CHANGELOG, FEATURES.md, the user docs and (for any new shortcut or gesture)
  the Shortcuts pane MUST describe multi-clip editing.

### Key Entities

- **Open clip set**: the MIDI clips shown in one Clip editor — each with its track,
  arrangement start, length, track colour and an editable/reference flag; exactly one is
  active.
- **Active clip**: the clip that receives new notes and drives the header, inspector and
  envelopes.
- **Note (in multi-clip mode)**: as today (pitch, start, length, velocity, expression),
  plus the clip it belongs to (the open clip containing its start); its on-screen
  position is clip start + note start.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A bass line split over 4 clips can be transposed by a semitone in one
  selection + one key press, versus opening each clip separately (4× today).
- **SC-002**: One Undo after any cross-clip edit leaves every affected clip identical to
  its state before the edit, in 100% of cases.
- **SC-003**: Editing with 8 clips and 2,000 notes open stays as responsive as the
  single-clip editor with the same number of notes (drags follow the pointer without
  visible lag).
- **SC-004**: Users who never select more than one clip see no change in behaviour.
- **SC-005**: No edit in a multi-clip editor ever changes a reference clip or a clip that
  is not open.

## Assumptions

- Only arrangement MIDI clips take part; audio clips and Session slots are out of scope.
- Track mode (#49 — editing all clips of a track without selecting them) is a separate
  feature; this spec's open clip set is designed so #49 can reuse it.
- Notes move between clips only by dragging across a seam between open clips of one
  track; merging or splitting clips stays an arrangement edit.
- The Scale overlay, snap grid, zoom and audition settings remain editor-wide.
- Clip loop is fixed ("clips always loop" in the inspector); the roll shows each clip's
  own notes over its span, not looped repetitions.
- Reference state is per editor session and not persisted.
- The engine already supports per-clip note commits; grouping several commits into one
  undo step is resolved in the plan (managed-side grouping preferred, engine change only
  if unavoidable).

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [x] Audio-thread impact considered: none expected — note edits go through the existing per-clip commit path
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); a grouped-undo call, if needed, is added there
- [x] Smoke-test impact assessed: none unless the plan adds an engine undo-group call, which would then be covered by the smoke test
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned, plus FEATURES.md and user docs (nota-features / nota-docs); Shortcuts pane via nota-shortcuts if a binding is added
- [x] UI change → follows `DESIGN.md` (Studio design language): track colours carry the work, the orange accent only for the active/chosen clip
