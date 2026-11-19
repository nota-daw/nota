# Implementation Plan: Next-Take Length for an Empty Session Slot

**Branch**: `feat/slot-record-length` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/106-slot-record-length/spec.md` (issue #503)

## Summary

Each Session slot gets a next-take length — Global (default, follows the toolbar Fixed), Open,
or Fixed bars+beats — stored as a POD field on the engine's `SessionSlot`, so Undo/Redo, scene
edits, count-in cancel and discard-and-retry carry it with no extra machinery. When a fresh
take starts, the engine resolves the slot's value to beats once and stores it with the take
(main take, each MIDI lane, each audio lane); the message-thread `serviceSessionFollow` stops
each take/lane at its own length instead of the one global value. The empty-slot inspector
gets a Take length row (chip + bars/beats numbers + caption), the grid shows non-Global values
on empty slots, the project file saves non-Global values, and MCP can read and set them.
Details: [research.md](research.md).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-5 (graph snapshot carries
  the field), AR-6 (all new logic runs on the message thread in `poll()`), AR-7 (new C ABI
  functions in `nota_engine.h`), AR-9 (lengths are beats of `Transport`'s meter).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: inspector rows,
  chips, captions and the grid's empty-slot look.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (Infrastructure, Application, App, Mcp, smoke test)

**Primary Dependencies**: Avalonia (existing); no new dependency

**Storage**: project file (`ProjectModel.cs` `TrackDto` gains an optional `SessionTakeLengths` list)

**Audio-Thread Impact**: None new. The value travels in the immutable graph snapshot (AR-5);
resolution and per-take stop checks run on the message thread (`serviceSessionFollow`, called
from `poll()`); the audio thread keeps reading the existing lane-slot table. No allocation,
lock or I/O added to the render path.

**Testing**: `tests/Nota.SmokeTest` — new `SlotTakeLengthTests.cs` plus an MCP check; `scripts/build.sh`

**Target Platform**: macOS, Linux, Windows (desktop app)

**Project Type**: desktop app (native engine + managed UI)

**Performance Goals**: take stops on the same block granularity as today's global Fixed length

**Constraints**: pre-feature projects behave identically (all Global); no change to the global
Fixed control or its 4-beats-per-bar conversion

**Scale/Scope**: one field per slot; ~6 engine sites, 3 ABI functions, 1 DTO list, 1 inspector
row, 1 grid label, 1 MCP tool

## Constitution Check

| Principle | Status |
|---|---|
| 0. Approved issue | ✅ #503 carries `approved` (2026-11-18) |
| I. Real-time safety | ✅ no audio-thread change; graph snapshot + message-thread checks |
| II. Boundary discipline | ✅ 3 new pure-C functions + 1 POD struct in `nota_engine.h`; no JUCE |
| III. Licensing | ✅ new test file gets the SPDX header; no dependency |
| IV. Verification gate | ✅ smoke test extended (engine behaviour changes) |
| V. One logical change | ✅ one PR for #503; #678 stays its own fix (research R5) |
| VI. Docs in sync | ✅ CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), user docs Session page (nota-docs, EN+RU) |
| VII. Spec-first | ✅ this spec/plan/tasks |
| VIII. Rules not weakened | ✅ |

Re-check after design: unchanged — the design added no thread, lock or dependency.

## Project Structure

### Documentation (this feature)

```text
specs/106-slot-record-length/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/abi.md
├── contracts/mcp.md
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h            # NotaSlotTakeLength + get/set/resolved-beats
└── src/
    ├── abi/nota_engine_session.cpp       # ABI wrappers
    └── engine/
        ├── Track.h                       # SessionSlot::nextTake
        ├── Engine.h                      # accessors, resolveTakeBeats, recordSessionLen_,
        │                                 #   MidiTakeLane::lenBeats, CaptureLane::lenBeats/startBeat
        ├── Engine_Session.cpp            # get/set, keep nextTake on clear/copy/arrangement→session,
        │                                 #   recordSessionSlot sizing, serviceSessionFollow per-take stop
        ├── Engine_Recording.cpp          # addSessionMidiLane / addSessionAudioLane sizing,
        │                                 #   audio materialise length
        └── Engine_Render.cpp             # verify slot-building capture sites keep nextTake
src/managed/
├── Nota.Application/Engine/IAudioEngine.cs        # SlotTakeLength API
├── Nota.Infrastructure/Interop/NativeMethods.Session.cs, NotaEngine.Session.cs
├── Nota.Infrastructure/Persistence/ProjectModel.cs, ProjectService.cs   # SessionTakeLengths
├── Nota.App/SessionView.Inspector.cs              # Take length row
├── Nota.App/SessionView.Grid.cs                   # empty-slot label
├── Nota.App/(EN string catalog)                   # session.inspector.take_length*
└── Nota.Mcp/Tools/SessionTools.cs                 # tool + snapshot
tests/Nota.SmokeTest/
├── SlotTakeLengthTests.cs                         # new
└── Program.cs                                     # run it; MCP check
CHANGELOG.md, FEATURES.md                          # entries
../nota-docs (Session page EN + RU)                # nota-docs skill
```

**Structure Decision**: existing layout; the only new source file is the smoke-test class.

## Risks / Coordination

- **#678** (retry + fixed length) touches the same stop check: retry acceptance check is
  parked until it lands (R5).
- **#481 / specs/105** (`feat/recorded-take-follow-action`, not merged) and **#480 / specs/104**
  edit `finalizeSessionRecord` / `SessionPlayer` paths near `serviceSessionFollow`: expect
  merge conflicts; whichever merges later rebases.

## Complexity Tracking

None — no constitution violations.
