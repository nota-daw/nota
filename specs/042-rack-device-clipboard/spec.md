# Feature Specification: Move, copy and paste devices in rack chains

**Feature Branch**: `feat/rack-nested-instruments` (follows specs/040 and specs/041)

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "Move, copy and paste devices inside rack chains, the same way as in a track's device chain. On the shared rack card (Instrument, Audio Effect and MIDI Effect Racks; the chosen chain's devices shown left to right as compact cards in the Chains tab), the user can: (1) drag a chain's audio effect (or MIDI effect) to a new position within its section of that chain, with the same drag gesture, drop indicator and undo as reordering devices on the track's own device chain; (2) select a device in a rack chain and copy, cut, paste, duplicate and delete it from its right-click menu and with the same shortcuts as the track chain (⌘C / ⌘X / ⌘V, ⌘D, Delete); (3) copy a device from the track's device chain and paste it into a rack chain, and copy a device from a rack chain and paste it onto a track's device chain (or into another rack's chain), keeping its parameters and state — audio effects into audio-effect slots, MIDI effects into MIDI-effect slots, refusing what a slot can't take (an instrument, a rack inside a rack except the nested Drum Rack rules of specs/041, a plug-in where the rack can't host plug-ins) with a status-line reason. Paste goes after the selected device, or at the end of the chain. Each move / paste / cut / duplicate is one undo step. Mappings to a moved device follow it; a pasted device carries no macro mappings. The Preferences → Shortcuts list and docs stay in sync. Works in all four themes."

User's words (Russian, 2026-10-27): "не могу перемещать эффекты внутри цепочки устройств внутри рэков (менять их положение). Нужно реализовать такую же логику как в основной цепочке + копировать / вставить (через контекстное меню и горячей клавишей). При этом я хотел бы копировать устройства из основной цепочки сюда и наоборот."

## Clarifications

### Session 2026-10-27

- Q: Should rack chain devices (and the track chain, so both stay the same) get a Duplicate command with ⌘D, or is Copy then Paste enough? → A: Add Duplicate (⌘D, plus a menu item) to both the rack chains and the main device chain.
- Q: When dragging a device, should it be able to go only within its own chain, or also into another chain of the same rack and between the track chain and a rack? → A: Within its chain, plus onto another chain's row in the same rack's chain list (the device moves there, to the end). To and from the track chain or another rack stays Cut + Paste.
- Q: When an effect that listens to a sidechain is pasted into a rack chain, where there's no sidechain input, what should happen? → A: Paste it without the key; the status line says the sidechain was dropped.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Engine work is likely small: the rack already moves a chain's devices and MIDI effects (with undo). Copying a device's full state between a track chain and a rack chain may need an engine path that takes a device's state across. The plan decides.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reorder a chain's effects (Priority: P1)

On a rack card, with a chain chosen, the user drags one of its audio effects by its header and drops it before or after another effect in the same chain. A drop indicator shows where it will land, as it does on the track's device chain. The chain plays in the new order. The same works for a chain's MIDI effects, within the MIDI section. The device's right-click menu also offers **Move left** and **Move right**, as the track chain's does.

**Why this priority**: The user can't reorder devices inside a rack today. Order changes the sound (a compressor before or after a reverb), so a rack chain is hard to work with without it.

**Independent Test**: Put an EQ, a compressor and a reverb in a chain. Drag the reverb in front of the EQ, then play: the reverb now comes first. One Undo brings the old order back.

**Acceptance Scenarios**:

1. **Given** a chain with effects A, B, C, **When** the user drags C onto the gap before A, **Then** the chain reads C, A, B, plays in that order, and one Undo restores A, B, C.
2. **Given** a chain with MIDI effects Arp then Chord, **When** the user drags Chord before Arp, **Then** the order is Chord, Arp.
3. **Given** a macro mapped to B's parameter, **When** B is moved, **Then** the macro still drives that same parameter on B.
4. **Given** a drag of an audio effect, **When** it hovers the chain's MIDI section or its instrument slot, **Then** no drop is offered there.
6. **Given** a drag of an effect from chain 1, **When** it is dropped on chain 2's row in the chain list, **Then** it leaves chain 1 and lands at the end of chain 2's matching section (effects or MIDI effects), keeps its settings and its macro mappings, and chain 2 becomes the chosen chain; one Undo puts it back in chain 1.
7. **Given** a drag of a MIDI effect, **When** it hovers the row of a chain that can't take it (chain 17+ of an Instrument Rack), **Then** no drop is offered there.
5. **Given** a device, **When** the user picks **Move left** or **Move right** in its menu, **Then** it swaps with its neighbour. At the first or last position the item is disabled.

---

### User Story 2 - Copy, cut, paste and delete inside rack chains (Priority: P1)

The user clicks a device in a rack chain to select it; the selection looks the same as on the track chain. **Copy**, **Cut**, **Paste**, **Duplicate** and **Delete** are in its right-click menu and on the same keys as on the track chain: ⌘C, ⌘X, ⌘V, ⌘D, and Delete or Backspace. Duplicate is new on the track chain too, so both chains offer the same commands. Paste puts the copy right after the selected device, or at the end of the section when nothing in that chain is selected. The copy keeps every parameter and the device's state.

**Why this priority**: The user asked for it explicitly. It is the everyday way to build variations across chains, such as the same compressor on every layer.

**Independent Test**: Copy a compressor on chain 1, choose chain 2, paste. Chain 2 now has a compressor with the same settings. Cut it and paste it on chain 3; delete it. Each step undoes on its own.

**Acceptance Scenarios**:

1. **Given** a selected effect in chain 1, **When** the user presses ⌘C, chooses chain 2 and presses ⌘V, **Then** chain 2 gains a copy with identical parameter values, at the end of its effects.
2. **Given** a selected effect in chain 2, **When** the user pastes, **Then** the copy lands right after the selected effect.
3. **Given** a selected effect, **When** the user presses ⌘X, **Then** it leaves the chain and the clipboard holds it; ⌘V elsewhere puts it back with its settings.
4. **Given** a selected device, **When** the user presses Delete, **Then** it is removed; one Undo brings it back with its macro mappings.
6. **Given** a selected device in a rack chain or on the track chain, **When** the user presses ⌘D (or picks **Duplicate**), **Then** a copy with the same settings lands right after it and is selected; the clipboard is left as it was; one Undo removes the copy.
5. **Given** a copied MIDI effect, **When** the user pastes into a chain of an Audio Effect Rack, **Then** nothing is pasted and the status line says why.

---

### User Story 3 - Copy between the track chain and rack chains (Priority: P2)

The user copies a device from the track's own device chain and pastes it into a rack chain, or the other way round, or from one rack into another rack's chain. It arrives with its settings and state. One clipboard serves all of these.

**Why this priority**: The user asked for it. It saves rebuilding a sound by hand when moving a tuned effect into a parallel chain, or pulling one out of a rack.

**Independent Test**: On a track, tune a reverb on the main chain and copy it. Select a chain in an Audio Effect Rack on the same track (or another track) and paste. The reverb in the chain has the same values. Copy it back out of the rack and paste it onto the main chain.

**Acceptance Scenarios**:

1. **Given** a reverb copied from the track chain, **When** the user pastes into a rack chain, **Then** the chain gains a reverb with the same values.
2. **Given** an effect copied from a rack chain, **When** the user selects a device on the track chain and pastes, **Then** the effect lands after it on the track chain with the same values.
3. **Given** an Arp copied from the track's MIDI effects, **When** pasted into an Instrument Rack chain or a MIDI Effect Rack chain, **Then** it lands among that chain's MIDI effects with its settings.
4. **Given** something a slot can't take (an instrument, a rack, a plug-in into a rack that can't host plug-ins, a MIDI effect into an audio-effect slot), **When** pasted, **Then** nothing changes and the status line names the reason.
5. **Given** a rack chain device copied while the track chain had keyboard focus elsewhere, **When** the user pastes, **Then** the paste goes to where the user last clicked: the selected rack chain device, or the selected track-chain device.

---

### Edge Cases

- **Paste with nothing selected anywhere on the track:** it lands at the end of the track's own chain, as today.
- **Paste into a full MIDI Effect Rack chain section, or into chain 17+ of an Instrument Rack** (MIDI effects run only in the first 16 chains): refused, with the reason.
- **Copy a device, then delete its chain or track before pasting:** the clipboard still holds the copy, which pastes normally.
- **Cut from a rack chain, then Undo:** the device returns to its place with its macro mappings.
- **A pasted device carries no macro mappings.** A mapping belongs to the rack it was made in.
- **Moving a device while it is being mapped (Map mode):** the move works, and its mapping highlights follow it.
- **A sidechained effect** (a compressor keyed from another track) copied into a rack chain: the chain has no sidechain, so it pastes without its key and the status line says so.
- **A Vocoder with a Track carrier** copied into a rack chain: it pastes and passes sound through, as specs/040's Vocoder note says.
- **A device inside a nested Drum Rack's pad** (specs/041): its pad window follows the same rules inside the Drum Rack card.
- **Keyboard focus:** the shortcuts act only while the device panel has focus. Typing in a rename box never copies or deletes a device.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to reorder a rack chain's audio effects by dragging a device's header within that chain's effect section. A drop indicator shows the landing spot, matching the track chain's drag.
- **FR-002**: Users MUST be able to reorder a rack chain's MIDI effects the same way, within the chain's MIDI section, for Instrument Rack and MIDI Effect Rack chains.
- **FR-003**: A drag MUST NOT drop a device into another section (audio effect ↔ MIDI effect ↔ instrument slot), and MUST NOT leave its rack: dropping onto the track chain or another rack offers no drop.
- **FR-003a**: Dropping a dragged device on another chain's row in the same rack's chain list MUST move it to the end of that chain's matching section, keeping its settings, state and macro mappings, and choose that chain. A row whose chain can't take it (FR-013) offers no drop.
- **FR-004**: A rack chain device's right-click menu MUST offer **Move left** and **Move right**, disabled at the section's ends.
- **FR-005**: Users MUST be able to select a rack chain device by clicking it, shown with the track chain's selection look in every theme. Selecting a rack chain device deselects any track-chain device, and the other way round.
- **FR-006**: A rack chain device's right-click menu MUST offer **Copy**, **Cut**, **Paste**, **Duplicate** and **Delete**, with the same names, icons, key hints and order as the track chain's menu.
- **FR-007**: ⌘C, ⌘X, ⌘V, ⌘D and Delete / Backspace MUST act on the selected rack chain device while the device panel has focus.
- **FR-007a**: The track's own device chain MUST gain **Duplicate** (menu item and ⌘D) for its effects and MIDI effects: a copy with the same settings lands right after the selected device and is selected, without touching the clipboard. A device that can't be copied (the track's instrument) offers it disabled.
- **FR-008**: Paste MUST land right after the selected device in its section, or at the end of the chosen chain's matching section when no device in that chain is selected.
- **FR-009**: The track chain and every rack chain on every track MUST share one device clipboard. A copy taken in either place pastes in the other.
- **FR-010**: A pasted device MUST keep its parameter values and its state (for example an Arp's steps or an EQ's bands), so it sounds the same as its source.
- **FR-011**: A pasted device MUST carry no macro mappings, and its source's mappings MUST stay unchanged.
- **FR-012**: Moving a device, within its chain or to another chain of the same rack, MUST keep every macro mapping on that device. Removing (cut or delete) a device removes its mappings, and undo brings them back.
- **FR-013**: A paste MUST be refused, with a status-line reason and nothing changed, when the target can't take the device. That covers:
  - an instrument;
  - an Audio Effect Rack or MIDI Effect Rack into a rack chain;
  - a plug-in into a rack that can't host plug-ins;
  - a MIDI effect into an audio-effect section, or the other way round;
  - a MIDI effect into an Instrument Rack chain past the first 16;
  - an audio effect into a MIDI Effect Rack.
- **FR-014**: Each move, cut, paste, duplicate and delete MUST be one undo step.
- **FR-015**: An effect with a sidechain key pasted into a rack chain MUST paste without the key (it then works on the chain's own signal), and the status line MUST say the sidechain was dropped. The source keeps its key.
- **FR-016**: Preferences → Shortcuts MUST list ⌘D (Duplicate device) and the rack chain behaviour if it adds anything else the list doesn't already say. The user docs (EN + RU) MUST describe moving and copying devices in rack chains.
- **FR-017**: The selection, drop indicator and menus MUST follow Studio design tokens and read correctly in Studio Dark, Studio Light, Ember Graphite and Ember Paper.

### Key Entities

- **Device clipboard**: the one copied device: its kind (audio effect, MIDI effect, plug-in effect), its parameter values and state, and where it was copied from (for the status line only). It is shared by the track chain and every rack chain.
- **Rack chain device selection**: which rack (track, rack address), chain, section (audio effect or MIDI effect) and index the user selected last. It is mutually exclusive with the track-chain selection.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can reorder an effect inside a rack chain, or move it to another chain of the rack, in one drag under 3 seconds, without opening any window.
- **SC-002**: A device copied from a track chain and pasted into a rack chain renders the same audio as the original, bit for bit or within 1e-4 of each sample, on the same input.
- **SC-003**: Every move, cut, paste, duplicate and delete in a rack chain (and duplicate on the track chain) undoes in exactly one step, verified for each operation by the smoke test.
- **SC-004**: All the refusals in FR-013 leave the project unchanged and show a reason, verified by the smoke test or a headless UI check.
- **SC-005**: The selection and drop indicator are legible in all four themes (checked on rendered cards).

## Assumptions

- **⌘D belongs to the device panel while it has focus.** Elsewhere ⌘D keeps whatever it does there (for example in the piano roll or arrangement); the device panel only handles it while focused, as with ⌘C / ⌘X / ⌘V.
- **Drag stays inside its rack.** A drag reorders within a chain or moves to another chain of the same rack (onto its row). Moving to the track chain or another rack is Cut + Paste.
- **Instruments are not copied or pasted by this feature.** A chain's instrument is swapped from the Browser or the chain menu, and the track chain's own clipboard doesn't copy instruments either.
- **Plug-in effects** copy with their state where the target hosts plug-ins (the Audio Effect Rack and Instrument Rack chains, per specs/040).
- **The nested Drum Rack's pad chains** (specs/041) behave like other rack chains inside the Drum Rack card's pad editor, as far as that card already shows pad effects.
- **The clipboard is in-app only.** It lives for the app session and isn't the system clipboard, as today.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [x] Audio-thread impact considered: moves and pastes are structural rack edits through the rack's immutable snapshots (AR-5), so nothing new happens on the audio thread
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); any new copy path is a C ABI function
- [x] Smoke-test impact assessed: copy/paste between track and rack chains and every undo step get smoke checks
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned, plus FEATURES.md and nota-docs (rack pages, EN + RU)
- [x] UI change → follows `DESIGN.md` (Studio design language); the selection uses the track chain's selection look
