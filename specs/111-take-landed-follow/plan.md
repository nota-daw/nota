# Implementation Plan: Follow Action When a Recorded Take Lands

**Branch**: `feat/take-landed-follow` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/111-take-landed-follow/spec.md` (issue #481)

## Summary

Each Session slot gets a **landing rule** — Off (default), Launch next scene, or Launch scene X,
on the Global launch quantum or a quantum of its own — stored as a POD field on the engine's
`SessionSlot` beside `nextTake` (specs/106), so Undo/Redo, scene edits and save/reopen carry it
with no extra machinery. Every place a **fresh** take lands in a slot (audio lane materialised,
MIDI lane finished or cut) notes the landing in a small message-thread list; `serviceSessionFollow`
(called from `poll()`) turns each noted landing into one ordinary `launchScene`, with the rule's
quantum as an override. Nothing is noted for a cancelled count-in, a take stopped before it
started, a discarded/retried attempt, an empty take, an overdub, a global stop or a deleted row,
and nothing is kept about past firings, so nothing replays. A rule's launch still waiting is
cancelled if its take is undone or its clip deleted. The empty-slot inspector gets a "When the
take lands" row, the grid marks slots with a rule, the project saves rules, MCP reads and sets
them. Details: [research.md](research.md).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-5 (the rule travels in the
  graph snapshot), AR-6 (all new logic on the message thread in `poll()` and the recording entry
  points; the audio thread is unchanged), AR-7 (new C ABI functions in `nota_engine.h`).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: inspector row, chip
  pickers, caption and the empty-slot mark.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (Application, Infrastructure, App, Mcp, smoke test)

**Primary Dependencies**: Avalonia (existing); no new dependency

**Storage**: project file — `TrackDto` gains an optional `SessionLandingRules` list (absent = all
Off), like `SessionTakeLengths`; no format version bump

**Audio-Thread Impact**: none. The rule is a POD field of the immutable snapshot (AR-5) that the
audio thread never reads; landings are noted and served on the message thread; the launch goes
through the existing `SessionPlayer::requestLaunch` path with a quantum value it already supports.

**Testing**: `tests/Nota.SmokeTest` — new `TakeLandedFollowTests.cs` plus an MCP check in
`Program.cs`; `scripts/build-win.ps1` (CI runs the macOS/Linux siblings)

**Target Platform**: macOS, Linux, Windows (desktop app)

**Project Type**: desktop app (native engine + managed UI)

**Performance Goals**: the launch is queued within one `poll()` of the landing (≤ one UI tick)

**Constraints**: pre-feature projects behave identically (all Off); existing Follow, scene follow,
bypass, take length and recording behaviour unchanged (FR-014)

**Scale/Scope**: one field per slot, ~8 engine sites, 2 ABI functions + 1 struct, 1 DTO list,
1 inspector row, 1 grid mark, 1 context-menu submenu, 1 MCP tool + snapshot field

## Constitution Check

| Principle | Status |
|---|---|
| 0. Approved issue | ✅ #481 carries `approved` |
| I. Real-time safety | ✅ no audio-thread change; snapshot field + message-thread service |
| II. Boundary discipline | ✅ 2 pure-C functions + 1 POD struct in `nota_engine.h`; no JUCE |
| III. Licensing | ✅ the new test file gets the SPDX header; no dependency |
| IV. Verification gate | ✅ smoke test extended (engine behaviour changes) |
| V. One logical change | ✅ one PR for #481 |
| VI. Docs in sync | ✅ CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), Session docs page (nota-docs) |
| VII. Spec-first | ✅ this spec/plan/tasks |
| VIII. Rules not weakened | ✅ |

Re-check after design: unchanged — no thread, lock, allocation on the render path or dependency.

## Project Structure

### Documentation (this feature)

```text
specs/111-take-landed-follow/
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
├── include/nota/nota_engine.h            # NotaSlotLandingRule + get/set
└── src/
    ├── abi/nota_engine_session.cpp       # ABI wrappers
    └── engine/
        ├── Track.h                       # LandingRule, SessionSlot::landing
        ├── Engine.h                      # accessors, landedTakes_, ruleWait_, landingMuted_,
        │                                 #   MidiTakeLane::fresh, CaptureLane::fresh
        ├── Engine_Session.cpp            # get/set, replaceSlotContent keeps it, launchScene quant
        │                                 #   override, scene-row remap, mute on global stop / row
        │                                 #   delete, serviceSessionFollow fires + watches the wait
        ├── Engine_Recording.cpp          # fresh flag on lanes, note landing (audio + MIDI)
        └── Engine.cpp                    # transportStop mutes landings
src/managed/
├── Nota.Application/Engine/EngineContracts.cs, IAudioEngine.cs   # NotaSlotLandingRule API
├── Nota.Infrastructure/Interop/NativeMethods.Session.cs, NotaEngine.Session.cs
├── Nota.Infrastructure/Persistence/ProjectModel.cs, ProjectService.cs   # SessionLandingRules
├── Nota.App/SessionView.Inspector.cs     # "When the take lands" row + submenu
├── Nota.App/SessionView.Grid.cs          # empty-slot mark
├── Nota.Application/Localization/*.json  # session.landing.* strings
└── Nota.Mcp/Tools/SessionTools.cs        # tool + snapshot
tests/Nota.SmokeTest/
├── TakeLandedFollowTests.cs              # new
└── Program.cs                            # run it; MCP check
CHANGELOG.md, FEATURES.md                 # entries
```

**Structure Decision**: existing layout; the only new source file is the smoke-test class.

## Complexity Tracking

None — no constitution violations.
