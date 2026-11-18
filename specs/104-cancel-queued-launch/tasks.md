---
description: "Tasks: cancel a queued Session launch (#480)"
---

# Tasks: Cancel a queued Session launch before its quantized start

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: smoke tests are required by the constitution (IV) for an engine behaviour change; they
are listed with each story.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Confirm the worktree `../nota-480` is on `feat/cancel-queued-launch` off `origin/develop`, `build/` is a Release configure, and `scripts/build.sh` runs there (first build fetches CPM sources)

---

## Phase 2: Foundational (engine core shared by every story)

- [X] T002 Add `pendingGen{0}` and `appliedGen{0}` atomics to `SessionPlayer`; `requestLaunch(slot, quant, legato, gain, gen = 0)` stores `pendingGen` before `pending`; `requestStop` / `requestStopNow` store `pendingGen = 0`; `maybeApply` stores `appliedGen = pendingGen` when it consumes a request — in `src/native/nota.engine/src/engine/SessionPlayer.h` and `SessionPlayer.cpp`
- [X] T003 Add audio-thread `SessionPlayer::cancelSlot(int32_t slot)` (CAS `pending: slot → kNone`; clear `repeatSlot` when it equals `slot`; returns bool) and `cancelGen(int32_t scene, int32_t gen)` (withdraw when `pending == scene` or, with `gen > 0`, `pendingGen == gen`; never `kStopNow`) in `src/native/nota.engine/src/engine/SessionPlayer.h/.cpp`
- [X] T004 Make the audio-thread follow / Repeat requests in `sessionPreApply` store `pendingGen = 0` before they queue — `src/native/nota.engine/src/engine/Engine_Session.cpp`
- [X] T005 Add `CommandType::CancelSessionSlot` (i0 track id, i1 scene) and `CancelSessionScene` (i0 scene, i1 generation) with comments in `src/native/nota.engine/src/engine/CommandQueue.h`
- [X] T006 Add the audio-thread cancel table (`cancelSlots_[8]`, `cancelSlotCount_`, `cancelSceneRow_`, `cancelSceneGen_`) to `Engine.h`; fill it in `drainCommands`, apply it per track at the top of the Session step (before `sessionPreApply` / `maybeApply`), clear it after the track loop — `src/native/nota.engine/src/engine/Engine.h`, `Engine_Render.cpp`

**Checkpoint**: requests can be withdrawn atomically per block; nothing calls it yet.

---

## Phase 3: User Story 1 — Cancel a queued clip launch (P1) 🎯 MVP

**Goal**: one queued slot can be withdrawn; the playing clip continues untouched.
**Independent test**: quickstart § Automated 1, 8; § By hand (desktop) 4–5.

- [X] T007 [US1] `Engine::cancelSlotLaunch(trackId, scene)` (message thread: true and push `CancelSessionSlot` when the track's `pending == scene`) in `src/native/nota.engine/src/engine/Engine_Session.cpp` + declaration in `Engine.h`
- [X] T008 [US1] `nota_session_cancel_slot_launch` in `include/nota/nota_engine.h` (doc comment from contracts/c-abi.md), `src/abi/nota_engine_session.cpp`, and the export lists `nota_exports.map` / `nota_exports.txt`
- [X] T009 [US1] Managed interop: `NativeMethods.Session.cs`, `NotaEngine.Session.cs` (`CancelSlotLaunch`), `IAudioEngine.CancelSlotLaunch` and every other `IAudioEngine` implementer (fakes / null engines) — `src/managed/Nota.Infrastructure/Interop/`, `src/managed/Nota.Application/Engine/IAudioEngine.cs`
- [X] T010 [US1] Smoke tests in new `tests/Nota.SmokeTest/CancelLaunchTests.cs` (wired into `Program.cs` full run and `--session-input`): slot cancel keeps A playing with an unbroken position and B at state 1; re-launch B starts at the next boundary; cancel with nothing queued returns false; a queued Repeat clip cancelled stops re-queuing
- [X] T011 [US1] Desktop clip slot: Alt+click on the launch zone of a queued slot (state 2) cancels instead of launching; group slot: Alt+click cancels each member queued on that row; "Cancel launch" in the slot context menu while queued (a group slot has no menu); queued launch tooltip "Queued — Alt+click to cancel"; status line "Launch cancelled" — `src/managed/Nota.App/SessionView.Grid.cs`
- [X] T012 [P] [US1] Strings `session.grid.cancel_launch`, `session.grid.queued_alt_click_to_cancel`, `session.launch_cancelled` in `src/managed/Nota.Application/Localization/en.json` (RU in the polish phase)

**Checkpoint**: MVP — a single queued clip can be cancelled from the desktop.

---

## Phase 4: User Story 2 — Cancel a queued scene launch (P1)

**Goal**: the whole queued row — clip starts and the stops it queued — is withdrawn, with its
follow action and tempo change.
**Independent test**: quickstart § Automated 2–5, 7; § By hand (desktop) 1–3.

- [X] T013 [US2] Scene launch record in `Engine.h` / `Engine_Session.cpp`: `sceneLaunchGen_`, `waitSceneGen_`, `waitScene_`, `prevFollowScene_`, `prevFollowStart_`; `launchScene` passes the new generation through `launchSlotImpl` to `requestLaunch` / a gen-carrying stop, confirms at once when it queued nothing, and saves the previous follow only when no launch is already waiting
- [X] T014 [US2] `serviceSessionFollow` resolves a waiting launch first (confirmed: any track `appliedGen == gen`; withdrawn: no track with `pendingGen == gen` and a request queued; else skip tempo + follow this tick); withdrawn → drop `sceneTempoPending_`, restore the saved follow unless its due time (start + followBeats) is at or before the cancelled start, else `sceneFollowScene_ = -1` — `src/native/nota.engine/src/engine/Engine_Session.cpp`
- [X] T015 [US2] `Engine::cancelSceneLaunch(scene)`: true when any slot of the row is queued or the waiting launch is this scene; push `CancelSessionScene` with the waiting generation (0 if the waiting launch is another scene) — `Engine_Session.cpp`, `Engine.h`
- [X] T016 [US2] `nota_session_cancel_scene_launch` in `nota_engine.h`, `abi/nota_engine_session.cpp`, export lists; managed `CancelSceneLaunch` through `NativeMethods.Session.cs`, `NotaEngine.Session.cs`, `IAudioEngine` and implementers
- [X] T017 [US2] Smoke tests in `CancelLaunchTests.cs`: the issue's drum / bass example (drum unbroken, bass silent); the scene's follow fires 0 times over 16 bars and its tempo never applies; cancel pushed in the boundary block vs one block later (all withdrawn vs all started); a newer direct launch on one track survives the scene cancel; the last slot cancelled by hand drops the follow / tempo; a follow-launched scene cancelled ends scene follow
- [X] T018 [US2] Desktop scene cell: Alt+click on a queued scene's launch box cancels; "Cancel launch" in the scene context menu while queued; queued scene tooltip names Alt+click — `src/managed/Nota.App/SessionView.Grid.cs`

---

## Phase 5: User Story 3 — Second press and Stop while a launch waits (P2)

**Goal**: settled, tested behaviour for re-press, Stop and Stop All.
**Independent test**: quickstart § Automated 6; spec Story 3.

- [X] T019 [US3] `stopAllSession` also clears `sceneTempoPending_` and `waitSceneGen_` — `src/native/nota.engine/src/engine/Engine_Session.cpp`
- [X] T020 [US3] Smoke tests in `CancelLaunchTests.cs`: re-launching a queued slot keeps it queued and it starts at the boundary; track Stop with A playing and B queued → B never starts, A stops at the boundary; Stop All with a queued scene → no tempo change, no follow

---

## Phase 6: User Story 4 — Nota Remote and MCP (P3)

**Goal**: the same cancel from the phone and from MCP clients.
**Independent test**: quickstart § By hand (Nota Remote); MCP smoke check.

- [X] T021 [P] [US4] MCP tools `cancel_slot_launch`, `cancel_scene_launch` (`get_session` already reports "queued") — `src/managed/Nota.Mcp/Tools/SessionTools.cs`; smoke check through the existing MCP harness in `tests/Nota.SmokeTest/Program.cs` (`mcpSession`)
- [X] T022 [P] [US4] Hub messages `cancel` {id, i} (group id → members) and `cancelScene` {i} in `src/managed/Nota.Remote/RemoteHub.Apply.cs` (+ the command record if fields are typed)
- [X] T023 [US4] `use:tap` optional hold handler (500 ms, fires once while still down, swallows that pointer's release tap) in `src/web/remote/src/lib/tap.ts`, unit test in `src/web/remote/test/`
- [X] T024 [US4] Scenes screen: hold on a queued clip / group slot → `cancel`, hold on a scene button whose row is queued → `cancelScene`; header comment updated — `src/web/remote/src/screens/Scenes.svelte`

---

## Phase 7: Polish & cross-cutting

- [X] T025 [P] Shortcuts screen: Session gesture row "Cancel a queued launch — Alt+click its launch button" in `src/managed/Nota.Application/Input/ShortcutLayout.cs` (nota-shortcuts skill)
- [X] T026 [P] `CHANGELOG.md` `[Unreleased]` Added entry (#480); `FEATURES.md` Session line (nota-features skill)
- [X] T027 [P] User manual (nota-docs skill, `../nota-docs` worktree): Session page EN + RU — cancel, second press, Stop / Stop All while a launch waits; Nota Remote page hold gesture
- [X] T028 Russian strings for T012 keys in the RU catalog (added uncommitted to `../nota-translations/languages/ru.json`; its validator accepts them once the app PR is on develop)
- [X] T029 Real-app check: desktop harness screenshots of a queued slot's tooltip and the menu item; Remote page hold against a running hub
- [X] T030 `scripts/build.sh` green (smoke incl. design-token / localization checks); perf gate unaffected — 6696 checks pass; the one failure is the real-VST3 render-pool timing check, which flakes the same way on a build without this change (2/100 there); the CI perf gate was not run locally
- [X] T031 `/speckit-converge` reports Converged; Nota Compliance Checklist in spec.md ticked

---

## Dependencies

- Phase 2 blocks everything. US1 (T007–T012) needs T002–T006. US2 needs T002–T006 and reuses
  T008/T009 patterns (independent of US1's UI). US3 needs US2's record (T013). US4 needs T008 and
  T016. Polish last.

## Parallel opportunities

- T012, T021, T022, T025, T026, T027 touch separate files.
- US1 UI (T011) and US2 engine (T013–T015) can proceed side by side once Phase 2 is in.

## Implementation strategy

MVP = Phase 2 + US1 (slot cancel on the desktop). Then US2 (the issue's own example), US3 (settled
behaviour), US4 (Remote + MCP), polish. One pull request for the whole issue.

## Phase 8: Convergence

- [X] T032 Regenerate reference/shortcuts in `../nota-docs-480` (`npm run shortcuts` with `NOTA_REPO=../nota-480`) for the new Session gesture row per FR-014 (partial)
- [X] T033 Send `cancel` (track and group id) and `cancelScene` through the hub in `tests/Nota.SmokeTest/RemoteTests.cs` and check the queued launches are withdrawn per FR-013 (partial)
- [X] T034 Cover Back to Arrangement dropping a waiting scene's tempo change in `tests/Nota.SmokeTest/CancelLaunchTests.cs` per FR-009 (unrequested)
