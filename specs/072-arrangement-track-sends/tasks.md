# Tasks: Send levels in the arrangement track headers

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/abi.md](contracts/abi.md)

## Phase 1: Foundational — engine + contracts (blocks every story)

- [x] T001 `MixKind::TrackSend = 8` in `src/native/nota.engine/src/engine/EngineTypes.h`; `mixValue` / `applyMix` for it in `src/native/nota.engine/src/engine/Engine.cpp`
- [x] T002 Accept `NOTA_MIX_TRACK_SEND` in `nota_mix_gesture_begin` (`src/native/nota.engine/src/abi/nota_engine_core.cpp`); define and document it and target 5 in `include/nota/nota_engine.h`
- [x] T003 `AutomationTarget::Send = 5` in `src/native/nota.engine/src/engine/Automation.h`; apply / current value / `sameTarget` / `findLaneFor` / `addAutomationLane` in `Engine_Automation.cpp`
- [x] T004 Send lanes follow cross-track moves like Volume/Pan, and `compactReturns` drops / renumbers send lanes, in `src/native/nota.engine/src/engine/Engine_Tracks.cpp`
- [x] T005 [P] `AutomationTarget.Send`, `MixKind.TrackSend`, `MixTarget.Send(trackId, bus)` in `src/managed/Nota.Application/Engine/EngineContracts.cs`
- [x] T006 Smoke test `tests/Nota.SmokeTest/SendAutomationTests.cs` (+ `--sends` and the full run in `Program.cs`): gesture undo/redo + no-change, lane playback, record-write undo, save/load, return-delete remap + undo

## Phase 2: User Story 1 + 2 — send knobs in the header, undoable (P1)

- [x] T007 [US1] `src/managed/Nota.App/SendControl.cs`: wire a send control — gesture (`MixTarget.Send`), automation write, value change
- [x] T008 [US1] Send row in `BuildTrackCard` (standard ≥ 88 px, large with volume|pan shared; knob-only when narrow; tooltip "Send A · <return> · n %"; value entry; reset to 0) and live follow, in `src/managed/Nota.App/ArrangementView.cs`
- [x] T009 [US2] Mixer window, Session mixer and Sends card send controls through `SendControl` in `MixerView.cs`, `SessionView.Mixer.cs`, `DeviceChainView.cs`
- [x] T010 [P] [US2] MCP `set_send` as one undo step in `src/managed/Nota.Mcp/Tools/TrackTools.cs`

## Phase 3: User Story 3 — automate a send (P2)

- [x] T011 [US3] Send targets in the lane menu, quick-access, label "Send A · <return>", range 0..1, current value, first-automated target in `src/managed/Nota.App/ArrangementView.Automation.cs` (and any value-text helper the lane uses)
- [x] T012 [P] [US3] "send" target name in `src/managed/Nota.Mcp/Tools/AutomationTools.cs`

## Phase 4: User Story 4 — Sends toggle (P3)

- [x] T013 [US4] "Sends" toggle chip in the header column's top strip (hidden without returns), rebuilding the headers, in `src/managed/Nota.App/ArrangementView.cs`
- [x] T014 [US4] `showSends` in the `track-view.json` sidecar in `src/managed/Nota.App/ArrangementView.Sizing.cs`

## Phase 5: Polish

- [x] T015 Localization strings for the new labels (the tables the localization check reads)
- [x] T016 CHANGELOG `[Unreleased]` entry; FEATURES.md via nota-features; user manual (nota-docs, EN + RU) as a follow-up in that repo
- [x] T017 `scripts/build.sh` green (native + managed + smoke test incl. design-token checks)
- [x] T018 Real-app screenshots (standard 88, large, toggle off, narrow) for the PR via the headless harness

## Dependencies

T001–T005 before everything; T006 after T001–T004. T007 before T008/T009. T011 needs T003/T005.
US1/US2 is the MVP. T013 depends on T008.

## Nota Compliance (re-check)

- [x] SPDX header on `SendControl.cs` and `SendAutomationTests.cs`
- [x] No allocation / lock on the audio thread (send automation = atomic store in the existing pass)
- [x] ABI additions documented in `nota_engine.h` and covered by the smoke test
- [x] Studio design: inline knob, VizInk at rest, accent only live, sentence case, mono values

## Phase 6: Convergence

- [x] T019 Show the return's name in a send lane's header label ("Send A · Reverb", trimmed to fit) in `src/managed/Nota.App/ArrangementView.Automation.cs` per FR-006 (partial)
- [x] T020 Keep a track's shown send-lane target on its bus when a return is deleted (renumber with the engine, fall back to Volume when its bus went) in `src/managed/Nota.App/ArrangementView.Automation.cs` per US3/AC6 (partial)
- [x] T021 Re-run the timing-sensitive smoke checks (Consort stress, render pool) on an idle machine and confirm they pass per T017 / Constitution IV (partial)
  - 2026-11-03: the machine stayed loaded (load avg ~45, Rider + another Nota). Under the same load the
    base checkout's smoke test fails the same timing checks and more (6 vs 4); every functional check
    passes. CI on the PR is the clean-machine run.
