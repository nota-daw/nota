# Feature Specification: MIDI Learn for Session clip and scene launch buttons

**Feature Branch**: `feat/session-launch-midi-learn`

**Created**: 2026-11-20

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#601: "I'm trying to use a hardware MIDI controller (Arturia
minilab) to launch clips and scenes in the Session views. However, when I enter MIDI Learn mode,
the clip launch buttons do not become highlighted or selectable for mapping. […] Allow clip launch
buttons and scene launch buttons in the Session grid to be selectable in MIDI Learn mode, so they
can be mapped to MIDI notes or CC messages from a hardware controller."

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — the MIDI Learn copy carries note-offs
- [x] Managed app (C#, `src/managed/*`)
- [x] UI — the Session grid in MIDI Learn mode, the MIDI map tab
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`FEATURES.md`](../../FEATURES.md) (MIDI Learn, Session),
[`specs/051-controller-auto-mapping`](../051-controller-auto-mapping/spec.md) (an explicit MIDI
Learn mapping wins over a controller profile), [`specs/104-cancel-queued-launch`](../104-cancel-queued-launch/spec.md).

**Existing behaviour this builds on**: MIDI Learn maps a CC, a note, a gamepad control or a Nota
Remote control onto any control that carries a MIDI Learn binding or exposes learn regions
(`IMidiLearnRegions`); buttons (mute, solo, transport) fire once on the press. The mappings are
saved with the project (`midimap.json`), tracks by position. The engine copies CC and note-on
to MIDI Learn, but not note-off — so a mapped note has no release, and since a button fires on the
edge of a latch that only a release resets, a note mapped to a button fires on its first press
only. The Session grid's launch buttons carry no binding, so learn mode skips them. A recognised controller can already walk the Session
and launch the *selected* slot (specs/051), and Nota Remote launches slots and scenes by id.

## User Scenarios & Testing

### User Story 1 - Map a pad to a clip slot (Priority: P1)

A performer with a pad controller wants each pad to launch one clip, the way a pad grid works.

**Independent Test**: arm MIDI Learn, click a slot's launch button, hit a pad; leave learn mode and
hit the pad again: the clip launches at the next launch boundary.

**Acceptance Scenarios**:

1. **Given** MIDI Learn armed, **Then** every track slot's launch button in the Session grid is
   highlighted — a clip's launch triangle, an empty slot's stop / record button — and a click
   selects it for learning; the next pad, key or CC binds it.
2. **Given** a mapped slot holding a clip, **When** its pad is hit, **Then** the clip launches as a
   click on its launch button does: queued to its quantum, Toggle / Repeat / legato honoured, the
   pad's velocity scaled by the clip's velocity amount.
3. **Given** a Gate or Repeat clip, **When** the pad is let go (note-off, or a CC back below the
   half-way point), **Then** the clip stops, as letting go of the on-screen button does; a Trigger
   clip plays on.
4. **Given** a mapped empty slot, **Then** its pad records on an armed track, else stops the track
   when the slot has its stop button, else does nothing — as clicking its button does. While the
   slot records (or waits to), the pad stops the take.
5. **Then** the mapping works whether or not the Session view is showing.

### User Story 2 - Map a button to a scene (Priority: P1)

**Acceptance Scenarios**:

1. **Given** MIDI Learn armed, **Then** each scene's launch button on the scene rail is highlighted
   and learnable.
2. **When** a mapped scene button is pressed, **Then** the whole row launches, as the on-screen
   button does.

### User Story 3 - Find and keep the mappings (Priority: P2)

**Acceptance Scenarios**:

1. **Then** the MIDI map tab lists a slot mapping as *Launch · <track> · <scene>* and a scene
   mapping as *Launch · <scene>*, from the names the grid shows when the tab draws it.
2. **When** the project is saved and reopened, **Then** the mappings come back on the same track
   and row.

### Edge Cases

- **A row that no longer exists** (scenes removed): the mapping does nothing — not "stop the track",
  which a launch of a missing row would otherwise mean to the engine.
- **A track that is gone**: the mapping does nothing; on reopening it is dropped, like every other
  track mapping.
- **Rows moved or inserted**: a mapping keeps its grid position (track, row), as on a pad grid —
  it drives whatever sits at that row now.
- **A note mapped to any other button** (Mute, Solo, Play …): with note-offs reaching MIDI Learn,
  it fires on every press, not only the first. A note mapped to a continuous target keeps the
  last note-on's velocity, as before (the note-off is ignored there).
- **A note let go while a control waits to be learned**: a note-off never binds.
- **A CC button**: fires as it crosses the half-way point upward and releases on the way down,
  once each way (the existing latch), at full velocity.
- **A controller profile owns the port** (specs/051): an explicit MIDI Learn mapping wins, as for
  every other mapping.
- **Group slots, track Stop buttons, Stop All**: not part of this change (the issue asks for clip
  and scene launch buttons).

## Requirements

### Functional Requirements

- **FR-001**: In MIDI Learn mode the Session grid MUST offer each track slot's launch button and
  each scene's launch button as learn targets.
- **FR-002**: A mapped slot MUST do what a click on its launch button does (US1 AS2–AS4), through the
  engine alone, so it works with the Session view hidden.
- **FR-003**: A note mapping MUST launch with the note's velocity; its note-off, and a CC or gamepad
  button's way back, MUST release the slot (stopping a Gate / Repeat clip). The engine's MIDI Learn
  copy MUST carry note-offs (and note-ons at velocity 0) as value 0, also on a port a controller
  profile owns whole.
- **FR-004**: A mapped scene MUST launch its row.
- **FR-005**: A mapping to a row past the last one, or to a missing track, MUST do nothing.
- **FR-006**: The mappings MUST be saved with the project and restored on the same track and row;
  the integer target kinds are appended, so existing `midimap.json` files read as before.
- **FR-007**: The MIDI map tab MUST name a Session mapping by its track and scene as the grid shows
  them.

## Success Criteria

- **SC-001**: A pad controller launches eight clips and four scenes of a set, mapped in under a
  minute, with the Session view hidden.

## Assumptions

- A row is a grid position, not a scene identity (there is no stable scene id to follow): this is
  what a pad grid maps, and it stays predictable when rows move.
- Velocity comes from notes only; a CC, a gamepad button and a Remote control launch at full
  velocity, as the on-screen button does.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header
- [x] No new dependency
- [x] Audio-thread impact: none — the MIDI thread pushes note-offs into the existing lock-free learn queue; the rest is managed code on the UI thread
- [x] No new managed/native crossings (existing `nota_session_*` functions)
- [x] Smoke test extended: `SessionLaunchTests` drives the launch logic against the engine; the MIDI surface self-test and `ControllerTests` check that a note's release reaches MIDI Learn
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] New texts go through `Loc` by key (`midi_map.launch_clip`, `midi_map.launch_scene`)
