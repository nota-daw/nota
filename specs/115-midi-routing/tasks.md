---

description: "Task list for spec 115 — MIDI routing"
---

# Tasks: MIDI routing — hardware ports, channel sets and external outputs

**Input**: Design documents from `specs/115-midi-routing/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: included. The constitution (IV) requires that engine behaviour changes extend
`tests/Nota.SmokeTest`; the test list comes from [quickstart.md](quickstart.md).

**Organization**: tasks are grouped by user story (US1 External MIDI, US2 input routes,
US3 MIDI routes panel, US4 output roles), so each story can be built and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1–US4 from spec.md
- Engine paths are relative to `src/native/nota.engine/`, managed paths to `src/managed/`, unless a path starts at the repository root.

---

## Phase 1: Setup

**Purpose**: register the new files and fixtures the later phases fill in.

- [ ] T001 Add `src/engine/MidiOutService.cpp` and `src/devices/instruments/ExternalMidi.cpp` (empty TUs with the SPDX two-line header and a "why this file exists" comment) to `src/native/nota.engine/CMakeLists.txt`
- [ ] T002 [P] Create `tests/Nota.SmokeTest/MidiRoutingTests.cs` (SPDX header, empty test class) and register a `--midi-routing` filter next to the existing `--controllers` / `--palette` switches in the smoke-test entry point
- [ ] T003 [P] Save a v35 fixture project with a legacy single-device + channel-3 MIDI input and an All-inputs track as `tests/Nota.SmokeTest/Fixtures/midi-input-v35.nota` (for SC-008)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: #1011, the test hooks every story's tests use, the format bump and the identity resolver.

**⚠️ CRITICAL**: no user-story work starts until this phase is green.

- [ ] T004 Add `bool overLimit` to `MidiInput::PortInfo` and stop clamping source indices in `src/audio_io/MidiInput.h`: ports past `kMaxSources` are listed with `overLimit = true`, not connected, and get no index (R7, #1011)
- [ ] T005 [P] Apply T004 in `src/audio_io/desktop/MidiInput_rtmidi.cpp` (line ~93: no `std::min(..., kMaxSources - 1)`; skip `openPort` for over-limit ports; `sourceUids()` never repeats)
- [ ] T006 [P] Apply T004 in `src/audio_io/macos/MidiInput.mm` (lines ~39 and ~90: no clamp to the last slot; do not `MIDIPortConnectSource` an over-limit source, so `parsers[]` is never shared)
- [ ] T007 Add `nota_midi_port_over_limit` and `nota_engine_midi_index_selftest(int32_t simulated_ports)` (factor the index assignment into a pure function both backends call) to `include/nota/nota_engine.h` and the MIDI ABI TU in `src/abi/`
- [ ] T008 Add the test hooks `nota_midi_test_port_remove` (input and `test:<name>` output disappear, `portsGeneration` bumps) and `nota_midi_test_output_read` (messages with `dueNs`) per [contracts/c-abi.md](contracts/c-abi.md) § Test hooks in `include/nota/nota_engine.h`, `src/engine/Engine.h` (`TestMidiPort` gains a timestamped message log) and `src/engine/Engine.cpp`
- [ ] T009 [P] P/Invoke + wrappers for T007/T008 in `Nota.Infrastructure/Interop/NativeMethods.Tracks.cs` and `Nota.Infrastructure/Interop/NotaEngine.Tracks.cs` (or the MIDI-specific partials where `nota_midi_port_*` already live), plus `IAudioEngine.cs`
- [ ] T010 [P] Show over-limit inputs in Settings → Inputs as "Not connected — Nota listens to 16 inputs; switch one off" in `Nota.App/PreferencesWindow.AudioIo.cs`; add `overLimit` to MCP `list_midi_devices` in `Nota.Mcp/Tools/MidiTools.cs`
- [ ] T011 Smoke test "#1011 index self-test, 17 ports" in `tests/Nota.SmokeTest/MidiInputFilterTests.cs`: indices are distinct, the 17th is over the limit, and a track on input 16 receives nothing from a 17th test port
- [ ] T012 Bump `ProjectService.CurrentFormatVersion` 35 → 36 in `Nota.Infrastructure/Persistence/ProjectService.cs` (v35 load path untouched)
- [ ] T013 Create `Nota.Application/Midi/MidiRouteResolver.cs`: `enum RouteAvailability { Available, Missing, Ambiguous, Unbound, OverLimit }`; resolve an input `{uid}` or output `{uid}` against the current port and output lists — exact uid → Available; no uid match → Missing **even when a port with the same name exists**; ≥ 2 connected with the uid → Ambiguous; a connected over-limit input → OverLimit. Re-run on the engine's `portsGeneration` change and raise `Changed`
- [ ] T014 Add `nota_midi_output_count/uid/name` (every destination, paired or not) to `src/audio_io/MidiOutput.h` + `MidiOutput_rtmidi.cpp` + `macos/MidiOutput.mm`, `include/nota/nota_engine.h` and its ABI TU, the interop wrappers and `IAudioEngine.cs`; feed them to `MidiRouteResolver`

**Checkpoint**: #1011 is fixed and tested, the test hooks exist, and the resolver and output list are ready.

---

## Phase 3: User Story 1 — Play an external synth from a track (Priority: P1) 🎯 MVP

**Goal**: an External MIDI instrument sends its track's post-effect stream to a chosen output and channel, with correct releases, unplug and reconnect.

**Independent Test**: quickstart rows "Arp + Session clip → External MIDI", "Same from live input", "Stop / remove / re-route / mute / undo", "test port remove then add", "Two routes on one output", "Saved uid gone", "Keep channels with MPE input", and the 8-track no-dropout render.

### Tests for User Story 1

- [ ] T015 [P] [US1] In `tests/Nota.SmokeTest/MidiRoutingTests.cs`, write: Arp + Session clip to `test:X` channel 5 (paired Note Ons, order, due times on the beat grid); the same from live test-port input; stop, remove track, change destination, mute and undo while held (zero unpaired Note Ons, releases only at the old output — SC-002)
- [ ] T016 [P] [US1] In `tests/Nota.SmokeTest/MidiRoutingTests.cs`, write: `nota_midi_test_port_remove` while notes are held → zero bytes while gone; re-add → Note Offs, CC 64 = 0 and CC 123 on the used channels, then new events (FR-011); two routes on one output and channel → each sends its own Note Offs and one reset per channel; a missing uid with a similarly named test port → status 2 and zero bytes to it (SC-003)
- [ ] T017 [P] [US1] In `tests/Nota.SmokeTest/MidiRoutingTests.cs`, write: Keep channels with MPE test input → the MPE Configuration Message (RPN 6, 15 member channels) first, then each note on its own member channel 2–16 with its bend and pressure on that channel; switching to channel 5 sends later events on 5 after releasing at the old channels. Also: 8 External MIDI tracks + Arp, 60 s offline render, `nota_external_midi_dropped == 0`

### Implementation for User Story 1

- [ ] T018 [US1] Implement `src/engine/MidiOutService.h/.cpp`: a thread started and stopped by `Engine`; owns `std::map<std::string, std::unique_ptr<MidiOutput>> outputs` (moved from `Engine::midiOutputs_`, `Engine.h:1824`); an atomic-swapped `std::shared_ptr<const std::vector<RingRef>>` of instrument rings, with old vectors freed on the message thread (AR-5); a message-thread SPSC byte queue for control sends (SysEx included); sends due events (CoreMIDI: immediately with a host timestamp; RtMidi: a timed wait until `dueNs − 1 ms`)
- [ ] T019 [US1] Route `Engine::midiSend` (`src/engine/Engine.cpp:203`) through MidiOutService's control queue, keeping the Identity Request arming and the `test:` capture, so controller LEDs and routes share one handle per port (R3 — WinMM exclusivity)
- [ ] T020 [US1] Add a `RouteBook` per instrument uid to `MidiOutService`: `{outputUid, notesOn[16][2] uint64, pedals per channel, available}`. Apply `Config` events (release the book at the old output, clear it, apply the new uid and channel), `Flush` (release the book), send failure or a `portsGeneration` change with the uid gone (→ unavailable, book kept), and the same uid returning (Note Offs, pedals up, then CC 123 + CC 64 = 0 per channel in the book, skipping channels held by a route that stayed available on that output) — per the data-model.md state diagram
- [ ] T021 [US1] Implement `src/devices/instruments/ExternalMidi.h/.cpp` (`final : public Instrument`, kind 19 — grep for the next free kind first): `takesTimedEvents`, `handlesSustain` and `takesArticulations` return true; `noteOn`/`noteOff`/`noteOffVelocity`/`controller`/`noteExpression`/`programChange`/`keyswitchOn`/`keyswitchOff`/`allNotesOff` push `OutMidiEv{dueNs, kind, len, bytes[3]}` into a pre-allocated `SpscRingBuffer<OutMidiEv, 4096>`, dropping on overflow and counting in `std::atomic<uint32_t> dropped`; `render()` writes silence; `dueNs` = block host time + `eventOffset / sampleRate` + reported output latency; `getState`/`setState` hold a versioned `{uid, channel}` blob
- [ ] T022 [US1] Add channel modes to `ExternalMidi`: channel 1–16 re-channels everything; channel 0 = Keep channels, where `uint8_t mpeChan[128]` plus an LRU of member channels 2–16 give each note its own channel (round-robin when more than 15 notes sound), per-note expression goes on the member channel, instrument-wide controllers/wheel/pedal go on channel 1, and the RPN 6 MPE configuration is sent on entering the mode (R5)
- [ ] T023 [US1] Deliver destination changes from the message thread through the command queue (AR-4) to `ExternalMidi` (`pendingUid` slot + `configGen`), which pushes a `Config` event into its ring at the next block start; register and unregister the ring with MidiOutService when the instrument enters or leaves the graph
- [ ] T024 [US1] Make mute, solo-out and transport stop reach `ExternalMidi::allNotesOff` (a `Flush` event), and keep External MIDI tracks out of freeze/bounce, in `src/engine/Engine_Render.cpp` / `src/engine/Engine_Tracks.cpp`
- [ ] T025 [US1] Register kind 19 in `src/engine/Engine_Devices.cpp` (`cloneInstrument`, `makeBuiltinInstrument`, `#include`), add `Engine::addExternalMidiTrack` in `src/engine/Engine.h`, and make `src/devices/racks/RackCore.h` `makeInstrument` refuse kind 19 (not inside racks, data-model.md)
- [ ] T026 [US1] Add `nota_engine_add_external_midi_track`, `nota_external_midi_set/get/status/dropped` per [contracts/c-abi.md](contracts/c-abi.md) in `include/nota/nota_engine.h` and `src/abi/nota_engine_track.cpp`
- [ ] T027 [P] [US1] P/Invoke + wrappers + interface for T026 in `Nota.Infrastructure/Interop/NativeMethods.Tracks.cs`, `Nota.Infrastructure/Interop/NotaEngine.Tracks.cs` and `Nota.Application/Engine/IAudioEngine.cs`
- [ ] T028 [US1] Persistence in `Nota.Infrastructure/Persistence/ProjectModel.cs` + `ProjectService.cs`: `ExternalMidiDto { OutputUid?, OutputName?, RoleId?, Channel }` on `InstrumentDto` when `Kind == 19`, where "Exactly one of `OutputUid` / `RoleId` is set, or neither" and `Channel` is "1–16 = route channel; 0 = Keep channels … Default 1"; the load path resolves through `MidiRouteResolver` and pushes `resolvedUid` or `""`
- [ ] T029 [P] [US1] Catalog entry in `Nota.Application/Palette/BuiltinDeviceCatalog.cs` (role, aliases such as "external", "hardware synth", "MIDI out", and a description) + category in `Nota.Application/Palette/DeviceCategories.cs`; the slug in `Nota.Application/Controllers/DevicePageCatalog.cs` with an empty page set in `DevicePages.json`; `PresetService.cs` mapping `19 =>`
- [ ] T030 [US1] Create the track in `Nota.Presentation/DeviceInsertService.cs` (`CreateInstrumentTrack`, `19 => Engine.AddExternalMidiTrack`); **Replace…** offers External MIDI like any built-in (clips and effects kept, Undo restores the previous instrument)
- [ ] T031 [US1] Build the card `Nota.App/DeviceCards/Instruments/ExternalMidiInstrumentCard.cs` and register it in `InstrumentCardFactory`: an output picker (available outputs plus the saved one shown missing or ambiguous), a channel picker (1–16 / Keep channels), and a status line (sending / unavailable / no destination, and a dropped count when > 0). Follow DESIGN.md and the nota-design skill
- [ ] T032 [US1] MCP `list_midi_outputs`, `set_external_midi`, `get_external_midi` per [contracts/mcp-tools.md](contracts/mcp-tools.md) in `Nota.Mcp/Tools/MidiTools.cs`; External MIDI appears in `list_instrument_kinds`

**Checkpoint**: US1 is fully usable; T015–T017 pass.

---

## Phase 4: User Story 2 — Combine selected ports and channels on one track (Priority: P1)

**Goal**: per-port channel sets on one track, with monitoring equal to recording and owner-exact releases.

**Independent Test**: quickstart rows "Port A {3} + Port B {7}", "Channel set {3,7,11}", "Disable A while held", "Remove channel 7 while held", "Same pitch from two ports", "Legacy shortcuts and v35 fixture".

### Tests for User Story 2

- [ ] T033 [P] [US2] In `tests/Nota.SmokeTest/MidiInputFilterTests.cs`, write: A{3}+B{7} (only A/3 and B/7 monitored and recorded in one take; A/7 and port C ignored); {3,7,11} on one port (recorded == monitored); disable A while A and B are held (A's voice ends, B's sounds); remove channel 7 while 7 and 11 are held (the 7 note and its sustain release, 11 sounds, another track untouched)
- [ ] T034 [P] [US2] In `tests/Nota.SmokeTest/MidiInputFilterTests.cs`, write: the same pitch from A/3 and B/7 (one voice, ends after the second release); legacy `nota_track_set_midi_input` / `_uid` shortcuts behave exactly as before; `Fixtures/midi-input-v35.nota` loads with identical routing (SC-008)

### Implementation for User Story 2

- [ ] T035 [US2] Replace `midiInputSource_` / `midiInputChannel_` in `src/engine/Track.h` (lines ~397–411, ~520–527) with `std::atomic<bool> midiAllInputs_`, `std::atomic<uint16_t> midiInMask_[MidiInput::kMaxSources]` and `std::atomic<uint32_t> midiInGen_`; `acceptsMidiFrom(src, ch) = all || (src >= 0 && src < kMaxSources && (mask[src] >> ch) & 1)`; keep `setMidiInput(source, channel)` as the legacy mapping (all channels = `0xFFFF`, channel n = `1 << (n−1)`); replace `midiInputUid` with `std::vector<InputRouteUid> midiInputUids` (authoring copy, message thread)
- [ ] T036 [US2] Replace `LiveHeld::source[128]` / `channel[128]` with `int8_t ownerSrc[128][4], ownerCh[128][4]; uint8_t ownerN[128]` in `src/engine/Track.h`: a note-on adds an owner (the oldest is replaced when full), and the instrument note-off happens only when `ownerN` reaches 0
- [ ] T037 [US2] Take lanes: replace `midiLaneSrc_/midiLaneCh_` and `rtLaneSrc_/rtLaneCh_` with all-flags + `[kMaxMidiTakeLanes][kMaxSources]` masks in `src/engine/Engine.h`; update `publishMidiLanes` and `setTrackMidiInput` (`src/engine/Engine_Recording.cpp:698–740`) and `laneTakes` / `laneStartCtl` (`src/engine/Engine_Render.cpp:1461–1490`) to the mask test, so monitor and record share one rule (FR-004)
- [ ] T038 [US2] Release on filter change in `src/engine/Engine_Render.cpp`: when a track's `midiInGen_` differs from the value its render last saw, walk the `LiveHeld` held bits once, drop owners that no longer pass, end pitches whose `ownerN` drops to 0, and return those owners' held controllers (`liveCtlIn_`) to rest — mirroring the spec 093 MIDI In release
- [ ] T039 [US2] Add `Engine::setTrackMidiInputs(trackId, vector<{uid, mask}>)` in `src/engine/Engine_Recording.cpp`: store the authoring list, resolve uids to source indices (unresolved or over-limit → no bits), publish the masks, bump `midiInGen_`, and update a recording lane at once; re-resolve every track's list whenever `MidiInput::portsGeneration()` changes
- [ ] T040 [US2] Add `nota_track_set_midi_inputs`, `nota_track_midi_input_count` and `nota_track_midi_input_at` per [contracts/c-abi.md](contracts/c-abi.md) in `include/nota/nota_engine.h` and `src/abi/nota_engine_track.cpp`; make the existing `nota_track_set_midi_input*` map onto the list and `nota_track_get_midi_input*` report its first entry
- [ ] T041 [P] [US2] P/Invoke + wrappers + interface for T040 in `Nota.Infrastructure/Interop/NativeMethods.Tracks.cs`, `NotaEngine.Tracks.cs` and `Nota.Application/Engine/IAudioEngine.cs`
- [ ] T042 [US2] Persistence in `Nota.Infrastructure/Persistence/ProjectModel.cs` + `ProjectService.cs`: `TrackDto.MidiInputs: List<MidiInputRouteDto>?` with `MidiInputRouteDto { Uid, Name, Channels }`, where `Channels` "Must not be 0: the UI won't save an empty entry, and load drops one" and uids are "unique within one list; a duplicate merges its mask into the first entry on load". `null` → the legacy `MidiInputUid` / `MidiInputChannel` apply; a non-empty list also writes the legacy fields for its first entry; All inputs / single device write `MidiInputs = null`
- [ ] T043 [US2] Extend the MIDI input picker in `Nota.App/RecordInputPick.cs` (and the track header / mixer I/O / context menu entries that open it): keep All inputs and one device + all channels / one channel; add **Ports and channels…**, which opens a list of ports (connected plus saved-missing, shown by saved name, with Missing / Ambiguous / Over-limit badges from `MidiRouteResolver`), each with a 16-channel toggle grid; entries with no channel cannot be saved; every edit is one undo step. Follow DESIGN.md
- [ ] T044 [US2] MCP: `set_track_midi_input` gains `inputs: [{ uid, channels: int[] }]` (mutually exclusive with `uid` / `channel`; `[]` = All inputs) and `get_track_midi_input` returns `inputs` with availability, in `Nota.Mcp/Tools/MixerTools.cs` (line ~87)

**Checkpoint**: US1 and US2 both work; T033 and T034 pass, and US1's tests still pass.

---

## Phase 5: User Story 3 — See every MIDI route in one place (Priority: P2)

**Goal**: a read-only MIDI routes side panel in the Mixer.

**Independent Test**: quickstart row "`get_midi_routes` with the A, B, C, D setup" plus manual step 5.

### Tests for User Story 3

- [ ] T045 [P] [US3] In `tests/Nota.SmokeTest/MidiRoutingTests.cs`, write: with A (test port ch 2), B (MIDI In from A), C (another port) and D (External MIDI), `get_midi_routes` returns one row each with direction; filter source = A → B's row; filter destination = D's port → D; removing A's test port → A's input flagged Missing; rename and reorder → names follow; transport position and routing are unchanged by the calls (FR-021)

### Implementation for User Story 3

- [ ] T046 [US3] Create `Nota.Application/Midi/MidiRoutesQuery.cs`: build `MidiRouteRow { TrackId, TrackName, TrackColor, Inputs, MidiFrom, Destination }` from the project document plus `MidiRouteResolver` (data-model.md § MidiRouteRow), with source filters (a port or a track matches inputs or MidiFrom) and destination filters (a port or a role matches External MIDI targets); read-only
- [ ] T047 [US3] Create `Nota.App/MidiRoutesPanel.cs`: a side panel with one row per instrument track (track colour chip, inputs with channels text such as "3, 7, 11", MIDI In source or "Removed track", destination or "Keep channels", availability badges); source and destination filter pickers; clicking a row selects and reveals the track; refresh on project-changed and `MidiRouteResolver.Changed`. Follow DESIGN.md and the nota-design skill
- [ ] T048 [US3] Dock the panel on the right of `Nota.App/MixerView.cs` with a **MIDI routes** toggle button in the Mixer header and a View → **MIDI routes** menu entry (`Nota.App/MainWindow.Mixer.cs`); the Mixer channels stay usable while it is open; panel visibility persists like other Mixer view flags
- [ ] T049 [US3] MCP `get_midi_routes(source?, destination?)` in `Nota.Mcp/Tools/MixerTools.cs`, backed by `MidiRoutesQuery`

**Checkpoint**: US3 works on its own data (inputs + MIDI In); it shows External MIDI rows once US1 exists.

---

## Phase 6: User Story 4 — Rebind an output role once (Priority: P3)

**Goal**: named project output roles that External MIDI tracks target, rebound in one confirmed step while transport is stopped.

**Independent Test**: quickstart row "Role Stage Synth, two tracks ch 2 and 5, rebind".

### Tests for User Story 4

- [ ] T050 [P] [US4] In `tests/Nota.SmokeTest/MidiRoutingTests.cs`, write: create role "Stage Synth" → `test:X`; two External MIDI tracks on it at channels 2 and 5; remove `test:X` → both unavailable, zero bytes to a similarly named `test:X2`; `rebind_midi_output_role` with `preview: true` lists exactly the 2 tracks; rebind while playing is refused; rebind when stopped → later notes reach `test:Y` on 2 and 5; one undo reverts it; save, reload and both still target the role; deleting the role while in use is refused with the track names

### Implementation for User Story 4

- [ ] T051 [US4] Persistence: `ProjectDocument.MidiOutputRoles: List<MidiOutputRoleDto>?` with `MidiOutputRoleDto { Id, Name, OutputUid?, OutputName? }`, where `Id` is a "GUID, stable across renames" and `Name` is "Unique within the project (case-insensitive), 1–40 characters", in `Nota.Infrastructure/Persistence/ProjectModel.cs` + `ProjectService.cs`
- [ ] T052 [US4] Create `Nota.Application/Midi/MidiOutputRoles.cs`: create, rename and delete (delete refused while any track uses the role, listing them); `PreviewRebind(roleId)` → affected External MIDI tracks; `Rebind(roleId, newUid)` refused unless transport is stopped, then sets the binding, re-resolves and pushes the new uid to each affected instrument via `nota_external_midi_set` (channels untouched) as **one** undo step; role resolution (`Unbound` / `Missing` / `Ambiguous`) through `MidiRouteResolver`
- [ ] T053 [US4] External MIDI card (`Nota.App/DeviceCards/Instruments/ExternalMidiInstrumentCard.cs`): the destination picker lists roles above ports and shows "Stage Synth → Port X" plus availability; a **Manage roles…** entry opens a small dialog to create, rename, bind or rebind, and delete roles; rebind shows the affected-tracks preview and is disabled while transport plays
- [ ] T054 [US4] MCP `list_midi_output_roles`, `create_midi_output_role`, `rename_midi_output_role`, `rebind_midi_output_role` (with `preview`), `delete_midi_output_role`, and `role` support in `set_external_midi` / `get_external_midi`, per [contracts/mcp-tools.md](contracts/mcp-tools.md), in `Nota.Mcp/Tools/MidiTools.cs`

**Checkpoint**: all four stories work independently; the full routing test suite is green.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T055 [P] Add a `Changed` / `Added` section for this feature (External MIDI, port and channel sets, MIDI routes panel, output roles, the #1011 fix) under `CHANGELOG.md` `[Unreleased]`
- [ ] T056 [P] Update `FEATURES.md` via the `nota-features` skill (the "MIDI input per instrument track" and "Routing" entries, a new External MIDI entry, the MCP tool list)
- [ ] T057 [P] Update the user manual via the `nota-docs` skill: External MIDI, input routes (including the MPE interaction note, spec Assumptions), MIDI routes panel, roles, the 16-input limit; and run `nota-shortcuts` if the panel gets a binding
- [ ] T058 Run the manual hardware checks 1–6 in [quickstart.md](quickstart.md) and note the results in the PR description

### Nota closing tasks (the PR gates)

- [ ] T059 Run `scripts/build-win.ps1` (or `scripts/build.sh` / `scripts/build-linux.sh`) until native, managed and the smoke test are green
- [ ] T060 Check that every new source file carries the AGPL-3.0-only SPDX two-line header (`MidiOutService.*`, `ExternalMidi.*`, `MidiRouteResolver.cs`, `MidiOutputRoles.cs`, `MidiRoutesQuery.cs`, `MidiRoutesPanel.cs`, `ExternalMidiInstrumentCard.cs`, `MidiRoutingTests.cs`)
- [ ] T061 New dependency: N/A (CoreMIDI and RtMidi are already in use) — confirm no `LICENSES/third-party.md` change is needed
- [ ] T062 Engine behaviour change → smoke test extended (T011, T015–T017, T033–T034, T045, T050): confirm all ran in T059
- [ ] T063 Run `/speckit-converge` until it reports **Converged**; the PR body has `Closes #1103` and `Closes #1011`, and links #690, #732, #762, #847 and #946 as superseded

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (1)** → **Foundational (2)** → the stories.
- **US1 (3)** and **US2 (4)** both depend only on Phase 2. They touch different engine code (instrument + MidiOutService vs. Track filter + lanes), but both edit `nota_engine.h`, `nota_engine_track.cpp`, the interop partials and `ProjectModel.cs`/`ProjectService.cs`, so run their ABI and persistence tasks one after the other.
- **US3 (5)** depends on Phase 2; its External MIDI and role columns light up once US1 and US4 exist (the query reads whatever the document holds).
- **US4 (6)** depends on **US1** (roles are External MIDI destinations).
- **Polish (7)** comes after every story.

### Inside each story

Tests first (they fail), then engine, then ABI, then interop, then persistence, then UI and MCP.

## Parallel Examples

```text
# Phase 2, after T004:
T005 MidiInput_rtmidi.cpp   ‖  T006 MidiInput.mm
T009 interop                 ‖  T010 Settings + MCP list_midi_devices

# US1 tests together:
T015 ‖ T016 ‖ T017            (one file — write them as separate test methods, then merge)

# US1 after T026:
T027 interop  ‖  T029 catalog/pages/presets

# US2 tests together:
T033 ‖ T034
```

## Implementation Strategy

1. **MVP** = Phase 1, Phase 2 and US1: Nota can play an external synth, and #1011 is fixed.
   Stop and validate with the quickstart US1 rows and manual check 1–4.
2. Add **US2** (input routes), then validate.
3. Add **US3** (panel), then validate.
4. Add **US4** (roles), then validate.
5. Polish, docs, converge, one PR, with one commit series per phase so each can be reviewed
   on its own (plan.md Complexity Tracking).
