# Feature Specification: Device cards XL — a large editor panel above the device chain

**Feature Branch**: `feat/new-lool-ambertape` (spec dir `085-device-xl-panel`)

**Created**: 2026-11-08

**Status**: Draft

## Clarifications

### Session 2026-11-08

- Q: What happens to the XL panel when the selected track changes? → A: It follows the selected track: it shows that track's XL device, if any, and hides otherwise. Devices keep their XL size.
- Q: What stays in the chain while a device is in XL? → A: A 260 × 260 card that says the device is open in XL; all controls move to the panel and stretch.

**Issue**: [#540](https://github.com/nota-daw/nota/issues/540) (approved)

**Input**: User description: "XL card size for device cards (issue #540). Next to the existing S/L toggle, devices that opt in get a third size, XL. Choosing XL moves the device's whole editor (all controls) into a separate panel docked above the device chain, at the chain's full width and taller than a card; the controls stretch to fill the panel. The device's card in the chain stays in place, becomes 260×260 and shows that the device is open in XL, with a way back to S/L. Only one XL panel at a time — opening XL on another device moves the panel to it. The panel header has the device name, the S/L/XL toggle and close. The chosen size is saved with the project per device, like S/L. UI only: no engine or param changes; automation and MCP tools unaffected. First devices: Nota EQ-8 and Nota Dynamic EQ-8; the mechanism is generic so other devices can opt in later. Open question: what happens to the panel when the selected track changes. Out of scope: XL for instruments and other effects, floating/detachable windows."

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

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open EQ-8 in XL and shape the sound on a large graph (Priority: P1)

A user mixing a track has Nota EQ-8 in the chain. The 700 × 260 card is too small for
fine work: band handles sit close together and the frequency axis is short. They
click **XL** in the card's size toggle. A large panel opens above the device chain, as
wide as the chain area. It holds the whole EQ-8 editor, all its controls, stretched to
fill the panel. The EQ-8 card stays in its place in the chain at 260 × 260 and says the
device is open in XL. The user drags bands on the large graph and the sound changes
exactly as it would from the card.

**Why this priority**: This is the feature. Without it nothing else matters.

**Independent Test**: Add EQ-8 to a track, choose XL, and check that the panel appears
above the chain with every EQ-8 control, that the card is 260 × 260 and says it is in
XL, and that editing in the panel changes the same params as the card.

**Acceptance Scenarios**:

1. **Given** an EQ-8 card in L, **When** the user picks XL in its size toggle, **Then** a
   panel opens above the device chain with the full EQ-8 editor stretched to the panel's
   width and height, and the card in the chain becomes 260 × 260 and shows that the
   device is in XL.
2. **Given** EQ-8 open in XL, **When** the user drags a band handle in the panel, **Then**
   the band's params change, the sound follows, and undo/automation/MCP see the same
   param change as an edit from the card.
3. **Given** EQ-8 open in XL, **When** the user picks S or L (in the panel header or on
   the 260 × 260 card), **Then** the panel closes and the card in the chain returns to the
   picked size with its full editor.
4. **Given** EQ-8 open in XL, **When** the user clicks the panel's close button,
   **Then** the panel closes and the card returns to L.

---

### User Story 2 - Dynamic EQ-8 in XL (Priority: P1)

The same as Story 1 for Nota Dynamic EQ-8: its whole editor, the graph with the dynamic
ranges, the band controls and its tabs, moves into the panel and stretches.

**Why this priority**: Dynamic EQ-8 is the other device named in the issue. It has the
densest graph, so it gains the most.

**Independent Test**: The same as Story 1 with Dynamic EQ-8. The live dynamic gain
movement and the spectrum keep animating in the panel.

**Acceptance Scenarios**:

1. **Given** a Dynamic EQ-8 card, **When** the user picks XL, **Then** the panel opens
   with every Dynamic EQ-8 control, and the live meters and dynamic curves keep updating
   while audio plays.
2. **Given** Dynamic EQ-8 in XL on a track with a sidechain source, **When** the user
   changes the sidechain in the panel, **Then** it behaves as it does on the card.

---

### User Story 3 - One XL panel at a time (Priority: P2)

A track has EQ-8 and Dynamic EQ-8. EQ-8 is in XL. The user picks XL on Dynamic EQ-8.
The panel switches to Dynamic EQ-8, and EQ-8 goes back to a normal card.

**Why this priority**: Keeps the layout predictable, so the panel never stacks or splits.

**Independent Test**: With two XL-capable devices on a track, switch XL between them
and check that only one panel is ever shown and the other device returns to L.

**Acceptance Scenarios**:

1. **Given** EQ-8 in XL and Dynamic EQ-8 in L on the same track, **When** the user picks
   XL on Dynamic EQ-8, **Then** the panel shows Dynamic EQ-8 and the EQ-8 card returns to
   L.

---

### User Story 4 - XL is remembered with the project (Priority: P2)

The user leaves EQ-8 in XL, saves and reopens the project. When they select that track,
EQ-8 opens in XL again.

**Why this priority**: S/L is already saved with the project, and XL should behave the
same way.

**Independent Test**: Set XL, save, reopen, select the track, and check that the panel
is there for that device.

**Acceptance Scenarios**:

1. **Given** a project saved with EQ-8 in XL, **When** it is reopened and the track is
   selected, **Then** EQ-8 is shown in the XL panel and as the 260 × 260 card.
2. **Given** EQ-8 in XL, **When** the device is duplicated, **Then** the copy opens in L
   (only one device per track can be in XL).
3. **Given** a project saved by an older Nota version, **When** it opens, **Then** every
   device opens in its saved S/L size, as before.

---

### User Story 5 - Changing the selected track (Priority: P3)

The user has EQ-8 in XL on track A and selects track B.

**Why this priority**: An edge of navigation. It needs a defined answer but is not the
core value.

**Independent Test**: Put a device in XL on one track, switch tracks back and forth,
and check that the panel hides and comes back with the track.

**Acceptance Scenarios**:

1. **Given** EQ-8 in XL on track A and no XL device on track B, **When** the user selects
   track B, **Then** the panel hides and EQ-8 stays in XL (it is not returned to L).
2. **Given** that state, **When** the user selects track A again, **Then** the panel
   shows EQ-8 again.
3. **Given** EQ-8 in XL on track A and Dynamic EQ-8 in XL on track B, **When** the user
   selects track B, **Then** the panel shows track B's Dynamic EQ-8.

---

### Edge Cases

- **The XL device is removed** (delete, cut, undo of its insertion): the panel closes.
- **The XL device is moved** within the chain or to another track: the panel keeps
  showing it if it stays on the shown track; otherwise it follows the rule of Story 5.
  If the target track already has a device in XL, the moved device returns to L.
- **The XL device is bypassed**: the panel stays and shows the bypassed state the same
  way the card does.
- **The XL device is inside a rack chain**: XL is not offered inside racks in this
  feature (the toggle shows only S/L there).
- **Small window**: the panel takes a fixed share of the device area's height, and when
  the window is too short for both the panel and the chain at their minimum heights, the
  chain keeps its 260 card height and the panel shrinks to its own minimum, then the
  devices area scrolls.
- **Undo**: the size choice is editor state, not an edit, so undo does not step through
  S/L/XL changes (as with S/L today).
- **Presets**: saving or applying a preset neither saves nor changes the size (as with
  S/L today).
- **Esc** with the panel focused closes the panel (the same as close).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Devices that opt in MUST show a three-way size toggle `S / L / XL` in the
  card header. Devices that don't opt in keep their current toggle (S/L or none).
- **FR-002**: In this feature exactly two devices MUST opt in: Nota EQ-8 and Nota Dynamic
  EQ-8. Opting in another device later MUST need no change to the panel itself, only the
  device's own large layout.
- **FR-003**: Choosing XL MUST open a panel docked above the device chain, spanning the
  device area's full width and taller than a device card.
- **FR-004**: The panel MUST hold the device's whole editor, with every control the L
  card has and no functionality left behind on the card. The editor MUST lay out to the
  panel's size: graphs stretch to fill the free width and height, and the controls keep
  their designed size and spacing.
- **FR-005**: The panel header MUST show the device name, the `S / L / XL` toggle (XL
  picked) and a close button. The device's on/off (bypass) control MUST be reachable
  either in the panel header or on the 260 × 260 card.
- **FR-006**: While the device is in XL, its card in the chain MUST stay in its position,
  be 260 × 260, keep the shared card header (name, bypass, size toggle) and show in its
  body that the device is open in XL above. It MUST offer a way back to S/L (the toggle)
  and, on click of the body, bring the panel into view.
- **FR-007**: At most one device per track MUST be in XL at a time. Choosing XL on
  another device of the same track MUST move the panel to it and return the previous
  device to L. Only one panel is ever shown: the one for the selected track.
- **FR-008**: Closing the panel (close button or Esc) MUST return the device to L.
- **FR-009**: Picking S or L while in XL (in the panel or on the card) MUST close the
  panel and set that size.
- **FR-010**: The XL choice MUST be saved with the project per device, as S/L are, and be
  restored on reopen. Projects saved before this feature MUST open unchanged.
- **FR-011**: The size MUST stay editor state: it is not automatable, not part of
  presets, not stepped by undo, and A/B comparison leaves it alone (the same rules as
  S/L today).
- **FR-012**: Editing in the panel MUST change the same device params, through the same
  path, as editing on the card, so audio, automation, undo, MIDI learn, macros and MCP
  tools see no difference.
- **FR-013**: Live views (spectrum, band meters, dynamic gain movement) MUST keep
  updating in the panel at the same rate as on the card. Only the visible editor (panel
  or card) MUST do this work, never both.
- **FR-014**: The panel MUST follow `DESIGN.md` (Studio): the same surface, header,
  typography, accent usage and the four themes as device cards.
- **FR-015**: The panel MUST follow the selected track: it shows that track's XL device
  if it has one and hides otherwise. Switching tracks MUST NOT change any device's size.
- **FR-016**: Removing the XL device MUST close the panel.

### Key Entities

- **Card size**: per-device editor state with the values S, L and XL (XL only for devices
  that opt in). It is saved with the project and is not a sound param.
- **XL panel**: a single region above the device chain that hosts one device's editor at
  a time: the XL device of the selected track, if any (Story 5).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With XL open on a 1440 px-wide window, EQ-8's frequency graph is at least
  twice as wide and twice as tall as on the L card.
- **SC-002**: Switching a device to XL, between devices or back to L shows the result
  within one frame (no visible flicker of an empty panel or card).
- **SC-003**: Every param of EQ-8 and Dynamic EQ-8 can be edited from the panel (100 %
  coverage against the L card).
- **SC-004**: A project with a device in XL reopens with the same device in XL in 100 %
  of save/reopen cycles. Projects from older versions open with no change to their
  device sizes.
- **SC-005**: An XL session leaves audio output bit-identical to the same edits made on
  the card (the smoke test passes unchanged).

## Assumptions

- The previous size is not remembered: leaving XL by close, Esc or another device taking
  XL returns the device to L. Picking S/L directly sets that size.
- The panel has a fixed default height (well above the 260 card height). Resizing it by
  drag is out of scope for this feature and can be added later.
- The panel lives in the Devices area of the main window, above the chain of the
  currently shown track. It is not a separate window.
- XL is offered for top-level devices of a track's chain only, not inside racks.
- Saving the size goes the same way as S/L (the device's editor-state value). If that
  value needs a third state, it is a widening of existing editor state, not a new sound
  param. Audio, automation lists and presets stay untouched.
- Out of scope: XL for instruments, MIDI effects and other audio effects; floating or
  detachable windows; drag-to-resize of the panel.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
