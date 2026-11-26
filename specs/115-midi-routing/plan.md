# Implementation Plan: MIDI routing — hardware ports, channel sets and external outputs

**Branch**: `115-midi-routing` | **Date**: 2026-11-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/115-midi-routing/spec.md` (issue #1103, plus #1011)

## Summary

This plan gives instrument tracks one MIDI routing model in both directions:

- **Inputs**: a track saves a list of `{port identity, channel mask}` entries. The engine
  turns the list into per-source 16-bit masks that monitoring and recording read the same
  way. Owned held notes are released when their source leaves the filter.
- **Outputs**: a new built-in instrument, **External MIDI**, turns the post-MIDI-effect
  stream it already receives into raw MIDI. Its bytes go through a per-instrument lock-free
  ring to a new MIDI out thread. That thread owns every MIDI output, timestamps or schedules
  each send, and keeps the owned-note books used for releases, unplug and reconnect.
- **Roles, identity and the MIDI routes panel**: managed concerns. A resolver decides
  available, missing or ambiguous per saved identity, and the engine only ever receives a
  resolved uid.
- **#1011 comes first**: ports beyond the 16-source limit stop sharing an index; they are
  not connected and are reported as over the limit.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-4, AR-5, AR-6, AR-7,
  AR-9. Each one is addressed in the Constitution Check below.
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill. This covers the
  External MIDI card, the input-route picker and the MIDI routes panel.
- **A built-in instrument** → the `nota-instrument` skill. External MIDI follows its
  registration checklist (§2), but it has no DSP and no parameters on the plugin-param
  interface.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed app, Avalonia UI, MCP)

**Primary Dependencies**: existing ones only — CoreMIDI (macOS) and RtMidi (Windows WinMM,
Linux ALSA), which are already used for MIDI in and out. **No new dependency.**

**Storage**: the project document (format 35 → **36**); see [data-model.md](data-model.md).

**Audio-Thread Impact**:
- *Input filter*: atomics in `Track` (one flag plus 16 × `uint16_t` masks) and the same per
  take lane, read lock-free. A per-track generation atomic triggers a single walk of
  `LiveHeld` to release notes whose owner was removed. No allocation.
- *External MIDI*: the instrument writes fixed-size events into its own pre-allocated
  `SpscRingBuffer` (AR-6). If the ring overflows, the event is dropped and counted, and the
  audio thread never blocks.
- *Ring list*: the MIDI out thread's list of rings is published as an immutable snapshot
  (AR-5) and freed on the message thread.
- *Config changes* (output uid, channel mode) reach the instrument through the command queue
  (AR-4) and travel into its ring as an ordered config event.

**Testing**: the `tests/Nota.SmokeTest` suite gets a new `MidiRoutingTests.cs` plus extended
`MidiInputFilterTests.cs`, using test MIDI ports (inputs) and captured `test:` outputs. A
native self-test covers #1011 index assignment. Hardware checks are in
[quickstart.md](quickstart.md).

**Target Platform**: macOS (CoreMIDI), Windows (WinMM via RtMidi), Linux (ALSA via RtMidi).

**Project Type**: desktop application (native engine plus managed app).

**Performance Goals**: no added audio-thread cost when no External MIDI instrument exists and
no track uses a port list (the masks replace the two existing atomics one-for-one).
External send jitter stays within about 1 ms of `dueNs` on every platform: the MIDI out
thread waits for each message and sends it when due (CoreMIDI timestamps are a later
refinement; research.md "As built"). No dropouts in the smoke project with 8 External MIDI tracks and an
arpeggiator (SC-005).

**Constraints**: AR-6 on the audio thread; WinMM's exclusive output handles (R3); the
16-source limit (R7); the identity rules (FR-006).

**Scale/Scope**: up to 16 input sources, any number of outputs, External MIDI tracks bounded
only by tracks, and a few roles per project.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design: still passes.*

| Principle | Status | How |
|---|---|---|
| 0 Approved issue | ✅ | #1103 carries `approved`; #1011 is folded in by clarification Q2; the PR closes both |
| I Real-time safety | ✅ | Masks are atomics; SPSC rings are pre-allocated; snapshots are swapped, never mutated; all port I/O runs on the MIDI out thread (R2, R6) |
| II Boundary discipline | ✅ | New calls are plain C in `nota_engine.h` ([contracts/c-abi.md](contracts/c-abi.md)); no JUCE involvement — External MIDI is not a plug-in |
| III Licensing | ✅ | New files get the SPDX header; no new dependency |
| IV Verification gate | ✅ | Smoke tests cover the ABI and engine behaviour (R12) |
| V One logical change | ⚠️ justified | One PR for one approved umbrella issue, as the user asked. Commits stay per story so review can follow; see Complexity Tracking |
| VI Docs in sync | ✅ | Planned: CHANGELOG `[Unreleased]`, FEATURES.md via nota-features, user manual via nota-docs |
| VII Spec-first | ✅ | spec → clarify → plan (this) → tasks |
| VIII Rules not weakened | ✅ | The WinMM exclusivity problem is solved by one owner thread, not by touching ports from the audio thread |

## Project Structure

### Documentation (this feature)

```text
specs/115-midi-routing/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R12 decisions
├── data-model.md        # engine + project entities
├── quickstart.md        # validation run guide
├── contracts/
│   ├── c-abi.md         # new / changed nota_engine.h calls
│   └── mcp-tools.md     # MCP tool shapes
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks (not yet)
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h                    # + input masks, outputs list, External MIDI, test hooks
├── src/abi/nota_engine_track.cpp                 # + track input-route + External MIDI calls
├── src/abi/nota_engine_midi.cpp (or existing MIDI ABI TU)  # outputs list, test port remove/read, index self-test
├── src/audio_io/MidiInput.h                      # PortInfo::overLimit; no shared indices (#1011)
├── src/audio_io/desktop/MidiInput_rtmidi.cpp     # #1011
├── src/audio_io/macos/MidiInput.mm               # #1011
├── src/audio_io/MidiOutput.h (+ _rtmidi.cpp, .mm)  # + enumerate outputs; send with timestamp (CoreMIDI)
├── src/engine/MidiOutService.h/.cpp              # NEW: MIDI out thread, rings registry, owned books, reconnect
├── src/engine/Track.h                            # masks + generation replace source/channel atomics; LiveHeld owners
├── src/engine/Engine.h, Engine.cpp               # midiSend → service; port-generation resolve of input uids
├── src/engine/Engine_Recording.cpp               # setTrackMidiInputs, lanes use masks
├── src/engine/Engine_Render.cpp                  # laneTakes/laneStartCtl masks; release on generation change
├── src/engine/Engine_Devices.cpp                 # kind 19 in add/clone/makeBuiltinInstrument
├── src/devices/racks/RackCore.h                  # makeInstrument (kind 19 refused inside racks, see data-model)
└── src/devices/instruments/ExternalMidi.h/.cpp   # NEW: the instrument (bytes → ring, MPE channel allocator)

src/managed/
├── Nota.Infrastructure/Interop/NativeMethods.*.cs, NotaEngine.*.cs   # P/Invoke + wrappers
├── Nota.Infrastructure/Persistence/ProjectModel.cs, ProjectService.cs # format 36 DTOs, load/save
├── Nota.Application/Engine/IAudioEngine.cs
├── Nota.Application/Midi/MidiRouteResolver.cs     # NEW: identity → available/missing/ambiguous
├── Nota.Application/Midi/MidiOutputRoles.cs       # NEW: roles + rebind (one undo step)
├── Nota.Application/Midi/MidiRoutesQuery.cs       # NEW: panel rows
├── Nota.Application/Palette/BuiltinDeviceCatalog.cs, DeviceCategories.cs  # External MIDI entry
├── Nota.Application/Controllers/DevicePageCatalog.cs, DevicePages.json    # kind 19 slug, no pages
├── Nota.Presentation/DeviceInsertService.cs       # create External MIDI track
├── Nota.App/DeviceCards/Instruments/ExternalMidiInstrumentCard.cs  # NEW card (output/role, channel mode, status)
├── Nota.App/RecordInputPick.cs (+ track header / mixer I/O menus) # multi-port + channel-set picker
├── Nota.App/MidiRoutesPanel.cs                    # NEW side panel
├── Nota.App/MixerView.cs                          # dock the panel + header button; View menu entry
├── Nota.App/PreferencesWindow.AudioIo.cs          # over-limit inputs shown
└── Nota.Mcp/Tools/MixerTools.cs, MidiTools.cs      # extended + new tools

tests/Nota.SmokeTest/
├── MidiRoutingTests.cs                            # NEW
└── MidiInputFilterTests.cs                        # extended (masks, legacy mapping, release)

CHANGELOG.md, FEATURES.md, docs (user manual)       # via nota-features / nota-docs
```

**Structure Decision**: the existing engine, managed and test layout is kept. There are two
new engine units (`MidiOutService`, `ExternalMidi`) and three new application services under
`Nota.Application/Midi/`. Everything else extends files that already own the behaviour.

## Delivery order (feeds /speckit-tasks)

1. **Foundation**: #1011 (distinct source indices, over-limit ports not connected,
   self-test), test hooks, format 36, the identity resolver, the outputs list.
2. **Story 1 (External MIDI, the MVP)**: MidiOutService (with `midiSend` moved onto it), the
   instrument, card, persistence, MCP, smoke tests.
3. **Story 2 (inputs)**: masks, lanes, release on generation change, owners, persistence,
   picker UI, MCP. Independent of Story 1 in the engine.
4. **Story 4 (roles)**: roles, rebind preview, undo, MCP.
5. **Story 3 (panel)**: query, panel, View menu.
6. **Docs**: CHANGELOG, FEATURES.md, manual, then converge.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| One PR spans five issues + #1011 (principle V) | The user asked for one spec and one implementation; #1103 was approved as one umbrella | Splitting would re-open the dependency chain (#946 needs #690; the overview needs both models). Mitigation: one commit series per story, each building and passing the smoke test on its own |
| A new engine thread (MidiOutService) | Timing that is independent of the message thread (SC-005) and a single owner for exclusive WinMM handles (R3) | Message-thread drain: UI-frame jitter. Audio-thread send: I/O on the audio thread (AR-6) |
