# Feature Specification: Track inspector

**Feature Branch**: `055-track-inspector`

**Created**: 2026-10-30

**Status**: Clarified

**Input**: User description: "Feature Request: Inspector Panel for Tracks (GitHub issue nota-daw/nota#186). Nota already has the vertical tabs to the left. Add another tab that acts as the inspector for the currently selected track, so track settings — colour, record input, freeze/unfreeze and other commonly used options — have an orderly home instead of living in the context menu. The panel can be toggled like any of the other tabs."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

UI only: every control writes through engine calls that already exist (the same ones the
track header and the track menu use). No engine, C ABI or project-format change.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Clarifications

### Session 2026-10-30

Defaults chosen for the open points (the request was clear on the place — a tab on the
left rail — and on the purpose):

- Q: Where does the inspector live? → A: A ninth tab on the browser's left icon rail,
  "Track inspector", last on the rail behind a hairline (devices · library · mappings ·
  track). It opens, folds and switches like every other rail tab.
- Q: Which track does it show? → A: The track the Devices panel shows: the selected track
  (the primary one in a multi-selection), from the Arrangement, the Session grid or the
  Global view. No track → a one-line empty state.
- Q: What goes in it? → A: The track's properties that today are reachable only from the
  track menu, plus the header's state keys: name, mute / solo / record-arm, record input and
  input monitoring (audio tracks), MIDI from (instrument tracks), colour, row height, and
  freeze (Freeze / Live freeze / Unfreeze / Flatten / the live-freeze edit actions — the same
  entries as the menu). Volume, pan, sends and output stay in the header and the mixer.
- Q: Does the track menu lose those entries? → A: No. The menu is unchanged in this
  change; trimming it is a separate decision once the inspector has been used.
- Q: Returns and the master? → A: A return shows name, mute and colour (what its footer
  row and menu offer). The master
  shows its name and a line pointing to the Devices panel (it has no other settings).
- Q: Is anything persisted? → A: No new state. Which rail tab is open is not remembered
  today and stays that way.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See and change the selected track's settings (Priority: P1)

A user selects a track and opens the Track inspector tab on the left rail. It shows the
track's name and kind and every setting listed above; changing one there changes the track
exactly as the header or the track menu would.

**Why this priority**: The core of the request.

**Independent Test**: Select an audio track, open the inspector, pick a colour, switch the
record input to Ext 1, turn monitoring on, set the height to Large and freeze the track:
the header shows each change, and each undoes as the same change made from the menu does.

**Acceptance Scenarios**:

1. **Given** an audio track is selected, **When** the user opens the Track inspector tab,
   **Then** it shows the name, mute / solo / arm, record input + monitor, colour, height
   and freeze.
2. **Given** an instrument track is selected, **When** the inspector shows it, **Then** it
   offers MIDI from instead of record input + monitor.
3. **Given** a group track is selected, **When** the inspector shows it, **Then** it offers
   name, mute / solo, colour and height (no arm, input or freeze).
4. **Given** the user renames the track in the inspector and presses Enter, **Then** the
   header shows the new name; Esc instead restores the old one.
5. **Given** the user picks a colour, height or input in the inspector, **Then** it undoes
   (or not) exactly as the same pick from the menu.

---

### User Story 2 - The inspector follows the selection and stays current (Priority: P1)

The inspector always describes the track in focus, and it does not show stale state.

**Independent Test**: With the inspector open, click through three tracks: the inspector
switches each time. Mute the shown track from its header and undo a colour change with ⌘Z:
the inspector shows both at once.

**Acceptance Scenarios**:

1. **Given** the inspector is open, **When** another track is selected, **Then** it shows
   that track.
2. **Given** the inspector shows a track, **When** the track changes elsewhere (header,
   menu, undo / redo, keyboard), **Then** the inspector reflects it within a moment.
3. **Given** the shown track is deleted or nothing is selected, **Then** the inspector shows
   "No track selected".

### Edge Cases

- A frozen track: the input and MIDI-from rows are still shown; the freeze section offers
  Unfreeze and Flatten.
- A track in a live-freeze link: the freeze section offers the link's actions (Live freeze
  off, Edit source / Done / Discard, Flatten), like the menu.
- The browser folded to its rail: clicking the inspector tab unfolds it on the inspector.
- A long name is trimmed with an ellipsis in the title; the name field scrolls.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The browser rail MUST have a "Track inspector" tab that shows the inspector.
- **FR-002**: The inspector MUST show the track the Devices panel shows, and an empty state
  when there is none.
- **FR-003**: For tracks it MUST offer name, mute, solo, colour and height; arm for audio
  and instrument tracks; record input + monitor for audio tracks; MIDI from for instrument
  tracks; the freeze entries the track menu offers.
- **FR-004**: For a return it MUST offer name, mute and colour; for the master, its
  name only.
- **FR-005**: Every edit MUST go through the same engine calls as the header / menu, so undo,
  automation and arming rules behave the same.
- **FR-006**: The inspector MUST reflect changes made elsewhere while it is visible.
- **FR-007**: The track menu MUST stay as it is.

## Success Criteria *(mandatory)*

- **SC-001**: Every setting listed in FR-003 can be changed for the selected track without
  opening a context menu.
- **SC-002**: A change made elsewhere shows in the open inspector in under half a second.
