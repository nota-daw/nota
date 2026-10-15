---

description: "Task list for 016 Undo for Device Parameters and Arrangement Sections"
---

# Tasks: Undo for Device Parameters and Arrangement Sections

**Input**: Design documents from `/specs/016-device-param-section-undo/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/c-abi.md](contracts/c-abi.md), [quickstart.md](quickstart.md)

**Tests**: requested (FR-010, constitution IV) in one new file,
`tests/Nota.SmokeTest/ParamSectionUndoTests.cs`.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [x] T001 Build the untouched tree (engine + managed + smoke, `CI=true`) and confirm green

## Phase 2: Foundational

- [x] T002 `Engine.h`: `MixKind` += DeviceParam 3 / MidiDeviceParam 4 / PluginParam 5; `MixTarget`, `MixChange` += `deviceIndex`, `paramIndex` (default −1, appended); update the undo comment
- [x] T003 `Engine.cpp`: `mixValue` / `applyMix` for the three kinds with existence checks; `mergeMix` matches all four fields
- [x] T004 `nota_engine.h` / `nota_engine_core.cpp`: `NOTA_MIX_*` 3–5, `NotaMixTarget.device_index/param_index`, copy them in `nota_mix_gesture_begin`; undo comment
- [x] T005 [P] Managed: `MixKind`, `MixTarget(Kind, TrackId, DeviceIndex = -1, ParamIndex = -1)` + `DeviceParam/MidiDeviceParam/PluginParam` helpers in `EngineContracts.cs`
- [x] T006 Create `tests/Nota.SmokeTest/ParamSectionUndoTests.cs` + `Program.cs` wiring (full run + `--paramundo`)

## Phase 3: US1 + US2 — knob undo, one history (P1) 🎯

- [x] T007 [US1] `Engine_Automation.cpp`: unlatched `beginAutomationWrite` on a parameter target opens a touch gesture, `endAutomationWrite` closes it; `clearUndoHistory` drops touches
- [x] T008 [P] [US1] `GenericMidiBody` knobs and `ArpGrid` edits send the touch
- [x] T009 [P] [US1] `DeviceChainView.Shell` A/B restore: one multi-target gesture
- [x] T010 [P] [US1] `ValueEntry`: restore the start value before opening the gesture
- [x] T011 [US1/US2] Smoke: G1–G7 on an effect, a built-in instrument and a MIDI effect; interleave with note edits

## Phase 4: US3 — sections (P1)

- [x] T012 [US3] `Graph.h`: `Section`, `Graph::sections`, `inheritGraphLevel`
- [x] T013 [US3] `Engine`: `setSections` / getters; C ABI per contract
- [x] T014 [P] [US3] Managed: `SectionInfo`, `IAudioEngine.Sections` / `SetSections`, P/Invokes
- [x] T015 [US3] Persistence: `ProjectDocument.Sections`, capture, apply (checkpoint 0), legacy sidecar read when absent, sidecar removed on save
- [x] T016 [US3] `ArrangementView.Sections.cs`: commit per edit, `ReloadSections()`, drop sidecar code; duplicate / delete-all as one commit
- [x] T017 [US3] `MainWindow`: reload sections after undo / redo / load / new; drop sidecar calls
- [x] T018 [US3] Smoke: create / rename / move / delete undo + redo, interleaved with notes; save → load; legacy sidecar migrated; load / reset leave no step

## Phase 5: US4 — other sources (P2)

- [x] T019 [P] [US4] `MidiLearnService`: parameter targets through `MixTouch`
- [x] T020 [P] [US4] Remote: macro and XY touches through the per-touch gesture
- [x] T021 [US4] MCP: `Mutate` in an undo group; single-param tools via `MixEdit.Step`; `list_sections` / `set_sections`
- [x] T022 [US4] Smoke: MIDI movement (simulated clock) and MCP tools are one step each; MCP sections round-trip

## Phase 6: Polish

- [x] T023 [P] CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), user docs EN + RU (nota-docs)
- [x] T024 Full build + smoke (`CI=true`) green; re-check the compliance list
