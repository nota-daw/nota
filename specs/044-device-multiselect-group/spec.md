# Feature Specification: Multi-select devices and group them into a rack

**Feature Branch**: `feat/device-multiselect-group`

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "Device chain: multi-select devices and group them into a rack (GitHub issue nota-daw/nota#328). Goal: select several devices in a chain at once and fold them into a rack so they can be duplicated, macro-mapped and saved as one preset. Today only one device can be selected at a time and there is no "group into rack" command. Wanted: (1) Multi-select devices — Shift-click selects a range, Cmd/Ctrl-click toggles — across MIDI effects, the instrument and audio effects; copy / cut / paste / delete / move act on the whole selection. (2) Group into rack (Cmd/Ctrl+G and the context menu), the rack type chosen from what's selected: only audio effects → Audio Effect Rack; an instrument (with MIDI effects before it and/or audio effects after it) → Instrument Rack whose first chain holds those devices in order; only MIDI effects → MIDI Effect Rack. The selection must be contiguous within its domain; the new rack takes its place in the chain. (3) Ungroup is the reverse. Group and Ungroup are each one undo step. Split out of #326; depends on #327 (selecting the instrument, done), #329 (MIDI effects in Instrument Rack chains, done) and #330 (MIDI Effect Rack, done)."

Issue: nota-daw/nota#328 (split out of #326).

## Clarifications

### Session 2026-10-27

- Q: What happens on Group / Ungroup to automation lanes, sidechain keys, modulation (CV) links and Arp CC lanes on the devices (today #257 refuses to wrap such a device)? → A: They travel with the devices into the rack chain on Group and back out on Ungroup; nothing is refused for having them.
- Q: What do Delete, Cut and Move to track do when a multi-selection includes the instrument along with effects? → A: Remove the track as in specs/043, but always ask for confirmation first when the selection holds more than the instrument, even if the track has no clips.
- Q: What does Paste do when the clipboard holds the instrument together with effects and the target is another instrument track? → A: Replace the target's instrument and insert the effects into their sections, as one undo step (specs/043); on a track that can't take an instrument, the instrument and MIDI effects are skipped.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

What exists today: the track's device chain has one selection (an audio effect, a MIDI
effect or, since specs/043, the instrument). ⌘G already wraps **one** selected audio effect
in an Audio Effect Rack and ⌘⇧G unwraps a one-chain Audio Effect Rack (#257), but the engine
refuses a device that has a sidechain key, automation, a modulation (CV) link or an Arp CC
lane on it. MIDI effects and the instrument can't be grouped at all, and there is no
ungroup for an Instrument Rack or a MIDI Effect Rack. The device clipboard holds one device.
This feature widens the selection to a set, the clipboard to a run of devices, and ⌘G / ⌘⇧G
to all three rack types. The plan decides how much is engine work (one undo step per group
/ ungroup, a multi-device clipboard, moving automation into a rack chain).

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Group several audio effects into an Audio Effect Rack (Priority: P1)

The user has EQ-8 → Compressor → Saturator → Reverb on a track. They click the Compressor,
Shift-click the Reverb (three cards now show the selection border) and press ⌘G. The three
effects are replaced, in place, by one Audio Effect Rack whose single chain holds them in the
same order with the same settings. The sound doesn't change. The rack can now be duplicated,
macro-mapped and saved as one preset. ⌘⇧G on the rack puts the three effects back.

**Why this priority**: The core of #328 and the most common case: building an effect chain,
then wanting to save, duplicate or macro-control it as one unit. Today it means rebuilding
the chain inside an empty rack device by device.

**Independent Test**: Build the chain above, select Compressor..Reverb, press ⌘G: the chain
shows EQ-8 → Audio Effect Rack, the rack's chain lists Compressor, Saturator, Reverb with the
same values, and a rendered clip sounds the same (level within 0.1 dB). One Undo restores the
four separate cards; ⌘⇧G on the rack does the same.

**Acceptance Scenarios**:

1. **Given** a contiguous selection of two or more audio effects, **When** the user presses ⌘G or picks **Group into rack** in the right-click menu of any selected card, **Then** they are replaced by an Audio Effect Rack at the position of the first one, holding them in one chain, in order, with identical parameters and state; the new rack is selected.
2. **Given** one selected audio effect, **When** grouped, **Then** it behaves as today (#257): a one-device Audio Effect Rack.
3. **Given** the grouped rack, **When** the user presses ⌘Z once, **Then** the chain is back exactly as it was, with the same devices selected.
4. **Given** a one-chain Audio Effect Rack, **When** the user presses ⌘⇧G or picks **Ungroup rack**, **Then** its chain's devices take its place in order, with their settings, as one undo step, and they are selected.
5. **Given** grouping during playback, **When** the rack replaces the devices, **Then** there is no audible gap, click or level jump on the track.

---

### User Story 2 - Multi-select devices and act on them together (Priority: P1)

In the device chain the user Shift-clicks to extend the selection to a range and ⌘-clicks
(Ctrl on Windows/Linux) to add or remove a single card. The selection may span MIDI effects,
the instrument and audio effects. Copy, Cut, Paste, Delete, Duplicate, dragging and
**Move to track ▸** act on every selected device at once, each as one undo step.

**Why this priority**: Multi-select is the precondition for grouping and is useful on its
own: deleting or copying half a chain is one action instead of many.

**Independent Test**: On a track with Arp → Volt → EQ-8 → Delay → Reverb, click EQ-8, ⌘-click
Reverb: two cards are selected. ⌘C, select another instrument track's last effect, ⌘V: EQ-8
and Reverb land after it, in that order, with their settings. Back on the first track,
Shift-click from EQ-8 to Reverb and press Delete: all three effects go in one step, and one
Undo brings them back with their settings.

**Acceptance Scenarios**:

1. **Given** one selected card, **When** the user Shift-clicks another card in the same track chain, **Then** every card between them (inclusive, in chain order MIDI effects → instrument → audio effects) is selected.
2. **Given** a selection, **When** the user ⌘-clicks (Ctrl-clicks) a card, **Then** that card toggles in or out of the selection and the others stay selected.
3. **Given** a selection, **When** the user plain-clicks a card, **Then** only that card is selected; clicking empty chain space or pressing Esc clears the selection.
4. **Given** several selected effects, **When** the user presses ⌘C then ⌘V on another track (or on the same one), **Then** copies of all of them are pasted together, in their original order, each into its own section (MIDI effects among MIDI effects, audio effects among audio effects), after the selected device of that section or at its end; one Undo removes them all.
5. **Given** several selected effects, **When** the user presses Delete, Backspace or ⌘X, **Then** all of them are removed (⌘X copies first) in one undo step.
6. **Given** several selected effects, **When** the user presses ⌘D, **Then** each selected run is duplicated right after itself, in one undo step, and the copies become the selection.
7. **Given** several selected effects of one section, **When** the user drags any selected card's header, **Then** the whole selection moves together to the drop gap, keeping its order, in one undo step.
8. **Given** a selection, **When** the user picks **Move to track ▸ X**, **Then** every selected device moves to X (appended to its matching sections, in order) in one undo step, following specs/043's rules for what X can take.
9. **Given** a multi-selection, **When** the user right-clicks a selected card, **Then** the menu acts on the whole selection and its header says how many devices are selected (e.g. "3 devices"); right-clicking an unselected card selects only that card first.

---

### User Story 3 - Group an instrument with its effects into an Instrument Rack (Priority: P2)

The user selects Arp → Volt → Chorus (a MIDI effect, the instrument and an audio effect) and
presses ⌘G. The track's instrument becomes an Instrument Rack whose first chain is
Arp → Volt → Chorus, in that order, with the same settings; the track plays the same. Now they
can add a second chain to layer another sound with the same Arp, map macros across both, and
save the whole thing as one instrument preset. ⌘⇧G on a one-chain Instrument Rack puts the
MIDI effects, the instrument and the audio effects back on the track.

**Why this priority**: The step from "a sound I built" to "a layered instrument", the second
half of #328. It depends on stories 1–2 and on MIDI effects in rack chains (#329, done).

**Independent Test**: Build Arp → Volt → Chorus, select all three, ⌘G: the track's instrument
is an Instrument Rack with one chain holding Arp, Volt, Chorus. Play a clip: same notes, same
sound. One Undo restores the three separate devices.

**Acceptance Scenarios**:

1. **Given** a selection containing the instrument plus the MIDI effects directly before it and/or the audio effects directly after it, **When** grouped, **Then** the track's instrument becomes an Instrument Rack whose first chain holds those devices in order; unselected MIDI effects before and audio effects after stay on the track, around the rack.
2. **Given** only the instrument selected, **When** grouped, **Then** it becomes a one-chain Instrument Rack holding just that instrument.
3. **Given** a one-chain Instrument Rack as the track's instrument, **When** ungrouped, **Then** its chain's MIDI effects go after the track's own MIDI effects, its instrument becomes the track's instrument, and its audio effects go before the track's own audio effects, in one undo step.
4. **Given** the instrument is a kind an Instrument Rack chain can't hold (an Instrument Rack itself, or anything else the rack refuses per specs/040–041), **When** the user tries to group, **Then** nothing changes and the status line says why.
5. **Given** a grouped Instrument Rack, **When** the user saves it as a preset and loads it on another track, **Then** that track gets the same chain with the same settings.

---

### User Story 4 - Group MIDI effects into a MIDI Effect Rack (Priority: P3)

The user selects Chord → Scale → Arp and presses ⌘G: they become one MIDI Effect Rack in the
MIDI effect section, with one chain holding them in order. ⌘⇧G reverses it.

**Why this priority**: Same gesture for the third rack type; less common than audio-effect
and instrument grouping.

**Independent Test**: Select three MIDI effects, ⌘G: the MIDI section shows one MIDI Effect
Rack; its chain lists the three with the same values; the clip plays the same notes. Undo and
⌘⇧G both restore them.

**Acceptance Scenarios**:

1. **Given** a contiguous selection of one or more MIDI effects only, **When** grouped, **Then** they become a MIDI Effect Rack at the position of the first one, in one chain, in order, with identical settings.
2. **Given** a one-chain MIDI Effect Rack, **When** ungrouped, **Then** its devices take its place in order, in one undo step.

---

### Edge Cases

- **Non-contiguous selection** (e.g. EQ-8 and Reverb with Delay between them unselected): **Group into rack** is disabled in the menu and ⌘G says "Select devices next to each other to group them" in the status line.
- **MIDI effects and audio effects selected without the instrument**: can't be grouped (no rack type holds both without an instrument); status line says why.
- **Instrument selection whose MIDI part doesn't end at the instrument or whose audio part doesn't start right after it**: not contiguous; refused as above.
- **A rack in the selection** (an Audio Effect Rack, MIDI Effect Rack or Drum Rack among the effects): racks can't nest inside rack chains (specs/041 allows only a Drum Rack as a chain's instrument); refused with a reason naming the rack.
- **A plug-in effect in the selection**: grouped like a built-in where the target rack hosts plug-ins (Audio Effect Rack, Instrument Rack chains); refused with a reason where it doesn't (MIDI Effect Rack).
- **A plug-in instrument** in an Instrument Rack group: grouped with its plug-in state where rack chains can host it; refused with a reason otherwise.
- **Devices with automation, a sidechain key, a modulation (CV) link or an Arp CC lane**: grouped and ungrouped like any other device; the lanes, key and links follow the device into the rack chain and back out (FR-016). Automation keeps playing during and after the move.
- **Ungroup on a rack with two or more chains**: disabled; status line says only a one-chain rack can be ungrouped (as today).
- **Ungroup of a rack with macro mappings**: the macros go away with the rack; the devices keep their current parameter values; one Undo restores the rack with its mappings.
- **Ungroup of a rack whose chain has a key/velocity zone, chain volume, pan or transpose other than default**: the chain's mix settings are dropped (the sound may change) and the status line says so (FR-019).
- **Selection with a folded card**: folded cards are selectable and grouped like unfolded ones; the new rack card opens unfolded.
- **Selection and track switch**: switching tracks clears the selection; the clipboard survives (as today).
- **Paste of a multi-device clipboard where some devices don't fit** (MIDI effects onto an audio track): the ones that fit are pasted, the rest are skipped, and the status line names how many were skipped; if none fit, nothing changes.
- **Pasting a multi-device clipboard into a rack chain** (specs/042): same per-section rules; the instrument is skipped.
- **Delete / Cut / Move with the instrument in the selection**: the whole track is removed as in specs/043, and the other selected devices go with it; with more than the instrument selected the confirmation always appears (FR-009), so a Shift-click range that swept over the instrument can't silently delete the track.
- **Paste of Arp → Volt → Chorus onto another instrument track**: that track's instrument becomes the Volt copy, Arp and Chorus land in their sections, one undo step (FR-005).
- **Typing in a rename box or value entry**: ⌘G, Delete and the clipboard keys never act on devices.
- **⌘G while the arrangement or track headers have focus**: still groups tracks, not devices (as today).
- **Rack chain devices** (inside a rack card) keep their own single selection; multi-select is the track chain only.

## Requirements *(mandatory)*

### Functional Requirements

**Selection**

- **FR-001**: The track's device chain MUST support selecting several devices at once across MIDI effects, the instrument and audio effects. Plain click selects one card; Shift-click selects the range from the anchor (the last plain- or ⌘-clicked card) to the clicked card in chain order; ⌘-click (Ctrl-click on Windows/Linux) toggles one card.
- **FR-002**: Every selected card MUST show the existing selection border; the anchor needs no separate look.
- **FR-003**: Esc, a click on empty chain space, a track switch, or selecting a device in a rack chain MUST clear the track chain's selection.

**Acting on the selection**

- **FR-004**: Copy (⌘C) MUST put every selected device on the device clipboard, in chain order, with full parameters and state. The clipboard holds one or more devices.
- **FR-005**: Paste (⌘V) MUST insert the clipboard's devices into their matching sections of the target chain (MIDI effects / instrument / audio effects), keeping order, after that section's last selected device or at its end, as one undo step. An instrument on the clipboard (alone or with effects) replaces the target's instrument as specs/043 FR-004 says, in the same undo step as the effects' insertion. Devices the target can't take (on an audio, return, group or master track: the instrument and MIDI effects) are skipped with a status-line count.
- **FR-006**: Delete / Backspace and Cut (⌘X) MUST remove every selected device in one undo step; Duplicate (⌘D) MUST duplicate each contiguous run of selected effects right after itself in one undo step.
- **FR-007**: Dragging the header of a selected card MUST move the whole selection when all selected cards are in that card's section; the selection keeps its order and lands at the drop gap in one undo step. A selection spanning sections drags only the grabbed card's section's devices.
- **FR-008**: **Move to track ▸** MUST move every selected device to the target track in one undo step, each landing at the end of its matching section, following specs/043 FR-013 for what the target can take and what travels.
- **FR-009**: When the instrument is in the selection, Delete, Cut and Move MUST follow specs/043 FR-005 (the source track is removed; the other selected devices go with it). When the selection holds the instrument plus at least one other device, a confirmation naming the track MUST appear every time, even if the track has no clips; the instrument alone keeps specs/043's rule (confirm only when the track has clips).
- **FR-010**: The right-click menu of a selected card MUST act on the whole selection and show the selection's size in its header; right-clicking an unselected card first makes it the only selection.

**Group into rack**

- **FR-011**: **Group into rack** (⌘G with the Devices panel focused, and in the device context menu) MUST pick the rack type from the selection:
  - only audio effects → **Audio Effect Rack**, placed where the first selected effect was;
  - the instrument, optionally with the MIDI effects directly before it and/or the audio effects directly after it → **Instrument Rack** as the track's instrument, its first chain holding those devices in order (MIDI effects → instrument → audio effects);
  - only MIDI effects → **MIDI Effect Rack**, placed where the first selected MIDI effect was.
- **FR-012**: Group MUST require a contiguous selection: within each section the selected devices are adjacent, and in an instrument group the MIDI part ends at the instrument and the audio part starts right after it. Any other selection (gaps, MIDI + audio effects without the instrument) MUST change nothing, with **Group into rack** disabled in the menu and a status-line reason on ⌘G.
- **FR-013**: Grouped devices MUST keep their parameters, state, bypass, fold state and A/B snapshots / preset labels; the track MUST sound the same right after grouping.
- **FR-014**: Group MUST refuse, with a reason, a selection that contains something the target rack's chain can't hold (a rack inside a rack beyond specs/041's nested Drum Rack, a plug-in where the rack can't host one, an instrument kind the Instrument Rack refuses).
- **FR-015**: The new rack MUST be selected, unfolded, with one chain named after its first device (or the instrument for an Instrument Rack), default macros (none mapped) and default rack settings.
- **FR-016**: Automation lanes, sidechain keys, modulation (CV) links, outer-rack / track macro mappings and Arp CC lanes on grouped devices MUST follow the devices into the rack chain on Group, and back to the track chain on Ungroup, still driving the same parameters; the #257 refusal for such devices goes away. Undo restores them where they were.

**Ungroup**

- **FR-017**: **Ungroup rack** (⌘⇧G and the context menu) MUST work on a one-chain Audio Effect Rack, Instrument Rack (as the track's instrument) and MIDI Effect Rack, putting the chain's devices back in the rack's place in order (for an Instrument Rack: MIDI effects after the track's own, its instrument as the track's instrument, audio effects before the track's own). The restored devices become the selection.
- **FR-018**: Ungroup MUST be disabled (with a status-line reason on ⌘⇧G) for a rack with two or more chains, a Drum Rack, or a nested Drum Rack in a chain.
- **FR-019**: On Ungroup the rack's macros, macro mappings, chain mix (volume, pan, transpose, zones) and rack settings are dropped; the devices keep their current parameter values. The status line says what was dropped when anything non-default was.

**Undo, persistence, sync**

- **FR-020**: Every Group, Ungroup and multi-device Copy-Paste / Cut / Delete / Duplicate / drag / Move MUST be exactly one undo step; Undo restores the chain, the devices' state and the selection.
- **FR-021**: A grouped rack MUST save and reopen with the project, and save / load as a preset (Audio Effect Rack and MIDI Effect Rack presets; Instrument Rack as an instrument preset) like any rack built by hand.
- **FR-022**: Grouping or ungrouping during playback MUST NOT cause an audible gap, click or dropout on that track or others.
- **FR-023**: The MCP device tools MUST be able to group a contiguous range of devices into a rack and ungroup a rack, matching the UI's rules and reasons.
- **FR-024**: The Preferences → Shortcuts list (Shift-click, ⌘-click, ⌘G, ⌘⇧G in the Devices panel), `CHANGELOG.md`, `FEATURES.md` and the user docs MUST reflect the new gestures and commands.
- **FR-025**: Selection borders, the disabled menu items and the multi-selection menu header MUST follow the Studio design language in all four themes.

### Key Entities *(include if feature involves data)*

- **Device selection**: a set of devices on one track's chain (MIDI effects, the instrument, audio effects) plus an anchor for Shift-click ranges. Lives in the panel, not in the project.
- **Device clipboard**: now an ordered run of one or more devices (each an audio effect, MIDI effect or instrument, with full state), shared by the track chain and rack chains.
- **Group**: a contiguous selection turned into one new rack of the matching type, in place, with one chain holding the devices in order; one undo step.
- **Ungroup**: a one-chain rack replaced by its chain's devices, in place; one undo step.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Folding N adjacent devices into a rack takes 3 actions (click, Shift-click, ⌘G) regardless of N, down from creating an empty rack and re-adding N devices by hand.
- **SC-002**: Grouping and then ungrouping (or grouping and undoing) returns a chain whose devices, order and parameter values are identical to the original, for 100% of built-in device kinds the rack chains can hold.
- **SC-003**: A clip rendered before and after Group matches within 0.1 dB peak and RMS for every rack type.
- **SC-004**: Copying, deleting or moving N devices is one action and one undo step, instead of N.
- **SC-005**: Every invalid Group / Ungroup attempt leaves the project unchanged and shows a reason; no attempt leaves a half-built rack.
- **SC-006**: No audible dropout on any track while grouping or ungrouping during playback.

## Assumptions

- Multi-select covers the track's device chain only; devices inside a rack card keep the single selection of specs/042.
- Shift-click ranges follow the chain's visual order (MIDI effects → instrument → audio effects); a range may cross sections.
- Pasting a multi-device clipboard spreads it by section, as single-device paste does today; the clipboard keeps no gaps between devices copied from non-adjacent positions.
- Group creates one chain; adding more chains afterwards uses the rack's existing controls.
- Ungroup stays limited to one-chain racks (as #257 today); flattening parallel chains has no single correct order.
- Grouping an Instrument Rack inside an Instrument Rack chain stays out of scope (specs/041), so selecting an Instrument Rack instrument and pressing ⌘G is refused.
- ⌘G / ⌘⇧G keep their meaning per focus: device grouping in the Devices panel, track grouping in the arrangement and track headers.
- Links that target a grouped device (automation, CV, sidechain key, Arp CC, macro mappings from outside) follow it (FR-016); links that belong to the removed rack itself (its own macros and chain mix) are dropped on Ungroup (FR-019).

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
