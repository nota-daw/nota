# Feature Specification: The instrument as a first-class device in the chain

**Feature Branch**: `feat/instrument-device-chain`

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "The instrument becomes a first-class device in the track's device chain (milestone 0.58.1, issues nota-daw/nota#146, #327, #110, #313). (1) #327 + #146 — The instrument card is selectable like audio/MIDI effects (accent border). Copy / cut / paste / delete work on it where they make sense. Its context menu gets Delete and Replace… (open question: does deleting the instrument leave an empty instrument track that a new instrument can be dropped onto, or remove the whole track?). Any selected device — instrument, audio effect or MIDI effect — can be moved to another track directly (e.g. "Move to track ▸" in the context menu and/or dragging the card onto another track), without a cut / switch track / paste round-trip; settings, automation and macro mappings travel where possible, each move is one undo step. (2) #110 — A compact, clearly labelled Remove device control in device headers, distinct from bypass and the plug-in window close; keyboard accessible; consistent across built-in devices, plug-ins and racks; removal is undoable with settings and connections preserved. (3) #313 — Session view: dropping an instrument (built-in or plug-in, also an instrument preset) onto a column header or the empty area right of the last column creates a new instrument track with it; dropping it onto a track's column/slots replaces that track's instrument. All of these share one path for removing and replacing a track's instrument, undoable in one step."

Issues: nota-daw/nota#146, nota-daw/nota#327, nota-daw/nota#110, nota-daw/nota#313.

## Clarifications

### Session 2026-10-27

- Q: What does Delete on the instrument do — leave an empty instrument track, or remove the whole track? → A: Remove the whole track (with a confirmation when the track has clips). Cut and Move of the instrument therefore also remove the source track.
- Q: When an instrument is moved or pasted onto a track that already has one, replace or swap? → A: Replace; the target's old instrument is gone and comes back on Undo.
- Q: Move to another track: menu only, or also by dragging? → A: Both — **Move to track ▸** in the device's menu, and dragging the card's header onto a track header in the Arrangement or Session view (⌥ while dropping copies instead of moving).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Today an instrument track always has an instrument: the engine can replace it (built-in or
plug-in), and the device clipboard copies only audio and MIDI effects. An instrument track
keeps that rule (deleting the instrument deletes the track, see Clarifications). Copying
and moving an instrument between tracks likely needs engine work (an instrument's full
state, rack contents and plug-in state, taken across tracks in one undo step). The plan
decides.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select, delete and replace the instrument (Priority: P1)

In a track's device chain the user clicks the instrument card and it is selected the same way
an effect is (accent border). Its right-click menu offers **Replace…** and **Delete**, and
Delete / Backspace work on it as on an effect. Replace… opens a picker of instruments
(built-in and plug-ins); the chosen one takes the old one's place in one undo step. Delete
removes the whole track (FR-005).

**Why this priority**: The instrument is the one device the user can't act on directly
today (#146, #327). Replacing or removing it means dragging from the Browser or deleting the
whole track.

**Independent Test**: On a Nota Volt track, click the Volt card: it shows as selected.
Pick **Replace… → Nota Keys**: the track now plays Keys; one Undo brings Volt back with its
settings. Press Delete with the instrument selected and confirm: the track is removed; one Undo
restores it with its clips.

**Acceptance Scenarios**:

1. **Given** an instrument track, **When** the user clicks the instrument card's header or empty body area, **Then** the card shows the selection border and any selected effect is deselected.
2. **Given** a selected instrument, **When** the user picks **Replace…** and chooses another instrument, **Then** the new instrument plays the track's clips and MIDI effects unchanged; one Undo restores the old instrument with its settings.
3. **Given** a selected instrument, **When** the user presses Delete or picks **Delete**, **Then** a confirmation appears if the track has clips, and on confirm the whole track is removed, as Delete Track does; one Undo restores the track with its instrument, settings, clips, automation and links. A track without clips is removed without asking.
4. **Given** the instrument is a Drum Rack or Instrument Rack, **When** it is replaced or deleted, **Then** its whole contents go with it and come back on Undo.
5. **Given** a selected instrument, **When** the user presses ⌘C then selects another instrument track's instrument and presses ⌘V, **Then** the target's instrument is replaced by a copy with identical settings, in one undo step.

---

### User Story 2 - A visible Remove control on every device header (Priority: P1)

Every removable device's header shows a small Remove (×) control, separate from the on/off
switch and from a plug-in's "open editor" control. Clicking it removes the device, the same
as Delete in its menu. It has a tooltip ("Remove device"), is reachable with the keyboard and
looks the same on built-in devices, plug-ins and racks, in every theme.

**Why this priority**: Requested on Discord (#110); today removal is hidden in the
right-click menu or behind selection + Delete, which new users don't find.

**Independent Test**: Add an EQ-8, a plug-in effect and an Audio Effect Rack to a track.
Each header shows the Remove control; clicking each removes that device; three Undos bring
all three back with their settings.

**Acceptance Scenarios**:

1. **Given** any audio effect, MIDI effect, rack or plug-in card, **When** the user clicks its Remove control, **Then** the device is removed and one Undo restores it with its settings, automation, sidechain key and macro / modulation links.
2. **Given** the instrument card, **When** the user clicks its Remove control, **Then** the result is the same as Delete in User Story 1 (the track is removed, with a confirmation when it has clips); its tooltip says "Remove track".
3. **Given** a folded device strip, **When** the user looks at it, **Then** the Remove control is still reachable (on the strip or its hover state).
4. **Given** keyboard navigation in the device panel, **When** the Remove control has focus and the user presses Enter or Space, **Then** the device is removed.
5. **Given** a device inside a rack chain, **When** the user looks at its compact card, **Then** it offers the same Remove control as the track chain's cards.

---

### User Story 3 - Drop an instrument in Session view (Priority: P2)

In Session view the user drags an instrument (built-in, plug-in, or an instrument preset)
from the Browser. Dropped on an instrument track's column header or any of its slots, it
replaces that track's instrument. Dropped on the empty area right of the last column (or on
an audio track's column), it creates a new instrument track with that instrument. Racks
follow the Arrangement's rules: dropping on a rack track adds a chain / pad instead of
replacing the rack.

**Why this priority**: A user report (#313). Session view only shows "Drop instruments onto
the arrangement.", so Session-first users have to switch views to build a set.

**Independent Test**: In Session view, drop Nota Volt right of the last column: a new
track appears with Volt. Drop Nota Keys on that column's header: the track now plays Keys.
Undo twice: back to the original set.

**Acceptance Scenarios**:

1. **Given** Session view, **When** the user drops a built-in instrument right of the last column, **Then** a new instrument track with that instrument is added at the end and selected; one Undo removes it.
2. **Given** an instrument track's column, **When** the user drops an instrument on its header or any of its slots, **Then** the track's instrument is replaced, its clips untouched; one Undo restores it.
3. **Given** an instrument preset (factory, user, Mosaic pack or drum kit), **When** dropped the same ways, **Then** the instrument arrives with the preset loaded.
4. **Given** a plug-in instrument, **When** dropped, **Then** it behaves as a built-in one; if the plug-in fails to load, nothing changes and the status line says so.
5. **Given** an audio track's column, **When** the user drops an instrument on it, **Then** a new instrument track is created right after it (an audio track can't take an instrument).
6. **Given** an Instrument Rack or Drum Rack track, **When** an instrument is dropped on its column, **Then** it is added as a chain / pad, as in the Arrangement.
7. **Given** a drag is in progress over Session view, **When** it hovers a valid target, **Then** the target is highlighted the same way as other Session drops.

---

### User Story 4 - Move a device to another track (Priority: P2)

The user moves the selected device — instrument, audio effect or MIDI effect — to another
track directly, without Cut, switching tracks and Paste. The device's menu has
**Move to track ▸** listing the tracks that can take it; picking one moves it there in one
undo step, with its settings. Dragging the card's header onto a track header in the
Arrangement or Session view does the same (FR-014); holding ⌥ on drop copies instead.

**Why this priority**: #327. Today it takes Cut, a track switch and Paste, and the
instrument can't be cut at all.

**Independent Test**: On track 1, pick a reverb's **Move to track ▸ Track 2**: it leaves
track 1 and lands at the end of track 2's effects with the same settings. One Undo puts it
back on track 1 in its old position.

**Acceptance Scenarios**:

1. **Given** a selected audio effect, **When** the user picks **Move to track ▸ X**, **Then** it is removed from its track and appended to X's audio effects with the same parameters and state; one Undo reverts both tracks.
2. **Given** a selected MIDI effect, **When** moved, **Then** only instrument tracks are offered and it lands at the end of the target's MIDI effects.
3. **Given** the selected instrument, **When** moved to instrument track X, **Then** X's instrument is replaced by it (FR-012) and the source track is removed (FR-005), after a confirmation when the source has clips; one Undo restores both tracks.
4. **Given** automation lanes on the moved device's parameters, **When** it moves, **Then** the lanes move with it to the target track.
5. **Given** a moved device with a sidechain key, **When** the target track is its own key source, **Then** the key is cleared and the status line says so.
6. **Given** a device that is mapped from a rack macro or a modulation source on the source track, **When** moved, **Then** those links are dropped (they belong to the source track) and come back on Undo.
7. **Given** a device card being dragged, **When** it hovers a track header in the Arrangement or Session view that can take it, **Then** the header highlights; on drop the device moves there (with ⌥ held, a copy lands there and the source is untouched). A header that can't take it offers no drop.

---

### Edge Cases

- **Audio, return, group and master tracks** take audio effects but no instrument or MIDI effects; Move to track ▸ lists only the tracks that can take the device.
- **A plug-in instrument copied or moved** carries its plug-in state; if it fails to load on the target, nothing changes and the status line says so.
- **Deleting / replacing the instrument while notes sound** stops those notes; nothing hangs.
- **The instrument is folded**: the folded strip is selectable and its Remove control works.
- **Cut on the instrument**: puts a copy on the clipboard and removes the track (confirmed when it has clips); Paste on another instrument track replaces its instrument.
- **The last track in the project**: deleting its instrument removes it, as Delete Track already does for the last track.
- **Dragging the instrument onto a track header without ⌥** from a track with clips: the same confirmation as the menu.
- **The Remove control in the instrument's nested Drum Rack window** removes pad devices, not the rack.
- **Typing in a rename box or value entry**: Delete / Backspace never remove a device.
- **Session drop on a frozen or bounced track**: same rule as the Arrangement (refused with a reason if the Arrangement refuses).
- **Instrument with MPE / per-note expression clips** replaced by one without: the clips stay; expression is ignored by the new instrument.
- **The clipboard holds an instrument and the user pastes onto an audio track or into a rack chain's effect slot**: refused with a status-line reason.
- **Paste an instrument onto an Instrument Rack chain** (outside scope of the track chain): handled by the rack's existing chain-instrument rules, or refused with a reason.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The instrument card MUST be selectable in the track's device chain, with the same selection look as effects, mutually exclusive with effect selection.
- **FR-002**: The instrument card's right-click menu MUST offer **Replace…**, **Copy**, **Cut** (moving it per FR-005 on the source), **Paste** (replace, FR-004), **Delete** and **Move to track ▸**.
- **FR-003**: **Replace…** MUST let the user pick any built-in instrument, rack or installed plug-in instrument and swap it in as one undo step; the track's clips, MIDI effects, audio effects and mixer state stay as they were.
- **FR-004**: The device clipboard MUST hold an instrument (with its full settings and state, including racks' contents and plug-in state). Pasting it onto an instrument track MUST replace that track's instrument in one undo step; pasting it anywhere that can't take an instrument MUST change nothing and say why.
- **FR-005**: Deleting the instrument (menu, Delete key, Remove control, Cut or Move) MUST remove the whole track, the same as Delete Track, asking for confirmation first when the track has clips. It is one undo step that restores the track with its instrument, settings, clips, automation and links. An instrument track never exists without an instrument.
- **FR-006**: Every removable device card (built-in audio effect, MIDI effect, plug-in, rack, instrument; on the track chain and in rack chains; full and folded) MUST show a Remove control in its header, visually distinct from the on/off switch and the plug-in editor control, with a "Remove device" tooltip.
- **FR-007**: The Remove control MUST be keyboard focusable and activate on Enter / Space, and MUST NOT take focus away from text entry.
- **FR-008**: Removing a device by any route MUST be one undo step that restores its position, parameters, state, automation, sidechain key and macro / modulation links.
- **FR-009**: In Session view, dropping an instrument (built-in, plug-in or instrument preset) on an instrument track's column header or slot MUST replace that track's instrument, using the same replace path as FR-003 (racks: add a chain / pad, as in the Arrangement).
- **FR-010**: In Session view, dropping an instrument right of the last column, or on an audio / return / group column, MUST create a new instrument track with it (at the end, or right after that column), selected, in one undo step.
- **FR-011**: Session view MUST highlight valid instrument drop targets (column headers, slots, the area right of the last column) during a drag, and the "Drop instruments onto the arrangement." message MUST no longer appear.
- **FR-012**: Moving or pasting an instrument onto an instrument track MUST replace that track's instrument; the old instrument is gone and comes back on Undo. A move's replace and the source track's removal are one undo step.
- **FR-013**: **Move to track ▸** MUST list only tracks that can take the device (instrument and MIDI effects: instrument tracks; audio effects: any track with a device chain), exclude the source track, and move the device with its parameters, state and automation lanes in one undo step; macro and modulation links on the source are dropped, a sidechain key pointing at the target itself is cleared, each with a status-line note.
- **FR-014**: Users MUST also be able to drag a device card's header onto a track header in the Arrangement or Session view: valid headers highlight, a drop moves the device as FR-013 (an instrument as FR-012 / FR-005), and holding ⌥ (Alt) on drop copies it instead, leaving the source untouched. Headers that can't take the device offer no drop.
- **FR-015**: The Preferences → Shortcuts list, `CHANGELOG.md`, `FEATURES.md` and the user docs MUST reflect the new commands and gestures.
- **FR-016**: The new controls and states MUST follow the Studio design language and draw correctly in all four themes.
- **FR-017**: The MCP device tools MUST be able to delete, replace and move the instrument and to move a device between tracks, matching the UI (see also existing instrument tools).

### Key Entities *(include if feature involves data)*

- **Track instrument**: the instrument slot of an instrument track — a built-in synth, a rack or a plug-in, never empty; its settings, state, automation lanes and links.
- **Device clipboard**: one shared clipboard that now holds an audio effect, a MIDI effect or an instrument, with full state.
- **Device move**: a device leaving one track and arriving on another as a single undoable edit, carrying parameters, state and automation lanes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Replacing a track's instrument takes at most 2 actions from the device panel (right-click → Replace… → pick), down from a Browser search and drag.
- **SC-002**: Removing any device takes 1 click on a visible control, on 100% of device card types (built-in, plug-in, rack, instrument; full and folded; track chain and rack chains).
- **SC-003**: Moving a device to another track takes 2 menu steps instead of 4 (cut, switch track, select position, paste), and one Undo reverts it fully on both tracks.
- **SC-004**: In Session view, adding a new instrument track or swapping a track's instrument needs no switch to the Arrangement; 100% of instrument kinds offered in the Browser drop there.
- **SC-005**: Every new edit (replace, delete, copy-paste, move, Session drop) round-trips: one Undo returns the project to a state that saves and reloads identically to before the edit.
- **SC-006**: No audible glitch or dropout on other tracks while an instrument is replaced, deleted or moved during playback.

## Assumptions

- "Move to track" moves a device to the end of the target's matching section (audio effects / MIDI effects); position on the target is adjusted afterwards by the existing in-chain drag.
- Macro mappings and modulation links belong to the track (or rack) they were made in and do not travel with a moved or pasted device, consistent with specs/042 ("a pasted device carries no macro mappings").
- Automation lanes do travel with a moved device (not with a copied one), since the user's intent is the same device on another track.
- The Remove control is always visible (not hover-only) so it is discoverable, as #110 asks; its exact look follows `DESIGN.md`.
- Replace… reuses the instrument list the Browser already shows (built-ins, racks, installed plug-ins); no new instrument kinds are added.
- Session drops reuse the Arrangement's instrument-drop rules (replace on an instrument track, chain / pad on a rack, new track otherwise) so both views behave the same.
- Moving / pasting into rack chains is unchanged from specs/042; this feature only adds the instrument to the track-level clipboard and the cross-track move.
- A copy by ⌥-drag carries no automation lanes, as a pasted device carries none.
- Multi-select of devices and "group a selection into a rack" (#328) are out of scope.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
