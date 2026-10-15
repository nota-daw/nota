# Feature Specification: Undo for Device Parameters and Arrangement Sections

**Feature Branch**: `fix/undo-device-params-sections`

**Created**: 2026-10-15

**Status**: Draft

## Clarifications

### Session 2026-10-15

- Q: Where should arrangement sections live so they can be undone — in the engine's project
  (one source of truth, saved in the project, visible to MCP / Nota Remote) or stay in the
  app with a bridge into the undo history? → A: Move them into the engine's project. The
  sections sidecar file of older projects is read once on load.
- Q: How is a continuous change grouped? → A: As for volume and pan (specs/009): a mouse drag
  from press to release is one step; a one-off change (a switch, a menu choice, a reset, a
  typed value) is one step; a MIDI-controller movement ends after a 1 s pause; one Nota
  Remote touch is one step; one MCP call is one step.
- Q: Devices nested inside racks and drum-kit pads? → A: Out of scope for this change: their
  parameters have no automation target and no gesture today. They follow on the same
  mechanism.

**Input**: User description: "Undo for device knobs and sections (nota-daw/nota#30,
nota-daw/nota#64). Ctrl+Z inside a device does nothing for a knob change and instead undoes
the last note edit (#30, reproduced with Nota Bass's cutoff). Creating, renaming, moving,
resizing or deleting an arrangement section can't be undone either (#64). Make every
user change of a device parameter one undo step in the one history, in order with all other
edits, and move the sections into the engine's project so their edits are undo steps too."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md`
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`BUILD.md`](../../BUILD.md),
[`FEATURES.md`](../../FEATURES.md), specs/009 (the volume / pan undo this builds on).

**Current state (for reference)**:

- One history (⌘Z / ⌘⇧Z, Edit menu, command palette, Nota Remote, MCP `undo`) covers
  structural edits and — since specs/009 — volume, pan and master-volume gestures. It does
  **not** cover device parameters: the engine's undo comment says so, and a knob change
  leaves the history untouched, so ⌘Z undoes whatever structural edit came before it (#30).
- Device parameters are changed from device cards (knobs, sliders, curve and graph
  handles, switches, step grids — built-in effects, built-in instruments, MIDI effects,
  hosted plug-ins), MIDI Learn, Nota Remote (macros, XY pad), and MCP tools
  (`set_device_param`, `set_instrument_param`, `set_plugin_param`, `set_midi_fx_param`
  and the device-specific tools). Every on-screen drag already reports its press and release
  to the engine (the automation-write "touch").
- Arrangement sections (named spans in the Sections lane) live only in the arrangement view
  and are saved to a `sections.json` file next to the project; the engine and its history
  never see them (#64). A failed sidecar write is silently ignored.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Take back a knob move (Priority: P1)

A producer turns Nota Bass's cutoff from 400 Hz to 300 Hz, doesn't like it and presses
⌘Z: the cutoff is back at exactly 400 Hz. ⌘⇧Z brings 300 Hz back. The same works for a
reverb's decay, an arpeggiator's rate, a plug-in's knob, a double-click reset and a
switch.

**Why this priority**: the reported bug (#30); the basic expectation of any DAW.

**Independent Test**: on an instrument and an effect, drag a knob, double-click-reset one,
flip a switch; undo and redo each.

**Acceptance Scenarios**:

1. **Given** a built-in instrument's knob at a value, **When** the user drags it in one
   press and release and presses ⌘Z, **Then** it is back at exactly the starting value and
   ⌘⇧Z returns the dragged value.
2. **Given** an effect, a MIDI effect or a hosted plug-in, **When** the user changes one of
   its parameters from its card, **Then** that change is one undo step.
3. **Given** a double-click reset, a switch, a menu choice or a step toggle, **When** it
   is done, **Then** it is one undo step.
4. **Given** a drag that ends where it started, **Then** no step is added.

---

### User Story 2 - One history, in order (Priority: P1)

The producer edits a note, turns a filter knob, then presses ⌘Z: the knob comes back
first, then the note — never the note first (#30).

**Why this priority**: undo that skips edits or jumps out of order is worse than none.

**Independent Test**: alternate note edits, knob moves, section edits and volume moves;
undo and redo the whole chain.

**Acceptance Scenarios**:

1. **Given** a mixed sequence of structural, volume/pan, device-parameter and section
   edits, **When** the user undoes repeatedly, **Then** each step reverts exactly the most
   recent remaining edit, and redo replays them in order.
2. **Given** an undone parameter change, **When** the user looks at the device card,
   **Then** it shows the restored value.
3. **Given** a device deleted after its parameter was changed, **When** the user undoes
   the delete and then the change, **Then** both come back correctly.
4. **Given** automation recording, **When** the user moves a knob in one gesture, **Then**
   the recorded lane and the value undo together as one step.

---

### User Story 3 - Take back a section edit (Priority: P1)

The producer drags out a new section, renames it "Chorus", stretches it by four bars, then
deletes another by mistake. ⌘Z brings the deleted section back, then shrinks "Chorus" to
its old length, then gives it its old name, then removes it — one step each.

**Why this priority**: the reported bug (#64).

**Independent Test**: create, rename, move, resize, duplicate, delete and delete-all; undo
and redo each.

**Acceptance Scenarios**:

1. **Given** each section edit (create, rename, move, resize, duplicate, delete, delete
   all), **When** it is done and the user presses ⌘Z, **Then** the lane shows the sections
   exactly as before that edit, and ⌘⇧Z redoes it.
2. **Given** a move or resize drag, **Then** the whole drag is one step; a click that only
   moves the playhead is no step.
3. **Given** a project with sections, **When** it is saved and reopened, **Then** the
   sections are there, and the history starts empty.
4. **Given** a project saved by an older build (sections in the sidecar file), **When** it
   is opened, **Then** its sections appear; after the next save they are stored in the
   project itself.
5. **Given** sections, **When** the user opens a new project, **Then** there are none.

---

### User Story 4 - Every source of a parameter change is undoable (Priority: P2)

A parameter moved from a MIDI controller, from Nota Remote (macro or XY pad) or by the AI
assistant (MCP) can be taken back with ⌘Z.

**Why this priority**: consistency across sources; on-screen editing (US1) carries most of
the value.

**Independent Test**: change a parameter once from each source and undo each.

**Acceptance Scenarios**:

1. **Given** a MIDI controller knob mapped to a parameter, **When** the user turns it and
   stops, **Then** the movement up to a 1 s pause is one step.
2. **Given** a Nota Remote macro knob or XY pad, **When** a finger moves it and lifts,
   **Then** the touch is one step.
3. **Given** an MCP call that sets one or several parameters, **When** it completes,
   **Then** it is one step.
4. **Given** the MCP tools, **When** the AI reads sections, **Then** it gets the same list
   the lane shows, and it can replace them in one undoable step.

---

### User Story 5 - Things that are not edits stay out (Priority: P1)

Automation playback moves knobs by itself; a project load, a preset load, a new project and
a freeze set parameters internally. None of that fills the history.

**Acceptance Scenarios**:

1. **Given** device-parameter automation in read mode, **When** the song plays, **Then** no
   undo steps appear.
2. **Given** a project load or a new project, **When** the user presses ⌘Z, **Then** no
   parameter or section step is undone and the loaded sections stay. (A new project has
   nothing to undo; a load's own track-building steps predate this feature and are out of
   scope.)

### Edge Cases

- **Undo during playback** with automation in read mode on that parameter: the automation
  keeps winning, as for a hand move today.
- **A step whose device or track no longer exists** (deleted, not undone): skipped quietly;
  the next step is undone instead.
- **A gesture interrupted** (focus lost, device removed mid-drag): the change so far is one
  step; nothing is left half-open.
- **Two parameters in one gesture** (a curve handle moving frequency and gain; an A/B
  switch restoring every parameter): one step.
- **Card refresh**: knobs follow the restored value on the next UI tick, including when
  the card is closed and reopened.
- **History depth**: parameter and section steps count against the existing limit.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every user change of a parameter of a track's instrument, audio effect, MIDI
  effect or hosted plug-in (track or master chain) MUST be undoable and redoable through
  the existing history.
- **FR-002**: Every user edit of the arrangement sections — create, rename, move, resize,
  duplicate, delete, delete all — MUST be undoable and redoable through the same history.
- **FR-003**: Parameter and section steps MUST share one ordered history with all other
  undoable edits.
- **FR-004**: One gesture MUST be exactly one step, grouped as in the Clarifications.
- **FR-005**: A step MUST restore the exact previous value(s) and redo the exact new ones; a
  gesture that ends where it started MUST NOT add a step.
- **FR-006**: Automation playback, project load, a new project, preset loads and other
  internal settings MUST NOT create steps; automation recording MUST undo together with the
  value it recorded.
- **FR-007**: A parameter step whose device or track no longer exists MUST be skipped
  without error.
- **FR-008**: Sections MUST be part of the project held by the engine and saved inside the
  project file. A project from an older build MUST open with its sections (read once from
  the sidecar file); a project load MUST leave no section step behind.
- **FR-009**: MCP MUST be able to list sections and to replace them in one undoable step;
  Nota Remote keeps showing the same sections as the lane.
- **FR-010**: The automated smoke test MUST cover: a knob gesture (begin, values, end) on an
  effect, a built-in instrument and a MIDI effect is one exact step; a no-change gesture
  adds none; parameter, note and section steps interleave in order; automation playback adds
  none; recording merges; a step for a removed device is skipped; MIDI-controller and MCP
  sources are one step each; section create / rename / move / delete undo and redo; sections
  survive save → load, and a legacy sidecar is migrated.
- **FR-011**: The change MUST be listed in the changelog, FEATURES.md and the user docs
  (EN + RU) where undo is described.

### Key Entities

- **Value step**: an undo step holding before/after values for one or more targets —
  volume, pan, master volume (specs/009) and now device parameters, addressed by track,
  device kind (instrument, audio effect, MIDI effect, hosted plug-in) and parameter.
- **Section**: a named span of arrangement time (start, length, name), held in the
  project next to the tempo map, scenes and pan law.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100 % of device-parameter changes made from the listed sources can be undone
  and redone, landing on the exact previous value.
- **SC-002**: 100 % of section edits can be undone and redone.
- **SC-003**: A mixed sequence of 50 structural, mix, parameter and section edits undoes and
  redoes in exactly the reverse / forward order.
- **SC-004**: Playing a song with device-parameter automation for 10 minutes adds 0 steps.
- **SC-005**: Every project saved with sections by an earlier build opens with the same
  sections.

## Assumptions

- Undo steps have no labels (Nota's history has none).
- Devices inside racks and drum-kit pads, rack macros and rack chain levels are out of
  scope (clarified); so are bypass, sidechain routing and device presets (a preset load is
  not an undo step today and stays so).
- Section colours follow their position, as today; there is no recolour edit.
- MCP calls run one at a time on the UI thread; each call is one step.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), immutable snapshots for structural edits (AR-5) — sections are graph-level data the audio thread never reads
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7)
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned, FEATURES.md and user docs via the nota-features / nota-docs skills
- [x] UI change → no new UI; the Sections lane keeps its look
