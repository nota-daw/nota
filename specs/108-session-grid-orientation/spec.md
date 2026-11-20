# Feature Specification: Session view — turn the grid, scenes across and tracks down

**Feature Branch**: `feat/session-grid-orientation`

**Created**: 2026-11-20

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#968 (approved): "Choose which way the Session grid runs on
the desktop. Today Session view always has a scene per row and a track per column […] I want to
flip it, with a scene per column and a track per row […] A preference: Session grid: Scenes down
(default) / Scenes across. […] Group tracks and their trays, clip contents, the playing and queued
indicators, and clip drag between slots all work the same, turned 90°. Keyboard navigation follows
what you see […] The choice is saved in the app preferences, not the project, and switching
applies right away. […] Nota NX (the Switch remote) gets the same choice for its Scenes grid […]"

## Nota Context

**Feature area**:

- [ ] Engine (C++, `src/native/nota.engine`) — no engine change: the grid's orientation is a view
  choice; scenes and tracks stay what they are
- [x] Managed app (C#, `src/managed/*`) — the preference in app settings, the Session commands
  that walk the grid, the Remote hub telling phones and the Switch what the desktop uses
- [x] UI (Avalonia, `src/managed/Nota.App` Session view + Preferences) — follows `DESIGN.md`
- [x] Nota Remote phone page (`src/web/remote`) and Nota NX (separate repository `nota-nx`)
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`DESIGN.md`](../../DESIGN.md), [`FEATURES.md`](../../FEATURES.md),
[`specs/102-remote-session-grid`](../102-remote-session-grid/spec.md) (Remote's own Scenes down /
across choice), [`specs/096-custom-shortcuts`](../096-custom-shortcuts/spec.md) (arrow keys are
fixed list navigation), [`specs/051-controller-auto-mapping`](../051-controller-auto-mapping/spec.md)
(controller Session commands).

**Existing behaviour this builds on**: Session view lays tracks across and scenes down. A
"Scenes" header sits in the top-left corner; track (and group) headers form a row across the top;
the scene rail runs down the left with each scene's name and launch button and can be dragged to
reorder scenes; Return and Master columns close the row at the right, mixer-only. Under the cells,
pinned to the bottom, come the Stop row (Stop All in the rail's corner) and the mixer sections the
toolbar shows or hides — I/O, Sends, Mixer (pan, volume, meter, mute / solo / arm) — one strip per
column, with their row labels in the scene rail. Headers pin to the top, the cells scroll up and
down between them, and all three strips scroll sideways together. Plain arrow keys walk the
selection on screen (↑/↓ scenes, ←/→ tracks, ← off the first track lands on the scene). The
commands `session.selectUp/Down/Left/Right` (palette, shortcuts, controllers) do the same moves;
the controller setup calls up/down "slot up / down" and left/right "slot left / right". Nota
Remote has its own per-phone Scenes down / Scenes across choice (specs/102); Nota NX has none — its
Scenes grid is always scenes down.

## User Scenarios & Testing

### User Story 1 - Turn the desktop Session grid (Priority: P1)

A producer whose sets have many scenes and a handful of tracks wants scenes to run left to right,
like sections on a timeline, with each track a row. They pick "Scenes across" in Preferences and
the Session view redraws turned, at once; every clip, launch button and Stop works as before.

**Why this priority**: it is the issue's core ask; everything else mirrors it.

**Independent Test**: open a project with groups, 8 scenes and 6 tracks; pick Scenes across: the
grid shows a scene per column with its name and launch button in a row across the top, a track per
row with its header at the left, every clip in the slot it was in (scene X / track Y), and
launching, stopping, selecting and dragging clips work; restart Nota — the grid is still turned.

**Acceptance Scenarios**:

1. **Given** Preferences, **Then** a "Session grid" choice offers "Scenes down" (the default, as
   today) and "Scenes across"; picking one redraws an open Session view at once, with no restart.
2. **Given** Scenes across, **Then** each scene is a column headed by its name and launch button,
   in a row across the top that moves with the grid as the scene rail does today; each track is a
   row headed by its name, colour, kind and the "back to Arrangement" mark, pinned at the left
   while the slots scroll sideways (as track headers stay pinned at the top today); the top-left
   corner holds the "Scenes" label; "+ Scene" closes the scene row.
3. **Given** Scenes across, **Then** each track's Stop opens the strip pinned at the right of its
   row (before its mixer block), and Stop All heads that strip in the top-right corner — the
   Stop row and Stop All of today, turned.
4. **Given** Scenes across, **Then** every slot shows the same contents and states as in Scenes
   down — clip name, colour, note / waveform preview, length, progress, queued blink, playing,
   recording, record affordance on armed empty slots, follow-action marks.
5. **Given** Scenes across, **When** a clip is dragged to another slot (Alt / Option to copy), a
   Browser item dropped on a slot or a track header, or a scene dragged to reorder, **Then** it
   lands where the pointer shows, in the turned layout; scene reordering drags sideways.
6. **Given** Scenes across, **Then** group tracks are rows with their summary slot per scene;
   folding a group hides its member rows; nested groups fold as today.
7. **Given** either orientation, **When** Nota restarts or another project opens, **Then** the
   choice is kept — it belongs to the app on this computer, not to the project.

---

### User Story 2 - Keys follow the screen, controllers follow the grid (Priority: P1)

The producer walks the turned grid with the arrow keys: ← / → move between scenes and ↑ / ↓
between tracks, as they see it. Their MIDI controller's "slot up / down" buttons keep stepping
through scenes and "slot left / right" through tracks, as printed on the controller and in the
controller setup, whichever way the screen is turned.

**Why this priority**: a turned grid that the keyboard walks sideways is broken; a controller that
changes meaning with a screen preference breaks muscle memory and the shipped profiles.

**Independent Test**: in Scenes across, select a slot and press → — the selection moves to the next
scene in the same track; press ↓ — it moves to the next track; with a controller mapped by the
shipped profile, its slot-down button moves to the next scene in both orientations.

**Acceptance Scenarios**:

1. **Given** Scenes across, **When** the arrow keys are pressed, **Then** ← / → go to the previous /
   next scene and ↑ / ↓ to the previous / next track; ↑ off the first track lands on the scene
   (the scene header is selected), as ← off the first track does in Scenes down.
2. **Given** Scenes across, **Then** Return and Master rows come after the tracks: ↓ off the last
   track walks into them, ← / → do nothing there.
3. **Given** either orientation, **When** a controller or a shortcut runs the "previous / next
   scene" commands, **Then** the selection moves by scene; "previous / next track" moves by track.
4. **Given** the command palette and Preferences → Shortcuts, **Then** the four Session
   selection commands are named by what they do (previous / next scene, previous / next track),
   not by screen direction, and existing bindings and controller mappings keep working unchanged.

---

### User Story 3 - Track mixer in the turned grid (Priority: P2)

With Scenes across, the producer still sets levels, pans, sends and arms from the Session view,
without switching to the Mixer.

**Why this priority**: the Session mixer strips are part of the view today; losing them would make
the turned grid a step back, but the grid itself is usable without them.

**Independent Test**: in Scenes across, show the Mixer and Sends sections from the toolbar; each
track row carries its volume, pan, meter, mute / solo / arm and its send amounts; changing them
changes the track as in Scenes down; hide the sections — the rows shrink back.

**Acceptance Scenarios**:

1. **Given** Scenes across, **Then** the toolbar's I/O, Sends and Mixer toggles still show or hide
   those sections, now as a block at the trailing (right) end of each track row, in the same
   order (I/O, Sends, Mixer) left to right; their labels sit in the pinned top-right corner, as
   they sit in the scene rail today.
2. **Given** a mixer section shown, **Then** it stays pinned at the right while the slots scroll
   sideways, as it stays pinned at the bottom while they scroll up and down in Scenes down.
3. **Given** Return and Master rows, **Then** they carry only their mixer section, as their
   columns do today.

---

### User Story 4 - Nota Remote and Nota NX (Priority: P2)

On stage the producer uses the phone and the Switch as well. The phone already lets them turn its
Scenes grid; the Switch gets the same choice, and both can simply show the grid the way the
desktop does.

**Why this priority**: the issue asks for parity on both remotes; the phone already has most of it.

**Independent Test**: on the desktop pick Scenes across; on a phone set to follow Nota, the Scenes
grid turns too; on the Switch, open the Scenes screen — scenes run across, tracks down, and the
D-pad / sticks move by screen direction; change the desktop back — both remotes follow.

**Acceptance Scenarios**:

1. **Given** Nota NX's Scenes screen, **Then** it offers the same orientation choice as Remote,
   and in Scenes across shows a scene per column (launch at the top, pinned) and a track per row
   (name and Stop at the left, pinned), Stop All in the corner.
2. **Given** Nota NX in Scenes across, **Then** the sticks and touch scroll by what is on screen
   (sideways through scenes, up / down through tracks); taps launch, stop and launch scenes as in
   Scenes down.
3. **Given** Nota Remote or Nota NX, **Then** their orientation choice offers "As in Nota" (the
   default), "Scenes down" and "Scenes across"; with "As in Nota" the grid follows the desktop's
   preference and turns live when it changes there; with either fixed choice the device keeps
   its own, remembered on that device.
4. **Given** a phone that already picked Scenes across before this feature, **Then** it keeps
   Scenes across; every other phone starts on "As in Nota" (which looks the same as its old
   Scenes down until the desktop turns).
5. **Given** an older Nota that does not tell remotes its orientation, **Then** "As in Nota" shows
   Scenes down and nothing breaks.

### Edge Cases

- **No scenes / no tracks**: the turned grid shows the pinned headers it has; nothing breaks.
- **Many scenes, wide window**: scene columns scroll sideways with the scene header row; the
  track headers and the Stop / mixer strip stay pinned.
- **Many tracks**: the grid scrolls up and down as a whole, the scene row with it — as the scene
  rail scrolls sideways with the grid today.
- **Switching while clips play or record**: playback and recording carry on untouched; the
  selection, folded groups and scroll position by scene / track are kept where possible.
- **Switching with the inspector open**: the inspector keeps editing the same clip, scene or slot.
- **Text that no longer fits**: scene names in a narrower column header and long track names in
  the row header are cut with an ellipsis and show in full on hover, as track names do today.
- **Session view not open**: the choice applies the next time Session view shows.
- **A project made with one orientation opened by someone using the other**: the project is
  unchanged; each person sees their own orientation.
- **Gamepad / MIDI grid controllers (#533)** that light pads by scene and track: they keep their
  own scene / track layout; the screen preference does not turn the controller's pads.

## Requirements

### Functional Requirements

- **FR-001**: Nota MUST offer a "Session grid" preference with "Scenes down" (default) and "Scenes
  across", saved in the app settings on this computer and never in the project.
- **FR-002**: Changing the preference MUST redraw an open Session view immediately, without a
  restart, without touching playback, recording or the project, and keeping the selection.
- **FR-003**: In Scenes across, the Session view MUST be today's layout turned: a scene per
  column and a track (or group) per row; scene headers (name, launch, selection, rename, context
  menu, drag to reorder) in a row across the top; track headers (name, colour, kind, fold,
  back-to-Arrangement, selection, rename, context menu, Browser drop) pinned at the left; each
  track's Stop and mixer block pinned at the right, Stop All above them.
- **FR-004**: Every slot interaction available in Scenes down — select, launch, stop, record,
  rename, colour, copy / cut / paste / duplicate / delete, drag-move / copy, Browser drop,
  context menu, inspector editing — MUST work identically in Scenes across on the same
  scene / track.
- **FR-005**: Group rows MUST show their summary slot per scene and fold their members as group
  columns do, including nested groups; Return and Master MUST follow the tracks as mixer-only rows.
- **FR-006**: Plain arrow keys MUST move the selection by screen direction in both orientations
  (in Scenes across: ← / → by scene, ↑ / ↓ by track), stepping onto scene headers and Return /
  Master rows as in Scenes down, turned.
- **FR-007**: The Session selection commands MUST be defined by grid meaning — previous / next
  scene, previous / next track — and keep that meaning in both orientations; existing shortcut
  bindings and controller mappings to them MUST keep working with the same effect as today.
- **FR-008**: The per-track mixer sections (I/O, Sends, Mixer) MUST remain available in Scenes
  across, shown and hidden by the same toolbar toggles, as a block at the right end of each
  track row, pinned while the slots scroll sideways.
- **FR-009**: Nota NX's Scenes screen MUST show Scenes across with the same layout rules as
  Nota Remote's (scene launch pinned at the top, track names and Stop pinned at the left, Stop All
  in the corner), with stick / touch scrolling that follows the screen.
- **FR-010**: Nota MUST tell connected remotes its Session grid orientation and every change of
  it. Nota Remote and Nota NX MUST offer "As in Nota" (default), "Scenes down" and "Scenes
  across", remembered per device; "As in Nota" follows the desktop live. A remote talking to an
  older Nota MUST treat "As in Nota" as Scenes down; a phone's existing Scenes across choice MUST
  be kept.
- **FR-011**: The preference MUST be reachable from Preferences and from the Session view itself
  (its toolbar), so switching does not require leaving the view.

### Key Entities

- **Session grid orientation**: an app preference on this computer — Scenes down or Scenes across.
  Each remote holds its own value per device — As in Nota (default), Scenes down or Scenes
  across.
- **Session selection commands**: previous / next scene and previous / next track — the grid moves
  that shortcuts, the palette and controllers run, independent of how the grid is drawn.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Switching orientation takes at most two clicks from the Session view and the grid is
  redrawn in under half a second on a project with 32 tracks × 32 scenes.
- **SC-002**: Every action available on a slot, scene or track header in Scenes down is available
  and has the same result in Scenes across (checked against the full Session action list).
- **SC-003**: With either orientation, a controller using a shipped profile moves the selection by
  scene and by track exactly as before this feature; no existing binding needs re-mapping.
- **SC-004**: On Nota NX, launching any visible scene, stopping any visible track or stopping
  everything takes one press with no scrolling, in both orientations.
- **SC-005**: The orientation survives restarting Nota, opening another project, and (on remotes)
  reconnecting.

## Clarifications

### Session 2026-11-20

- Q: Where does the per-track mixer go in Scenes across? → A: a block at the right end of each
  track row, pinned while the slots scroll sideways; same toolbar toggles.
- Q: How do Nota Remote and Nota NX relate to the desktop's orientation? → A: each offers "As in
  Nota" (default, follows the desktop live), "Scenes down" and "Scenes across", per device.

## Assumptions

- The preference lives in Preferences → Editing, in a new "Session" section next to the Piano roll
  and Arrangement sections, plus a toggle in the Session view toolbar.
- Orientation is purely visual: scenes, tracks, the project file, undo history and the engine are
  unchanged by it; switching is not an undoable edit.
- Column and row sizes reuse today's: in Scenes across a scene column is as wide as a track column
  is today and a track row as tall as a slot is today; the track header column is as wide as the
  scene rail is today. Density and zoom work is out of scope.
- The selection command ids already shipped (`session.selectUp/Down/Left/Right`) keep their ids so
  saved shortcuts and controller profiles keep working; only their names and their meaning in
  Scenes across are fixed (up/down = scene, left/right = track).
- Reordering scenes and tracks by drag beyond what exists today is #931, not this feature; the
  existing scene drag works turned.
- Nota NX's part ships in the `nota-nx` repository on top of whatever the Remote protocol carries.

## Nota Compliance Checklist

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [x] Audio-thread impact: none — a view preference; no engine call changes
- [x] No new managed/native crossings
- [ ] Smoke-test impact assessed: no engine change; the Remote protocol checks cover the new
  orientation field
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry, FEATURES.md and user docs (nota-docs)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
