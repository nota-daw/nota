# Feature Specification: Mosaic and Drum Rack in Instrument Rack chains

**Feature Branch**: `feat/rack-nested-instruments`

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "Let an Instrument Rack chain host Nota Mosaic (built-in instrument kind 17) and a Nota Drum Rack (kind 4). Today the Browser drop and the chain-instrument picker refuse both. Both must be addable from the Browser and the rack card, play with zones and macros, survive save/reopen, presets and undo, and have their own editors inside the chain (the Mosaic editor; the Drum Rack's pads and macros). A Drum Rack in a chain is a rack inside a rack: settle the nesting depth (one level)." Reported during the review of the racks redesign ([nota-daw/nota#331](https://github.com/nota-daw/nota/pull/331), specs/040).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Main area: Engine (a rack chain gains two new instrument kinds, one of them a rack), with
the rack card and its device windows on top.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A multisampled piano as one layer of an Instrument Rack (Priority: P1)

A producer layers a pad over a grand piano. They drop Nota Mosaic from the Browser onto
an Instrument Rack (as a new chain, or onto an existing chain to replace its instrument),
or pick Nota Mosaic in the card's "add chain" / instrument menus. They open the chain's
instrument and get the full Mosaic editor: they load a multisample preset, see the zones
fill in as the samples load, edit a zone, and play. The chain's key and velocity zone
splits the keyboard between the piano and the pad, and a rack macro mapped to the Mosaic's
filter cutoff moves it.

**Why this priority**: Mosaic is the instrument for realistic keys and orchestral sounds;
layering it is the most common reason to put it in a rack, and it needs no rack nesting.

**Independent Test**: Add Mosaic to an Instrument Rack chain, load a program in its
editor, play notes inside and outside the chain's zone, move a mapped macro, save and
reopen the project — everything works without the Drum Rack story.

**Acceptance Scenarios**:

1. **Given** an Instrument Rack, **When** the user drops Nota Mosaic from the Browser on the "add chain" area, **Then** a new chain holding an empty Mosaic appears and is chosen.
2. **Given** a chain holding Nota Volt, **When** the user drops Nota Mosaic on that chain (or picks it in the chain's instrument menu), **Then** the chain's instrument becomes Mosaic and its effects, zone, mix and MIDI effects stay.
3. **Given** a Mosaic chain, **When** the user opens its editor and loads a multisample preset, **Then** the editor shows the program, its loading progress and then its zones, and notes in the chain's zone play the samples.
4. **Given** a Mosaic chain with a program, **When** the user edits a zone in the editor and then undoes, **Then** the zone goes back, and each step is one undo step.
5. **Given** a rack macro mapped to a Mosaic parameter, **When** the user moves the macro, **Then** the parameter follows within the mapping's range.
6. **Given** a project with a Mosaic chain, **When** it is saved and reopened, or the rack is saved as a preset and loaded on another track, **Then** the chain comes back with the same program (its samples load again from where they are) and the same parameters.

---

### User Story 2 - A Drum Rack inside an Instrument Rack (Priority: P2)

A beat-maker wants one instrument track that plays a drum kit on the low octave and a
bass on the rest of the keyboard. They add a Nota Drum Rack to an Instrument Rack chain
(Browser drop or the card's menus), set that chain's key zone to C1–B1, and open the
chain's instrument: the Drum Rack's own card opens in a device window with its pads, pad
editor and macros. They drop samples on pads, set choke groups and pad tune, map a Drum
Rack macro to a pad's decay, and map an Instrument Rack macro to that Drum Rack macro.

**Why this priority**: Splitting drums and a bass or keys across one track is a familiar
rack use, but it needs a rack inside a rack, so it builds on the chain work of story 1.

**Independent Test**: Put a Drum Rack in a chain, load two pads, play their notes, edit
pads and macros in its window, undo each edit, save and reopen.

**Acceptance Scenarios**:

1. **Given** an Instrument Rack, **When** the user adds a Nota Drum Rack chain, **Then** the chain shows "Nota Drum Rack" as its instrument and starts with an empty kit.
2. **Given** a Drum Rack chain, **When** the user opens its instrument, **Then** a device window shows the Drum Rack card (pads, pad editor, macros) working on that nested Drum Rack, not on the track's rack.
3. **Given** the nested Drum Rack card, **When** the user drops a sample on a pad and plays that pad's note inside the chain's key zone, **Then** the pad sounds; a note outside the chain's zone does not reach the Drum Rack.
4. **Given** the nested Drum Rack card, **When** the user changes a pad's choke group, tune, decay, volume or name, adds or removes a pad, or changes a macro or a mapping, **Then** the nested rack changes and Undo reverts that step without touching the rest of the Instrument Rack.
5. **Given** an Instrument Rack macro, **When** the user maps it to the nested Drum Rack's macro 2, **Then** moving the outer macro moves macro 2 and whatever it drives.
6. **Given** a project with a nested Drum Rack, **When** it is saved and reopened, or the Instrument Rack is saved as a preset and loaded, **Then** the nested kit comes back: pads, samples, pad shaping, macros and mappings.

---

### User Story 3 - Agents and tests reach the nested instruments (Priority: P3)

An MCP client builds the same layered rack: it sets a chain's instrument to Mosaic or a
Drum Rack, loads a Mosaic preset into the chain, and lists the rack. The engine smoke
test covers both chain kinds end to end.

**Why this priority**: Keeps the MCP surface honest about what a chain can hold, and the
smoke test guards the feature; it adds no new UI.

**Independent Test**: Call the MCP rack tools on a scratch project and run the smoke test.

**Acceptance Scenarios**:

1. **Given** an Instrument Rack track, **When** an MCP client sets chain 0's instrument to kind 17 or 4, **Then** the call succeeds and the rack listing names the new instrument.
2. **Given** the smoke test, **When** it runs, **Then** it covers a Mosaic chain (program, playback, persistence, undo) and a nested Drum Rack (pads, playback inside the zone, persistence, undo, the nesting refusal).

---

### Edge Cases

- **Nesting depth.** Only one level: a Drum Rack may sit in a chain of a track's Instrument Rack. A Drum Rack's pads, a nested Drum Rack's pads, an Audio Effect Rack and a MIDI Effect Rack never take a rack; an Instrument Rack never goes in a chain. Such a drop or menu pick is refused (the menu doesn't offer it; a drop shows the not-allowed cursor) and nothing changes.
- **A Drum Rack chain and its MIDI effects.** A Drum Rack chain of an Instrument Rack can still hold MIDI effects in front of it, like any chain; the nested Drum Rack's own pads take none, as on a track.
- **Kit timing.** Swing and humanize shift the notes of the whole track, so they are a track-level Drum Rack's controls only; the nested Drum Rack card hides them and the engine ignores them for a nested Drum Rack.
- **Mosaic in a Drum Rack pad.** A Drum Rack pad (on a track or nested) may hold Mosaic too, through the same pad instrument menu: the editor and persistence are the same chain editor.
- **Missing samples.** A reopened Mosaic chain whose sample files moved reports them as missing in its editor, like a Mosaic track; the rest of the rack plays.
- **Loading while playing.** While a chain's Mosaic loads its samples it is silent; other chains keep playing and nothing glitches.
- **Replacing the instrument.** Turning a Mosaic or Drum Rack chain into another instrument drops the chain's mappings onto that instrument's parameters like any instrument change does today (and keeps the rest); undo brings the old instrument back with its program or kit.
- **Removing / duplicating a chain.** Duplicating a Mosaic or Drum Rack chain copies its program or whole kit; removing it removes the nested rack and its window closes or shows nothing.
- **Old projects.** Projects and presets saved before this feature load unchanged.
- **Audition.** Clicking a pad in the nested Drum Rack card plays the pad through the track, so other chains whose zone holds that note also sound.
- **Open window, structural change.** If the chain behind an open nested-Drum-Rack or Mosaic window changes its instrument or goes away, the window stops driving it (shows the new state or nothing) and never edits another chain by accident.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An Instrument Rack chain MUST accept Nota Mosaic and Nota Drum Rack as its instrument, both as a new chain and as a replacement of a chain's instrument, from the Browser (drop) and from the rack card's add-chain and chain-instrument menus.
- **FR-002**: A Drum Rack pad MUST accept Nota Mosaic as its instrument from the pad instrument menu and a Browser drop.
- **FR-003**: Rack nesting MUST be at most one level deep: a Drum Rack only inside a chain of a track's Instrument Rack; every other rack-in-rack combination is refused by the engine and not offered by the UI.
- **FR-004**: A chain's Mosaic MUST play its program's zones (key, velocity, round robin, release) for the notes the chain's zone and MIDI effects let through, and its parameters MUST be reachable by rack macro mappings.
- **FR-005**: Opening a Mosaic chain's instrument MUST show the full Mosaic editor working on that chain: program loading (presets, SFZ, packs as on a track), loading status and missing-file count, zone editing, waveforms, telemetry.
- **FR-006**: A Mosaic program change from the chain editor MUST be one undo step; a drag inside the editor MUST be live and then one step, as on a track.
- **FR-007**: A nested Drum Rack MUST route a note to the pads assigned to it with choke groups, pad tune and pad decay, as on a track, for the notes its chain lets through.
- **FR-008**: Opening a Drum Rack chain's instrument MUST show the Drum Rack card (pads grid, pad editor with its instrument and effects, macros and macro mapping) driving the nested rack; every structural edit there MUST be one undo step of the project, as on a track.
- **FR-009**: The nested Drum Rack's eight macros MUST be visible to the Instrument Rack's macro mapping as the chain instrument's parameters, so an outer macro can drive them.
- **FR-010**: Project save/reopen, Instrument Rack presets, chain duplication and undo/redo MUST keep a chain's Mosaic (parameters and program) and a nested Drum Rack (pads, pad instruments and their state including Sampler audio, pad effects, pad shaping, macros, macro names and mappings).
- **FR-011**: Factory presets for Mosaic MUST apply to a Mosaic chain from its editor's preset picker, as they do for other chain instruments; the factory kits MUST load into a nested Drum Rack from its window's preset picker.
- **FR-012**: The MCP rack tools MUST accept kind 17 and kind 4 as a chain instrument and report them; setting a Mosaic chain's preset MUST be possible through MCP.
- **FR-013**: Nothing on the audio thread may allocate, lock or wait because of these chains; program loading stays on the message and loader threads (AR-4, AR-5, AR-6).
- **FR-014**: The smoke test MUST cover both chain kinds: adding, playback, persistence round trip, undo and the nesting refusal.
- **FR-015**: CHANGELOG `[Unreleased]`, FEATURES.md and the nota-docs rack pages (EN + RU) MUST describe the new chain instruments and the one-level nesting rule.

### Key Entities

- **Chain instrument**: the instrument at the head of a rack chain; now one of the built-in synths, Sampler, Mosaic, a hosted plug-in, or (Instrument Rack chains only) a Drum Rack.
- **Nested Drum Rack**: a Drum Rack living in an Instrument Rack chain; addressed through the track and the chain that holds it; has its own pads, macros and mappings; stored inside the Instrument Rack's saved state.
- **Mosaic program**: the text description of a Mosaic's zones and groups plus where its sample files live; stored with the instrument's parameters wherever the instrument is stored.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can put a playable multisampled piano in an Instrument Rack chain in under 30 seconds (drop Mosaic, pick a preset in its editor).
- **SC-002**: A user can build a split track — a drum kit on one octave, a synth on the rest — inside one Instrument Rack without leaving the rack card and its device windows.
- **SC-003**: 100% of Mosaic programs and nested kits come back identical after save/reopen, preset load and chain duplication (checked by the smoke test comparing the program text and every pad's state).
- **SC-004**: Every edit made in a chain's Mosaic editor or nested Drum Rack card undoes in one step, and undoing it changes nothing else in the project.
- **SC-005**: No dropout or audio-thread stall when a chain's Mosaic loads a large program while the transport plays.

## Assumptions

- The racks redesign (specs/040, nota-daw/nota#331) is the base; this work extends its shared rack card and Drum Rack card rather than drawing new UI. The nested card reuses the Drum Rack card as is, in a device window.
- Instrument Rack inside an Instrument Rack chain is out of scope (a nested Instrument Rack adds nothing a wider rack doesn't); so is any rack inside an Audio Effect or MIDI Effect Rack.
- The factory drum kits load into a nested Drum Rack from its window's preset picker; a kit dropped from the Browser onto a nested Drum Rack, and a Mosaic pack preset dropped onto a chain, are follow-ups. A chain Mosaic's editor window lists the factory Mosaic presets; pack presets reach a chain through MCP `load_mosaic_preset` (and the editor's own source controls).
- Nested Drum Rack macros and Mosaic chain parameters are not directly automatable or MIDI-learnable (chain parameters never are); the outer rack's macros are the way to automate and learn them.
- Mosaic samples are referenced by path, as on a track; a rack preset carries the program text, not the audio.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
