---
description: "Tasks for specs/100 — automate the Nota Rhythm pattern bank (#609)"
---

# Tasks: Automate the Nota Rhythm pattern bank

**Input**: Design documents from `/specs/100-rhythm-bank-automation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Yes — smoke-test coverage for lane timing, Pattern mode, old blobs and Twin (SC-001–SC-004).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Foundational — exact-time lane reads

- [X] T001 Add `ParamLaneSteps` (fixed table of a lane's value per step grid beat for one block) in `src/native/nota.engine/src/devices/ParamLaneSteps.h` (AGPL header)
- [X] T002 Declare `Instrument::pluginParamLane(index, lane, beatFrom, beatTo)` (no-op default) in `src/native/nota.engine/src/devices/Instrument.h`, per `contracts/instrument-param-lane.md`
- [X] T003 Pass the block's end beat into `Engine::applyAutomation` and call `pluginParamLane` for instrument plugin-param lanes (track + nested) in `src/native/nota.engine/src/engine/Engine_Automation.cpp`, `Engine.h`, `Engine_Render.cpp`

## Phase 2: User Story 1 + 3 — Bank param, exact switching, old projects (P1) 🎯 MVP

- [X] T004 [US1] Append `bank` / `bank_switch` params (ids, names, text, RTH4 magic) in `src/native/nota.engine/src/devices/instruments/RhythmMachine.h/.cpp`
- [X] T005 [US1] Replace `bank_` with the param (edit bank) and an audio-thread playing bank; Step / Pattern switching at the step with lane lookup; scope `S_Bank` = playing bank; `A_SelectBank` sets the param
- [X] T006 [US3] Load RTH1–RTH3: `bank` from the bank byte, `bank_switch` = Step; write the byte from the param; clone copies the playing bank
- [X] T007 [P] [US1] `RhythmModel`: `Bank` / `BankSwitch` ids, RTH4 parse (RTH3 = 93 params), `BankNorm` / `BankOf`, guide text in `src/managed/Nota.Application/Instruments/RhythmModel.cs`
- [X] T008 [US1] Smoke: param count 95, names under Perform, text A–D / Step / Pattern; lane A→B on a bar line plays B from the downbeat at an odd block size; chunked vs whole render identical; Pattern mode holds a mid-bar change to the next bar; RTH3 blob opens on its bank (`tests/Nota.SmokeTest/Program.cs`)

## Phase 3: User Story 2 — the card follows and drives the bank (P1)

- [X] T009 [US2] Bank buttons write `bank` as a discrete control edit (Begin/Set/End); the card follows the param and the playing bank (scope), tints a queued bank; Bank Switch radio items in the bank menu; EN strings in `src/managed/Nota.App/DeviceCards/Instruments/RhythmInstrumentCard.cs` + `Nota.Application/Localization/en.json` (RU lives in nota-translations)
- [X] T010 [P] [US2] MCP: `select_rhythm_bank` sets the param; snapshot reports playing bank and switch mode in `src/managed/Nota.Mcp/Tools/RhythmTools.cs`

## Phase 4: User Story 4 — Nota Twin pattern lane on time (P2)

- [X] T011 [US4] Twin reads `GSeqPattern` through `ParamLaneSteps` at step onsets while playing in `src/native/nota.engine/src/devices/instruments/Twin.h/.cpp`
- [X] T012 [US4] Smoke: a Twin Pattern lane changing on the bar that ends pattern 1 starts pattern 2 on that bar (`tests/Nota.SmokeTest/Program.cs`)

## Phase 5: Polish

- [X] T013 [P] CHANGELOG `[Unreleased]` entry and FEATURES.md (nota-features skill)
- [X] T014 [P] User docs: Rhythm page EN + RU in `../nota-docs` (nota-docs skill)
- [X] T015 Visual check of the card (bank buttons playing / queued) in the headless harness; screenshots for the PR
- [ ] T016 `scripts/build.sh` green (smoke test included)
- [ ] T017 Open the PR (nota-pr skill), `Closes #609`

## Phase 6: Converge (found while implementing)

- [X] T018 Rhythm rendered in pieces (the engine splits a block at note events) restarted its step clock at the block start for every piece and re-fired the block's first steps; advance the clock per piece (`RhythmMachine::advanceClock`) and drop a block's lane samples at its end, not after the first piece (`ParamLaneSteps::rendered` / `transport`). Smoke: unmapped MIDI notes splitting blocks leave the render unchanged.
- [X] T019 MCP `set_rhythm_perform` takes `bankSwitch` (step | pattern) in `src/managed/Nota.Mcp/Tools/RhythmTools.cs`
- [X] T021 Review (PR #653): lane hooks run after every block-start value is set, so a Twin Pattern lane samples the grid of a Rate lane added after it (`Engine::applyAutomation` second pass; smoke: both lane orders)
- [X] T022 Review: Rhythm's playing bank and last step are atomics and carry into `clone()`; Pattern mode keeps a queued bank across a forward gap inside the cycle (smoke: a sample load mid-pattern keeps the queue)
- [X] T023 Review: `ParamLaneSteps` holds 512 steps (any block at 999 BPM, 1/32, ≥ 4.3 kHz) and samples a lane in one pass
- [X] T024 Review: stepped params reach the lane editor — `nota_plugin_param_steps` / `nota_plugin_param_step_text`, `Instrument::pluginParamSteps` / `pluginParamStepText` (Rhythm Bank A–D, Bank Switch, Twin's stepped params); the lane draws named levels, snaps points to them and draws segments as the stairs they play
- [X] T025 Review round 2: a transport jump (seek, loop wrap) is detected in `RhythmMachine::setTransport` and makes the next step take the destination's bank in Pattern mode; a replacement copy never reads as a jump (smoke: forward seek inside the cycle)
- [X] T026 Review round 2: the lane editor bends with the unsnapped pointer height (points still snap when placed or moved), hit-tests the stairs it draws, and draws them from the level crossings (`AutomationCurve.InverseShape`) instead of per-pixel sampling
- [X] T027 Review round 3: a click while automation records (a write that ends before poll() samples it) records a step at the playhead — the value before, then the new one — in `Engine::endAutomationWrite` (every discrete control gains it); smoke: a bank click while recording writes and plays back
- [ ] T020 RU strings for `device.rhythm.switch_at_*` and the updated bank tooltip in nota-translations (`languages/ru.json`) — follow-up, as for earlier features

## Dependencies

T001–T003 → T004–T006 → T008; T007 ∥ T004; T009 after T004–T007; T011 after T001–T003; T016 last before T017.

## Nota Compliance (re-check)

- [X] AGPL header on `ParamLaneSteps.h`
- [X] No allocation / lock on the audio thread in the hook or the step lookup
- [X] C ABI unchanged
- [X] CHANGELOG / FEATURES / docs updated
