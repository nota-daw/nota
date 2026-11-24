# Feature Specification: Remap one controller function without redoing the wizard

**Feature Branch**: `feat/controller-remap-function`

**Created**: 2026-11-20

**Status**: Draft

**Input**: User description: "Issue nota-daw/nota#973 — Controllers: remap a single button in the setup window without redoing the whole wizard. The Controller setup window (specs/051 FR-030) should open on the controller's current profile and list every function it knows, grouped as in the wizard (knobs, faders, transport, track/bank/page, Session, extra buttons, jog), each with its current hardware control or "—". Click a function, press/move a control → learned with the wizard's detection (note/CC/14-bit/NRPN, encoder mode, button pair vs one encoder). If the control already drives another function, say so and offer to move it or cancel. A function can be cleared. Editing a shipped profile saves a user copy in the user profiles folder that overrides the shipped one, keeping init SysEx / LED feedback. "Learn everything again" keeps the full step-by-step walk. Acceptance: remap Undo to a button and Finish — only that changes; moving Play's control to Stop asks first; works for shipped and wizard-made profiles; survives restart. The user also asks for a convenient, comfortable interface for this editor (follow nota-design)."

**Issue**: [nota-daw/nota#973](https://github.com/nota-daw/nota/issues/973) (approved, milestone 0.67.1). Builds on
[specs/051](../051-controller-auto-mapping/spec.md) FR-030 / FR-030a–c (the setup wizard) and
[specs/107](../107-controller-focus-display/spec.md).

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

## Clarifications

### Session 2026-11-20

- Q: Can the editor put any Nota command on a spare button, or only the wizard's functions? → A: Any command — Extra buttons gets a "+ Command…" row that picks from the command palette's catalog of commands.
- Q: Does a user copy of a shipped profile get a "Back to shipped profile" button? → A: Yes — in the editor header, with confirmation.
- Q: What does Learn everything again start from on a profiled controller? → A: Each step is pre-filled with the current control; Next / Enter keeps it, a new press replaces it; init SysEx, LED feedback and other controls are kept.
- Q: What happens after a function is learned in the editor? → A: Listening stops and the row stays selected; with "unassigned only" on, listening moves to the next unassigned function.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reassign one function on a profiled controller (Priority: P1)

A musician's controller already works with Nota (a shipped profile, or one they made with the
wizard). They want Undo on a different button. They open Preferences → MIDI → their controller →
Set up. Instead of the step-by-step wizard, the window opens on the controller's current mapping:
every function listed by group, each showing the control it is on now. They click **Undo**, press
the button they want, and press Finish. That button now does Undo; nothing else changed.

**Why this priority**: This is the whole point of the issue — today one change costs a full wizard
run or hand-editing JSON.

**Independent Test**: With a controller that has a wizard-made profile, open the window, reassign
Undo to an unused button, Finish; press the button in the arrangement → Nota undoes. Diff the
profile file: only Undo's entry changed.

**Acceptance Scenarios**:

1. **Given** a connected controller with a profile, **When** the user opens Set up, **Then** the
   window shows the mapping editor (not the wizard's first step) listing every function in the
   wizard's groups, each with its current control or "—".
2. **Given** the editor is open, **When** the user picks **Undo** and presses an unused button,
   **Then** the row shows the new control at once and is marked changed; **When** they press
   Finish, **Then** the button does Undo and every other function behaves as before.
3. **Given** the user picked a function and is listening, **When** they press Esc or click the
   row again, **Then** listening stops and the function keeps its previous control.
4. **Given** the user changed several rows, **When** they press Cancel, **Then** nothing is saved
   and the controller behaves as before the window opened.
5. **Given** a change was finished, **When** Nota restarts, **Then** the change is still there.

---

### User Story 2 - Move a control that is already in use (Priority: P1)

The user wants the button that is on Play to be Stop instead. They pick **Stop** and press the
Play button. The editor does not silently steal it: it says that this button drives Play and
offers to move it (Play loses it) or cancel.

**Why this priority**: Without it, the editor either produces a control mapped to two functions or
fails mysteriously — the issue lists it as an acceptance criterion.

**Independent Test**: Pick Stop, press the Play button → a prompt names Play; choose Move → Play
shows "—", Stop shows the button; Finish; pressing the button stops the transport and nothing
starts playback from it.

**Acceptance Scenarios**:

1. **Given** a control drives Play, **When** the user learns it for Stop, **Then** the editor
   names the conflict (control and the function it drives) in the Stop row and offers **Move** and
   **Cancel**; nothing changes until they choose.
2. **Given** the conflict prompt, **When** they choose Move, **Then** Play shows "—" (marked
   changed) and Stop shows the control.
3. **Given** the conflict prompt, **When** they choose Cancel (or Esc), **Then** both rows keep
   their previous controls.
4. **Given** one half of a pair (e.g. next track) is on an encoder, **When** that encoder is moved
   to another function, **Then** the prompt names the pair, and Move clears both halves.

---

### User Story 3 - Edit a shipped profile safely (Priority: P1)

The user has a KeyLab with Nota's shipped profile. They remap Metronome to a spare button. Nota
saves the change as their own copy of that profile, which from then on replaces the shipped one
for this controller. Everything the shipped profile carried that the editor does not show — init
SysEx, LED feedback, controls outside the wizard's groups — is kept in the copy. They can go back
to the shipped profile later.

**Why this priority**: Shipped profiles are the most common case and today can't be touched up in
the window at all.

**Independent Test**: With a shipped profile, assign Metronome, Finish → a user profile file with
the same identity appears in the user profiles folder; the controller's init / LED behaviour is
unchanged; Metronome works; after restart the user copy is still in use. Choose "Back to shipped
profile" → the copy is removed and the shipped mapping is back.

**Acceptance Scenarios**:

1. **Given** a shipped profile, **When** the editor opens, **Then** its header says the profile is
   shipped and that changes are saved as the user's own copy.
2. **Given** a change to a shipped profile, **When** the user finishes, **Then** a user copy is
   written that overrides the shipped profile for this controller and keeps its init / release
   messages, LED feedback and every control the editor did not change.
3. **Given** a user copy of a shipped profile, **When** the editor opens, **Then** it offers
   "Back to shipped profile", which (after confirmation) removes the copy and re-applies the
   shipped profile.
4. **Given** a wizard-made (user) profile, **When** the user finishes a change, **Then** that same
   profile file is updated in place, keeping anything the user added by hand.

---

### User Story 4 - Clear a function (Priority: P2)

The user's pad sends the same note Nota uses for Loop and they keep toggling loop by accident. They
open the editor and clear Loop so no control does it.

**Why this priority**: Explicitly in the issue; simple, but less common than reassigning.

**Independent Test**: Clear Loop, Finish → pressing the former Loop control no longer toggles loop.

**Acceptance Scenarios**:

1. **Given** a function with a control, **When** the user clears it, **Then** the row shows "—"
   (marked changed) and after Finish no control drives it.
2. **Given** a cleared row before Finish, **When** the user reverts that row, **Then** it gets its
   original control back.

---

### User Story 5 - Find what a control does (Priority: P2)

The user isn't sure which function a fader on their controller is on. With the editor open and not
listening, they move it: the row it drives is highlighted and scrolled into view (or the editor
says it drives nothing).

**Why this priority**: Makes the editor comfortable to use with a physical device in hand — you
start from the hardware, not from a list of names. Part of the "convenient interface" ask.

**Independent Test**: Not listening, press the Play button → the Play row lights up and comes into
view; press an unmapped pad → a status line says it isn't mapped and offers to assign it.

**Acceptance Scenarios**:

1. **Given** the editor is open and not listening, **When** a mapped control sends, **Then** its
   function row is highlighted briefly and scrolled into view.
2. **Given** an unmapped control sends, **Then** the status line names the control and says it
   drives nothing.

---

### User Story 6 - Put any command on a spare button (Priority: P2)

The user's controller has spare buttons and they want one to Duplicate. In Extra buttons they click
**+ Command…**, type "dupl", pick Duplicate, press the spare button, Finish. The button now
duplicates the selection.

**Why this priority**: Turns spare hardware into shortcuts; added on clarification (2026-11-20).

**Independent Test**: Add Duplicate via + Command…, learn a button, Finish, select a clip, press the
button → the clip is duplicated; restart → still works; remove the row → the button does nothing.

**Acceptance Scenarios**:

1. **Given** the editor, **When** the user opens + Command… and types part of a name, **Then** the
   list narrows to matching commands with their category.
2. **Given** a picked command, **Then** a new row appears in Extra buttons already listening.
3. **Given** a command row, **When** the user removes it and finishes, **Then** no control drives it.

---

### User Story 7 - Learn everything again (Priority: P3)

For a new controller, or to start over, the user can still walk the full step-by-step wizard from
the editor via **Learn everything again**.

**Why this priority**: The existing wizard keeps working; this is only the way back into it.

**Independent Test**: Click Learn everything again → the familiar wizard starts at its first step;
Finish replaces the profile as the wizard does today.

**Acceptance Scenarios**:

1. **Given** a controller with no profile, **When** the user opens Set up, **Then** the wizard opens
   as today (there is nothing to edit).
2. **Given** the editor, **When** the user chooses Learn everything again, **Then** the wizard walk
   starts; Cancel in the wizard returns to the unchanged editor.
3. **Given** a profiled controller, **When** the wizard walk starts, **Then** each step shows the
   current control; pressing Enter on every step and Finish leaves the profile unchanged.

---

### Edge Cases

- **Controller disconnected while the editor is open**: listening stops, the editor says the
  controller is gone; existing rows stay editable (clear / revert) and Finish still saves.
- **A control that is pinned / unbound / MIDI-learned** (specs/051 FR-012, FR-031–034): those
  per-parameter overrides are separate from the profile and stay as they are; the editor changes
  only the profile.
- **Knobs and faders**: the editor shows slots 1–8 for both, like the wizard, plus master fader;
  slots the profile doesn't have show "—" and can be assigned.
- **Pair functions** (previous / next track, bank, page, device, clips, Session tracks, undo /
  redo, tempo −/+, rewind / forward): one row per pair. Learning works as in the wizard — turn an
  encoder and both halves are set; press a button and it is the first half, then the editor asks
  for the second button in the same row; leaving before the second button changes nothing.
  Each half can also be reassigned on its own; an encoder that did both halves then leaves both
  (it can't be limited to one turning direction), and the editor says which function lost it.
- **Ambiguous encoder** (the wizard asks for Next): the editor asks the same way, inside the row.
- **Learning the control a function already has**: no conflict, no change.
- **Two controls send the same message** (rare hardware): the editor can't tell them apart — the
  conflict prompt applies as for any shared control.
- **Profiles with controls outside the wizard's groups** (pads, device-page specific knobs,
  protocol surfaces' extras): shown in an "Other" group as read-only, kept untouched on save.
- **Protocol (non-generic) profiles** — Mackie Control / HUI style or closed DAW modes, where the
  mapping is the protocol: the editor says this profile's controls are fixed by its protocol and
  only offers Learn everything again (which makes a generic user profile).
- **The profile file was edited by hand while the editor is open**: on Finish the editor writes
  its result over the file the user saw when opening it; it does not merge.
- **Nothing changed**: Finish writes nothing (no user copy of a shipped profile is created).

## Requirements *(mandatory)*

### Functional Requirements

**Opening**

- **FR-001**: Set up on a controller that has a generic profile (shipped or user) MUST open the
  mapping editor on that profile; a controller without a profile MUST open the step-by-step wizard
  as today.
- **FR-002**: The editor MUST list every function the wizard learns, in the wizard's groups —
  knobs, faders + master, transport, track / bank / page / device, Session, extra buttons + jog —
  and, read-only, any other controls the profile declares (group "Other").
- **FR-002a**: The Extra buttons group MUST end with a **+ Command…** row that opens a searchable
  picker of Nota's commands (the command palette's catalog, by name and category); picking one adds a row
  for it, which then learns, conflicts, clears and saves like any other function. A row added this
  way can be removed. Commands that already have a row (undo, metronome…) are not offered twice.
  Commands bound to a controller button fire once per press.
- **FR-003**: Each row MUST show the function (name and icon, as in the wizard) and its current
  control in readable form (e.g. "CC 21 · ch 1 · relative", "Note C1 · ch 10", "NRPN 12 · 14-bit"),
  or "—" when it has none. Pair rows show both halves.
- **FR-004**: The header MUST name the controller and the profile, and whether the profile is
  shipped (changes will be saved as the user's own copy) or the user's.

**Learning one function**

- **FR-005**: Picking a row MUST start listening for that function: the row shows it is listening
  and what the controller sends as it arrives (readout with an activity light, as in the wizard).
  Only one row listens at a time; picking another row moves listening there.
- **FR-006**: The first movement or press MUST be learned with the wizard's detection (message
  type, channel, number, 14-bit, NRPN, encoder mode, encoder vs pair of buttons — FR-030 / 030c of
  specs/051) and shown in the row at once, marked as changed.
- **FR-006a**: After a learn (including a resolved conflict), listening MUST stop and the row stay
  selected; when the "unassigned only" filter is on, listening MUST instead move to the next
  unassigned function (none left → listening stops and the status line says so).
- **FR-007**: Esc, clicking the listening row again, or Cancel MUST stop listening without changing
  that function.
- **FR-008**: If the learned control already drives another function (including one half of a
  pair), the editor MUST name it in the row and offer **Move** (the other function loses the
  control) and **Cancel**; nothing changes until the user chooses.
- **FR-009**: Each function MUST be clearable (no control), and each changed row MUST be revertable
  to the control it had when the editor opened.
- **FR-010**: When not listening, a control arriving from the controller MUST highlight and scroll
  to the row it drives, or say in the status line that it drives nothing.

**Saving**

- **FR-011**: Finish MUST save the changes; Cancel (or closing the window) MUST discard them. With
  unsaved changes, closing via the window's close control MUST ask first.
- **FR-012**: Saving MUST change only the edited functions; every other control, the profile's
  identity, init / release messages, LED feedback and anything the user added by hand MUST be kept.
- **FR-013**: Saving a change to a shipped profile MUST write a user copy into the user profiles
  folder that overrides the shipped profile for this controller; saving a user profile MUST update
  that profile in place.
- **FR-014**: The change MUST take effect on Finish without a restart and persist across restarts.
- **FR-015**: For a user copy of a shipped profile, the editor header MUST offer **Back to shipped
  profile**, which after confirmation removes the copy and re-applies the shipped profile.
- **FR-016**: The editor MUST offer **Learn everything again**, which runs the existing wizard;
  cancelling it returns to the editor unchanged. On a profiled controller each wizard step starts
  with the function's current control shown as learned: Next (or Enter) keeps it, a new movement
  or press replaces it, "No more knobs" style skips clear the rest of the group. Finish saves like
  the editor (FR-012, FR-013): init / release messages, LED feedback and controls outside the
  wizard's groups are kept, and + Command… rows stay.

**Comfort** *(the "convenient interface" ask)*

- **FR-017**: The editor MUST be usable from the keyboard: Up / Down move between rows, Enter
  starts listening, Delete / Backspace clears, Esc stops listening (or, when not listening, is
  Cancel), Tab reaches the footer.
- **FR-018**: The editor MUST let the user jump to a group (the wizard's strip of groups, each with
  its count of assigned functions) and filter to "unassigned only".
- **FR-019**: The window MUST follow Nota's dialog look (DESIGN.md / nota-design): flat rows, the
  listening row in solid orange, changed rows marked with a soft orange tint or dot, controls shown
  as chips with tabular figures, Cancel / Finish (primary) in a footer under a seam; it MUST work in
  all four colour themes and in Russian.

### Key Entities

- **Controller function**: something Nota does from a controller — a knob or fader slot, a transport
  role, a command (undo, metronome…, or any palette command added via
  + Command…), or a pair of opposite actions. Has a group, a name, an icon.
- **Assigned control**: the hardware message a function is on — kind (note / CC / NRPN / pitch
  bend…), channel, number, resolution, encoder mode; for pairs, one per half or one encoder.
- **Profile**: the controller's set of assignments plus what the editor doesn't edit (identity,
  init / release messages, LED feedback, other controls). Shipped or user; a user profile with a
  shipped profile's identity overrides it.
- **Pending edit**: per function, its original control and its new one (or cleared) — what Finish
  writes and Cancel drops.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Reassigning one function on a profiled controller takes at most 3 actions (pick row,
  press control, Finish) and under 15 seconds, versus walking all ~30 wizard steps today.
- **SC-002**: After a single-function edit, the saved profile differs from the previous one only in
  that function's entry (and, for a move, the function that lost the control) — verified by test.
- **SC-003**: 100% of conflicting assignments are announced before they take effect; no control
  ends up driving two functions after Finish.
- **SC-004**: A change to a shipped profile keeps the controller's init and LED behaviour identical
  to the shipped profile (verified on the KeyLab Essential 61 mk3).
- **SC-005**: Every edit survives a restart.
- **SC-006**: The whole editor can be operated with keyboard plus the controller, without the mouse.

## Assumptions

- The editor replaces the wizard as the window's first screen only when a generic profile exists;
  the wizard itself (steps, pictures, detection) is reused unchanged.
- "Back to shipped profile" removes only the user copy (confirmed 2026-11-20).
- Per-parameter overrides from specs/051 (pins, unbinds, MIDI Learn) stay outside this editor.
- Init SysEx and LED feedback remain hand-edited (as FR-030 says); the editor only preserves them.
- Protocol-driven profiles (MCU / HUI / closed DAW modes) are not remappable here.
- Russian strings follow the usual nota-translations flow.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [x] Audio-thread impact considered: none — the editor is UI + profile files; MIDI arrives through the existing controller listener
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no engine change expected
- [x] Smoke-test impact assessed: no engine behaviour change; profile read / write covered by managed tests
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned, FEATURES.md and user docs via nota-features / nota-docs
- [ ] UI change → follows `DESIGN.md` (Studio design language)
