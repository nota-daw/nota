# Feature Specification: Linked editing on group tracks

**Feature Branch**: `feat/folder-track-groups`

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "Group button on folder tracks (issue #184): a toggle on a folder track that links the editing and recording behaviour of all tracks inside the folder. While the group is on, record-arming one track arms the others; cutting, splitting or moving a clip on one track performs the matching action on clips at the same timeline position on the other grouped tracks; selecting a clip or a range on one track selects the matching material on the others. Use case: dry and wet guitar recorded on separate tracks, edited as one."

**Issue**: [nota-daw/nota#184](https://github.com/nota-daw/nota/issues/184) (approved)

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

In Nota the "folder track" of the issue is the **Group** track (a nestable submix that
owns child tracks). Nota already uses the word *group* for that track type, so the toggle
this spec adds is called **Link** (it links the editing of the group's tracks), to avoid
"the group button of a group".

## Clarifications

### Session 2026-11-04

- Q: Is a way needed to edit one track of a linked group without turning Link off? → A: Yes, a modifier: ⌥-click / Alt-click (gesture picked in planning — ⌃-click is the macOS context click, ⌘ and ⇧ already toggle and extend the selection) (FR-012).
- Q: Which clip on another linked track counts as the matching one? → A: Same start and same end, within tolerance (FR-006).
- Q: Should Mute and Solo also be linked? → A: No — only record arm, selection and clip edits.
- Q: What happens when a linked clip is moved to another track? → A: The partners take the same time shift but stay on their own tracks; only the source clip changes track.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Edit two takes of one performance as one (Priority: P1)

A guitarist records the same performance onto two tracks — the dry DI and the amped
sound — and puts both in a group. They switch the group's **Link** on. From then on,
splitting, moving, trimming, duplicating or deleting a clip on either track does the same
to the clip recorded alongside it on the other track, so the two takes never drift apart
and they edit one track's worth of clips instead of two.

**Why this priority**: This is the core value of the issue: keeping parallel recordings
in step while editing. Without it the toggle does nothing useful.

**Independent Test**: Put two audio tracks with clips at the same positions in a group,
turn Link on, split one clip at the cursor, move one of the halves by two bars and delete
another — the second track shows the same cuts, positions and deletions, and one undo
reverts each edit on both tracks.

**Acceptance Scenarios**:

1. **Given** a linked group with tracks A and B whose clips start and end together,
   **When** the user splits A's clip at the cursor (⌘E), **Then** B's clip is split at
   the same position too, as one undo step.
2. **Given** the same group, **When** the user drags A's clip two bars later,
   **Then** B's matching clip moves two bars later as well, and the two land at the same
   position after snapping.
3. **Given** the same group, **When** the user trims the start or end of A's clip, or
   changes its fade, **Then** B's matching clip gets the same new edge or fade.
4. **Given** the same group, **When** the user deletes, duplicates (⌘D), nudges (← / →),
   cuts or copies A's clip, **Then** the same happens to B's matching clip, and a paste
   puts both back on their own tracks at the paste position.
5. **Given** Link is off on the group, **When** the user edits A's clip, **Then** B is
   untouched (today's behaviour).

---

### User Story 2 - Select once, see the whole group selected (Priority: P1)

Clicking a clip on one linked track also selects the matching clips on the group's other
tracks, and drawing a time range on one linked track spans every track of the group. The
user sees what the next edit will act on before they make it.

**Why this priority**: Selection is what makes the linked edits predictable; it ships
together with Story 1 (every linked edit goes through the selection) but can be tested on
its own.

**Independent Test**: In a linked group, click a clip on one track — the matching clips
on the other tracks are highlighted as selected; ⇧-drag a range on one track — the range
covers all of the group's tracks; Delete removes the covered part on all of them.

**Acceptance Scenarios**:

1. **Given** a linked group, **When** the user clicks a clip on one of its tracks,
   **Then** the matching clips on the group's other tracks are selected with it.
2. **Given** a linked group, **When** the user ⇧-drags a time range starting on one of its
   tracks, **Then** the range extends over every track of the group (and over any other
   tracks the drag itself crosses).
3. **Given** a linked selection, **When** the user ⌘-clicks one of the selected clips,
   **Then** it and its matching clips leave the selection together.

---

### User Story 3 - Arm the group with one click (Priority: P2)

Arming any track of a linked group for recording arms all of the group's recordable
tracks, and disarming one disarms them all, so both sides of the guitar are always
recorded together.

**Why this priority**: A clear quality-of-life gain named in the issue, but recording
already works by ⌘-clicking each arm; editing is the bigger daily pain.

**Independent Test**: In a linked group of two audio tracks, click the arm on one —
both are armed and any armed track outside the group is disarmed (exclusive arm, as
today); record — both get a take; click an arm again — both are disarmed.

**Acceptance Scenarios**:

1. **Given** a linked group of tracks A and B and an armed track C outside it, **When**
   the user clicks A's arm, **Then** A and B are armed and C is disarmed.
2. **Given** the same state, **When** the user ⌘-clicks A's arm (additive), **Then** A and
   B are armed and C stays armed.
3. **Given** A and B armed, **When** the user disarms B, **Then** A is disarmed too.
4. **Given** Link is off, **When** the user arms A, **Then** only A is armed (today's
   behaviour).

---

### User Story 4 - Turn the link on, see it, keep it (Priority: P2)

The user turns Link on or off from the group's header (and the Mixer strip of the group),
sees at a glance which groups are linked and which tracks belong to a linked group, and
finds the setting again after reopening the project.

**Why this priority**: The entry point and its feedback; small but required for the other
stories to be usable and discoverable.

**Independent Test**: Toggle Link on a group, save, reopen — it is still on; the toggle
reads as on in the header and the Mixer; undo turns it back off.

**Acceptance Scenarios**:

1. **Given** a group, **When** the user clicks its Link toggle, **Then** the toggle shows
   as on (accent colour) in the Arrangement header and the Mixer, and the group's child
   track headers show they are linked.
2. **Given** a linked group, **When** the project is saved and reopened, **Then** the group
   is still linked.
3. **Given** the user toggled Link, **When** they undo, **Then** Link goes back to its
   previous state.

---

### Edge Cases

- **No matching clip** on another track (only one side was recorded there): the edit acts
  on the tracks that have a match; the others are left as they are. Time-based edits
  (split at a position, a range delete) act on whatever each track has at that time.
- **Clips that only partly overlap** on another track: not a match — see FR-006; they are
  only touched by time-based edits.
- **Nested groups**: a linked group links every track below it, including the tracks of
  groups nested inside it; a nested group's own Link adds nothing while its parent is
  linked. A track inside an unlinked group inside a linked one is still linked through
  the outer group.
- **Mixed track kinds** (audio and instrument tracks in one linked group): edits act on
  matching clips of any kind; a pasted clip only lands on a track that takes its kind.
- **Moving a clip to another track** (dragging vertically, ⌥↑ / ⌥↓): the time shift is
  linked, the track change is not — the partners move by the same amount in time and stay
  on their own tracks; only the source clip changes track (and stops matching them).
- **Dropping clips below the last track** (New track per source track): the linked
  partners are not dragged along unless they were selected explicitly.
- **Moving a track out of a linked group** (ungroup, drag out): it stops being linked
  immediately; moving one in links it.
- **Group tracks themselves** have no clips and no arm; Link affects only the tracks inside.
- **Return and master tracks** cannot be in a group and are never linked.
- **While recording**: changing Link does not disarm or arm anything mid-take; the arm rule
  applies to the next arm click.
- **Clip-content edits** (notes in the piano roll, warp markers, gain envelope inside a
  clip, clip properties like transpose or name) are not linked — they are specific to one
  track's material.
- **Session View** clips and slots are not linked (scope is the Arrangement); arming from
  the Session View is linked, since arm is a track property.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every Group track MUST have a **Link** toggle, off by default for new and
  existing groups, shown in the group's Arrangement header (slim and full rows) and its
  Mixer strip.
- **FR-002**: While a group is linked, every track below it (recursively, excluding group
  tracks, return and master) MUST be a **linked member** of that group; a track's link set
  is the members of its outermost linked ancestor group.
- **FR-003**: The linked state MUST be saved with the project, restored on open, and every
  Link toggle MUST be one undo step.
- **FR-004**: Linked members MUST be marked in their Arrangement headers so the user sees
  which tracks will follow an edit.
- **FR-005**: Arming a linked member MUST arm every recordable linked member of the same
  set, following today's exclusive / additive rule for tracks outside the set (a plain
  click disarms tracks outside the set, ⌘-click leaves them); disarming one MUST disarm the
  whole set. Arm-on-select MUST treat the set as one track.
- **FR-006**: A clip on another linked member **matches** a source clip when it starts and
  ends at the same timeline positions, within 1/256 of a beat, so clips recorded together
  match exactly.
- **FR-007**: Selecting a clip on a linked member MUST also select its matching clips on
  the other members; removing it from the selection removes them too. ⌘A selection and
  rubber-band selection need no change (they already select everything they cover).
- **FR-008**: A time range started on a linked member MUST cover every member of the set.
- **FR-009**: These Arrangement clip edits MUST apply to the matching clips of linked
  members, as one undo step together with the source edit: move in time (drag, nudge;
  a move to another track shifts the partners in time only, on their own tracks),
  trim start / end, fade in / out changes, delete, duplicate, cut, copy (pasting puts each
  clip back on its own track).
- **FR-010**: Split at the cursor and split across a selection MUST split every linked
  member at the same position(s), whatever clip each one has there.
- **FR-011**: Range edits (copy, cut, duplicate, delete, paste over a range) on a range
  that covers linked members MUST act on all members, as they do today for tracks the
  range covers.
- **FR-012**: The user MUST be able to act on a single linked track without turning Link
  off: ⌥-click (Alt-click on Windows / Linux) on a clip selects that clip alone, without its
  matches, and the edits that follow act on it alone; ⌥-click on a linked track's arm arms
  or disarms that track alone. ⌥-*drag* keeps its meaning (move without snapping) and stays
  linked. Time ranges have no bypass — they follow Link. The gesture is listed in
  Preferences → Shortcuts.
- **FR-013**: The Link state MUST be readable and settable through the MCP server, like
  other track properties.
- **FR-014**: With Link off on every group, editing and arming MUST behave exactly as
  today.

### Key Entities

- **Group link**: an on/off property of a Group track, saved with the project.
- **Link set**: the non-group tracks under a group's outermost linked ancestor; derived,
  not stored.
- **Matching clips**: clips on other members of a link set with the same start and end
  positions as a source clip (within tolerance).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With two linked tracks recorded together, a split + move + delete sequence
  takes the same number of user actions as on one track (half of today's).
- **SC-002**: After any sequence of linked edits on clips recorded together, the clips on
  every member of the set start and end at identical positions.
- **SC-003**: Every linked edit, and every Link toggle, is undone by a single undo.
- **SC-004**: One click arms or disarms every recordable track of a linked group.
- **SC-005**: Projects saved before this feature open unchanged, with every group unlinked.

## Assumptions

- The issue's "folder track" is Nota's Group track; Link lives on Group tracks only (no
  separate track-group object without a submix).
- Linking covers recording arm, Arrangement clip selection and Arrangement clip edits
  only (confirmed 2026-11-04). Mute, solo, volume, pan, sends, devices and automation are not linked (mixer
  linking / VCA is a separate feature).
- Clip-content editors (piano roll, audio clip editor) and the Session View clip grid are
  out of scope.
- Matching by identical start and end (FR-006, confirmed 2026-11-04) is chosen because it is predictable
  and fits the main use case (simultaneously recorded takes); partly overlapping clips are
  only affected by time-based edits.
- Nested linked groups merge into the outermost linked one (FR-002).
- The toggle follows DESIGN.md: a small key in the header controls, orange accent when on.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
