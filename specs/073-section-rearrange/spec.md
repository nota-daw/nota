# Feature Specification: Rearrange the song by moving sections with their content

**Feature Branch**: `feat/section-rearrange`

**Created**: 2026-11-03

**Status**: Draft

**Input**: GitHub issue [#192](https://github.com/nota-daw/nota/issues/192) (approved): "When I
move a section, everything within that section moves with it — every audio clip, MIDI clip,
automation lane and other content keeps its relative timing — and the surrounding sections
shift as needed. Restructure a song (move a verse before a chorus) by moving whole sections
instead of selecting and dragging clips."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md), [`specs/016`](../016-device-param-section-undo/spec.md)
(sections are undoable project data), [`specs/058`](../058-section-automatic-names/spec.md)
(automatic section names follow position).

**Existing behaviour this builds on**: the Sections lane sits between the Overview and the
ruler. Dragging the empty lane draws a section; dragging a section's body moves **only the
section** (its name and span) to another whole bar — the clips under it stay where they
were; dragging an edge resizes it. Sections may leave gaps between them and may overlap.
The section menu loops it, selects its span as a time range across every track, duplicates
the section marker, or deletes it. Elsewhere the Arrangement already has whole-song time
edits that move content on every track and the master: **Insert silence** (⌘⇧I) and
**Duplicate time** (⌘⇧D) open a gap at a point, cutting clips that cross it and moving every
clip and automation point after it — but they do not move sections. The song has one tempo
and one time signature (no tempo or meter changes along the timeline).

## Clarifications

### Session 2026-11-03

- Q: Where can a dragged section be dropped — only on a boundary between sections (reorder,
  the rest closes up) or on any bar? → A: Only on section boundaries (before the first,
  between two neighbours, after the last); the others shift by the moved section's length and
  the song's length never changes. Free placement on any bar is not a content move.
- Q: Does a plain drag of a section's body move it with its content, or only the marker? →
  A: A plain drag moves it with its content; an Option/Alt drag moves only the marker (as
  today, to any whole bar).
- Q: What happens to song time not covered by any section (a gap) when a neighbouring section
  moves? → A: Only the moved section's own span travels; gaps and their content are ordinary
  song material and shift along with whatever lies between the old and new place, so the
  spacing between the other sections is preserved.
- Q: Are Duplicate and Delete a section *with its content* (rippling the rest) part of this
  issue? → A: Yes — the section menu gains **Duplicate with content** and **Delete with
  content**; the existing Duplicate / Delete keep acting on the marker only.
- Q: What happens when a section that overlaps another is moved, duplicated or deleted with
  content? → A: Content operations are unavailable for it: the drag falls back to a
  marker-only move, the with-content menu entries are disabled, and the status bar says why.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Drag a section to reorder the song (Priority: P1)

A producer has Intro · Verse · Chorus · Verse 2 laid out as sections over a full
arrangement. They grab the Chorus in the Sections lane and drop it before the Verse. The
song now plays Intro · Chorus · Verse · Verse 2: every clip, MIDI note and automation
point that was inside the Chorus moved with it, the Verse moved right by the Chorus's
length to make room, and nothing after the two swapped sections moved at all. One Undo
puts everything back.

**Why this priority**: this is the whole request — restructuring a song in one gesture
instead of selecting clips on every track and dragging them around.

**Independent Test**: build four sections with a distinct MIDI clip, audio clip and
automation ramp in each; drag the third before the second; play it — the third's content is
heard where the second used to start, the second follows directly, the fourth is untouched;
Undo restores the original layout exactly.

**Acceptance Scenarios**:

1. **Given** sections A · B · C back to back, **When** the user drags C and drops it before
   B, **Then** the order is A · C · B, C starts where B used to start, B starts right after
   C, and everything inside each section kept its position relative to its section's start.
2. **Given** the same song, **When** the user drags A and drops it after C, **Then** the
   order is B · C · A, B starts where A used to start, and A ends where C used to end.
3. **Given** a section moved, **When** the user plays the song, **Then** audio clips,
   MIDI clips, every track's automation, the master track's automation and the master volume
   automation are heard in the new order, with no gap or overlap where content was taken
   out or put in.
4. **Given** a completed move, **When** the user presses Undo once, **Then** all clips,
   automation and sections are exactly as before the drag; Redo repeats the move.
5. **Given** a move, **When** it finishes, **Then** content outside the span from the
   earliest to the latest affected position (before the first moved section and after the
   last) did not move.
6. **Given** a drag in progress, **When** the user presses Escape, **Then** the drag is
   abandoned and nothing changes.

---

### User Story 2 - See where the section will land while dragging (Priority: P1)

While dragging, the user sees where the section will go before letting go: an insertion
marker at the boundary it will drop on, and the other sections showing the order they will
take. Dropping back where it started changes nothing and records no undo step.

**Why this priority**: moving whole chunks of a song is destructive-looking; without a clear
preview users won't trust the gesture.

**Independent Test**: start dragging a section across the lane, watch the insertion marker
snap from boundary to boundary; drop it on its own place — Undo has nothing new.

**Acceptance Scenarios**:

1. **Given** a drag over the lane, **When** the middle of the dragged section passes another
   section's middle, **Then** the insertion marker jumps to that section's other edge and the
   preview order updates. (The dragged section's middle, not the pointer, decides — so where
   the section was grabbed doesn't change where it lands.)
2. **Given** a drag, **When** the user drops on the section's original place, **Then**
   nothing changes and no undo step is added.
3. **Given** a drag near the lane's left or right edge, **When** more of the song lies
   beyond the visible range, **Then** the view scrolls so any position can be reached.

---

### User Story 3 - Move the section marker alone (Priority: P2)

Sometimes the user only wants to re-mark where a section is (fix a section placed a bar too
early) without touching the music. They hold the marker-only modifier while dragging and the
section moves the way it does today — the clips stay put. The modifier is Option (macOS) /
Alt (Windows, Linux), the Arrangement's existing "free / off the grid" modifier.

**Why this priority**: today's behaviour is still needed for correcting section markers;
the content move must not take it away.

**Independent Test**: hold the modifier and drag a section by two bars — only the section
moved, no clip or automation changed; Undo restores it.

**Acceptance Scenarios**:

1. **Given** a section, **When** the user drags it with the marker-only modifier held,
   **Then** only the section's span moves (on whole bars), as before this feature.
2. **Given** a section, **When** the user drags one of its edges, **Then** it resizes as
   before — resizing never moves content.

---

### User Story 4 - Move a section without dragging (Priority: P3)

From the section's right-click menu (and the command palette, for the section at the
playhead) the user picks **Move earlier** or **Move later**, which swaps the section with
its neighbour along with all their content — the same result as dragging it one place over.

**Why this priority**: gives a precise, keyboard-reachable way to reorder, and an easy
path for testing and MCP use; the drag covers the main need.

**Independent Test**: on A · B · C, run Move later on A — the order is B · A · C with the
content following; Move earlier on the first section is disabled.

**Acceptance Scenarios**:

1. **Given** the first section, **When** its menu opens, **Then** Move earlier is disabled;
   for the last section Move later is disabled.
2. **Given** the playhead in no section, **When** the user runs the palette command, **Then**
   it says so and changes nothing.

---

### User Story 5 - Duplicate or delete a section with its content (Priority: P2)

The user wants a second chorus: they pick **Duplicate with content** in the Chorus's menu and
a copy of the section and everything under it appears right after it, pushing the rest of
the song later. Later they drop a bridge: **Delete with content** removes the section and its
content and pulls the rest of the song earlier to close the gap. Each is one undo step.

**Why this priority**: the same "work in whole sections" idea as moving; the move covers the
issue's main need, these round out the set.

**Independent Test**: on A · B · C, Duplicate with content on B gives A · B · B′ · C with B′
playing exactly like B and C starting B's length later; Delete with content on B (from the
original) gives A · C with C starting where B started; Undo restores each.

**Acceptance Scenarios**:

1. **Given** sections A · B · C, **When** the user picks Duplicate with content on B, **Then**
   a copy B′ with B's name and naming mode and a copy of all of B's content starts at B's
   end, and everything from B's end onward (C, gaps, content) moves later by B's length.
2. **Given** sections A · B · C, **When** the user picks Delete with content on B, **Then** B
   and all content in its span are removed and everything after B's end moves earlier by B's
   length; content before B is untouched.
3. **Given** either command, **When** the user presses Undo once, **Then** content and
   sections are exactly as before; the plain Duplicate / Delete entries still act on the
   marker only.
4. **Given** the playhead inside a section, **When** the user runs the command palette's
   Duplicate section with content / Delete section with content, **Then** it acts on that
   section; with none under the playhead it says so and changes nothing.

---

### Edge Cases

- **Clips crossing a section boundary**: a clip that starts before a moved section and ends
  inside it (or starts inside and ends after it) is cut at the boundary; the inside part
  moves with the section and the outside part stays, both keeping their audio/notes in
  time — the same cut rule as Insert silence / Duplicate time. A MIDI note crossing the
  boundary is shortened to the boundary in the part it starts in.
- **Automation at a boundary**: each lane keeps its value right at both cut points and at
  both join points, so the curve inside the moved section and the curve around where it
  was taken out play exactly as they did, without a jump that wasn't there before.
- **Gaps between sections**: time not covered by any section is not a section and cannot be
  dragged. Only the moved section's span travels; a gap and its content shift with the rest
  of the material between the old and new place, so the spacing between the other sections
  is preserved. Dropping "between X and Y" with a gap between them inserts right before Y's
  start (the gap stays after X); "after the last section" inserts at its end.
- **Overlapping sections**: a section that overlaps another can't be moved, duplicated or
  deleted with content — the drag falls back to moving the marker only, the menu entries
  are disabled, and the status bar / palette says why.
- **Only one section**: dragging it has no other place to go in the order; it behaves as a
  marker-only move.
- **Empty section** (no content under it): it still moves and reorders, the other sections
  and content shift as for any move.
- **Section starting at beat 0 / ending at the end of the song**: moves work the same; the
  song's total length never changes.
- **Playback running**: the move applies while playing without a click or dropout; the
  playhead stays at the same time position (it doesn't follow the moved content).
- **Loop region and time selection**: the loop region stays where it is; an active time
  selection is cleared by a move.
- **Session clips, scenes and anything not on the arrangement timeline** are untouched.
- **Locked automation** (the automation-lock toggle): automation still moves with its
  section — a section move is a whole-song edit like Insert silence.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Dragging a section's body in the Sections lane MUST move the section together
  with all arrangement content inside its span — audio clips, MIDI clips, every track's
  automation, the master track's content and automation and the master volume automation —
  keeping every item's position relative to the section's start.
- **FR-002**: A move MUST reorder the song: exactly the moved section's span is taken out
  and inserted at a section boundary, and everything between the old and new place shifts by the moved span's
  length to close the gap and make room. The song's total length MUST NOT change.
- **FR-003**: Drop targets MUST be section boundaries (before the first section, between
  two sections, after the last section) — never an arbitrary bar and never inside another
  section; the drag MUST show an insertion marker and the resulting section order before
  release.
- **FR-004**: Clips crossing a moved section's edges MUST be cut at those edges; the parts
  inside move with it, the parts outside stay, with audio and notes still in time.
- **FR-005**: Automation MUST read the same value just before and just after every cut and
  join it creates as it did at the corresponding place before the move.
- **FR-006**: Sections themselves MUST move with the content: the moved section and the
  sections it passes take their new positions, keeping their names and naming mode
  (automatic names renumber by position, specs/058).
- **FR-007**: Each completed move MUST be exactly one undo step covering content and
  sections together; a drop that changes nothing MUST NOT add an undo step; Escape during a
  drag MUST cancel it.
- **FR-008**: Holding Option/Alt (the marker-only modifier) while dragging a section MUST move just the
  section, as today; resizing a section's edges MUST never move content.
- **FR-009**: A section that overlaps another section MUST NOT move, duplicate or delete
  content: its drag MUST fall back to a marker-only move, its Move earlier / Move later /
  Duplicate with content / Delete with content entries MUST be disabled, and the status bar
  (or the palette, for a palette command) MUST say why. Other sections still move with
  content; overlapping sections between the old and new place shift intact, and no drop
  target is offered inside an overlap.
- **FR-010**: The section menu MUST offer **Move earlier** / **Move later** (swap with the
  previous / next section, with content), disabled at the ends; the command palette MUST
  offer the same for the section at the playhead.
- **FR-011**: A move, duplicate or delete with content during playback MUST NOT interrupt
  audio (no dropout or click from the edit itself) and MUST NOT move the playhead.
- **FR-012**: The loop region MUST stay where it is; an active arrangement time selection
  MUST be cleared by a move.
- **FR-013**: The result MUST be saved with the project and survive save / reopen.
- **FR-014**: MCP clients MUST be able to move a section (by index, to a target boundary
  index) with the same result as the drag.
- **FR-015**: The section menu and the command palette (for the section at the playhead)
  MUST offer **Duplicate with content**: insert a copy of the section and all content in its
  span right after it, moving everything from its end onward later by its length, cutting
  clips that cross its edges as a move does.
- **FR-016**: The section menu and the command palette MUST offer **Delete with content**:
  remove the section and all content in its span, cutting clips that cross its edges, and
  move everything after it earlier by its length.
- **FR-017**: Duplicate with content and Delete with content MUST each be one undo step, keep
  automation values continuous at every cut and join (as FR-005), and be available to MCP
  clients; the existing Duplicate / Delete entries MUST keep acting on the marker only.

### Key Entities

- **Section**: a named span of the arrangement (start, length, name, automatic or custom
  naming). Defines what moves.
- **Section boundary**: a drop target — the start of a section or the end of the last one;
  where the moved span is inserted.
- **Arrangement content**: everything placed on the timeline — audio and MIDI clips on every
  track and the master, automation lanes (track, device, master volume). Moves with the
  section whose span covers it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Moving one section of a song with 8 tracks to another place in the order
  takes one drag and under 5 seconds, versus selecting and moving every track's clips and
  automation by hand.
- **SC-002**: After any move and its Undo, a render of the song is identical to the render
  before the move (sample-exact for audio-only content).
- **SC-003**: After a move, rendering the moved section's span at its new place matches
  rendering it at its old place before the move (same clips, notes and automation values).
- **SC-004**: A move on a 50-track project with 500 clips completes and redraws within
  200 ms of release, and audio playback during it has no dropout.
- **SC-005**: Zero content outside the affected span changes position in any move
  (checked by comparing clip and automation positions before and after).

## Assumptions

- The issue's video shows a reorder model (sections swap places, the rest closes up), not a
  free placement on any bar; that is what plain dragging does. Free placement of the marker
  stays available as the marker-only drag.
- The Preferences → Shortcuts pane lists both section drags (plain = with content,
  Option/Alt = marker only).
- Clip cutting at boundaries reuses the rules of Insert silence / Duplicate time, so clips
  look and sound the same as after those edits.
- The song has a single tempo and time signature, so moving content in beats is enough.
- Duplicate / Delete with content go beyond the issue's text (it asks for moving); they were
  added to its scope in clarification and the issue should note that before the PR.
- Unlike a move, Duplicate and Delete with content change the song's total length.
- Overlapping sections are uncommon; supporting content moves for them is out of scope.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
