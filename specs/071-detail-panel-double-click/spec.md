# Feature Specification: Open the Detail panel on double-click, not on every selection

**Feature Branch**: `071-detail-panel-double-click`

**Created**: 2026-11-02

**Status**: Clarified

**Input**: User description: "Issue #308 (https://github.com/nota-daw/nota/issues/308): Open the Detail panel on double-click, not on every selection. Single click on a track or clip selects only — the Detail panel does not open; if it is already open it keeps following the selection. Double-click a track opens the Detail panel on Devices; double-click a clip opens it on Clip (piano roll / audio editor / pattern). Existing double-click gestures on clips, track headers and Session view clip slots (rename, create clip on empty lane, etc.) must be kept or relocated so nothing is lost. The panel stays reachable via view.showDevices and the View menu. Consider a Preferences toggle "Open Detail panel on select" for the old behaviour; note the change in CHANGELOG; update Preferences → Shortcuts and docs for the new gestures."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Today's behaviour (baseline, for reviewers)**:

- Arrangement: a single click on a track header, a mixer strip or a clip selects it and
  also opens the Detail panel (Devices for a track, Devices or Clip for a clip).
- Arrangement: double-clicking a clip already opens its editor in the Clip tab.
  Double-clicking empty lane space on an instrument track creates a MIDI clip.
  Double-clicking an instrument track's header opens its instrument's plug-in window.
- Session view: a single click on a filled slot selects it and opens its clip editor in
  the Detail panel. Double-clicking a filled slot opens its editor; double-clicking an empty
  slot on an instrument track creates a MIDI clip and opens it. Double-clicking a column
  header renames the track.
- The Detail panel can also be opened from the View menu and the "Show devices" command.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select without the panel popping up (Priority: P1)

A user on a small screen wants to pick a clip to split it, or pick a track to mute or
reorder it. With the Detail panel closed, a single click selects the clip or track and
nothing else changes on screen: the arrangement keeps its full height.

**Why this priority**: This is the complaint in the issue. Every other story only matters
once a plain click stops opening the panel.

**Independent Test**: Close the Detail panel, click a track header, a clip, a mixer strip
and a Session slot one after another. Each gets selected; the panel stays closed.

**Acceptance Scenarios**:

1. **Given** the Detail panel is closed, **When** the user clicks a clip in the arrangement,
   **Then** the clip (and its track) is selected and the Detail panel stays closed.
2. **Given** the Detail panel is closed, **When** the user clicks a track header or a mixer
   strip, **Then** the track is selected and the Detail panel stays closed.
3. **Given** the Detail panel is closed and Session view is showing, **When** the user
   clicks a filled clip slot, **Then** the slot is selected and the Detail panel stays closed.
4. **Given** the Detail panel is closed, **When** the user selects a clip and splits it,
   **Then** the panel is still closed afterwards.

---

### User Story 2 - An open panel keeps following the selection (Priority: P1)

A user on a big screen keeps the Detail panel open. Clicking around keeps working as it
does today: the panel shows the selected track's devices, or the selected clip in the Clip
tab when the Clip tab is the one showing.

**Why this priority**: Users who like the panel must not lose the "it follows me" behaviour.
Without it the change would be a regression for them.

**Independent Test**: Open the panel on Devices, click another track — the panel shows that
track's devices. Switch to Clip, click another clip — the Clip tab shows that clip.

**Acceptance Scenarios**:

1. **Given** the Detail panel is open on Devices, **When** the user clicks another track,
   **Then** the panel shows that track's devices.
2. **Given** the Detail panel is open on Clip, **When** the user clicks another clip,
   **Then** the Clip tab shows that clip's editor (piano roll, audio editor or pattern, as today).
3. **Given** the Detail panel is popped out into its own window, **When** the user changes
   the selection, **Then** the window follows the selection as it does today.

---

### User Story 3 - Double-click opens the panel where you want it (Priority: P1)

A user double-clicks a track to see its devices, or double-clicks a clip to edit it.
The Detail panel opens on the matching tab.

**Why this priority**: With single click no longer opening the panel, double-click is the
quick way in. Without it the panel would only be reachable through the menu.

**Independent Test**: With the panel closed, double-click a track header: the panel opens
on Devices for that track. Close it, double-click a clip: the panel opens on Clip for it.

**Acceptance Scenarios**:

1. **Given** the panel is closed, **When** the user double-clicks a track header in the
   arrangement, **Then** the panel opens on Devices for that track.
2. **Given** the panel is closed, **When** the user double-clicks a MIDI clip, an audio clip
   or a drum-pattern clip, **Then** the panel opens on Clip with that clip's editor (the
   editor kind the clip uses today).
3. **Given** the panel is closed and Session view is showing, **When** the user
   double-clicks a filled slot, **Then** the panel opens on Clip for that slot.
4. **Given** the panel is open on Clip, **When** the user double-clicks a track header,
   **Then** the panel switches to Devices for that track.

---

### User Story 4 - Existing double-click gestures still exist (Priority: P2)

A user who double-clicks empty lane space to create a MIDI clip, double-clicks a Session
column header to rename a track, or double-clicks an empty Session slot to create a clip can
still do all of that. The instrument plug-in window that double-clicking an instrument track
header opens today is still one gesture away.

**Why this priority**: The change must not remove anything users rely on.

**Independent Test**: Run every double-click gesture listed in the baseline and confirm
each one still does its job (or its new documented gesture does).

**Acceptance Scenarios**:

1. **Given** an instrument track, **When** the user double-clicks empty space in its lane,
   **Then** a new MIDI clip is created there (unchanged).
2. **Given** Session view, **When** the user double-clicks an empty slot on an instrument
   track, **Then** a MIDI clip is created and opened in the Clip tab (unchanged).
3. **Given** Session view, **When** the user double-clicks a track's column header,
   **Then** the rename prompt opens, as today (the Session column header keeps rename).
4. **Given** an instrument track whose instrument has its own window, **When** the user
   double-clicks its arrangement header, **Then** the Detail panel opens on Devices for that
   track, like any other track; a plug-in instrument's own window opens from **Open editor** on
   its card in Devices.

---

### User Story 5 - Keep the old behaviour if I prefer it (Priority: P3)

A user who liked the panel opening on every selection turns on "Open Detail panel on select"
in Preferences and gets today's behaviour back.

**Why this priority**: The change breaks a habit; a single switch lets those users keep it
without a fork in every gesture.

**Independent Test**: Turn the option on, close the panel, click a track: the panel opens on
Devices. Turn it off: the click only selects.

**Acceptance Scenarios**:

1. **Given** "Open Detail panel on select" is off (the default), **When** the user clicks a
   track or clip, **Then** the panel does not open (User Story 1).
2. **Given** the option is on, **When** the user clicks a track, a clip or a Session slot,
   **Then** the panel opens exactly as it does before this feature.
3. **Given** the option is turned on or off, **When** the app is restarted, **Then** the
   choice is kept.
4. **Given** the option is on, **When** the user double-clicks a track or clip, **Then** the
   double-click gestures from User Story 3 still work.

---

## Clarifications

### Session 2026-11-02

- Q: Double-click on an instrument track's arrangement header opens the plug-in window today; the issue asks for Devices. Which wins? → A: Devices, for every track kind; the plug-in window opens from **Open editor** on the plug-in's card in Devices.
- Q: Session column header double-click — keep rename, or open Devices? → A: Keep rename; in Session, Devices are reached through the View menu / "Show devices" (or a double-clicked slot's Clip tab → Devices tab).
- Q: Preferences toggle for the old behaviour, or CHANGELOG note only? → A: Add "Open Detail panel on select" in Preferences, off by default, plus the CHANGELOG note.

---

### Edge Cases

- A clip is selected as a side effect of another gesture (an edge trim, a move, a rubber-band
  select, a paste, Undo/Redo restoring a selection): the panel must not open on its own.
- Keyboard selection (arrow keys moving between tracks or clips) follows the same rule as a
  single click: no opening, but an open panel follows.
- A new track or clip is created (menu, browser drop onto empty space, recording): it may get
  selected, but the panel does not open on its own. Explicit "show me" actions (a browser
  drop of a device onto a track, Convert, the Devices tab, the "Show devices" command) still
  open the panel as today.
- The Modular (global) view's island double-click, which opens a track today, keeps working.
- The panel is maximised when the user changes the selection: it stays maximised and follows.
- The selected track is deleted while the panel is closed: nothing opens.
- A double-click on a clip's ADSR handle still resets that stage (it does not open the panel).
- A double-click on a row seam still resets the row height (it does not open the panel).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A single click that selects a track (arrangement header, mixer strip, Session
  column header, Modular island) MUST NOT open the Detail panel when it is closed.
- **FR-002**: A single click that selects a clip (arrangement clip, Session slot) MUST NOT
  open the Detail panel when it is closed.
- **FR-003**: Selection changes made by other gestures and by the keyboard MUST NOT open a
  closed Detail panel (see Edge Cases).
- **FR-004**: While the Detail panel is open (docked, maximised or popped out), it MUST keep
  following the selection exactly as today: same tab choice rules, same Clip/Devices
  switching.
- **FR-005**: Double-clicking a track header in the arrangement MUST select the track and
  open the Detail panel on Devices for it, for every track kind. On an instrument track this
  replaces today's "open the plug-in window" double-click; that window stays one click away
  through **Open editor** on the plug-in's card in Devices.
- **FR-006**: Double-clicking a clip in the arrangement MUST select it and open the Detail
  panel on Clip with the editor that clip uses today (piano roll, audio editor, pattern).
- **FR-007**: Double-clicking a filled Session slot MUST select it and open the Detail panel
  on Clip for it.
- **FR-008**: Every double-click gesture in the baseline MUST keep working, or be moved to
  another gesture that is listed in Preferences → Shortcuts and the user docs.
- **FR-008a**: Double-clicking a Session column header MUST keep renaming the track.
- **FR-009**: The Detail panel MUST stay reachable without a double-click through the View
  menu and the "Show devices" command (unchanged).
- **FR-009a**: Preferences MUST offer "Open Detail panel on select", off by default. When on,
  FR-001–FR-003 do not apply and a selection opens the panel exactly as before this feature;
  the double-click gestures keep working either way. The choice MUST persist across restarts.
- **FR-010**: Preferences → Shortcuts MUST list the new mouse gestures (double-click track →
  Devices, double-click clip → Clip) and drop "double-click instrument header → plug-in window".
- **FR-011**: The CHANGELOG `[Unreleased]` section MUST describe the change of habit, and the
  user docs (EN and RU) MUST describe how to open the Detail panel.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the Detail panel closed, 0 of the single-click selection gestures listed
  in FR-001/FR-002 open it.
- **SC-002**: With the Detail panel open, 100% of the follow-the-selection scenarios in
  User Story 2 behave as before the change.
- **SC-003**: A user reaches a track's devices or a clip's editor from a closed panel in one
  gesture (a double-click).
- **SC-004**: Every double-click gesture in the baseline list is still available (same or
  documented new gesture): 0 gestures lost.

## Assumptions

- "Track" covers instrument, audio, group and return tracks; master behaves like a track.
- The mixer and Modular views follow the same rule as the arrangement: a click selects, it
  does not open the panel.
- Double-clicking a clip already opens the Clip tab today; this feature keeps that and makes
  sure it also shows the panel when it is closed.
- The Detail panel's open/closed state is not newly persisted by this feature; it keeps
  whatever persistence it has today.
- No engine change: this is a UI-only behaviour change.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI only, no audio-thread code touched
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
