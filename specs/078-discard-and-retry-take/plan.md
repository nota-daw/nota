# Implementation Plan: Discard and Retry a Take

**Branch**: `feat/discard-and-retry-recording` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/078-discard-and-retry-take/spec.md`
(issue [nota-daw/nota#185](https://github.com/nota-daw/nota/issues/185))

## Summary

One command — "Discard and retry take", `⌘.` — throws away the take being recorded and records
it again. The engine already keeps immutable track snapshots (AR-5), so a take attempt
remembers the pre-take `Track` objects it may write and the undo/redo stacks; a discard
republishes those tracks raw, trims the undo entries the take pushed and restores redo
(research R1). Late MIDI from the audio thread is fenced with a take epoch (R2). The
Arrangement restarts from where the transport was started, counting in again when the take did
(R3); a Session slot restarts on the next launch quantum with a new "record pending" slot state
while the rest of the Session keeps playing (R4). The app exposes it through the command
catalog (menu, palette, shortcut), MCP and a status message.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-4, AR-5, AR-6, AR-7, AR-9.
- **UI work** → [`DESIGN.md`](../../DESIGN.md): the pending slot reuses the Session grid's
  existing record/queued visuals.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed app, MCP, smoke test)

**Primary Dependencies**: none new

**Storage**: none — nothing new in the project file; a discarded take never enters the graph,
so save/autosave never write it (R5)

**Audio-Thread Impact**: one relaxed atomic load of the take epoch per block, copied into the
already-queued recorded-event structs (a new `uint32_t` field). No allocation, lock or I/O
(AR-6). Discard, restore and restart run on the message thread and publish immutable graphs
(AR-5); transport moves go through the existing command queue (AR-4, AR-9).

**Testing**: `tests/Nota.SmokeTest` (offline render through the C ABI) — new `RetryTakeTests`;
`PaletteTests` for the catalog entry

**Target Platform**: macOS, Windows, Linux (desktop)

**Project Type**: desktop app (native engine + managed UI)

**Performance Goals**: retry completes within one UI tick; no extra dropout beyond the
transport restart (SC-005)

**Constraints**: real-time safety (AR-6); pure C ABI (AR-7)

**Scale/Scope**: ~1 engine file new (`Engine_TakeRetry.cpp`), edits in recording / render /
session / undo; 1 ABI call; managed: engine wrapper, catalog, transport VM, MCP, localization,
Session grid state 5, smoke test

## Constitution Check

| Principle | Status |
|-----------|--------|
| 0 Approved issue | ✅ #185 carries `approved` |
| I Real-time safety | ✅ only an atomic epoch on the audio thread; restore/publish on the message thread |
| II Boundary discipline | ✅ one new C function, plain `int32_t` return; no JUCE |
| III Licensing | ✅ new files get the SPDX header; no dependency |
| IV Verification gate | ✅ `RetryTakeTests` in the smoke test |
| V One logical change | ✅ one command and what it needs |
| VI Docs in sync | ✅ CHANGELOG, FEATURES.md, Shortcuts page, user docs (EN+RU) |
| VII Spec first | ✅ this spec/plan/tasks |
| VIII Rules not weakened | ✅ |

Post-design re-check: unchanged — the design keeps all audio-thread work to the epoch stamp.

## Design

### Engine (`src/native/nota.engine`)

1. **Take attempt** (`Engine.h`, new `src/engine/Engine_TakeRetry.cpp`): struct per
   [data-model.md](data-model.md). `openTakeAttempt(kind)` is called where an Arrangement take
   (`setRecording(true)`, before the MIDI clip is created) or a Session take
   (`recordSessionSlot`, before `addSessionMidiClip`) begins; `noteTakeTrack(trackId)` from
   `beginAutomationWrite` while an attempt is open; `closeTakeAttempt()` on Stop / finalize /
   count-in cancel. `pushUndo` decrements `undoBase` when it trims the oldest entry.
2. **Discard** (`discardTake()`): end automation writes without committing; abort the audio
   capture without materialising (`abortAudioRecording()`: taps off, drain, clear buffers and
   passes); clear MIDI fold state (`takeNotes_`, `takeCtls_`, `ctlBatch_`, `pendingExpr_`,
   ctl-stop); bump `takeEpoch_`; republish base tracks raw — Arrangement: whole track objects;
   Session: only the target slot copied from the base track into the current track; run the
   undo-restore follow-ups (`placeTakesForTempo`, `recomputePdc`, …) only when a restored track's
   chains differ; trim `undoStack_` to `undoBase`, restore `redoStack_`.
3. **Restart** (`retryTake()`): Arrangement — remember `countedIn` / `startedTransport` /
   `returnBeat`; discard; `transportStop()`; seek to `returnBeat` when the take started the
   transport; call the internal start with `allowCountIn = countedIn`. Session — remember
   track/scene/audio; discard; for audio set `retryStartBeat_` = next launch-quantum boundary
   (now when quantize is 0); call `recordSessionSlot(track, scene)` without touching other
   players or the transport.
4. **Epoch**: add `uint32_t epoch` to `RecordedNote`, `RecordedCtl`, `RecordedExpr`; the audio
   thread loads `takeEpoch_` once per block; `poll()` / `collectRecordedExpr()` skip stale ones.
5. **Session audio trim**: `stopAudioRecording()` (session branch) drops frames before
   `retryStartBeat_` and resets it.
6. **Slot state 5**: `sessionSlotState` returns 5 when this slot is the record target and the
   MIDI player isn't playing it yet, or the audio playhead is before `retryStartBeat_`.
7. **ABI**: `nota_engine_retry_take` in `nota_engine.h` + `src/abi/nota_engine_core.cpp`;
   doc for slot state 5 ([contracts/c-abi.md](contracts/c-abi.md)).

### Managed (`src/managed`)

- `Nota.Infrastructure/Interop`: P/Invoke + `NotaEngine.RetryTake()`; `IAudioEngine` +
  `RetryTakeResult` enum (`Nota.Application/Engine`). Any test doubles implementing
  `IAudioEngine` get the member.
- `Nota.Application/Palette/CommandCatalog.cs`: `transport.retryTake`, `Shortcut.Cmd('.')`.
- `Nota.Presentation/ViewModels/TransportViewModel.cs`: `RetryTakeCommand` → engine, keeps
  `RecordOn` true, raises `TracksChanged` and the status message.
- `Nota.App`: bind the command id (`MainWindow.Commands.cs`), Transport menu item
  (`MainWindow.axaml`), Session grid draws state 5 as record-pending (`SessionView.Grid.cs`),
  refresh after retry.
- `Nota.Mcp/Tools/TransportTools.cs`: `retry_take`.
- Localization keys in `Nota.Application/Localization/en.json` (+ bundled languages per the
  localization workflow).

### Tests & docs

- `tests/Nota.SmokeTest/RetryTakeTests.cs` + registration in `Program.cs`, scenarios in
  [quickstart.md](quickstart.md); `PaletteTests` expectation for the new shortcut if it lists them.
- `CHANGELOG.md` `[Unreleased]` → Added; FEATURES.md (nota-features); Shortcuts page
  (nota-shortcuts); user docs EN+RU (nota-docs).

## Project Structure

### Documentation (this feature)

```text
specs/078-discard-and-retry-take/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi.md
│   └── commands.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h          # nota_engine_retry_take, slot state 5 doc
├── src/abi/nota_engine_core.cpp        # ABI shim
└── src/engine/
    ├── Engine.h                        # TakeAttempt, takeEpoch_, retryStartBeat_
    ├── Engine_TakeRetry.cpp            # NEW: open/close/discard/retry
    ├── Engine_Recording.cpp            # open/close hooks, abortAudioRecording, session trim
    ├── Engine_Render.cpp               # epoch stamp + stale filter in poll()
    ├── Engine_Session.cpp              # open hook, slot state 5
    ├── Engine_Automation.cpp           # noteTakeTrack on write begin
    └── Engine.cpp                      # pushUndo trims undoBase
src/managed/
├── Nota.Application/Engine/IAudioEngine.cs, EngineContracts.cs
├── Nota.Application/Palette/CommandCatalog.cs
├── Nota.Application/Localization/en.json
├── Nota.Infrastructure/Interop/NativeMethods.cs, NotaEngine*.cs
├── Nota.Presentation/ViewModels/TransportViewModel.cs
├── Nota.App/MainWindow.Commands.cs, MainWindow.axaml, SessionView.Grid.cs
└── Nota.Mcp/Tools/TransportTools.cs
tests/Nota.SmokeTest/RetryTakeTests.cs, Program.cs
```

**Structure Decision**: the existing engine/managed split; one new engine translation unit
keeps the attempt logic out of the already long recording file (it must be added to the
engine's CMake source list).

## Complexity Tracking

None.
