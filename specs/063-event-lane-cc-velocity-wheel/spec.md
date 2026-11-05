# Feature Specification: Event lane — MIDI CC lanes and the velocity wheel

**Feature Branch**: `063-event-lane-cc-velocity-wheel`

**Created**: 2026-10-30

**Status**: Draft

**Input**: User description: "GitHub issue nota-daw/nota#169 — Event Line Editor: add more automation source controls, plus UX improvements. Add the ability to switch the lane's automation control to any MIDI CC. Add the ability to change note velocity with Shift + mouse wheel: with notes selected the selection changes; with the cursor on one note only that note changes. Custom: a setting for the wheel direction (default: up = plus, down = minus) and a multiplier for the change (default 1, range 0–2). Optional: snap the change to the piano roll grid."

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

**Existing behaviour this builds on**:

- The "event line editor" is the piano roll's lane under the notes, switched from its
  gutter between VEL / BEND / PRES / SLIDE (`PianoRollView.Expression.cs`).
- MIDI clips and Session slots already store, record and play one curve per instrument-wide
  controller — any CC 0–127 except the reserved ones, the pitch wheel and channel pressure
  (specs/020, PR 1: `nota_midi_clip_ctl_*`, `nota_session_ctl_*`, `IAudioEngine.Get/SetMidiClipController`).
  Nothing draws or edits them yet: specs/020 User Story 2 (tasks T032–T039,
  [contracts/ui-lanes.md](../020-midi-cc-record-automate/contracts/ui-lanes.md)) designed
  that lane and was not built. This feature builds it.
- Shift + wheel over the note grid scrolls the piano roll sideways (specs/019 FR-003); ⌥↑ / ⌥↓
  already change the selection's velocity by 10 (⌥⇧ by 1).
- An earlier attempt (PR #292) bundled this issue with tempo automation and MIDI file
  import/export and was closed. This spec covers #169 only.

## Clarifications

### Session 2026-10-30

- Q: Shift + wheel already scrolls the piano roll sideways. Where does it change velocity instead? → A: Only with the pointer over a note: that note, or the whole selection when the note under the pointer is selected. Anywhere else (empty grid, lanes, ruler) Shift + wheel keeps scrolling, so a selection never takes the scroll away.
- Q: How big is one wheel step? → A: One MIDI velocity step (1 of 127) per wheel notch at 1×; the multiplier scales it (2× = 2 per notch, 0.5× = 1 per two notches). A trackpad moves in proportion to its travel. Velocity stays within 1–127, the range ⌥↑ / ⌥↓ use.
- Q: Does the multiplier apply to both directions? → A: Yes, one multiplier for up and down; at 0× the gesture is off and Shift + wheel scrolls everywhere.
- Q: Undo granularity of the wheel? → A: One undo step per burst: wheel moves less than 0.6 s apart are one edit.
- Q: Where do the two settings live? → A: Preferences → Shortcuts, in a "Velocity wheel" section above the reference list, next to the gesture they change.
- Q: The optional "snap the velocity change to the grid (size + multiplier)" item? → A: Out of scope for this PR: the formula maps a time grid onto a value step and needs its own design; the issue marks it optional.
- Q: The controller lane's design? → A: As specs/020 [contracts/ui-lanes.md](../020-midi-cc-record-automate/contracts/ui-lanes.md) says, with one change: the gutter tab is 48 px wide, so it shows the controller's short name when it fits ("Sustain", "CC 74") and the lane's top-left corner always names it in full ("Mod wheel · CC 1"). The "Track automation overrides this lane" note waits for the CC automation target (specs/020 US3), which does not exist yet.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Draw and edit any MIDI controller under the notes (Priority: P1)

A user records a piano part with the sustain pedal, opens the clip and switches the lane
under the notes to Sustain: the pedal shows as on/off blocks they can move, stretch, draw
and delete. They switch to Mod wheel (or any CC 0–127, the pitch wheel or channel pressure)
and draw a curve; playback follows on the next pass.

**Why this priority**: It is the first request of the issue and the clip data already exists
but cannot be seen or edited.

**Independent Test**: In a clip with and without recorded controllers, pick Sustain, Mod
wheel, Pitch wheel, Channel pressure and CC 74 from the lane's picker; draw, move and delete;
undo each gesture once; play and hear the change.

**Acceptance Scenarios**:

1. **Given** the piano roll, **When** the user clicks the fifth gutter tab, **Then** the lane shows the current controller (Sustain, or the first one with data in the clip); a click on the tab while it is shown opens the picker: controllers with data first (marked), then Sustain, Mod wheel, Pitch wheel, Channel pressure, Expression and Breath, then **Other CC ▸** with every non-reserved CC in groups of 16.
2. **Given** a switch controller (CC64–69), **When** the user drags on an empty stretch, **Then** a block is drawn on the grid (⌘/Ctrl: off the grid); dragging a block's edge resizes it, dragging its body moves it, a double-click deletes it.
3. **Given** a continuous controller, **When** the user drags across the lane, **Then** the drawn stretch replaces the points under it (⌥: a straight line); a point can be dragged between its neighbours; a double-click adds a point or deletes the one under the pointer; the pitch wheel snaps to centre within 2 %.
4. **Given** any controller lane, **When** the user right-clicks, **Then** *Clear <name>* removes that controller from the clip.
5. **Given** any controller edit, **When** the user presses Undo once, **Then** the whole gesture is undone, and the next playback pass plays the edited curve.
6. **Given** several clips open, **Then** the lane edits the active clip's controllers at that clip's position; a Session slot's editor edits the slot's.

---

### User Story 2 - Change velocity with Shift + wheel (Priority: P2)

A user hovers a note and rolls the wheel with Shift held: its velocity steps up or down,
the note's shade and stem follow. With several notes selected, hovering any of them and
rolling changes them all. The direction and the step multiplier are set in Preferences.

**Why this priority**: A faster way to shape dynamics than dragging stems, but the lane and
⌥↑ / ⌥↓ already cover the need.

**Independent Test**: Hover a lone note, Shift + wheel up three notches: velocity +3. Select
four notes, hover one, wheel down: all four go down together. Undo once: all back. Reverse
the direction and set 2× in Preferences: one notch up now lowers by 2. Over empty grid,
Shift + wheel still scrolls.

**Acceptance Scenarios**:

1. **Given** the pointer over an unselected note, **When** Shift + wheel moves one notch up, **Then** that note's velocity rises by one MIDI step and no other note changes.
2. **Given** a selection and the pointer over one of its notes, **When** Shift + wheel moves, **Then** every selected note changes by the same steps, each clamped to 1–127.
3. **Given** the pointer over empty grid (with or without a selection), **When** Shift + wheel moves, **Then** the roll scrolls sideways as before.
4. **Given** Preferences → Shortcuts → Velocity wheel set to Reverse and 2×, **When** the wheel moves one notch up over a note, **Then** its velocity falls by two steps; at 0× Shift + wheel scrolls everywhere.
5. **Given** a burst of wheel moves, **When** the user presses Undo once, **Then** the velocities return to where the burst began.

### Edge Cases

- A note of a reference (locked) clip is never changed by the wheel; the wheel scrolls there.
- A velocity already at 127 (or 1) stays; the burst still ends normally.
- A trackpad's small deltas accumulate, so a slow swipe still steps; one event never jumps more than two notches' worth.
- A controller lane on a clip that is gone (undo, project load) draws empty and writes nothing.
- A block drawn past the clip's end is cut at the end; one drawn over another block merges with it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The lane under the notes MUST offer a fifth mode that shows one instrument-wide controller of the active clip, picked from Sustain, Mod wheel, Pitch wheel, Channel pressure, Expression, Breath and any non-reserved CC 0–127; controllers holding data in the clip MUST be marked and listed first (specs/020 FR-010).
- **FR-002**: Switch controllers MUST draw and edit as on/off blocks; partial values show as the block's fill height (specs/020 FR-011).
- **FR-003**: Continuous controllers MUST support freehand drawing, ⌥ straight lines, point drag, double-click add/delete and right-click clear, consistent with the MPE lanes (specs/020 FR-012).
- **FR-004**: Every controller gesture MUST be one undo step and MUST be heard on the next playback pass (specs/020 FR-013).
- **FR-005**: Shift + wheel over an editable note MUST change the velocity of that note, or of the whole selection when the note is selected, by one MIDI step per notch × the multiplier, clamped to 1–127.
- **FR-006**: Shift + wheel anywhere else, and everywhere when the multiplier is 0, MUST keep scrolling sideways (specs/019 FR-003).
- **FR-007**: The wheel direction (default: up raises) and the multiplier (default 1×, 0–2×) MUST be user settings, persisted per user and editable in Preferences.
- **FR-008**: A burst of wheel moves MUST be one undo step.
- **FR-009**: The Shortcuts reference MUST list the new gestures.

## Success Criteria *(mandatory)*

- **SC-001**: Every controller shown by the picker can be drawn, edited and cleared in a clip and in a Session slot, and a single Undo reverses each gesture.
- **SC-002**: A recorded sustain pedal opens as blocks at the beats it was pressed.
- **SC-003**: Shift + wheel over empty grid still scrolls in every configuration.
- **SC-004**: The velocity-step computation (direction, multiplier, accumulation, clamping) is covered by the smoke test.

## Assumptions

- No engine or C ABI change: the controller store, its undo and playback are specs/020 PR 1.
- Copying notes does not copy controllers (specs/020 contract).
