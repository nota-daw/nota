# Feature Specification: Articulation maps

**Feature Branch**: none (worked on the checked-out `develop` branch, local only)

**Created**: 2026-11-09

**Status**: Draft

**Input**: User description: "A native Articulation Map system for orchestral sample libraries (Kontakt, Cinematic Studio Strings and similar), with the workflows of Cubase Expression Maps and Studio One / Fender Studio Pro Sound Variations: import both formats, assign a map to an instrument track, give each MIDI note an articulation, colour notes by articulation, edit articulations in a lane under the piano roll, and compensate each library's attack latency with a track-level offset plus a per-articulation offset and an independent keyswitch lead time."

Issue: [#547](https://github.com/nota-daw/nota/issues/547) (`approved`). Out of this feature's scope by the owner's choice: the issue's articulation lane (FR-016, removed) and automatic latency measurement (US6, deferred).

## Nota Context

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.App`) — follow `DESIGN.md` (Studio)
- [x] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Import a library's articulation map (Priority: P1)

A composer who owns Cinematic Studio Strings (with the Babylonwaves Art Conductor files)
drops a Cubase expression map, or a Studio One keyswitch list, onto Nota. A preview lists
every articulation with its name, colour and the MIDI it sends, plus anything Nota could not
carry over. They accept; the map is kept in Nota's library and they assign it to their
Kontakt track.

**Why this priority**: Without a map there are no articulations. Real libraries ship these
files, so importing them is the fastest route to a usable setup.

**Independent Test**: Import `CSSS Violins 1 A.expressionmap` and
`CSSS Violins 1 Attribute.keyswitch`; both give 44 articulations whose keyswitch notes
agree with each other; a damaged or unrelated file is refused with a clear message.

**Acceptance Scenarios**:

1. **Given** a Cubase expression map, **When** the user imports it, **Then** each
   articulation keeps its name, short name, order, colour information, and its MIDI actions in
   the order the file lists them.
2. **Given** a Studio One keyswitch list, **When** the user imports it, **Then** each
   articulation keeps its name and keyswitch notes, and any action the format cannot describe
   is reported, not dropped silently.
3. **Given** a file with a wrong extension or the wrong content, **When** the user imports it,
   **Then** Nota decides by content and refuses what it cannot read, without crashing.
4. **Given** an imported file, **When** the import finishes, **Then** the source file on disk is
   unchanged.
5. **Given** a file dropped from Windows Explorer onto the Articulation Map Manager or the
   track's Articulation section, **When** it is released, **Then** the same preview and import
   happen as from the file picker.

---

### User Story 2 - Give notes articulations (Priority: P1)

In the piano roll the user selects notes, right-clicks, and chooses an articulation from
the list of the track's assigned map. Each note remembers its articulation. Playback sends
the library the MIDI it needs, before the note, so the right sound plays.

**Why this priority**: This is the point of the feature: expressive control per note.

**Independent Test**: Assign Staccato to one note of a chord and Legato to another; play; the
library receives the matching keyswitch before each note; undo restores the previous
articulations; save and reopen keeps them.

**Acceptance Scenarios**:

1. **Given** a track with an assigned map, **When** the user right-clicks selected notes,
   **Then** the Articulation menu lists the map's articulations, "Default" and "Clear".
2. **Given** several selected notes, **When** one articulation is chosen, **Then** exactly
   those notes change, in one undo step.
3. **Given** overlapping notes or a chord with different articulations, **When** played,
   **Then** each note triggers its own articulation and no other note is affected.
4. **Given** notes with articulations, **When** they are copied, duplicated, quantized,
   transposed, split, looped or moved, **Then** the articulation stays with the note.
5. **Given** an articulation deleted from its map, **When** a note still refers to it,
   **Then** the note plays as the map's default and the interface marks it as missing.

---

### User Story 3 - Compensate sample-library latency (Priority: P1)

String libraries speak late, and each articulation has its own delay. The user sets a track
offset (for example −80 ms) and a per-articulation offset (for example Legato −150 ms) and
Nota plays each note that much earlier, without moving it on the page. The keyswitch has its
own lead time so it arrives before the note.

**Why this priority**: Without it the notes land late and the music feels sloppy; this is
the difference between a notation tool and a playable orchestral setup.

**Independent Test**: With track offset −100 ms and Legato −150 ms, a Legato note sounds
250 ms before its grid position; the keyswitch with a 30 ms lead sounds 30 ms before that;
the notes in the piano roll have not moved.

**Acceptance Scenarios**:

1. **Given** a track offset and an articulation offset, **When** a note plays, **Then** it is
   played at its position plus both offsets, and the inspector shows that sum.
2. **Given** a trigger lead time, **When** the note plays, **Then** the articulation's MIDI is
   sent that much earlier than the (offset) note and the note's own time does not change.
3. **Given** a negative offset on a note at the very start of the project, **When** playback
   starts there, **Then** it is not lost or delayed; what cannot be played early is stated.
4. **Given** playback that starts, loops or is moved in the middle of a phrase, **When**
   the first note arrives, **Then** the articulation it needs is already selected, with no
   duplicated trigger and no stuck note.

---

### User Story 4 - See articulations on the notes (Priority: P2)

Notes are coloured by articulation. (An Articulation Lane under the piano roll was dropped from
this feature at the owner's request; articulations are chosen through the note menu.)

**Why this priority**: It makes a dense orchestral part readable at a glance, but the music
works without it.

**Independent Test**: Switch Note Color Mode to Articulation Color; notes take their
articulation colours and selected notes stay clearly visible; switch back and track colours
return.

**Acceptance Scenarios**:

1. **Given** Articulation Color mode, **When** notes have articulations, **Then** each is drawn in
   its articulation colour; selected, muted and ghost notes stay distinguishable.

---

### User Story 5 - Manage maps (Priority: P2)

A manager window lists maps and lets the user create, rename, duplicate, delete, edit,
reorder, recolour and export them, and assign one to a track.

**Why this priority**: Imports are rarely perfect (colours, switch modes, offsets), so users
must be able to adjust them.

**Independent Test**: Duplicate an imported map, edit an articulation's colour and offsets,
assign it to one track; a second track using the original map is unaffected.

**Acceptance Scenarios**:

1. **Given** a map used by several tracks, **When** it is edited, **Then** the change applies to
   every track using that map, and the manager says how many; "Duplicate" gives an independent copy.
2. **Given** any edit in the manager, **When** the user presses undo, **Then** the previous
   state returns.
3. **Given** a map, **When** exported, **Then** a Nota file is written that imports back to an
   equal map.

---

### User Story 6 - Offset presets and measurement (Priority: P3)

Offsets can be typed in (Manual), taken from saved recommendations for a library (Library
Preset, each overridable), or measured from the instrument's real audio (Automatic
Measurement) with a confidence value the user accepts or rejects.

**Why this priority**: Manual offsets already deliver the value; presets and measurement save time.

**Independent Test**: Apply a library preset, override one articulation; run a measurement on
a test instrument with a known delay and get that delay back within tolerance.

**Acceptance Scenarios**:

1. **Given** a library preset, **When** applied, **Then** offsets fill in and each remains editable.
2. **Given** a measurement run, **When** it completes, **Then** Nota shows the suggested offset and a
   confidence, and applies it only on acceptance.
3. **Given** audio that gives no usable onset, **When** measured, **Then** Nota says so and suggests nothing.

---

### Edge Cases

- A file that is truncated, empty, huge, or contains entity declarations → refused with a
  message; never a crash or a hang.
- A MIDI value outside 0–127, or a message type Nota does not send (for example pitch bend in
  an action list) → reported, that action skipped, the rest of the articulation kept.
- A map with two articulations that share a name or an id → each keeps its own stable id.
- An articulation with no usable action → listed, selectable, sends nothing, and says so.
- Two notes at the same time with different articulations on one instrument → both
  articulations are sent in a defined order, and the later note's wins afterwards.
- A Session slot clip or a MIDI clip with notes shorter than the keyswitch hold → the keyswitch
  still ends cleanly.
- A track with no map assigned, or an old project → plays exactly as before.
- Notes outside the clip range, or clips that overlap → follow the existing rules for them.
- A map assigned to a track whose instrument ignores channels, programs or banks → those
  actions are skipped and the interface says so.
- Tempo changes during playback → offsets stay in milliseconds, not beats.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Nota MUST hold articulation maps containing a stable map id, name, instrument or
  library name, optional description, default articulation, groups, ordered articulations and a
  format version.
- **FR-002**: Each articulation MUST have a stable id (never reused within its map), full name,
  short name, colour, group, trigger mode (latching or momentary), MIDI channel setting
  (note's own or 1–16), ordered MIDI actions, a note timing offset, a trigger lead time and an
  enabled flag.
- **FR-003**: MIDI actions MUST support keyswitch notes, controller changes, program changes and
  bank select (MSB and LSB), in any number and order per articulation, each with an optional
  explicit channel.
- **FR-004**: Nota MUST import Cubase `.expressionmap` files, preserving names, short names,
  order, colour information, groups when present and the order of MIDI actions.
- **FR-005**: Nota MUST import Studio One keyswitch lists (`.keyswitch`) in the format verified
  from a real file, and MUST import `.soundvariation` files once a real example has been supplied
  and verified.
- **FR-006**: Importers MUST decide by file content, not extension, MUST NOT modify the source
  file, MUST bound the file size and refuse entity expansion, and MUST report every construct
  they cannot carry over instead of dropping it silently.
- **FR-007**: The user MUST be able to import by file picker and by dropping files from Windows
  Explorer onto the Articulation Map Manager and the track's Articulation section (and, if
  feasible, a track header), with a preview and warnings before accepting.
- **FR-008**: Imported and created maps MUST be stored in Nota's own library and in the project
  that uses them, so a project opens without the library.
- **FR-009**: A manager MUST allow creating, importing, renaming, duplicating, deleting,
  editing, reordering, recolouring, setting the default, and exporting maps in Nota's own
  format, and assigning a map to an instrument track.
- **FR-010**: Editing a map used by several tracks MUST apply to all of them and say so;
  duplicating MUST produce an independent copy.
- **FR-011**: Every MIDI note MUST be able to carry an articulation, stored as the articulation's
  stable id, not its label.
- **FR-012**: The piano-roll note context menu MUST list the assigned map's articulations plus
  Default and Clear, for one note, many notes, chords and overlapping notes, as one undo step.
- **FR-013**: A note's articulation MUST survive copy and paste, Alt/Shift duplication,
  quantize, transpose, velocity editing, clip duplication, splitting, looping, deletion of other
  notes, undo and redo, saving and loading.
- **FR-014**: A note referring to an articulation that no longer exists MUST play as the default
  and be shown as missing.
- **FR-015**: Notes MUST be drawn in their articulation colour when Note Color Mode is
  "Articulation Color" and in the track colour when it is "Track Color", using the existing note
  drawing; selected, muted and ghost notes MUST stay distinguishable.
- **FR-016**: *Removed at the owner's request* (an Articulation Lane under the piano roll). The
  number is kept so later requirements keep theirs.
- **FR-017**: Each instrument track MUST have a MIDI playback offset in milliseconds (positive
  delays, negative advances), shown in the track inspector and saved with the project. It MUST
  change when notes play and never where they are stored.
- **FR-018**: The effective note offset MUST be the track offset plus the articulation offset,
  applied once, and shown in the interface.
- **FR-019**: Each articulation's MIDI actions MUST be sent at the effective note time minus its
  trigger lead time. The lead time MUST NOT move the note.
- **FR-020**: Timing MUST be computed with the engine's real time and sample conversion, in
  milliseconds converted at the current sample rate and tempo, never as a fixed number of beats.
- **FR-021**: Playback MUST handle chords, overlapping notes, clip boundaries, looping, seeking
  and transport restart without duplicated triggers, missed articulation changes or stuck notes.
  After a seek, loop wrap or start in the middle of a phrase, the articulation in force at that
  point MUST be selected before the first note sounds.
- **FR-022**: Latching keyswitches MUST be released shortly after pressing; momentary ones MUST be
  released with the note. Bank select MUST be sent before program change.
- **FR-023**: Notes whose compensated start lies before playback began MUST NOT be silently
  dropped; cases that cannot be played early MUST be documented in the user manual.
- **FR-024**: Articulation MIDI MUST reach built-in instruments harmlessly (ignored) and hosted
  plug-ins as real MIDI: channel, controller, program change and bank select.
- **FR-025**: The audio thread MUST NOT allocate, lock, perform I/O or log for this feature; the
  maps in use MUST reach it as an immutable prepared table.
- **FR-026**: Offset values MUST be enterable manually, filled from a library preset with
  per-articulation overrides, and measured automatically only if real audio analysis is
  implemented and verified; otherwise Nota MUST say exactly what is missing.
- **FR-027**: Projects without maps or offsets MUST open and play exactly as before; malformed
  articulation data in a project MUST NOT prevent it from loading.
- **FR-028**: All new user-visible strings MUST use the localization keys; new controls follow
  `DESIGN.md`; user-visible changes get a `CHANGELOG.md` entry.

### Key Entities

- **Articulation map**: a named set of articulations for one instrument or library, with a
  default, groups, a version and its provenance (where it was imported from).
- **Articulation**: one playing technique, with names, colour, group, trigger mode, channel,
  ordered MIDI actions, a note offset and a trigger lead.
- **MIDI action**: one message that selects an articulation: keyswitch note, controller change,
  program change, or bank select.
- **Articulation group**: a set of mutually exclusive articulations in one map.
- **Track timing offset**: the per-track playback shift in milliseconds.
- **Note articulation**: the stable id on a note that links it to an articulation of its track's map.
- **Import result**: the map plus every message about what was kept, changed or left out.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Both real Cinematic Studio Strings files import with 44 articulations each, with
  identical keyswitch notes between the two formats and no unreported loss.
- **SC-002**: A user can import a map and assign it to a track in under one minute, without
  leaving Nota.
- **SC-003**: A note with track offset −100 ms and articulation offset −150 ms sounds 250 ms
  before its position, within one audio block of the requested time, at any tempo.
- **SC-004**: A keyswitch with a 30 ms lead arrives 30 ms before its note, in 100 % of notes in
  a test pass, including after seeks and loop wraps, with no duplicate or stuck notes.
- **SC-005**: Assigning an articulation to selected notes changes only those notes, in 100 % of
  tested selections, and undo restores every one.
- **SC-006**: Saving and reopening a project with maps, offsets and note articulations restores
  all of them exactly; a project saved before this feature opens and plays unchanged.
- **SC-007**: Playing a project with articulations adds no audio dropouts compared with the same
  project without them, on the project's existing performance test.
- **SC-008**: Every file that is damaged or not a map is refused with a message in 100 % of tested
  cases; none crashes or hangs Nota.

## Clarifications

### Session 2026-11-09

- Q: Which Studio One file is the Sound Variation format? → A: The owner supplied Studio One
  keyswitch lists with the extension `.keyswitch` (root `Music.KeySwitchList`), verified against
  the matching Cubase map. No `.soundvariation` file has been seen. Both extensions are accepted
  by content; `.soundvariation` stays unverified until an example exists.
- Q: Do colours come across from Cubase? → A: The file stores a Cubase palette index, not RGB.
  Nota keeps the index and shows its own colour for it until the user sets one; it does not copy
  Cubase's palette from memory.

## Assumptions

- Cubase and Studio One do not store latching or momentary mode in the files seen; imported
  articulations default to latching and the user can change them.
- Offsets are per instrument track and apply to Arrangement MIDI clips; Session clips are
  addressed by the plan (see the plan's research).
- The Studio One keyswitch list does not carry the controller selecting each CSS articulation;
  users who need it import the Cubase map instead. This is reported at import.
- Hosted plug-ins (Kontakt and others) receive MIDI on the channel set by the articulation or the
  note's own channel; built-in instruments ignore channels, programs and banks.
- Verifying real playback in Kontakt needs the owner's library; automated tests use a test
  instrument that records the MIDI it receives.
- The feature implements the approved issue #547 (see the top of this file).
- No new third-party dependency is needed: XML parsing uses the .NET base library.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency (XML parsing is in the .NET base library)
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
