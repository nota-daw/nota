# Feature Specification: Fold a device into a narrow strip

**Feature Branch**: `022-device-fold`

**Created**: 2026-10-17

**Status**: Clarified

**Input**: User description: "Devices panel: double-click a device header to fold it into a narrow strip (GitHub issue nota-daw/nota#147). Long device chains don't fit the panel; you scroll sideways to reach the device you need. Double-click a device's header to collapse the card into a narrow vertical strip (name rotated, bypass switch still there), like Ableton Live. Double-click again to expand. The folded state is saved with the project."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Also touches the engine (a message-thread flag on each device, never read by the audio
thread) and the project format (one boolean per device).

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Clarifications

### Session 2026-10-17

Defaults chosen for the open points (the request was clear on the gesture and on saving):

- Q: Which cards fold? → A: Every device card in the chain: MIDI effects, the instrument, and audio effects (built-in and hosted plug-ins). The Sends card and the "+" key are not devices and don't fold.
- Q: Which double-clicks count? → A: A double-click anywhere on the header except its controls (the bypass switch, the preset picker and any header accessory such as a plug-in's size toggle). On a folded strip, a double-click anywhere except its bypass switch unfolds it.
- Q: What does the strip keep? → A: The bypass switch at the top (same switch, same behaviour), the device name rotated to read top to bottom, selection, the right-click menu and drag-to-reorder. The body is not built while folded.
- Q: Is folding an undo step? → A: No. Like bypass, it is a view state, not an edit; but the fold follows its device through move, delete + undo and redo, and it is saved.
- Q: Is there a way besides the double-click? → A: Yes, "Fold" / "Unfold" in the header's right-click menu, for discoverability.
- Q: Does copy/paste carry the fold? → A: No; a pasted device opens unfolded.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fold and unfold with a double-click (Priority: P1)

A user with a long chain double-clicks the header of a device they are not editing. The card
collapses into a narrow strip showing its name and its on/off switch, so the rest of the chain
moves into view. Double-clicking the strip opens the card again, exactly as it was.

**Why this priority**: The core of the request.

**Independent Test**: Put four effects on a track, double-click the first one's header: it
becomes a ~28 px strip, the others shift left; double-click the strip: the full card returns
with its parameters unchanged.

**Acceptance Scenarios**:

1. **Given** an unfolded card, **When** the user double-clicks its header (not on a control), **Then** it becomes a narrow strip with the rotated name and the bypass switch.
2. **Given** a folded strip, **When** the user double-clicks it (not on the switch), **Then** the full card returns.
3. **Given** an unfolded card, **When** the user double-clicks the bypass switch or the preset picker, **Then** the card does not fold (the control behaves as it does today).
4. **Given** a folded strip, **When** the user clicks its switch, **Then** the device bypasses / un-bypasses and stays folded.
5. **Given** a folded strip, **When** the user clicks, right-clicks or drags it, **Then** it selects, opens the device menu, or moves within its chain like a full card.

---

### User Story 2 - The fold is saved with the project (Priority: P1)

A user folds a few devices, saves, and reopens the project later: the same devices are folded.

**Independent Test**: Fold an effect, a MIDI effect and the instrument; save; reopen: all three are folded, the rest unfolded.

**Acceptance Scenarios**:

1. **Given** folded devices, **When** the project is saved and reopened, **Then** exactly those devices are folded.
2. **Given** a project saved before this feature, **When** it is opened, **Then** every device is unfolded.
3. **Given** a folded device, **When** it is moved, or deleted and the delete is undone, **Then** it is still folded.

### Edge Cases

- Folding the selected device keeps it selected (the selection ring wraps the strip).
- An instrument replaced in place (a new instrument dropped on the track) opens unfolded.
- Folding marks the project as changed (it is part of what gets saved).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A double-click on a device card's header, outside its controls, MUST fold the card into a narrow vertical strip; a double-click on the strip, outside its switch, MUST unfold it.
- **FR-002**: The strip MUST show the device name rotated to read top to bottom and, for a bypassable device, the same bypass switch as the full card.
- **FR-003**: The strip MUST keep selection, the right-click device menu and drag-to-reorder.
- **FR-004**: The header's right-click menu MUST offer Fold (on a card) / Unfold (on a strip).
- **FR-005**: The fold MUST belong to the device: it follows moves, delete + undo, and redo, and resets for a new device.
- **FR-006**: The fold MUST be saved with the project for audio effects, MIDI effects and the instrument; older projects open unfolded.
- **FR-007**: Folding MUST NOT affect audio (it is never read on the audio thread).

## Success Criteria *(mandatory)*

- **SC-001**: A folded card takes ≤ 28 px of chain width (plus the 6 px gap).
- **SC-002**: Fold state round-trips through save/load for all three chain kinds (smoke test).
