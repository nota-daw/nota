# Feature Specification: Racks redesign and MIDI Effect Rack

**Feature Branch**: `040-racks-redesign`

**Created**: 2026-10-26

**Status**: Draft

**Input**: User description: "Я обновил дизайн в nota-design, добавил Nota Racks.html. Нужно добавить Midi Effect Rack и обновить Instrument Rack / Audio Effect Rack. Важно, чтобы все текущие темы поддерживались." Design source: `nota-design/Nota Racks.html`, frames 1a–1f (1a–1b Instrument, 1c–1d Audio Effect, 1e–1f MIDI Effect). Also covers [nota-daw/nota#329](https://github.com/nota-daw/nota/issues/329) "Instrument Rack: chains can hold MIDI effects" (added 2026-10-26).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Main area: UI (the shared rack card), with an engine part for the new MIDI Effect Rack.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Design translation.** The mockup is drawn in fixed hex colours of one dark look, with
caps-and-tracking labels, a 24 px header holding text "Fold", "Bypass" and an A/B switch,
and teal as a mapping colour. The app version keeps the mockup's layout, content and
interactions but is built from Nota's Studio rules: the shared device header (name ·
preset · badges · power), sentence-case labels, theme tokens only, the accent only for
what is live or chosen, resting values in the neutral value ink, Mute / Solo in their
semantic fills. A/B and fold stay where the shared header and the right-click menu
already put them.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Layer and split instruments in the reworked Instrument Rack (Priority: P1)

A producer builds a layered keys patch: three chains (two synths and an electric piano),
each with its own effects. On the rack card they see every chain at once with its zone
summary and level, pick a chain to see its devices left to right with their main
parameters, and on the Zones tab drag key and velocity edges so the piano only answers hard
hits. Hovering over the keyboard strip tells them which chains a note would play.

**Why this priority**: The Instrument Rack is the most used rack; the new layout is the
heart of the design and is shared by all three racks, so it must exist first.

**Independent Test**: Open a project with an Instrument Rack of three chains, use the
Chains, Zones and Chain panels, and confirm every action changes the sound and is shown
in the card — no other story needed.

**Acceptance Scenarios**:

1. **Given** an Instrument Rack with three chains, **When** the user opens its card, **Then** the card shows the macro column, the Chains tab with one row per chain (colour stripe, name, mute, solo, key/velocity summary, live level) and the first chain's devices, and the Chain panel on the right.
2. **Given** the Chains tab, **When** the user clicks a chain row, **Then** that chain becomes the chosen one (marked as chosen), its devices appear in the centre and the Chain panel shows its volume, pan, key and velocity range.
3. **Given** the Zones tab, **When** the user drags the high edge of a chain's key zone, **Then** the zone follows the pointer in whole notes, the label shows the new range as note names, and notes above the new edge no longer reach that chain.
4. **Given** the Zones tab, **When** the user hovers over a key position, **Then** a probe line marks that key, the chains that would play it are highlighted, and the footer reads the note and the chains (or "nothing").
5. **Given** the Rack out panel, **When** the user changes volume or glide or turns on mono (last note), **Then** the rack's output follows, and with mono on all chains share one voice.
6. **Given** a chain is chosen, **When** the user presses Duplicate or Delete in the Chain panel, **Then** the chain is copied next to it (with its devices and settings) or removed, and macro mappings that pointed at a removed chain are removed with it.

---

### User Story 2 - Map macros directly in the chain (Priority: P1)

The producer wants one knob to brighten the patch. They select Macro 1, press Map, and the
parameters in the chain's device cards light up as mappable. Clicking a parameter maps it,
clicking again unmaps it. On the Macros tab they set each target's range (min above max
inverts) and the macro's response curve, and rename the macro. There is no separate map
screen and no Done button.

**Why this priority**: Macros are the reason to build a rack; the old separate map mode is
the main usability complaint the design fixes. Shared by all three racks.

**Independent Test**: In any rack, map two parameters to one macro, set ranges and a
curve, turn the macro, and confirm both parameters move within their ranges.

**Acceptance Scenarios**:

1. **Given** a rack card, **When** the user selects a macro and presses Map, **Then** the card switches to the Chains tab, the Map button shows it is on, and every mappable parameter in the visible devices is marked as a target (already mapped to this macro vs. not yet).
2. **Given** mapping is on, **When** the user clicks an unmapped parameter, **Then** it becomes a target of the selected macro with a default range around its current value, shows the macro's tag, and the macro's target-count badge goes up by one.
3. **Given** mapping is on and a parameter is mapped to another macro, **When** the user clicks it, **Then** it moves to the selected macro (a parameter belongs to at most one macro).
4. **Given** mapping is on, **When** the user clicks a parameter mapped to the selected macro, presses Map again, or presses Esc, **Then** respectively the parameter is unmapped, or mapping ends.
5. **Given** mapping is off and a parameter is mapped, **When** the user clicks it, **Then** the Macros tab opens on the macro that drives it; the parameter cannot be dragged by hand while mapped.
6. **Given** the Macros tab, **When** the user drags a target's min or max handle, **Then** the range and its readout (in the parameter's own units) follow, a marker shows where the macro currently drives the parameter, and min above max inverts the response.
7. **Given** the Macros tab, **When** the user picks Lin, Exp, Log or S, **Then** the curve preview and every target of that macro follow the new response.
9. **Given** an older project where one macro's targets have different curves, **When** the user opens the Macros tab on it, **Then** the curve choice shows "Mixed", each target keeps responding as before, and picking a curve applies it to all targets.
8. **Given** a macro knob, **When** the user drags it vertically or double-clicks it, **Then** it turns (fine with a modifier) or resets, all its targets follow, and the drag can be recorded as automation and bound with MIDI Learn like other value controls.

---

### User Story 3 - Route audio through the reworked Audio Effect Rack (Priority: P2)

A mix engineer builds a "Crunch & Space" rack with two chains. In Rack out they choose how
chains run — Parallel (summed), Serial (top to bottom) or Select (only chains whose select
range contains the selector) — and set dry/wet, output gain and latency compensation. On
the Select tab they drag each chain's select range and the selector itself, and can map the
selector to a macro.

**Why this priority**: The Audio Effect Rack already routes this way; the story moves it to
the shared layout so all racks behave the same.

**Independent Test**: Build a two-chain Audio Effect Rack, switch the three modes and drag
the selector; confirm audibly and in the card's status line which chains play.

**Acceptance Scenarios**:

1. **Given** an Audio Effect Rack, **When** the user opens its card, **Then** the centre tabs read Chains · Select · Macros and the chain rows show their select range in Select mode or their device count otherwise.
2. **Given** Rack out, **When** the user picks Parallel, Serial or Select, **Then** the routing changes, a one-line explanation under the choice describes it, and the status line names the mode.
3. **Given** Select mode and the Select tab, **When** the user drags the selector, **Then** only chains whose range contains it play, they are highlighted, and the footer names them (or "silence").
4. **Given** Parallel or Serial mode, **When** the user opens the Select tab, **Then** the ranges are still editable and the footer says they apply only in Select mode.

---

### User Story 4 - Split and transform notes with the new MIDI Effect Rack (Priority: P2)

A producer wants an arpeggio above C3, chords below, and random accents only on hard hits,
all feeding one synth. They add a MIDI Effect Rack before the track's instrument, create
three chains (Arp → Scale, Chord → Velocity, Random), set each chain's key and velocity
zone, and every incoming note goes to the chains whose zone it hits; the chains' outputs
merge into the instrument.

**Why this priority**: The only new device in the design; it depends on the shared card
(stories 1–2) but brings a new musical capability.

**Independent Test**: Add a MIDI Effect Rack with two chains of built-in MIDI effects on an
instrument track, play notes across the split, and confirm in the instrument's output and
the card's probe which chain processed each note.

**Acceptance Scenarios**:

1. **Given** an instrument track, **When** the user adds MIDI Effect Rack from the Browser (or drops it onto the MIDI effects area), **Then** it appears with the other MIDI effects, before the instrument, with one empty chain.
2. **Given** a MIDI Effect Rack, **When** the user drops a built-in MIDI effect onto a chain, onto the chain list or onto "+ Chain", **Then** it is added to that chain or to a new chain; devices in a chain run left to right.
3. **Given** chains with key/velocity zones and Parallel mode, **When** a note is played, **Then** it reaches every unmuted chain whose zone contains its pitch and velocity (solo respected), and all chains' output notes reach the instrument.
4. **Given** Select mode, **When** a note is played, **Then** only the chains whose select range contains the selector (and whose key/velocity zone the note hits) receive it; dragging the selector or turning its macro switches chains.
5. **Given** "Merge duplicate notes" is on, **When** two chains emit the same pitch at the same time, **Then** the instrument plays one note; with it off it receives both.
6. **Given** "Pass through outside zones" is on, **When** a note hits no chain's zone, **Then** it reaches the instrument unchanged; with it off it is dropped.
7. **Given** the Chain panel of a MIDI chain, **When** the user changes the chain's transpose, **Then** that chain's output notes shift by that many semitones.
8. **Given** the Chain panel of a MIDI chain, **When** the user sets Volume to 50 %, **Then** that chain's output notes reach the instrument at half their velocity (at least 1); at 0 % the chain sends nothing.
9. **Given** a project with a MIDI Effect Rack, **When** it is saved, reopened, undone/redone, saved as a preset or driven through the MCP tools, **Then** its chains, devices, zones, macros and output settings come back exactly.

---

### User Story 6 - MIDI effects inside an Instrument Rack chain (Priority: P2)

A producer groups an instrument with its own note processing: in one Instrument Rack, the
pad chain plays Chord → Nota Pentad → Reverb while the lead chain plays Arp → Nota Volt.
Each Instrument Rack chain runs **MIDI effects → instrument → audio effects**, like a
track (issue #329).

**Why this priority**: Needed to pack an instrument together with its MIDI effects into
one rack (split out of #326); it builds on the shared card (stories 1–2) and the MIDI
effect hosting the MIDI Effect Rack brings (story 4).

**Independent Test**: Put an Arp in one chain of a two-chain Instrument Rack, play a held
chord, and confirm only that chain arpeggiates while the other sustains; save, reopen,
save as rack preset, load it on another track — the Arp is still there.

**Acceptance Scenarios**:

1. **Given** an Instrument Rack chain, **When** the user opens it on the Chains tab, **Then** the device strip shows a MIDI effects section before the instrument, then the instrument, then the audio effects, each with its own drop target.
2. **Given** a chain, **When** the user drops a built-in MIDI effect from the Browser onto the MIDI section or picks it from the chain's Add menu, **Then** it is added there; dropping it elsewhere in the strip places it in the MIDI section; MIDI effects reorder within their section only.
3. **Given** a chain with MIDI effects and a key/velocity zone, **When** notes are played, **Then** the zone picks which notes enter the chain, the chain's MIDI effects transform them in order, and only that chain's instrument plays the result, sample-accurately and in time with the transport (an Arp in a rack chain plays the same pattern as on a track).
4. **Given** mono (last note) is on, **When** some chains hold MIDI effects, **Then** mono shapes the chains without MIDI effects; a chain with MIDI effects gets the notes its zone takes as played (its effects decide what sounds).
5. **Given** a chain's MIDI effect, **When** the user maps one of its parameters to a macro, bypasses it, or opens its full editor, **Then** this works as for the chain's other devices.
6. **Given** a rack whose chains hold MIDI effects, **When** the project is saved and reopened, the rack is saved as a preset and loaded elsewhere, the track or rack is duplicated, an edit is undone/redone, or the rack is driven through the MCP rack tools, **Then** the MIDI effects and their settings come back exactly.

---

### User Story 5 - The racks look right in every theme (Priority: P1)

A user who works in Studio Light (or either Ember theme) opens any of the three racks. The
card reads as part of the app in that theme: no dark islands, no unreadable text, the
accent only on what is live or chosen. Switching theme while the card is open recolours it
immediately.

**Why this priority**: An explicit hard requirement of the request; a rack that only works
in one theme blocks release.

**Independent Test**: Open each rack in each built-in theme (and switch live), compare
against the mockup's structure and the design guide's readability bar.

**Acceptance Scenarios**:

1. **Given** any built-in theme, **When** each of the three rack cards is shown with every tab and both right-panel tabs, **Then** every surface, line, label and value uses that theme's colours and meets the design guide's readability bar.
2. **Given** a rack card is open, **When** the user switches theme in Preferences → Appearance (or the system switches it under Follow system), **Then** the card recolours at once without reopening.
3. **Given** a chain is muted or soloed, **Then** its M / S keys use the Mute and Solo fills of the current theme with legible ink.
4. **Given** the design guard checks, **When** the smoke test runs, **Then** it passes with no new entries in any allow list.

---

### Edge Cases

- A rack with one chain: Delete is disabled (a rack keeps at least one chain).
- A rack at the chain limit: "+ Chain", Duplicate and drops that would add a chain are refused with a short tooltip.
- A chain with no devices: the device strip shows only the drop target; an Instrument Rack chain with no instrument is silent; a MIDI chain with no devices passes its zone's notes through unchanged.
- A macro with no targets shows "—" instead of a value, a muted knob, and on the Macros tab a one-line empty state telling the user to press Map.
- Mapping on, then the user switches to another chain: mapping stays on and the new chain's parameters are mappable; switching to Zones/Select or Macros tab ends mapping.
- Deleting a device or chain that has mapped parameters removes those mappings and updates every count badge.
- Zone edges cannot cross: dragging low past high stops at high (and vice versa); ranges are whole MIDI values 0–127.
- Velocity zones exist only for racks that receive notes (Instrument, MIDI); the Audio Effect Rack shows select ranges instead.
- Device kinds that cannot live in a rack (an instrument in a MIDI rack, a MIDI effect in an Audio Effect Rack, an audio effect in a MIDI rack, a second instrument in an Instrument Rack chain, any rack inside a rack) are refused on drop with a tooltip.
- An Instrument Rack chain with MIDI effects but no instrument is silent; its MIDI effects still advance so they stay in time when an instrument is added.
- Transport stop, seek, loop wrap and Session slot switches reset or shift the MIDI effects inside rack chains exactly as they do for a track's MIDI effects (no ghost notes, Arp phase kept across a loop).
- The rack is bypassed with the power switch: the body stays readable but marked inactive (no opacity), and a bypassed MIDI Effect Rack passes notes straight through.
- Stuck notes: changing zones, mode, mute, solo, transpose or deleting a chain while notes are held must not leave notes hanging in the instrument.
- Projects saved before this change with Instrument or Audio Effect Racks (including per-target curves that differ within one macro) open and sound the same.
- Long chain, device and macro names are cut with an ellipsis; numbers never shift layout.

## Clarifications

### Session 2026-10-26

- Q: Existing projects can hold different curves per target of one macro — keep per-target curves or adopt one curve per macro on load? → A: Keep curves per target; the macro's curve choice shows "Mixed" until the user picks one, which then applies to all its targets.
- Q: What picks the chain in the MIDI Effect Rack's Select mode? → A: A selector 0–127 with per-chain select ranges, as in the Audio Effect Rack; shown as a Selector row on the MIDI rack's Zones tab and mappable to a macro.
- Q: Which parameters does a compact device card on the Chains tab show? → A: Up to three key parameters curated per device kind; while mapping, the card scrolls to offer every parameter of the device.
- Q: What does a MIDI Effect Rack chain's Volume do? → A: It scales the velocity of the chain's output notes (shown in percent, 100 % = unchanged, 0 % = the chain's notes are not sent).
- Q: What goes on the right side of the rack's status line? → A: Nothing — the status line carries only the one-line summary; no sample rate, device count or CPU.

## Requirements *(mandatory)*

### Functional Requirements

**Shared rack card (all three racks)**

- **FR-001**: Instrument Rack, Audio Effect Rack and MIDI Effect Rack MUST share one card layout at the standard device-card size: macro column (left), tabbed work area (centre), tabbed chain/output panel (right), status line (bottom).
- **FR-002**: The card header MUST be the shared device header (name, preset picker where presets exist, type badge, power switch); the mockup's text Bypass, Fold and A/B controls MUST NOT be added as separate header buttons — bypass is the power switch, fold and A/B stay where the app already offers them.
- **FR-003**: The macro column MUST show 8 macros (2 × 4) each with a knob, name, value and a badge with its number of targets when it has any, plus one Map button for the selected macro.
- **FR-004**: The centre tabs MUST be Chains · Zones · Macros (Audio Effect Rack: Chains · Select · Macros), with a short summary on the right of the tab strip (chain count and routing; zone scale; "N of 8 mapped").
- **FR-005**: The Chains tab MUST list chains with colour stripe, name, mute, solo, zone/range summary and a live level meter, a "+ Chain" control, and the chosen chain's devices left to right as compact cards with on/off, name, kind and up to three key parameters (a curated list per device kind) as horizontal sliders with values; while mapping, a compact card MUST let the user scroll to and map any parameter of its device.
- **FR-006**: Users MUST be able to add a device to a chain or create a new chain by dropping a device from the Browser on the chain list, "+ Chain" or the device strip; each compact device card MUST offer to open the device's full editor.
- **FR-007**: The right panel MUST have a Chain tab (chosen chain: name with rename, index "n / N", volume (for MIDI chains: velocity scale), pan — for MIDI chains transpose instead of pan —, key and velocity range or select range linking to the Zones/Select tab, Duplicate, Delete) and a Rack out tab (Output for the MIDI rack).
- **FR-008**: The status line MUST summarise the rack in one line (chains, routing or zone probe result, mapped macros) and show nothing else — no sample rate, device count or CPU readout.
- **FR-009**: All value controls on the card MUST follow the app's value-control contract: no value jump on click, double-click to default, fine adjustment with a modifier, automation write gestures, MIDI Learn, live follow that pauses while dragging, values always visible in a number font.
- **FR-010**: Chain rows MUST be colour-coded with a stable per-chain colour that stays recognisable in every theme.

**Macro mapping**

- **FR-011**: The separate macro-map view with its Done button MUST be removed; Map MUST turn on mapping for the selected macro in place, switching to the Chains tab.
- **FR-012**: While mapping, every mappable parameter in the visible device cards MUST be marked as a target, distinguishing parameters already mapped to the selected macro; clicking toggles the mapping; a parameter MUST belong to at most one macro (mapping it to another macro moves it).
- **FR-013**: Mapping MUST end on Map pressed again, Esc, or leaving the Chains tab; selecting another macro while mapping MUST switch mapping to that macro.
- **FR-014**: A mapped parameter MUST show its macro's tag, follow the macro, refuse manual drags, and open the Macros tab on its macro when clicked outside mapping.
- **FR-015**: The Macros tab MUST show the selected macro's slot, name (rename by double-click), value, response curve choice (Lin, Exp, Log, S) with a small preview marking the current position, and each target (chain › device › parameter) with a draggable min/max range, the current driven position, a readout in the parameter's units, and unmap; min above max MUST invert.
- **FR-016**: The response curve is chosen per macro on the Macros tab: picking a curve MUST apply it to all of that macro's targets. Curves MUST stay stored per target, so a project whose targets of one macro hold different curves keeps responding exactly as before; for such a macro the curve choice MUST show "Mixed" (no curve key chosen, no single preview) until the user picks one, which then applies to all its targets. A target added while the macro is Mixed takes Lin.

**Zones and Select**

- **FR-017**: Instrument and MIDI racks MUST offer per-chain key zones (note names, whole notes) and velocity zones (0–127) edited by dragging either edge, with a keyboard ruler and an on-hover probe that marks the hovered key, highlights the chains that would play it (at a reference velocity) and names them in the footer.
- **FR-018**: The Audio Effect Rack (on its Select tab) and the MIDI Effect Rack (as a Selector row and per-chain select ranges on its Zones tab) MUST offer per-chain select ranges (0–127) and a draggable selector, mappable to a macro; outside Select mode the ranges MUST be shown as inactive with a one-line note.

**Rack out**

- **FR-019**: Instrument Rack Rack out MUST offer volume, glide and a mono (last-note) switch under which all chains share one voice.
- **FR-020**: Audio Effect Rack Rack out MUST offer chain routing Parallel · Serial · Select with a one-line explanation, dry/wet, gain and latency compensation.
- **FR-021**: MIDI Effect Rack Output MUST offer chain routing Parallel · Select with a one-line explanation, "Merge duplicate notes" and "Pass through outside zones".

**MIDI Effect Rack (new device)**

- **FR-022**: The system MUST provide a MIDI Effect Rack device placed among a track's MIDI effects, before the instrument, available from the Browser and the add-device menus like the other racks.
- **FR-023**: Each MIDI rack chain MUST hold an ordered series of built-in MIDI effects, a key zone, a velocity zone, mute, solo, transpose and a velocity-scaling volume; notes MUST be routed to chains per FR-017 and the routing mode, and the chains' outputs merged into the track's note stream with FR-021's options.
- **FR-023a**: A MIDI chain's Volume MUST scale the velocity of its output notes, 0–200 % shown in percent: 100 % leaves velocity unchanged, results are clamped to 1–127, and at 0 % the chain sends no notes. It is automatable and mappable to a macro like any chain volume.
- **FR-024**: The MIDI Effect Rack MUST support 8 macros mapped to its devices' parameters with the same behaviour as the other racks.
- **FR-025**: The MIDI Effect Rack MUST be saved with the project, take part in undo/redo, support user presets (and factory presets if shipped), and be controllable through the MCP tools at parity with the existing rack tools (create, chains, devices, zones, macros, mappings, output settings, read back).
- **FR-026**: Changing routing, zones, mute/solo, transpose or removing chains/devices while notes are held MUST NOT leave notes stuck in the instrument.

**MIDI effects in Instrument Rack chains (issue #329)**

- **FR-030**: An Instrument Rack chain MUST hold an ordered list of built-in MIDI effects before its instrument; the chain processes MIDI effects → instrument → audio effects.
- **FR-031**: The chain's device strip MUST show the MIDI effects as their own section before the instrument; Browser drag-and-drop and the chain's Add menu MUST accept MIDI effects and place them in that section; reordering stays within the section.
- **FR-032**: Notes MUST pass the chain's key/velocity zone before the chain's MIDI effects (mono and the sustain pedal shape the chains without MIDI effects only); MIDI effect output MUST reach only that chain's instrument, sample-accurate within the block, on the same clock a track's MIDI effects use (transport, stopped-but-live, Session slot), with reset / loop-wrap behaviour identical to a track's.
- **FR-033**: Parameters of chain MIDI effects MUST be mappable to macros, bypassable and openable in their full editor like other chain devices.
- **FR-034**: Chain MIDI effects MUST be saved with the project and rack presets, copied by track/rack duplication and device copy/paste, take part in undo/redo, and be reachable from the MCP rack tools (list, add, remove, move, params, bypass).

**Themes and compatibility**

- **FR-027**: Every surface, line, text, fill and graph on all three rack cards MUST take its colour from the active theme and recolour live on a theme switch; this MUST hold for Studio Dark, Studio Light, Ember Graphite and Ember Paper and any theme added later through the theme files.
- **FR-028**: The cards MUST follow the design guide: sentence-case labels, the accent only for what is live or chosen (selected chain/macro/tab, active toggle, a value control while touched), resting values in the neutral value ink, Mute/Solo in their semantic fills with derived ink, mapping marks in a non-accent colour, no opacity for inactive state, icons from the app's glyph set (no unicode symbols such as ‹ › ▾ × ⠿).
- **FR-029**: Projects, presets and MCP scripts made with the current Instrument and Audio Effect Racks MUST load and sound unchanged; the Drum Rack is unaffected.

### Key Entities

- **Rack**: a device of kind Instrument, Audio Effect or MIDI Effect; has chains, 8 macros, routing mode and output settings (per kind), bypass state, preset.
- **Chain**: name, colour, ordered devices (Instrument Rack: MIDI effects, then an instrument, then audio effects; Audio: effects; MIDI: MIDI effects), volume, pan or transpose, mute, solo, key zone, velocity zone, select range (Audio and MIDI racks), level.
- **Macro**: slot 1–8, name, value 0–1, targets; its shown curve is the targets' common curve or "Mixed".
- **Macro target (mapping)**: chain, device, parameter, min, max (min > max inverts), response curve (kept per target; the macro shows one curve or "Mixed").
- **Selector**: Audio Effect / MIDI Effect rack value 0–127 that picks chains in Select mode (chains whose select range contains it); can be a macro target.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can map a parameter to a macro in at most 3 clicks from an open rack card (select macro, Map, click parameter), with no screen change other than the tab switch.
- **SC-002**: A user can build a two-way key split in an Instrument or MIDI Effect Rack in under one minute without opening any window other than the rack card.
- **SC-003**: All three rack cards, with every tab, pass the design readability bar and the design guard checks in all 4 built-in themes, with zero new allow-list entries.
- **SC-004**: 100 % of projects in the existing rack test set (Instrument and Audio Effect Racks) render identically before and after the change.
- **SC-005**: A MIDI Effect Rack routing notes through three chains adds no audible timing shift and no stuck notes across 1 000 random note-on/off and zone/mode changes in the end-to-end smoke test.
- **SC-006**: Every MIDI Effect Rack setting survives save → reopen, undo → redo, and preset save → load with no difference.

## Assumptions

- The mockup is authoritative for layout, content and interaction; DESIGN.md and the theme files are authoritative for colour, type, header, geometry and icons where they disagree (DESIGN.md wins the conflict, the mockup's hex values are not used).
- The status line is part of the rack body inside the standard card height, so the card stays the standard device-card size.
- The chain limit stays what the racks allow today; the mockup's limit of 4 is a mockup simplification.
- MIDI rack chains hold built-in Nota MIDI effects only (Arp, Chord, Scale, Velocity, Random, Length and the others in the MIDI effect list); hosted plug-ins inside a MIDI rack are out of scope.
- The MIDI rack's probe and the Instrument rack's probe use velocity 100 as the reference velocity.
- Every device kind that can live in a rack gets a curated list of up to three key parameters for its compact card; a kind without a list falls back to its first three parameters. The full editor opens from the card.
- Engine support for audio-rack routing, select ranges, latency compensation, key/velocity zones, glide and macro curves already exists and is reused; mono (last note) for the Instrument Rack and everything for the MIDI Effect Rack are new.
- Drum Rack keeps its own card and is out of scope, including MIDI effects in drum pads.
- MIDI effects in Instrument Rack chains are built-in Nota MIDI effects only (kinds as in the MIDI Effect Rack); a MIDI Effect Rack cannot be nested in a chain.
- CHANGELOG, FEATURES.md, user docs (EN + RU) and the Shortcuts list (Esc ends mapping) are updated as part of this feature.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
