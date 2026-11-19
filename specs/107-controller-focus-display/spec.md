# Feature Specification: Controller focus you can see — in Nota and in Remote

**Feature Branch**: `feat/controller-focus-display`

**Created**: 2026-11-19

**Status**: Draft

**Input**: Issue [nota-daw/nota#929](https://github.com/nota-daw/nota/issues/929) (approved):
"Controllers: show the controller's focus (selected clip, track, device) in Nota and in Remote,
and step between devices on a track." Builds on [specs/051](../051-controller-auto-mapping/spec.md)
(controller auto-mapping, #100).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — controller service, Remote hub and protocol
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Also: the Remote web app (`src/web/remote`) and its protocol. Nota NX (sibling repo, Switch
remote) consumes the same protocol after this feature; it is not part of this spec.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

### What exists today (from specs/051 and the Remote specs)

- The knobs drive the *focused device*: the device selected in the Devices panel, else the
  selected track's instrument, else its first device (051 FR-016). When the Devices panel shows,
  the card's controls carry knob numbers and the page ("2 / 3 · Filter", 051 FR-017).
- The controller indicator beside the MIDI button shows the controller and the knob mode.
- Track ◀ / ▶ on a controller step the *bank* of eight tracks by one. While the Session shows,
  they also move the selected slot left / right, and the jog wheel moves it up / down; the
  encoder push launches / stops the selected slot.
- In the Session, selecting a slot puts that slot's clip in the Clip editor — so a controller
  walking the grid keeps the lower panel on the Clip editor and the user never sees the device
  the knobs now drive. Outside the Session, Track ◀ / ▶ only shift the bank: the selected track,
  and with it the focused device, does not change at all.
- There is no control to step from one device to another on the same track.
- Nota Remote knows Nota's selected track ("Follow Nota's selection") and shows the Session grid
  (Scenes screen) and a Macros screen with its own device picker, but nothing of the Session
  selection or of the controller's focus.

## Clarifications

### Session 2026-11-19

- Q: When the controller switches track or device in the Session (Clip editor in the lower panel), how is the focused device shown? → A: An always-visible title-bar readout "track · device · page" beside the controller indicator; the Clip editor stays; a click on the readout opens Devices (FR-001–FR-005). No pop-up, no automatic panel switch.
- Q: What do Track ◀ / ▶ do (they stepped the bank by one)? → A: Select the previous / next track in every view; the bank follows to keep it inside; Bank ◀ / ▶ still step by eight (FR-006–FR-009).
- Q: Remote: focus line only, or also Macros following the focus? → A: Header focus line + Scenes selection outline, and with Follow Nota on, Macros switches to the focused device (FR-013–FR-016).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See what the controller is on, wherever I am (Priority: P1)

A performer plays from a KeyLab: they press Track ▶, turn knobs, walk the Session grid. Whatever
view is open, a compact readout in Nota's title bar always says which track, which device and
which page the knobs are on — "Bass · Twin · 2 / 4" — and it updates the moment any of them
changes. When the Devices panel is visible, the focused device's card is plainly marked as the
controller's (on top of the existing knob numbers).

**Why this priority**: it answers the user's core question — "which device am I controlling?" —
in every view, including the Session where the Clip editor holds the lower panel.

**Independent Test**: with a controller connected (or the test port), select tracks and devices
by mouse and by controller in Arrangement, Session and Mixer; the readout always names the
focused track, device and page, and the focused card is marked whenever it is on screen.

**Acceptance Scenarios**:

1. **Given** a controller in Device mode and track "Bass" (instrument Twin, pages 4) selected,
   **When** nothing else happens, **Then** the readout shows the track, "Twin" and "1 / 4".
2. **Given** the readout shows page 1 / 4, **When** the user presses Page ▶ on the controller,
   **Then** the readout shows 2 / 4 within one screen refresh.
3. **Given** the Session view with the Clip editor in the lower panel, **When** the controller
   moves the selection to another track's slot, **Then** the readout names that track and its
   focused device, while the Clip editor stays on the new slot's clip.
4. **Given** the Devices panel is visible, **When** the focused device changes, **Then** the newly
   focused card is marked as the controller's and the previous card loses the mark.
5. **Given** the knobs are in Volume / Pan / Send mode, **Then** the readout shows the mode and the
   bank of tracks ("Volume · tracks 9–16") instead of a device.
6. **Given** no controller with knobs is connected, **Then** no readout is shown.

---

### User Story 2 - Track buttons select the track (Priority: P1)

Track ◀ / ▶ on the controller select the previous / next track in every view, so the focused
device follows and the user sees it move (readout, track highlight, and — if open — the Devices
panel). The eight-track bank follows the selection so the selected track stays inside it; Bank
◀ / ▶ keep stepping the bank by eight. In the Session the selected slot moves to the same row of
the new track, as today.

**Why this priority**: today the track buttons appear to do nothing outside the Session (they
only shift an invisible bank), which is the second half of the user's report.

**Independent Test**: in the Arrangement, press Track ▶ three times; the selected track moves
three tracks down and the readout follows; the Mixer's bank outline still contains it.

**Acceptance Scenarios**:

1. **Given** track 3 selected in the Arrangement, **When** Track ▶ is pressed, **Then** track 4 is
   selected (header highlighted, scrolled into view) and the knobs drive its focused device.
2. **Given** the bank shows tracks 1–8 and track 8 is selected, **When** Track ▶ is pressed,
   **Then** track 9 is selected and the bank moves so it contains track 9.
3. **Given** the last track is selected, **When** Track ▶ is pressed, **Then** nothing changes.
4. **Given** the Session with slot (track 2, scene 3) selected, **When** Track ▶ is pressed,
   **Then** slot (track 3, scene 3) is selected and track 3 is Nota's selected track.
5. **Given** the Devices panel is open on track 3, **When** Track ▶ selects track 4, **Then** the
   panel shows track 4's chain.

---

### User Story 3 - Step between devices on one track (Priority: P2)

On a track with an Arp, Twin, Valve and EQ the user presses Device ▶ on the controller (or a
key, or a palette command) and the knobs move from the Arp to the Twin, to the Valve, to the EQ,
in chain order; Device ◀ goes back. The device they land on becomes the selected device — the
Devices panel, when shown, selects its card — and the readout names it.

**Why this priority**: the user can't reach any device but the default one from the hardware.

**Independent Test**: bind Device ▶ / ◀ to two controller buttons (or run the commands from the
palette) on a four-device track and step through: the readout and the panel selection walk the
chain in order and stop at its ends.

**Acceptance Scenarios**:

1. **Given** a track whose chain is MIDI effect Arp → instrument Twin → effects Valve, EQ8 and the
   focus on Twin, **When** "Next device" runs, **Then** the focus moves to Valve and its page 1.
2. **Given** the focus on the last device, **When** "Next device" runs, **Then** it stays there.
3. **Given** the Devices panel is hidden, **When** "Next device" runs, **Then** the focus moves and
   the readout shows it; the panel does not open by itself.
4. **Given** a shipped profile with spare buttons for device stepping (see FR-012), **When** the
   controller is recognised, **Then** those buttons step devices without any setup.
5. **Given** a device inside a rack, **Then** stepping treats the rack as one device (its macros);
   stepping into a rack's chains is out of scope.

---

### User Story 4 - Remote mirrors the selection and the controller's focus (Priority: P2)

On a tablet running Nota Remote next to the controller, the Scenes screen outlines the slot that
is selected in Nota (moved by the controller or the mouse), and a focus line in Remote shows the
controller's track, device and page, updating live. When Remote follows Nota's selection, its
Macros screen opens on the focused device.

**Why this priority**: the user plays away from the screen; Remote is where they look.

**Independent Test**: with Remote connected, walk the Session grid with the controller: the
outline on the phone follows within half a second; change device / page on the controller: the
focus line follows.

**Acceptance Scenarios**:

1. **Given** Remote's Scenes screen, **When** the controller moves Nota's Session selection,
   **Then** the same slot is outlined in Remote; a selected scene row is outlined as a row.
2. **Given** Nota shows the Arrangement (no Session selection), **Then** Remote outlines nothing.
3. **Given** a controller is connected, **Then** Remote shows "track · device · page" of its focus;
   with none connected the line is hidden.
4. **Given** Remote follows Nota's selection and the controller steps to the next device, **Then**
   Remote's Macros screen switches to that device (if it is one Remote can show).
5. **Given** an older Remote client (or Nota NX before its update), **Then** it keeps working and
   simply ignores the new state.

---

### Edge Cases

- The focused device is deleted, moved to another track or replaced: the readout and Remote
  follow the focus rule's new pick immediately (051 behaviour); stepping continues from there.
- A track with no devices: the readout shows the track and "no device"; device stepping does
  nothing.
- Return tracks and the master are not in the track order Track ◀ / ▶ walk — consistent with the
  bank (051 FR-009). Group tracks are included as tracks; collapsed groups' members are skipped.
- Two controllers connected: the readout and Remote show the lead controller (the one the
  indicator already shows).
- A plug-in whose parameter list changes while focused: the page count in the readout updates.
- Very long track / device names are trimmed with an ellipsis; the full text is in the tooltip.
- The selected Session slot is scrolled off the Remote grid: Remote does not scroll by itself
  unless the user enabled following [clarified in Assumptions].

## Requirements *(mandatory)*

### Functional Requirements

**Readout and marking in Nota**

- **FR-001**: While at least one controller with knobs is connected, Nota MUST show a readout of
  the lead controller's focus in the title bar, as part of the controller indicator: in Device
  mode the focused track, device and page ("track · device · page / pages"); in Volume / Pan /
  Send mode the mode and the current bank of tracks.
- **FR-002**: The readout MUST update on every focus, page, mode and bank change and on renames,
  within one screen refresh, in every view (Arrangement, Session, Mixer, Modular).
- **FR-003**: A click on the readout MUST open the Devices panel on the focused track with the
  focused device selected (the explicit way to see it); the readout's tooltip MUST carry the full
  names.
- **FR-004**: When the focused device's card is on screen, it MUST carry a visible "controller"
  mark on the card itself (in addition to the per-control knob numbers of 051 FR-017), following
  DESIGN.md (orange lines family, no new colour).
- **FR-005**: A controller changing the track or device MUST NOT take the lower panel away from
  the Clip editor or open hidden panels by itself; the readout is how focus is shown when the
  Devices panel is not visible.

**Track buttons**

- **FR-006**: Track ◀ / ▶ roles MUST select the previous / next track in the same order the bank
  uses, in every view; the selection is the same as a click on the track's header (focus,
  arm-on-select, Remote follow and Devices panel content all follow).
- **FR-007**: After a track step the bank MUST be moved the least amount needed to contain the
  selected track. Bank ◀ / ▶ keep moving the bank by eight without changing the selection.
- **FR-008**: In the Session a track step MUST select the slot in the same scene row of the new
  track (current behaviour, kept), and that track becomes the selected track.
- **FR-009**: At the first / last track a step MUST do nothing.

**Device stepping**

- **FR-010**: Nota MUST offer "Next device" and "Previous device" commands that move the focus
  along the focused track's chain in chain order — MIDI effects, instrument, audio effects —
  treating a rack as one device, stopping at the ends. The device stepped to becomes the track's
  selected device (the Devices panel selects it when it shows that track) and the knobs go to its
  first page.
- **FR-011**: The commands MUST be available from the command palette, assignable to a keyboard
  shortcut (no default key), to a controller button through the controller's `command` role, and
  as a step on the controller setup wizard's extra-buttons page.
- **FR-012**: Shipped profiles MUST bind device stepping where the hardware has buttons meant for
  it or free buttons with that obvious use; the plan lists each profile and its choice. Profiles
  with no such buttons are left unchanged.

**Remote**

- **FR-013**: Remote's state MUST include Nota's current Session selection (a slot: track and
  scene; a scene row; or none when the Session is not showing or nothing is selected) and MUST
  update when it changes.
- **FR-014**: Remote's Scenes screen MUST outline the selected slot or scene row, styled as the
  desktop Session's selection.
- **FR-015**: Remote's state MUST include the lead controller's focus (track, device name, page,
  page count, knob mode, bank; or none) and Remote MUST show it as a one-line focus readout in its
  header area while a controller is connected.
- **FR-016**: When a Remote client follows Nota's selection, its Macros screen MUST switch to the
  controller's focused device when that device is one Remote can show on that track.
- **FR-017**: The new state MUST be additive to the protocol: existing clients that do not know it
  keep working unchanged. The protocol change MUST be documented for other clients (Nota NX).
- **FR-018**: Headless clients (MCP) MUST be able to read the Session selection and run device
  stepping, consistent with how the controller focus is already exposed (051 FR-029).

### Key Entities

- **Controller focus**: the lead controller's knob mode; in Device mode the focused track,
  device (name, position in the chain) and page / page count; in the other modes the bank.
- **Session selection**: none, a slot (track, scene) or a scene row — the one the desktop Session
  view, the controller and now Remote share.
- **Chain order**: the order device stepping walks — MIDI effects, instrument, audio effects.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In every view, a user can tell the controlled track, device and page by looking
  only at the title bar, without opening any panel.
- **SC-002**: Every focus / page / track change from the controller is reflected in Nota's
  readout within one screen refresh and in Remote within half a second on a local network.
- **SC-003**: From the controller alone the user can reach every top-level device of every
  (non-return) track: Track ◀ / ▶ to the track, Device ◀ / ▶ to the device.
- **SC-004**: Remote clients built before this feature connect and work with no errors.
- **SC-005**: No added audio-thread work: CPU in the perf gate is unchanged.

## Assumptions

- The "lead controller" is the one the existing indicator already shows; per-controller focus
  stays as 051 defines it.
- Changing Track ◀ / ▶ from "step bank by one" to "select track" supersedes that part of 051
  (confirmed 2026-11-19); the bank still moves with the selection, so mixer-mode faders keep
  landing on a visible bank.
- Remote's Scenes grid does not auto-scroll to the selection; a slot outside the visible area
  shows no outline until scrolled to. (Revisit if it feels wrong on a tablet.)
- Device stepping does not enter rack chains or Drum Rack pads; that can come later.
- Nota NX gets the same state in its own repo afterwards, using the protocol notes this feature
  writes.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [x] Audio-thread impact considered: UI / hub thread only; nothing on the audio thread
- [x] Managed/native crossings go through the pure C ABI only — no new engine calls expected
- [ ] Smoke-test impact assessed: Remote protocol and controller commands → extend the smoke / Remote tests
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry, FEATURES.md, user docs (controllers + Remote pages, EN + RU), shortcuts list (new commands)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
