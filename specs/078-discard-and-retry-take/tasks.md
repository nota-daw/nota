---

description: "Tasks: Discard and Retry a Take (#185)"
---

# Tasks: Discard and Retry a Take

**Input**: Design documents from `specs/078-discard-and-retry-take/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required — the constitution's verification gate (IV) and SC-002 ask for smoke-test
coverage of engine behaviour. Tests live in `tests/Nota.SmokeTest/RetryTakeTests.cs`.

**Organization**: grouped by user story (spec.md priorities).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1–US4 from spec.md

---

## Phase 1: Setup

- [X] T001 Add `src/engine/Engine_TakeRetry.cpp` (SPDX header, why-comment) to the engine source list in `src/native/nota.engine/CMakeLists.txt`

---

## Phase 2: Foundational (blocks every story)

- [X] T002 Declare the take-attempt state in `src/native/nota.engine/src/engine/Engine.h` per data-model.md: `TakeAttempt { bool active; bool session; std::vector<std::pair<int32_t, std::shared_ptr<Track>>> baseTracks; size_t undoBase; std::vector<UndoEntry> redoSaved; bool countedIn; int32_t sessionTrackId; int32_t sessionScene; }`, `std::atomic<uint32_t> takeEpoch_`, `double retryStartBeat_ = NaN`, and private methods `openTakeAttempt(bool session)`, `noteTakeTrack(int32_t)`, `closeTakeAttempt()`, `discardTake()`, `abortAudioRecording()`, public `int32_t retryTake()`
- [X] T003 Implement `openTakeAttempt` / `noteTakeTrack` (first write wins; master track id included) / `closeTakeAttempt` in `src/native/nota.engine/src/engine/Engine_TakeRetry.cpp`
- [X] T004 In `pushUndo` (`src/native/nota.engine/src/engine/Engine.cpp`) decrement `takeAttempt_.undoBase` when the oldest entry is trimmed past `kMaxUndoDepth`
- [X] T005 Add a `uint32_t epoch` to `RecordedNote`, `RecordedCtl`, `RecordedExpr` (Engine.h); stamp it on the audio thread from one relaxed load of `takeEpoch_` per block in `src/native/nota.engine/src/engine/Engine_Render.cpp` (live, routed, loop-wrap split, controller and expression pushes); drop stale entries in `poll()` and `collectRecordedExpr()`
- [X] T006 Implement `abortAudioRecording()` in `src/native/nota.engine/src/engine/Engine_Recording.cpp`: taps off, `releaseAudioInputIfIdle`, drain, clear `audioCaptureBuf_`, `capturePasses_`, loop-capture fields, `audioRecordTrackId_`, `recordSessionAudio_` — no materialise
- [X] T007 Implement `discardTake()` in `Engine_TakeRetry.cpp`: `recording_ = false`; end automation writes without committing (reuse `finishAllWrites` paths, then restore lanes via base tracks); `abortAudioRecording()`; clear MIDI fold state (`takeNotes_`, `takeCtls_`, `ctlBatch_`, `pendingExpr_`, `ctlStopPending_`, `resetCtlFold()`); bump `takeEpoch_`; republish base tracks raw (Arrangement: whole tracks still present; Session: only the target slot copied into the current track); `recomputePdc()` / `reapplyRackMacros()` / `reapplyCcRoutes()` after the restore; trim `undoStack_` to `undoBase`, restore `redoStack_`; close the attempt
- [X] T008 Add `nota_engine_retry_take` to `src/native/nota.engine/include/nota/nota_engine.h` (doc per contracts/c-abi.md) and its shim in `src/native/nota.engine/src/abi/nota_engine_core.cpp`
- [X] T009 [P] Managed mirror: `RetryTakeResult { None = 0, Arrangement = 1, Session = 2, Failed = -1 }` and `RetryTakeResult RetryTake()` in `src/managed/Nota.Application/Engine/IAudioEngine.cs` / `EngineContracts.cs`; P/Invoke in `src/managed/Nota.Infrastructure/Interop/NativeMethods.cs` and wrapper in `NotaEngine*.cs`; update any other `IAudioEngine` implementations (test doubles)

**Checkpoint**: the engine can discard a take; nothing calls it yet.

---

## Phase 3: User Story 1 — Throw the take away and go again (P1) 🎯 MVP

**Goal**: one command discards an Arrangement take and records again from the same place.

**Independent Test**: quickstart.md automated scenarios 1, 2, 4, 5, 6, 9.

### Tests

- [X] T010 [US1] Create `tests/Nota.SmokeTest/RetryTakeTests.cs` (SPDX header) and register it in `tests/Nota.SmokeTest/Program.cs`; scenarios: MIDI from stop with count-in (clip removed, playhead at start, count-in running, second take only, one Undo → pre-first-take, no Redo); retry into an existing clip restores its notes; audio via internal resample source (no clip, then one clip at the original start); punch-in from beat 4 at beat 8 → restarts at 4 with no count-in; automation written during the take is gone; a note released after the retry is not in the new take; nothing recording → `None`, project unchanged

### Implementation

- [X] T011 [US1] Call `openTakeAttempt(false)` in `setRecording(true)` before the MIDI clip is created and record `countedIn`; call `closeTakeAttempt()` on `setRecording(false)` in `src/native/nota.engine/src/engine/Engine_Recording.cpp`
- [X] T012 [US1] Call `noteTakeTrack(trackId)` from `beginAutomationWrite` while an attempt is open, before the lane is created, in `src/native/nota.engine/src/engine/Engine_Automation.cpp`
- [X] T013 [US1] Implement the Arrangement branch of `retryTake()` in `Engine_TakeRetry.cpp`: remember `countedIn`, `recordStartedTransport_`, `recordReturnBeat_`; `discardTake()`; `transportStop()`; seek to `recordReturnBeat_` when the take started the transport; restart through an internal start that takes `allowCountIn` (refactor `setRecording(true)` body into `startRecordingTake(bool allowCountIn)`); return 1, or −1 when nothing could start
- [X] T014 [US1] `RetryTakeCommand` in `src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs`: call `RetryTake()`; `Arrangement` → keep `RecordOn`, `TracksChanged`, message `transport.take_discarded_retrying`; `None` → `transport.no_take_to_discard`; `Failed` → set `RecordOn` false without re-calling the engine and show the record-start failure message
- [X] T015 [US1] Localization keys `commands.discard_and_retry_take`, `transport.take_discarded_retrying`, `transport.no_take_to_discard` in `src/managed/Nota.Application/Localization/en.json` and the bundled languages the localization flow requires

**Checkpoint**: MVP — Arrangement retry works through the view-model.

---

## Phase 4: User Story 2 — Retry a loop-recorded take (P2)

**Goal**: no pass of a multi-pass loop take survives a retry; Replace-mode removals come back.

**Independent Test**: quickstart.md scenario 3.

- [X] T016 [US2] Add loop scenarios to `tests/Nota.SmokeTest/RetryTakeTests.cs`: audio loop take with ≥2 passes → retry → `TakeCount` unchanged from before; MIDI Replace pass that removed notes → retry → clip notes equal the pre-take notes
- [X] T017 [US2] Verify `abortAudioRecording()` / `discardTake()` cover loop punch state (`captureLoop_`, `captureWrapped_`, `capturePasses_`, `replaceUpTo_`, `appliedReplaceUpTo_`) and fix gaps in `Engine_TakeRetry.cpp` / `Engine_Recording.cpp`

---

## Phase 5: User Story 3 — Reach it the way that fits the session (P2)

**Goal**: shortcut, menu, palette, MCP.

**Independent Test**: quickstart.md scenario 10; manual step 1.

- [X] T018 [P] [US3] Add `C("transport.retryTake", Loc.T("commands.discard_and_retry_take"), Loc.T("common.transport"), Shortcut.Cmd('.'), "undo restart retry discard take again redo record over")` after `transport.record` in `src/managed/Nota.Application/Palette/CommandCatalog.cs`
- [X] T019 [US3] Bind `transport.retryTake` to `_vm.Transport.RetryTakeCommand` in `src/managed/Nota.App/MainWindow.Commands.cs`; add the Transport menu item with `app:Commands.Id="transport.retryTake"` in `src/managed/Nota.App/MainWindow.axaml`; make sure `⌘.` reaches it from the keyboard handler in `MainWindow.Input.cs` (not swallowed by a focused text box only when typing)
- [X] T020 [P] [US3] MCP tool `retry_take` in `src/managed/Nota.Mcp/Tools/TransportTools.cs` returning `{ result: none|arrangement|session|failed }` (contracts/commands.md)
- [X] T021 [US3] Smoke check in `RetryTakeTests.cs` (or `PaletteTests`) that the catalog holds `transport.retryTake` with `⌘.`

---

## Phase 6: User Story 4 — Retry a take recorded into a Session slot (P3)

**Goal**: only the slot's take is dropped; other clips keep playing; pending until the quantum.

**Independent Test**: quickstart.md scenarios 7, 8.

- [X] T022 [US4] Session scenarios in `tests/Nota.SmokeTest/RetryTakeTests.cs`: MIDI slot with another track's slot playing → retry → other slot still state 3; target state 5 until the quantum, then 4; stop → target slot plays; fresh slot removed then re-created (no undo residue); audio slot retry → materialised take starts on the quantum (trimmed length)
- [X] T023 [US4] Call `openTakeAttempt(true)` in `recordSessionSlot` before `addSessionMidiClip`, store track/scene; close it in `finalizeSessionRecord` and `cancelSessionCountIn` in `src/native/nota.engine/src/engine/Engine_Session.cpp`
- [X] T024 [US4] Session branch of `retryTake()` in `Engine_TakeRetry.cpp`: remember track/scene/audio; `discardTake()` (slot restore only, other players untouched, transport untouched); for audio set `retryStartBeat_` to the next launch-quantum boundary (now when quantize is 0); `recordSessionSlot(track, scene)`; return 2
- [X] T025 [US4] Trim frames before `retryStartBeat_` in the session branch of `stopAudioRecording()` (`Engine_Recording.cpp`) and reset it; also reset it on discard / close
- [X] T026 [US4] Slot state 5 "record pending" in `sessionSlotState` (`Engine_Session.cpp`) and the ABI doc in `nota_engine.h`; `TryGetSessionRecordTarget` unchanged
- [X] T027 [US4] Session grid draws state 5 as record-pending (the queued visual in record colour) in `src/managed/Nota.App/SessionView.Grid.cs`; `TransportViewModel` / MainWindow refresh the Session view after a `Session` result

---

## Phase 7: Polish & Cross-Cutting

- [X] T028 [P] `CHANGELOG.md` `[Unreleased]` → Added entry (closes #185)
- [X] T029 [P] FEATURES.md via the nota-features skill
- [X] T030 [P] Preferences → Shortcuts list via the nota-shortcuts skill
- [X] T031 [P] User docs EN + RU (recording page) via the nota-docs skill in `../nota-docs`
- [X] T032 Run `scripts/build.sh` — native + managed + smoke test green
- [X] T033 Re-check the Nota Compliance Checklist in spec.md; run `/speckit-converge`

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → stories. T002 before T003–T007; T008 after T007; T009 after T008.
- US1 (Phase 3) is the MVP and is needed by US2 (same retry path) and US3's command (T014).
- US3 T018/T020 can start once T009 is done; T019 needs T014.
- US4 needs Phase 2 and T013's refactor (shared `retryTake()` entry); independent of US2/US3.
- Polish after the stories.

## Parallel Opportunities

- T009 (managed mirror) alongside T010 (test scaffolding).
- T018 and T020 in parallel (different projects).
- T028–T031 in parallel.

## Implementation Strategy

1. Phases 1–2, then US1 → run the smoke test → MVP.
2. US2 (mostly verification), US3 (surface), US4 (Session).
3. Polish, `scripts/build.sh`, converge.

## Phase 8: Convergence

- [X] T034 Restart a Session take's count-in from its first click when retried during it, in `src/native/nota.engine/src/engine/Engine_TakeRetry.cpp`, per Edge Cases: command during the count-in (partial)
- [X] T035 Release the Record button when recording ended outside the UI (MCP `retry_take` failed) by following the engine in `TransportViewModel.Tick` (`src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs`), per FR-009 / FR-010 (partial)

## Phase 9: Review of PR #486

- [X] T036 Remember a track before its first automation write in the take (`beginAutomationWrite`), so a retry drops lanes written on tracks the take doesn't record, per FR-002 (T012 had been marked done without it)
- [X] T037 Mark the take's own checkpoints (`UndoEntry::take`); a discard drops only those and removes the take from the user's checkpoints made meanwhile, so their undo steps survive, per FR-003 / FR-011
- [X] T038 Close the take attempt when disarming ends the take, and return None when nothing records, so a retry never deletes a kept take, per FR-010
- [X] T039 Session MIDI retry: the audio thread adopts the new take epoch only when the slot has relaunched (held keys reseeded there), per FR-012 / FR-013
- [X] T040 Session retry relaunches the slot whatever its launch mode (Toggle), per FR-012
- [X] T041 Capture epoch marker frames in the input ring: a retried audio take drops what an in-flight block wrote for the discarded one, per FR-002
- [X] T042 Session audio retry notes the boundary frame when the capture crosses it (tempo changes after it don't move the trim), per FR-012
- [X] T043 Disarming one of two recording tracks keeps its part and leaves the attempt for the capture still running (`keepTakeTrack`), per FR-010 / FR-014
- [X] T044 Every take started without a retry syncs the audio thread's take epoch and cancels a Session retry still waiting for its slot (`syncTakeEpoch`), per FR-007
