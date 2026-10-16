# Implementation Plan: Loop record mode — Overdub / Replace

**Branch**: `fix/loop-seam-note-cut` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/018-loop-record-replace/spec.md`

## Summary

Add an app-wide loop record mode, Overdub (the default, today's behaviour) or Replace, for MIDI
recorded into the Arrangement with the loop on. The audio thread numbers the passes of a take at
each loop wrap, tags every recorded note with its pass, and on the first fresh note of a new pass in
Replace mode publishes "replace everything before this pass". The message thread then removes this
take's earlier-pass notes from the record clip within the same undo step. The mode is switched by a
transport toggle next to Loop, ⌘⇧R, a menu item and an MCP tool, and persists as an app preference.
Design details: [research.md](research.md).

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (Application, Infrastructure, Presentation, App, Mcp)

**Primary Dependencies**: None new (Avalonia already used)

**Storage**: App settings (`Settings.LoopRecordReplace`). Not stored in the project file.

**Audio-Thread Impact**: Small and allocation-free. An `int pass` field on `RecordedNote` (fixed-size SPSC ring). A pass counter, a latched mode flag and a "triggered" flag as plain members. One atomic int (`replaceUpTo_`, release store) set at most once per pass. The mode crosses in as an atomic bool written by the message thread. Clip edits stay on the message thread via immutable snapshots (AR-5). No locks, no I/O (AR-6).

**Testing**: `tests/Nota.SmokeTest` (new `LoopRecordModeTests.cs`, offline render through the C ABI), `scripts/build.sh`

**Target Platform**: macOS, Windows, Linux desktop

**Project Type**: Desktop app (native engine + managed UI)

**Performance Goals**: The replace is visible within one UI tick (~33 ms) of the triggering key press. No audio glitch (SC-005).

**Constraints**: AR-4/5/6/7. The take stays one undo step.

**Scale/Scope**: Arrangement MIDI loop recording only. Session slots and audio punch are unchanged.

## Constitution Check

| Principle | Status |
|-----------|--------|
| I. Real-time audio safety | ✅ The audio thread only touches fixed-size state and atomics. Clip mutation happens on the message thread through a snapshot republish. |
| II. Boundary discipline | ✅ Two new C ABI functions in `nota_engine.h` (contracts/c-abi.md). No JUCE. |
| III. Licensing hygiene | ✅ The one new file (`LoopRecordModeTests.cs`) gets the SPDX header. No dependencies. |
| IV. Verification gate | ✅ New smoke group covers every acceptance scenario (quickstart.md). `scripts/build.sh` must pass. |
| V. One logical change per PR | ⚠️ It shares a branch with the seam bug fix at the user's request ("close the issue in one go"). Separate commits keep the two reviewable. |
| VI. Docs in sync | ✅ CHANGELOG `[Unreleased] → Added`. nota-features, nota-shortcuts (⌘⇧R) and nota-docs (recording page) are planned in tasks. |
| VII. Spec-first | ✅ Spec, clarifications, plan, tasks. |
| VIII. Safety rules not weakened | ✅ |

Post-design re-check: unchanged, all gates pass. The V deviation is recorded under Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/018-loop-record-replace/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi.md
│   └── ui.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h          # + set/get loop record mode
└── src/
    ├── Engine.h                        # RecordedNote.pass; pass/replace state; takeNotes_
    ├── Engine_Render.cpp               # onLoopWrap: pass++ & latch; drainLiveMidi/captureRoutedNotes: tag pass, trigger; collectRecorded: apply replace
    ├── Engine_Recording.cpp            # setRecording: reset pass state, clear takeNotes_
    └── nota_engine_core.cpp            # ABI shims
src/managed/
├── Nota.Application/
│   ├── Engine/IAudioEngine.cs          # SetLoopRecordMode / LoopRecordReplace
│   ├── Settings.cs                     # LoopRecordReplace (default false)
│   └── Palette/CommandCatalog.cs       # transport.loopRecordMode, ⌘⇧R
├── Nota.Infrastructure/Interop/        # NativeMethods + NotaEngine wrappers
├── Nota.Presentation/ViewModels/TransportViewModel.cs   # LoopRecordReplace property → engine
├── Nota.App/
│   ├── MainWindow.axaml                # transport cell + menu item
│   ├── MainWindow.axaml.cs             # restore from settings, persist on change
│   └── MainWindow.Commands.cs          # bind the command
└── Nota.Mcp/Tools/TransportTools.cs    # get_transport field, set_loop_record_mode
tests/Nota.SmokeTest/
├── LoopRecordModeTests.cs              # new
└── Program.cs                          # register group
```

**Structure Decision**: Follow the existing count-in path end to end (engine atomic → C ABI → `IAudioEngine` → `TransportViewModel` → `Settings` and the transport console), plus pass bookkeeping in the existing recording path.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Two logical changes on one branch (seam fix + this feature) | The user asked to close nota-daw/nota#29 in one branch | Two branches would split one issue across PRs, against the user's request. Mitigation: separate commits. |
