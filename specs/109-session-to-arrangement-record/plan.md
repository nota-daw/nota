# Implementation Plan: Record a Session Performance into the Arrangement

**Branch**: `feat/session-to-arrangement-record` | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/109-session-to-arrangement-record/spec.md` (issue #971)

## Summary

Every Arrangement take also records the Session performance. The audio thread reports each
Session launch or stop as it really applies (track, slot, beat, slot position) on a lock-free
ring. The message thread turns these reports into played spans. When the take ends, each span
becomes ordinary Arrangement clips: an instrument slot is unrolled into one clip, an audio slot
becomes one clip per repetition. These replace the track's clips only over the span. On an
armed instrument track, the notes played over the span are merged into the written clip. Scene
tempos are written to the master Tempo lane when the project has no Tempo lane. The whole pass
is one undo step and is discarded by ⌘. through the existing take attempt. With nothing armed
and a Session clip playing, Record starts a performance-only pass. Research: [research.md](research.md).

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (view model, MCP text, localization)

**Primary Dependencies**: none new

**Storage**: none new. Written clips and the Tempo lane are saved like any Arrangement content.

**Audio-Thread Impact**: The render loop pushes one fixed-size `LaunchEvent` into a
preallocated SPSC ring when `maybeApply` reports a change and a pass is running. It also pushes
one event per playing track on the block where the pass flag rises. No allocation, lock or I/O
(AR-6). The pass flag reaches the audio thread through the existing `TakeRestart` command path
(AR-4). Written clips are published as new track snapshots on the message thread (AR-5). Beats
come from the transport (AR-9).

**Testing**: `tests/Nota.SmokeTest`, a new `SessionToArrangementTests.cs` (offline render,
device-free), registered in `Program.cs`. See [quickstart.md](quickstart.md).

**Target Platform**: macOS, Windows, Linux (no platform-specific code)

**Project Type**: desktop app (engine + Avalonia)

**Performance Goals**: no measurable change to the render path outside a pass. At pass end,
writing ≤ 64 tracks × a few hundred spans stays well under one UI frame budget per track
snapshot.

**Constraints**: the perf CI instruction gate (render loop) must not regress. The event push
sits behind a branch on a flag that is false outside a pass.

**Scale/Scope**: one engine feature in the recording and session paths, one status message,
docs.

## Constitution Check

| Principle | Status |
|---|---|
| 0 Approved issue | ✅ #971, `approved`, milestone 0.61.1 |
| I Real-time safety | ✅ ring push only; no alloc / lock / I/O; snapshots published off the audio thread |
| II Boundary | ✅ no new ABI function; one documented status value (contracts/c-abi.md) |
| III Licensing | ✅ no dependency; new files carry the SPDX header |
| IV Verification | ✅ new smoke group; `scripts/build.sh` must pass |
| V One change | ✅ one feature, one PR (docs and RU strings in their own repos) |
| VI Docs | ✅ CHANGELOG, FEATURES.md, nota-docs Session + Recording pages; no new shortcut |
| VII Spec-first | ✅ this spec |
| VIII Rules not weakened | ✅ |

Post-design re-check: unchanged, all pass.

## Project Structure

### Documentation (this feature)

```text
specs/109-session-to-arrangement-record/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/c-abi.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h          # record_start_status doc: value 3
└── src/engine/
    ├── Engine.h                        # LaunchEvent ring, SessionPass state, rtPassActive_ flag
    ├── SessionPass.h / SessionPass.cpp # NEW: spans from events, span → clips (unroll / slice), merge of take notes
    ├── Engine_Render.cpp               # push LaunchEvent after maybeApply; opening events on pass start
    ├── Engine_Recording.cpp            # startArrangementTake: start pass, performance-only path, undo group; setRecording(false): write pass
    ├── Engine_TakeRetry.cpp            # discardTake: clear pass state (tracks restored via noteTakeTrack)
    ├── Engine_Session.cpp              # applySceneTempo: record tempo step during a pass
    └── Engine.cpp                      # poll(): drain the ring into spans; loop seam
src/managed/
├── Nota.Infrastructure/NotaEngine.cs   # RecordStartStatus enum/doc: PerformanceOnly = 3
├── Nota.Presentation/ViewModels/TransportViewModel.cs  # status 3 → message, no auto-arm
├── Nota.Presentation (Loc catalog)     # transport.recording_session_performance
└── Nota.Mcp/Tools/TransportTools.cs    # start_recording reports the performance-only pass
tests/Nota.SmokeTest/
├── SessionToArrangementTests.cs        # NEW
└── Program.cs                          # register the group
CHANGELOG.md, FEATURES.md
```

**Structure Decision**: the span-to-clip logic goes in its own `SessionPass.{h,cpp}` with pure
functions (events → spans, span → clips). It can then be tested without timing, and
`Engine_Recording.cpp` (already 1900 lines) only calls it.

## Complexity Tracking

None.
