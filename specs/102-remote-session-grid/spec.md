# Feature Specification: Nota Remote — Session grid: pinned launch and Stop controls, groups, and a choice of orientation

**Feature Branch**: `feat/remote-session-grid`

**Created**: 2026-11-18

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#662: "Nota Remote's Session grid has three gaps that get in
the way during a live set on a phone. (1) Scene launch, track Stop and Stop All scroll out of view
[…] Expected: the scene launch strip and the track Stop strip (with Stop All in their corner) stay
pinned, like frozen headers in a spreadsheet; only the clip slots scroll. Track headers stay pinned
on their axis too. (2) Group tracks are missing […] Expected: groups appear as on the desktop — a
group header with its member tracks, a group Stop button, and group slots that launch the members'
clips in that scene. (3) Choose the grid orientation […] a setting in Nota Remote, kept per device,
current layout as the default. Pinned controls and groups work in both orientations."

## Nota Context

**Feature area**:

- [ ] Engine (C++, `src/native/nota.engine`)
- [x] Managed app (C#, `src/managed/Nota.Remote`) — the hub sends group tracks in the Session
  message and launches / stops a group's members
- [x] UI — Nota Remote's phone page (`src/web/remote`), follows `DESIGN.md` as spec 090 maps it
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`DESIGN.md`](../../DESIGN.md), [`FEATURES.md`](../../FEATURES.md),
[`specs/014-remote-studio`](../014-remote-studio/spec.md), [`specs/090-remote-new-look`](../090-remote-new-look/spec.md).

**Existing behaviour this builds on**: the Scenes screen lays tracks across and scenes down — a
"Scene" corner, track headers on top, a scene launch button at the head of each row, and a row of
Stop buttons with Stop All at the bottom — all inside one scroll area, so every control scrolls
away with the clips. The hub leaves group and return tracks out of the Session message. On the
desktop, a group column shows per scene a summary slot ("2 of 3", one bar per member, a launch
triangle) that launches each member's clip in that scene, and its Stop stops every member; a
group's members are every non-group track below it, nested groups included. The Mixer screen
folds groups (closed until tapped) and remembers which are open on the phone (`groupsOpen`).

## User Scenarios & Testing

### User Story 1 - Launch and stop without scrolling back (Priority: P1)

A performer with 16 scenes and 10 tracks scrolls deep into the grid on a phone and still launches
a scene, stops a track, or hits Stop All with one tap.

**Why this priority**: losing the launch and stop controls mid-set is the most direct harm.

**Independent Test**: on a phone-sized screen, open a project bigger than the screen, scroll to
the far corner of the grid: every scene launch button for the visible scenes, every Stop for the
visible tracks, Stop All and the track names are on screen and work.

**Acceptance Scenarios**:

1. **Given** scenes as rows, **When** the grid scrolls down, **Then** the track headers stay at the
   top and the Stop row with Stop All stays at the bottom; **When** it scrolls sideways, **Then**
   the scene launch column stays at the left.
2. **Given** any scroll position, **When** a pinned button is tapped, **Then** it does what it did
   before (launch the scene, stop the track, stop everything).
3. **Then** the clips slide under the pinned strips, never over them, and a pinned strip never
   hides a clip that can't be scrolled into view.

### User Story 2 - See and play group tracks (Priority: P1)

A project groups its drums; on the phone the Drums group appears in the grid, its slot launches
the kick, snare and hats clips of that scene together, and its Stop stops them.

**Why this priority**: today a grouped set is incomplete on the phone; it's a reported bug.

**Independent Test**: group two tracks on the desktop; on the phone the group column appears with a
summary slot per scene; tapping a summary slot launches its members' clips in that scene; the
group's Stop stops them; tapping the group header folds and unfolds its members.

**Acceptance Scenarios**:

1. **Given** a group, **Then** it shows in project order, before its members, with its name and
   colour, and its members show after it, marked as inside it.
2. **Given** a scene where at least one member has a clip, **Then** the group's slot reads "n of m"
   (members with a clip of all members), shows one bar per member (its colour where it has a
   clip, a dim bar where not, the playing colour where it plays), and fills when any member plays.
3. **When** a filled group slot is tapped, **Then** every member's clip in that scene launches; a
   group slot with no member clips is an empty cell.
4. **When** a group's Stop is tapped, **Then** every member track stops.
5. **When** the group header is tapped, **Then** its members fold away or come back; the choice is
   the same one the Mixer screen uses, so folding in one folds in both.
6. **Given** nested groups, **Then** the outer group counts and launches every track below it, and
   folding the outer group hides the inner one too.

### User Story 3 - Choose the grid's orientation (Priority: P2)

A performer holding the phone upright with many scenes and a few tracks turns the grid so scenes
run across and tracks down.

**Why this priority**: comfort, not a blocker; it builds on stories 1 and 2.

**Independent Test**: in the ⋯ menu pick "Scenes across"; the grid redraws with a track per row
(name, then Stop, pinned at the left), a scene per column (launch button pinned at the top), Stop
All in the top-left corner; reload the page — the choice is kept.

**Acceptance Scenarios**:

1. **Given** the ⋯ menu, **Then** a "Session grid" choice offers "Scenes down" (the default, as
   today) and "Scenes across".
2. **Given** scenes across, **Then** each track is a row headed by its name and its Stop, pinned at
   the left; each scene is a column headed by its launch button, pinned at the top; Stop All sits
   in the pinned top-left corner.
3. **Given** scenes across, **Then** groups, folding, group slots and every clip state look and act
   as in scenes down.
4. **When** the phone reloads or reconnects, **Then** it keeps its orientation; another phone keeps
   its own.

### Edge Cases

- **No scenes or no tracks**: the grid shows the pinned headers it has; nothing breaks.
- **A group with no members** (all removed): its slots are empty cells; Stop does nothing harmful.
- **A play-only phone** (Nota lets it play notes only): pinned controls and group slots answer with
  the same "notes only" notice as the other Session buttons.
- **Return tracks**: stay out of the grid, as on the desktop's Session grid (they have no clips).
- **A folded group with a playing member**: the group's own slot still shows the member playing.
- **An older Nota** that sends no groups: the grid works as before, without group columns.
- **Rotation / form change** (phone ↔ phone landscape ↔ tablet): the pinned strips stay pinned.

## Requirements

### Functional Requirements

- **FR-001**: The Scenes grid MUST keep its scene launch buttons, track headers, track Stop
  buttons and Stop All on screen at every scroll position, in both orientations; only the clip
  slots scroll under them.
- **FR-002**: Nota MUST send group tracks (not return tracks) in the Session message, in project
  order.
- **FR-003**: The phone MUST show each group as a header and a summary slot per scene, as the
  desktop's group slot reads ("n of m", member bars, playing state).
- **FR-004**: Launching a group slot MUST launch each member's clip in that scene; stopping a group
  MUST stop each member — members being every non-group track below it.
- **FR-005**: Tapping a group header MUST fold / unfold its members, sharing the Mixer's
  remembered open groups; groups start folded as in the Mixer.
- **FR-006**: The ⋯ menu MUST offer the orientation (Scenes down / Scenes across), default Scenes
  down, remembered on the phone.
- **FR-007**: Play-only phones MUST NOT launch or stop anything from the grid (unchanged rule).

### Key Entities

- **Session grid orientation**: a per-phone preference — scenes down (rows) or scenes across
  (columns).
- **Group column**: a group track with its member tracks and, per scene, a summary of their slots.

## Success Criteria

- **SC-001**: From any scroll position, launching any visible scene, stopping any visible track or
  stopping everything takes one tap, with no scrolling.
- **SC-002**: Every group shown on the desktop's Session grid is reachable on the phone, and
  launching its slot starts the same clips as on the desktop.
- **SC-003**: Switching orientation takes two taps and survives a reload.

## Assumptions

- Groups start folded on the Scenes screen as in the Mixer, because both share one remembered set.
- Group slot launch and stop are done by Nota (one message), not by the phone sending one message
  per member, so every member launches on the same tick.
- Scene names stay "Scene N", as the phone shows today; scene colours / tempos are out of scope.
- The group slot shows members' state only; a group has no clip of its own.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header (no new source files planned)
- [x] No new dependency
- [x] Audio-thread impact: none — the hub calls the existing engine launch / stop API from its own thread, as now
- [x] No new managed/native crossings; the Remote protocol grows group entries in `ses` and accepts a group id in `launch` / `stopSlot`
- [x] Smoke-test impact: the Remote smoke checks are extended to cover a group in the Session message
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry, FEATURES.md and user docs
- [x] UI change → follows `DESIGN.md` as spec 090 maps it (theme tokens, sentence case, the type scale)
