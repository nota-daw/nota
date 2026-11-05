# Feature Specification: Send levels in the arrangement track headers

**Feature Branch**: `feat/arrangement-track-sends`

**Created**: 2026-11-02

**Status**: Draft

**Input**: User description: "Issue nota-daw/nota#143 — Arrangement: edit send levels from the track headers. Set how much of a track goes to the return tracks while working in the Arrangement, without switching to Session / Mixer or opening the Devices panel. Send controls (small knobs or faders, one per return) in the arrangement track header, shown when the header is tall enough or via a toggle, like the volume/pan controls there. Undoable, automatable, kept in sync with the Mixer and the Sends card."

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
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: a track can send post-fader to up to four return
buses (A–D). Send levels are set today in the Mixer window (A/B mini faders), the Session
view's mixer rows and the Devices panel's Sends card; all three follow one another (#138).
Send moves are **not** undoable and sends **cannot** be automated: the value-undo gestures
(specs/009, 016) cover volume, pan, master volume and device parameters only, and
automation lanes target volume, pan and device / plug-in parameters only. The arrangement
track header lays itself out by row height (specs/015): compact (one line), standard
(name · routing + M/S/arm · volume + pan) and large (a row each, pan on its own row).

## Clarifications

### Session 2026-11-02

*(Autonomous run: the user asked for no involvement; each answer is the recommended option.)*

- Q: How tall must a standard-height row be before it shows its send row? → A: 88 px or more (the standard layout's three rows plus one 24 px knob row); large rows always show it.
- Q: How does a send automation lane name itself in the lane header and target menu? → A: "Send A · <return name>" (bus letter, then the return's name); the target menu groups the sends after Pan.
- Q: Do sends gain MIDI-learn / Remote control as part of this change? → A: No — out of scope; the header knobs only follow such changes if another surface already makes them.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Set send levels from the track header (Priority: P1)

A producer arranging a song wants a bit more reverb on the vocal during the chorus. They
stay in the Arrangement: the vocal's header shows one small knob per return track (A, B,
…), they drag the "A" knob up, and the vocal goes to the reverb return. The Mixer window,
the Session mixer and the Sends card show the new level at once.

**Why this priority**: it is the request — edit sends without leaving the Arrangement.

**Independent Test**: a project with two returns and an instrument track at the large
height; drag the header's A knob, check the return's meter moves and the Mixer / Sends
card show the same level.

**Acceptance Scenarios**:

1. **Given** a project with returns and a track row tall enough for sends, **When** the
   user looks at its header, **Then** it shows one send knob per return, in bus order,
   each labelled with the bus letter and named by its return on hover.
2. **Given** a send knob, **When** the user drags it or types a value into it (the
   value-entry box, like the header's volume), **Then** the track's send to
   that return changes and the audio follows while dragging.
3. **Given** a send knob, **When** the user double-clicks it, **Then** the
   send resets to off (0 %).
4. **Given** a send changed in the header, **When** the user looks at the Mixer window,
   the Session mixer or the Devices panel's Sends card, **Then** they show the same level;
   and a change made there shows in the header within a refresh tick.
5. **Given** a return is added, deleted or renamed, **When** the headers redraw, **Then**
   the knobs match the returns that exist (a deleted return's knob goes and the later ones
   move down with their buses).

---

### User Story 2 - Undo a send change (Priority: P1)

The user overdoes the reverb send and presses Undo: the send returns to its old level.
Redo puts it back. One drag is one undo step.

**Why this priority**: the issue asks for it, and every other mix control in the header
already undoes; a header control that doesn't would surprise.

**Independent Test**: drag a send knob from 0 % to 60 %, Undo → 0 %, Redo → 60 %; the
History lists one step for the drag.

**Acceptance Scenarios**:

1. **Given** a send knob dragged from one value to another, **When** the user undoes,
   **Then** the send goes back to the value before the drag, in one step; redo reapplies it.
2. **Given** a send changed from the Mixer window, the Session mixer or the Sends card,
   **When** the user undoes, **Then** that change undoes too (all send controls share the
   same undo behaviour).
3. **Given** a drag that ends where it started, **When** it ends, **Then** no undo step
   is added.
4. **Given** a send undo step whose track was since deleted, **When** it is undone,
   **Then** it is skipped quietly, like a volume step.

---

### User Story 3 - Automate a send (Priority: P2)

The user wants the delay send to open only on the last word of each verse. They pick
"Send A" in the track's automation lane, draw a ramp, and on playback the send — and the
header knob, the Mixer and the Sends card — follow the lane. They can also record it: with
automation record on, moving the knob while playing writes the lane, as volume and pan do.

**Why this priority**: the issue asks for automatable sends; drawing comes with the lane
target, recording comes with the header control.

**Independent Test**: choose Send A in a track's automation lane, draw 0 → 100 % over two
bars, play; the return's level rises and the header knob turns with it. Save, reopen: the
lane is still there.

**Acceptance Scenarios**:

1. **Given** a track with returns, **When** the user opens its automation target menu,
   **Then** it offers one "Send" entry per return (named by bus letter and return name)
   next to Volume and Pan.
2. **Given** a send lane with points, **When** the transport plays, **Then** the send
   follows the lane and every send control shows the played value.
3. **Given** automation record on and the transport playing, **When** the user moves a
   send control, **Then** the send's lane is written (created if missing) and the write
   and the value undo as one step, as with volume.
4. **Given** automation record off and a send lane with points, **When** the user moves
   the send control, **Then** the hand overrides the lane until automation is re-enabled,
   as with volume.
5. **Given** a project with send automation, **When** it is saved and reopened, **Then**
   the lanes come back on the same buses.
6. **Given** a return with send lanes pointing at it is deleted, **When** the buses
   compact, **Then** lanes to the deleted bus go and lanes to later buses follow their bus;
   undo brings the return and its lanes back.

---

### User Story 4 - Choose when the sends show (Priority: P3)

The user wants tall headers without sends (or the opposite: sends on standard-height rows).
A "Sends" toggle in the header column's top strip shows or hides the send row on every
track header; with it on, a row shows its sends once it is tall enough for them.

**Why this priority**: keeps the headers uncluttered for those who don't use returns from
the Arrangement; secondary to the controls themselves.

**Independent Test**: toggle Sends off — the knob rows disappear from every header; on —
they come back on rows tall enough; the choice is still there after reopening the project.

**Acceptance Scenarios**:

1. **Given** a project with returns, **When** the user looks at the header column's top
   strip, **Then** a "Sends" toggle sits there, on by default; with no returns it is hidden.
2. **Given** the toggle on, **When** a track row is large, **Then** its header shows the
   send row; **when** a row is standard but at least as tall as the send row needs,
   **Then** it shows the send row too; **when** a row is shorter (or compact), **Then** it
   shows no send row.
3. **Given** the toggle off, **When** any header is drawn, **Then** no send row shows and
   the headers look exactly as before this feature.
4. **Given** the toggle changed, **When** the project is saved and reopened, **Then** the
   toggle keeps its state (it is arrangement view state, like row heights).

### Edge Cases

- No return tracks: no send row, no toggle, no Send entries in the automation menu.
- Return tracks themselves and the master have no sends: their footer rows are unchanged.
- Group tracks send like any track (the Mixer shows their sends): their full header card
  gets the send row; the slim collapsed group bar does not.
- Four returns in a narrow header column (minimum 160 px): the knobs still fit on one row
  (the read-out moves to the hover / value-entry box).
- A frozen track: sends still work (sends run after the frozen audio), as in the Mixer.
- Moving clips between tracks with "automation follows" moves send automation like
  volume/pan automation (track-mix lanes, not device lanes).
- Reordering or removing devices never touches send lanes.
- MIDI-learn and Remote control of sends are out of scope; any send change from
  another surface is reflected in the header knob (FR-003).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The arrangement track header MUST show a send row — one knob per existing
  return, in bus order, labelled A–D — on audio, instrument and group track cards when
  the project has returns, the Sends toggle is on, and the row is tall enough (FR-007).
- **FR-002**: Each send knob MUST set the post-fader send level 0–100 % by drag or typed
  value entry (⌥-click), and reset to 0 % by double-click; a mono read-out beside it MUST
  show the level (whole percent, "–" when off) where the column has room, and its tooltip
  MUST name the return and show the level.
- **FR-003**: Send knobs MUST follow changes made anywhere else (Mixer window, Session
  mixer, Sends card, automation playback, Remote, MIDI mapping) within one UI refresh tick,
  and those surfaces MUST follow the header.
- **FR-004**: A send change made through any send control (header, Mixer window, Session
  mixer, Sends card) MUST be one undo step per gesture, restoring the previous level on
  undo and the new one on redo; a gesture that changes nothing MUST add no step.
- **FR-005**: The engine MUST support a send automation target per return bus: lanes can
  be added, drawn, played back, recorded from the send controls (touch / override /
  record, as for volume), saved and reloaded.
- **FR-006**: The track's automation target menu MUST offer one Send entry per return,
  after Pan, named "Send A · <return name>"; the lane header shows the same name.
- **FR-007**: The send row MUST show on large rows and on standard rows 88 px or taller
  (tall enough to hold it without clipping the other controls); never on compact rows. On a large row with
  sends, volume and pan MAY share one row (as on a standard row) to make room.
- **FR-008**: A "Sends" toggle in the header column's top strip MUST show/hide every
  header's send row; on by default; hidden when there are no returns; saved with the
  project's arrangement view state.
- **FR-009**: Deleting a return MUST drop send lanes to it and renumber lanes to later
  buses together with the send levels; undo MUST restore both.
- **FR-010**: Moving clips across tracks with their automation MUST carry send lanes the
  way it carries volume and pan lanes.
- **FR-011**: The audio thread MUST stay allocation- and lock-free: send automation
  writes the existing per-bus send level, as volume automation writes the volume.

### Key Entities

- **Send level**: a track's post-fader level into one return bus (0 … 1), already part of
  the track's mix state and the project file.
- **Send automation lane**: a track automation lane whose target is "send to bus N";
  points in the send's units (0 … 1).
- **Sends toggle**: arrangement view state (per project, beside row heights), not undoable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A send level can be changed from the Arrangement in one gesture, without
  opening another view or panel.
- **SC-002**: 100 % of send changes made through any send control undo and redo in one
  step each.
- **SC-003**: A drawn send lane is audible and visible (knob moves) on playback, and
  survives save / reopen with no loss.
- **SC-004**: With the Sends toggle off (or no returns), the arrangement headers are
  pixel-identical to the previous release.
- **SC-005**: The engine smoke test covers send undo, send automation playback and
  return-delete lane remapping, and passes on every platform build.

## Assumptions

- Knobs, not faders: four mini faders don't fit a 160 px column beside their labels;
  inline knobs match the Sends card.
- A send's value-undo uses the same gesture mechanism as volume/pan (specs/009), so
  the Mixer window, Session mixer and Sends card gain undo by switching to it.
- Mute, solo and bypass stay outside value undo (unchanged).
- The Session view, Mixer window and Sends card layouts are unchanged; only their send
  controls gain undo and automation recording.
- MCP / Remote surfaces gain the new automation target name ("send") where they list
  targets; no new Remote UI.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
