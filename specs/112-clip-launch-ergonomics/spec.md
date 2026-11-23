# Feature Specification: Clip launching ergonomics

**Feature Branch**: `feat/clip-launch-ergonomics`

**Created**: 2026-11-23

**Status**: Draft

**Input**: User description: "Issue nota-daw/nota#364 — Clip launching ergonomics. In the Session view the rec / stop buttons are microscopic. The arrows already walk the slot selection; there should be an intuitive key for record/play on the selected clip and one for stop on the selected track (two keys to use with the arrows), as well as play all within a row and stop all."

**Issue**: [nota-daw/nota#364](https://github.com/nota-daw/nota/issues/364) (approved). Builds on
[specs/096](../096-custom-shortcuts/) (rebindable keys, Session scope) and
[specs/108](../108-session-grid-orientation/) (grid orientation; the arrows follow the screen).

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

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Play and record the selected slot from the keyboard (Priority: P1)

A musician walks the Session grid with the arrow keys. On a clip, Return launches it. On an empty
slot of an armed track, Return starts recording into that slot — exactly what clicking the slot's
record circle does — and Return again on the recording slot stops the take. On an empty slot of an
unarmed track, Return stops that track, as it does today.

**Why this priority**: Recording into a slot is the one launch action that has no key today; the
musician has to reach for a tiny circle with the mouse in the middle of a performance.

**Independent Test**: Arm a track, select an empty slot with the arrows, press Return → the slot
records. Press Return again → the take ends and the slot holds the new clip.

**Acceptance Scenarios**:

1. **Given** a filled slot is selected, **When** the user presses Return, **Then** the clip launches (unchanged behaviour).
2. **Given** an empty slot on an armed track is selected, **When** the user presses Return, **Then** that slot starts recording.
3. **Given** the selected slot is recording, **When** the user presses Return, **Then** the recording stops, as a click on its launch button does.
4. **Given** an empty slot on an unarmed track is selected, **When** the user presses Return, **Then** that track stops (unchanged behaviour).

---

### User Story 2 - Stop the selected track, launch the row, stop all (Priority: P1)

With the selection on any slot, ⇧Return stops whatever plays on that slot's track (on a group's
slot: every track in the group). ⌘Return (Ctrl+Return on Windows/Linux) launches the selected
slot's whole scene row. ⌘⇧Return stops every clip, as the grid's Stop All button does.

**Why this priority**: These are the "stop on the selected instrument", "play all within a row" and
"stop all" asks of the issue; together with Return they make the Session playable from the
keyboard with the arrows.

**Independent Test**: Launch two clips on different tracks; select one with the arrows, ⇧Return →
only that track stops. ⌘Return on a slot → its whole row launches. ⌘⇧Return → everything stops.

**Acceptance Scenarios**:

1. **Given** a clip plays on track A and the selection is on any slot of track A, **When** the user presses ⇧Return, **Then** track A stops and other tracks keep playing.
2. **Given** the selection is on a group's slot, **When** the user presses ⇧Return, **Then** every track in that group stops.
3. **Given** any slot or scene is selected, **When** the user presses ⌘Return, **Then** that scene row launches and the selection moves as a scene launch does today.
4. **Given** clips play, **When** the user presses ⌘⇧Return, **Then** every clip stops.
5. **Given** the user opens Preferences → Shortcuts, **When** they look at the Session group, **Then** all four keys are listed and can be rebound; the palette commands show the keys.

---

### User Story 3 - Buttons you can hit (Priority: P2)

The record circle and stop square drawn in an empty slot, and the stop button under each track, are
large enough to see at a glance and hit without precise aiming.

**Why this priority**: Directly reported ("microscopic"); the keys help, but the mouse still needs
to work.

**Independent Test**: Open the Session at the default zoom; the record circle in an armed empty
slot is about twice its old size and its click area is larger; the per-track stop glyph is
larger.

**Acceptance Scenarios**:

1. **Given** an armed track, **When** the user looks at an empty slot, **Then** the record circle is at least 11 px across (was 6.5 px) and a click within 12 px of the slot's centre records.
2. **Given** an empty slot with a stop button, **When** the user looks at it, **Then** the stop square is at least 10 px (was 6 px) with the same larger click area.
3. **Given** the per-track stop buttons under the grid, **When** the user looks at them, **Then** their glyph is at least 10 px (was 7 px); the Stop All button grows to match.

### Edge Cases

- A text field (rename box, tempo) has focus: the keys type into it, they do not launch.
- The selection is on a Return / Master column: ⇧Return does nothing (no clips there); ⌘Return still launches the row; ⌘⇧Return still stops all.
- The selection is a scene row: ⇧Return does nothing (no single track); Return and ⌘Return launch the row.
- A slot is so small (narrow lanes) that the enlarged click area would exceed it: the area is clipped to the slot.
- A user rebinds one of the keys to something already taken in the Session scope: the Shortcuts screen's existing conflict check applies.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Return on a selected empty slot of an armed track MUST start recording into that slot, as a click on its record circle does.
- **FR-002**: Return on a selected slot that is recording MUST stop that recording, as a click on its launch button does.
- **FR-003**: Return on a filled slot, a group slot, a scene, or an empty unarmed slot MUST keep today's behaviour.
- **FR-004**: ⇧Return MUST stop the selected slot's track (a group slot: all its tracks), as the track's stop button does.
- **FR-005**: ⌘Return (Ctrl+Return) MUST launch the selected slot's (or scene's) scene row, as the scene's launch button does.
- **FR-006**: ⌘⇧Return (Ctrl+Shift+Return) MUST stop all clips, as Stop All does.
- **FR-007**: The keys in FR-004–FR-006 MUST be the defaults of the existing commands "Stop selected track", "Launch scene" and "Stop all clips", scoped to the Session view, rebindable, and shown in the Shortcuts screen, the command palette and the Session menus.
- **FR-008**: The keys MUST act only while the Session view has focus and not while a text field has focus; elsewhere ⇧Return / ⌘Return keep whatever they do today.
- **FR-009**: The empty-slot record circle and stop square MUST be drawn at least 11 px / 10 px, with a click area of at least 24 × 24 px centred in the slot (clipped to the slot).
- **FR-010**: The per-track stop buttons and Stop All MUST draw their stop glyph at least 10 px.
- **FR-011**: A controller's launch button (which uses the same "toggle selected slot" path) MUST gain the record behaviour of FR-001/FR-002 too.

### Key Entities

None — no new data; no project-file change.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can record a clip into any armed track's slot, stop it, launch a row and stop everything without touching the mouse — four keys plus the arrows.
- **SC-002**: The record circle and stop square in empty slots are at least 1.6× their old size; the click area is at least 1.5× wider.
- **SC-003**: The smoke test drives the record / stop-track / launch-row / stop-all actions end to end and passes.

## Assumptions

- Return already launches the selection (specs/096); the issue's "record/play" key stays Return rather than a new letter, since the letter keys belong to the computer-keyboard piano.
- Modified Return keys are free in the Session scope today (only the command palette uses them, inside its own popup).
- No engine change: the engine already records into a slot and stops a track; this is a key and drawing change in the app.
- "Toggle" behaviour (Return on a playing clip stops it) is not added: Return keeps re-launching, as a click does; ⇧Return is the stop.
