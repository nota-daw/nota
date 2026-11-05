# Feature Specification: Pick the Bend lane's range in the piano roll

**Feature Branch**: `073-bend-lane-range`

**Created**: 2026-11-03

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#426: "Draw a pitch bend bigger than 2 semitones in the
piano roll's Bend lane, from scratch, without an MPE controller. The Bend lane auto-scales to
the bend already on a note, so a note with no bend always opens at ±2 st. […] A way to choose
the Bend lane's range (±2 / 12 / 24 / 48 / 96 st) directly, e.g. a dropdown or clickable
`±2 st` label in the lane, a right-click option, or a per-clip/per-track setting."

## Nota Context

**Feature area**:

- [ ] Engine (C++, `src/native/nota.engine`)
- [x] Managed app (C#, `src/managed/*`) — the piano roll's Bend lane
- [x] UI (Avalonia) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: the lane under the piano roll's notes switches between
VEL, BEND, PRES, SLIDE and a controller. The Bend lane draws each note's bend curve in
semitones and scales itself to the largest bend on the clip's notes: ±2, 12, 24, 48 or 96
semitones, read out by a `±N st` label at its top left. Drawing is clamped to the range shown,
so on a clip without bends nothing past ±2 st can be drawn. The engine stores and plays bend in
semitones whatever the lane shows. The roll is rebuilt for every clip it opens; its scroll and
zoom are remembered per clip for the session (`s_view`).

## User Scenarios & Testing

### User Story 1 - Draw a big bend from scratch (Priority: P1)

A user wants a note to bend up an octave. Its clip has no bends, so the Bend lane shows ±2 st.
They click the `±2 st` label, choose ±12 st, and draw the bend up to +12.

**Independent Test**: on a clip with no bends, open the Bend lane, click `±2 st ▾`, choose
±12 st; the label reads ±12 st, the curve area spans ±12, and a stroke drawn to the top of the
lane stores +12 semitones.

**Acceptance Scenarios**:

1. **Given** the Bend lane, **Then** its range label carries a caret and highlights with a hand
   cursor under the pointer.
2. **When** the user clicks the label, **Then** a menu lists ±2, 12, 24, 48 and 96 st with the
   range shown checked.
3. **When** the user picks a range, **Then** the lane shows at least that range and strokes
   and points can reach it.

### User Story 2 - Nothing gets clipped (Priority: P1)

A clip already holds a 30 st bend (the lane shows ±48). Picking a narrower range would cut the
curve off, so the steps that are too narrow are disabled; a pick narrower than a bend drawn
later is widened past by the lane, as it does today.

**Acceptance Scenarios**:

1. **Given** a bend of 30 st on the clip, **When** the menu opens, **Then** ±2, 12 and 24 st
   are disabled and ±48 and 96 st are enabled.
2. **Given** a pick of ±12, **When** the notes' largest bend grows past 12 (a recording, an
   undo, a paste), **Then** the lane widens to the smallest step that fits it.
3. **Given** a pick of ±24 and a bend of 5 st, **Then** the lane shows ±24.

### User Story 3 - The pick stays with the clip (Priority: P2)

The user picks ±24 on a clip, opens another clip and comes back: the first clip still shows
±24, the other one its own range.

**Acceptance Scenarios**:

1. **Given** a pick on a clip, **When** the clip is reopened in the same session, **Then** the
   pick is kept, like the clip's scroll and zoom.
2. **Given** a multi-clip (track mode) view, **Then** a pick holds while that view is open.

### Edge Cases

- **A curve point under the label**: it stays grabbable; the label only opens the menu where
  no point is under the pointer.
- **View-only lane** (no store for curves): the range can still be picked; it changes only the
  view.
- **Pressure and Slide lanes**: no label, no menu; their scale is 0..1.
- **During a gesture** the range does not change, as before.
- **A moved clip**: a clip is known by its track and index, and a move changes the index.
  A pick holds only while the clip at that index starts where it did, so a moved clip opens at
  the default and no other clip inherits its pick.

## Requirements

### Functional Requirements

- **FR-001**: The Bend lane's range label MUST open a menu of the ranges ±2, 12, 24, 48 and
  96 semitones, the one shown checked.
- **FR-002**: The lane MUST show the smallest step that is at least the picked range and fits
  the largest bend on the notes.
- **FR-003**: Steps that would clip a bend already on the notes MUST be disabled in the menu.
- **FR-004**: The pick MUST be remembered per clip for the session, and forgotten when a
  project is opened, created or recovered, since the new project reuses track ids.
- **FR-005**: The gesture MUST be listed in Preferences → Shortcuts, through `Loc` with French.

## Success Criteria

- **SC-001**: Drawing a 12 st bend on a clip without bends takes one click on the label and one
  menu choice, instead of recording a bend from an MPE controller or Nota Remote first.

## Assumptions

- The range is a view of the curve, not a property of the clip: bends are stored in semitones,
  so nothing is saved with the project and nothing changes in the engine.
- The pick is not saved across launches: once a clip has a big bend the lane fits it anyway.
- The steps stay those the lane already uses (and the issue lists).

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header
- [x] No new dependency
- [x] Audio-thread impact: none — the piano roll on the UI thread
- [x] No new managed/native crossings
- [x] Smoke-test impact: `BendLaneRangeTests` cover the steps, the pick and the widening
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` (label ink, caret like the controller tab, MenuKit items)
