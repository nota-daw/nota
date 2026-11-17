# Feature Specification: Tempo as master-track automation

**Feature Branch**: `feat/master-tempo-automation`

**Created**: 2026-11-15

**Status**: Draft

**Issue**: [#93](https://github.com/nota-daw/nota/issues/93) (approved). Reworks the tempo
editing surface added by #568 (`specs/086-tempo-track`).

**Input**: User description: "Issue #93: tempo changes become ordinary automation on the
master track, replacing the separate tempo track lane added in #568. The master lane in
the Arrangement gets an automation target picker like regular tracks (Volume, Tempo;
master pan and master device parameters per #93), and tempo automation is drawn and
edited with the same points and curves as any other automation lane, so smooth tempo
transitions (ramps) are natural. The dedicated, bulky tempo lane from #568 is removed so
it no longer eats arrangement space; the engine tempo map stays as the internal
beat↔time mechanism and is fed from the master Tempo automation. Projects saved with the
#568 tempo lane migrate their tempo points into the master Tempo automation."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The engine tempo map from #568 is kept as the beat↔time mechanism; engine changes are
expected where the master automation has to feed it, and for the master Pan and master
device-parameter automation targets the engine does not have yet.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Problem

#568 added tempo changes as a separate, always-present tempo lane above the tracks. In use
it has two problems:

1. The lane is a thick band that takes vertical space from the arrangement in every
   project, including ones that never change tempo.
2. Its editing model differs from every other automation lane in Nota (its own point
   shapes, handles and gestures), so making a smooth tempo transition is awkward and does
   not behave the way a musician who already draws volume or filter automation expects.

Tempo is conceptually a song-wide parameter that changes over time — exactly what master
automation is for — but the master lane today can only automate master volume.

## Clarifications

### Session 2026-11-15

- Q: What does the transport tempo field do when the master has Tempo automation? → A: It shows the tempo under the playhead and is read-only, with a tooltip saying tempo is automated on the master.
- Q: What vertical scale does the Tempo lane use? → A: Editable min/max BPM per project (default project tempo ±40, within 20–300), saved with the project as view state.
- Q: What BPM range can a Tempo automation point take? → A: 20–300 everywhere (transport, lane, automation); #568 points outside it are clamped on open with a warning.
- Q: How does the arrangement's horizontal axis behave when tempo changes? → A: Time-linear, as #568 made it: bar widths follow the tempo, audio keeps its physical length.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Draw tempo changes as master automation (Priority: P1)

The musician opens the master track's automation, picks **Tempo** from the same target
picker regular tracks use, and draws tempo with ordinary automation points: click to add
a point, drag to move it, set the curve between two points to get a ramp (accelerando /
ritardando) or keep two points at one position for an instant jump. Playback, the bar grid,
the metronome and export follow the drawn tempo.

**Why this priority**: This is the core of the rework — tempo becomes one more automation
target, edited the way all other automation is edited.

**Independent Test**: In a new project, pick Tempo on the master lane, draw 120 BPM at bar 1
ramping to 90 BPM at bar 9 and a jump to 140 BPM at bar 13; play, export, undo/redo,
save and reload — the audible timing, grid and exported file follow the curve each time.

**Acceptance Scenarios**:

1. **Given** a project with no tempo automation, **When** the musician opens the master
   lane's target picker, **Then** Tempo is offered next to Volume and its lane shows the
   project tempo as a flat line with the current value readable in BPM.
2. **Given** a Tempo lane, **When** the musician adds and drags points, **Then** the bar
   grid, playhead, MIDI, audio and metronome follow the new tempo while it plays.
3. **Given** two tempo points with a curve between them, **When** the musician bends the
   curve, **Then** the tempo changes smoothly between them (no stepping) and the timing
   matches the drawn curve.
4. **Given** two tempo points at the same position with different values, **When** played,
   **Then** the tempo jumps instantly at that position.
5. **Given** an edited Tempo lane, **When** undo/redo and save/reload are used, **Then**
   every point, value and curve, and the resulting timing, are restored exactly.

---

### User Story 2 - Get the arrangement space back (Priority: P1)

A project that does not use tempo automation shows no tempo band at all. A project that
does use it shows tempo only inside the master automation, at the size the musician chose
for that lane, and the lane can be hidden like any other automation lane.

**Why this priority**: The extra band is the most visible complaint; removing it is
independent of how tempo is edited.

**Independent Test**: Open a new project and an existing #568 project: neither shows a
separate tempo band; tracks start directly below the ruler/sections as before #568.

**Acceptance Scenarios**:

1. **Given** any project, **When** the Arrangement is shown, **Then** there is no separate
   tempo lane between the ruler and the tracks.
2. **Given** a project with tempo automation, **When** the master lane shows another target
   (e.g. Volume) or is collapsed, **Then** tempo still drives playback but takes no
   arrangement space.

---

### User Story 3 - Open projects made with the #568 tempo lane (Priority: P1)

A project saved with the #568 tempo lane opens with the same timing; its tempo points now
appear as Tempo automation on the master track.

**Why this priority**: #568 is already on develop; any project saved since must keep its
timing.

**Independent Test**: Save a project with a stepped, a linear and a smooth tempo segment
on the #568 build; open it on this build; compare the time of every bar start before and
after.

**Acceptance Scenarios**:

1. **Given** a project with #568 tempo points, **When** it is opened, **Then** the master
   track has Tempo automation with one point per original point and the same values.
2. **Given** stepped, linear and smooth segments, **When** converted, **Then** every bar
   start lands within one millisecond of its original time.
3. **Given** a project saved before #568 (single tempo), **When** opened, **Then** it has
   no tempo automation and plays at its stored tempo.

---

### User Story 4 - Other master automation targets (Priority: P2)

The same master target picker offers master pan and parameters of devices on the master
chain (EQ, Ceiling, Dynamics, …), automated the same way as on regular tracks.

**Why this priority**: Requested by #93 alongside tempo; it rides on the same picker but
is not needed to fix the tempo problem.

**Independent Test**: Automate master pan and a master EQ band gain; play and export;
the changes are audible and survive save/reload.

**Acceptance Scenarios**:

1. **Given** a master chain with an EQ, **When** the musician picks one of its parameters
   in the master target picker, **Then** a lane for it appears and its automation plays back.
2. **Given** master pan automation, **When** played and exported, **Then** the stereo
   position follows the lane.
3. **Given** master device automation, **When** the device is removed from the master
   chain, **Then** its lanes are removed with it, as on regular tracks (undo restores both).

This story is in scope (decided 2026-11-15): the feature closes #93's master-automation
request in full — Volume, Tempo, Pan and master device parameters.

---

### User Story 5 - Ctrl-drag time-scale mapping is removed (Priority: P2)

#568 also added a Ctrl-drag gesture that drags a bar line onto a moment in a recording and
writes the tempo needed to land it there. It is removed together with the tempo lane
(decided 2026-11-15): tempo is edited only as ordinary automation, with no tempo-only
gesture.

**Independent Test**: Ctrl-hover and Ctrl-drag over the arrangement do what they did before
#568 (no time-scale cursor, no tempo change, no marker created).

**Acceptance Scenarios**:

1. **Given** the Arrangement, **When** the musician holds Ctrl and drags over the timeline,
   **Then** no time-scale indicator appears and the tempo is unchanged.
2. **Given** Preferences → Shortcuts, **When** opened, **Then** the time-scale mapping
   gestures from #568 are no longer listed.

### Edge Cases

- Tempo point values outside 20–300 BPM are clamped to it; the lane can never
  produce zero, negative or non-finite tempo.
- Deleting every tempo point returns the project to its single tempo (the transport tempo
  field), not to an undefined state.
- While Tempo automation exists the transport tempo field is read-only (FR-009a); MIDI
  Learn or tap tempo aimed at it does nothing.
- Tempo automation before the first point holds the first point's value; after the last
  point holds the last point's value.
- Moving, duplicating or deleting arrangement sections and inserting time leave the Tempo
  lane where it is (it is the ruler those edits are measured on, as the tempo map was in
  #568); other master lanes move with the content like track lanes. Copy/paste of a Tempo
  lane range by hand works like any lane.
- Very steep ramps, many points close together, seeking and looping inside a ramp keep a
  valid, monotonic beat↔time mapping and no audible glitch at the loop boundary.
- A #568 project whose tempo data is malformed opens at its stored single tempo, with no
  crash and no partial map.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The master track's automation MUST offer a target picker with at least
  Volume and Tempo, presented and operated like the picker on regular tracks.
- **FR-002**: Tempo automation MUST be drawn and edited with the same points, curves,
  selection, copy/paste, delete and undo behaviour as any other automation lane — no
  tempo-only gestures or point types.
- **FR-003**: The Tempo lane MUST show and accept values in BPM (axis labels, hover and
  drag readout).
- **FR-003a**: The Tempo lane's vertical range MUST be set by two editable values, min and
  max BPM, within 20–300 BPM (min < max). By default they are the project tempo −40 and +40
  when the lane is first opened, clamped to 20–300, and widened to cover any existing
  points (e.g. after migration). They are saved with the project as view state, like
  lane height (not an undo step). Points outside the range stay where they are and are drawn clamped to the lane
  edge; changing the range never changes point values.
- **FR-004**: A curve between two tempo points MUST produce a continuous tempo change;
  two points at the same position MUST produce an instant change.
- **FR-005**: Playback, the bar grid and ruler, snapping, the metronome, MIDI, warped
  audio, tempo-synced devices, loop and seek, freeze/bounce and export MUST follow the
  master Tempo automation, as they follow the #568 tempo map today.
- **FR-005a**: The arrangement's horizontal axis MUST stay time-linear as #568 made it:
  bars are drawn wider where the tempo is slower and narrower where it is faster, and
  unwarped audio keeps its physical length on screen.
- **FR-006**: Edits to tempo automation MUST be heard while playing without restarting
  transport.
- **FR-007**: The separate tempo lane added by #568 MUST be removed from the Arrangement.
- **FR-008**: Projects saved with #568 tempo points MUST open with those points converted
  into master Tempo automation, keeping bar timing within SC-002 for every point inside
  20–300 BPM.
- **FR-008a**: Tempo is limited to 20–300 BPM everywhere (transport field, Tempo lane
  min/max, automation points, MCP tools). #568 points outside that range are clamped to it
  on open, and the project shows a one-time notice that its tempo was limited to
  20–300 BPM (timing after the first clamped point may differ).
- **FR-009**: Projects without tempo automation MUST behave exactly as with a single
  project tempo, including the transport tempo field.
- **FR-009a**: When master Tempo automation exists, the transport tempo field MUST show
  the tempo under the playhead (updating during playback and seek) and MUST NOT accept
  input; its tooltip says tempo is automated on the master track. Deleting every tempo
  point makes the field editable again.
- **FR-010**: Tempo automation MUST be saved, reloaded and covered by undo/redo like other
  automation.
- **FR-011**: The MCP automation tools MUST be able to list, read and write master Tempo
  automation the same way they handle other automation lanes.
- **FR-012**: The master target picker MUST also offer master Pan and the parameters of
  every device on the master chain; their automation MUST play back, export, save/reload
  and undo like the same targets on regular tracks.
- **FR-013**: The Ctrl-drag time-scale mapping gesture from #568 (and its hover indicator,
  marker insertion and shortcut entries) MUST be removed.

### Key Entities

- **Master automation lane**: an automation lane on the master track with a chosen target
  (Volume, Tempo, and possibly Pan / master device parameter), an ordered list of points,
  and display state (shown, height).
- **Tempo automation point**: position in beats, value in BPM, curve to the next point.
- **Tempo map**: the derived beat↔time mapping built from the Tempo automation; not edited
  directly any more.
- **Legacy tempo points (#568)**: beat, BPM, shape (step / linear / smooth with handles);
  read only for migration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-007**: Every master target (Volume, Tempo, Pan, each master device parameter) can be
  picked, drawn, played, exported and reloaded with identical results.

- **SC-001**: A project without tempo automation shows the first track at the same
  vertical position as before #568 (zero pixels used by tempo).
- **SC-002**: Migrated #568 projects whose tempo stays within 20–300 BPM keep every bar
  start within 1 ms of its original time over the first 200 bars.
- **SC-003**: A musician who can draw volume automation can draw a 4-bar ritardando with
  no new instructions, in under 30 seconds.
- **SC-004**: A tempo ramp drawn as a curve plays with no audible stepping, and drawn,
  played and exported timing agree within one sample at 48 kHz.
- **SC-005**: Save/reload and undo/redo restore identical tempo automation and timing.
- **SC-006**: Existing build, smoke tests and the performance gate pass; playback with a
  fixed tempo costs no more than today.

## Assumptions

- The engine tempo map, its beat↔time conversions and all downstream consumers from #568
  stay; only its source changes from the tempo lane to the master Tempo automation.
- Automation curves in Nota are a single per-segment bend, not Bezier handles. #568 smooth
  segments are converted to the closest curve, adding intermediate points where one curve
  cannot hold the shape within SC-002; stepped segments become a pair of points at one
  position.
- Time-signature changes stay out of scope (#93 lists them as markers; #588 covers them).
- Return-track automation gaps mentioned in #93 are out of scope.
- Tempo lives on the master track only; regular tracks do not get a Tempo target.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — the tempo map is still compiled off the audio thread and published as a snapshot
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: tempo-from-automation, migration of #568 projects, master pan and master device automation
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned; FEATURES.md and user docs (tempo lane pages written for #568) updated
- [ ] UI change → follows `DESIGN.md` (Studio design language)
