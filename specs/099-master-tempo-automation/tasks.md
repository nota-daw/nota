---

description: "Tasks for tempo as master-track automation (#93)"
---

# Tasks: Tempo as master-track automation

**Input**: Design documents from `specs/099-master-tempo-automation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the constitution (IV) requires engine behaviour changes to extend the
smoke test, and the spec's success criteria (SC-002, SC-004, SC-005, SC-007) are verified there.

**Organization**: Grouped by user story. Phases 1–6 are **PR 1** (tempo); Phase 7 is **PR 2**
(master pan + device parameters); Phase 8 is shared polish, done per PR.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1…US5 from spec.md)

## Path Conventions

- Engine: `src/native/nota.engine/` (ABI `include/nota/nota_engine.h`)
- Managed: `src/managed/<Project>/`
- Tests: `tests/Nota.SmokeTest/`

---

## Phase 1: Setup

**Purpose**: Fixtures and references captured before the #568 model is removed.

- [X] T001 (done in-test: `TempoMigrationTests` writes the v33 bundles itself, no fixture files) Hand-write two format-v33 project fixtures with `tempoPoints` in `tests/Nota.SmokeTest/fixtures/tempo-v33/stepped-linear-smooth.nota` (beat 0 120 step, beat 16 100 linear → beat 32 140 Bezier x1 .25 y1 0 x2 .75 y2 1 → beat 48 90, one `incomingBpm` anchor at beat 64) and `tests/Nota.SmokeTest/fixtures/tempo-v33/out-of-range.nota` (a 400 BPM point and a 10 BPM point); copy them to the smoke output in `tests/Nota.SmokeTest/Nota.SmokeTest.csproj`
- [X] T002 [P] Add an independent reference integrator of the #568 tempo shapes (step / linear / cubic Bezier with x/y handles / incomingBpm, Simpson to 1e-9 s) in `tests/Nota.SmokeTest/LegacyTempoReference.cs` — used only to compute the expected bar-start seconds of the v33 fixtures (SC-002)

---

## Phase 2: Foundational (engine + interop — blocks every story)

**Purpose**: The master Tempo lane becomes the engine's single tempo source (research R1, R2).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Add `Tempo = 6` to `AutomationTarget` in `src/native/nota.engine/src/engine/Automation.h` (comment: master only, value = BPM 20–300, played through the tempo map not `applyAutomation`) and to the managed mirror in `src/managed/Nota.Application/Engine/EngineContracts.cs`
- [X] T004 Reshape `src/native/nota.engine/src/engine/TempoMap.h`: internal point `{beat, bpm, incomingBpm, curve}`; `bpmAt` uses `AutomationLane::shape(t, curve)` between `incomingBpm`-aware endpoints (no step / Bezier); `valid()` requires first beat 0, strictly increasing beats ≥ 1e-6 apart, bpm and incomingBpm in [20, 300], curve in [-1, 1], ≤ 4096 points; add `std::vector<AutomationPoint> source` (the lane points it was compiled from); add static `fromLane(const std::vector<AutomationPoint>&)` doing: clamp values to [20, 300]; same-beat pair → one point with `incomingBpm` = left value, `bpm` = right value, `curve` = right point's curve (more than two on a beat: keep first and last); first point after 0 → prepend `{0, firstValue}`; returns nullptr for an empty lane
- [X] T005 Add `Engine::deriveTempoMap(Graph&)` in `src/native/nota.engine/src/engine/Engine.cpp`: find the Tempo lane on `g.masterTrack->automation`; if its points equal `g.tempoMap->source` (or both empty) return; else build `TempoMap::fromLane`, assign `g.tempoMap`, and reproject unwarped clips and takes to keep their seconds (move the loop from `Engine::setTempoPoints`); call it at the top of `Engine::publish` and `Engine::publishRaw` (skip when `restored` — undo/redo snapshots are already consistent); declare in `src/native/nota.engine/src/engine/Engine.h`
- [X] T006 Delete `Engine::setTempoPoints` (Engine.cpp/Engine.h) and its callers; remove `NotaTempoPoint`, `nota_tempo_point_count`, `nota_tempo_get_point`, `nota_tempo_set_points`, `NotaTempoPreview`, `nota_tempo_preview_*` from `src/native/nota.engine/include/nota/nota_engine.h` and `src/native/nota.engine/src/abi/nota_engine_core.cpp`; keep `nota_tempo_beat_to_seconds`, `nota_tempo_seconds_to_beat`, `nota_tempo_bpm_at`; fix every other engine reference (`grep -rn "NotaTempoPoint\|setTempoPoints\|incoming_bpm" src/native`)
- [X] T007 In `Engine::addAutomationLane` (`src/native/nota.engine/src/engine/Engine_Automation.cpp`) accept target 6 only when `trackId == kMasterTrackId` (else return -1); make `applyAutomation` skip Tempo lanes; document target 6 on `nota_track_add_automation_lane` in `nota_engine.h` per contracts/c-abi.md
- [X] T008 Add `nota_engine_tempo_automated` (1 when the master Tempo lane has points) in `nota_engine.h` + `src/native/nota.engine/src/abi/nota_engine_automation.cpp`; make `nota_transport_set_bpm` return `NOTA_ERR_INVALID_ARG` without change and make scene-launch tempo (`nota_session_take_scene_tempo` path in `src/native/nota.engine/src/engine/Engine_Session.cpp`) not apply while it is 1
- [X] T009 Add `nota_engine_tempo_lane_selftest` in `src/native/nota.engine/src/engine/Engine_Automation.cpp` + ABI: (1) lane 120→90 curve 0.5 → `bpmAt` equals `AutomationLane::valueAt` at 64 sample beats within 1e-9; (2) same-beat jump; (3) first point at beat 8 holds before; (4) values 10/400 clamp; (5) section move (specs/073 path) re-derives the map; (6) undo/redo restore lane+map pair; (7) an unwarped clip keeps its seconds after a tempo edit; (8) live set (`set_points_live`) re-derives; (9) removing the lane clears the map; return the failed check number
- [X] T010 Reshape managed interop: in `src/managed/Nota.Infrastructure/Interop/NotaEngine.Tempo.cs` remove `TempoPoints`, `SetTempoPoints`, `CreateTempoPreview`, `TempoPreview`, `TempoPointNative` and their P/Invokes; add `IsTempoAutomated` (P/Invoke `nota_engine_tempo_automated`) and `TempoLaneSelfTest`; in `src/managed/Nota.Application/Engine/IAudioEngine.cs` drop `TempoPoints`/`SetTempoPoints`/`CreateTempoPreview`, add `bool IsTempoAutomated { get; }`; delete `ITempoPreview` and `TempoShape`/`TempoPoint` from `src/managed/Nota.Application/Engine/TempoPoint.cs` (move a `LegacyTempoPoint` record into `src/managed/Nota.Infrastructure/Persistence/` for migration); update every fake/stub engine implementing `IAudioEngine` (`grep -rln ": IAudioEngine" src tests`)
- [X] T011 Add smoke helper `tests/Nota.SmokeTest/TempoLane.cs`: `Set(engine, params (double beat, double bpm, float curve)[])` adds/reuses the master Tempo lane and sets points; `Step(beat, from, to)` emits a same-beat pair
- [X] T012 Port `tests/Nota.SmokeTest/TempoMapTests.cs`, `TempoMapCorrectnessTests.cs`, `TempoMapRenderingTests.cs`, `TempoMapMoveTests.cs`, `TempoMapCopyTakeTests.cs`, `TempoBlockAutomationTests.cs`, `TempoFollowTests.cs` from `SetTempoPoints` to `TempoLane.Set` (step → same-beat pairs, linear → curve 0, Bezier cases → curve cases); delete checks that only exercised Bezier handles or the preview; keep every timing/rendering/warp/freeze/bounce assertion; add `Check(e.TempoLaneSelfTest() == 0, …)` in `tests/Nota.SmokeTest/Program.cs`
- [X] T013 Build engine + managed (`scripts/build.sh`) — Phase 2 green before stories

**Checkpoint**: Tempo is driven by the master lane in the engine; all ported tempo smoke checks pass.

---

## Phase 3: User Story 1 — Draw tempo changes as master automation (P1) 🎯 MVP

**Goal**: The master row has a target picker (Volume, Tempo); Tempo is edited with the shared automation editor in BPM.

**Independent Test**: spec US1 — draw 120 → 90 ramp and a 140 jump; play, export, undo/redo, save/reload.

### Tests for User Story 1

- [X] T014 [P] [US1] Smoke test in `tests/Nota.SmokeTest/MasterTempoLaneTests.cs`: draw via `SetAutomationPoints` on the master Tempo lane, render and export 30 s at 48 kHz, compare onset of a click track at each bar against `BeatToSeconds` within 1 sample (SC-004); save/load and undo/redo restore identical points and `BeatToSeconds` (SC-005); `SetBpm` fails while automated, succeeds after removing the lane (FR-009/FR-009a)

### Implementation for User Story 1

- [X] T015 [US1] Persist master lanes: add `List<AutomationLaneDto>? Automation` and (for PR 2) `float? Pan` to `MasterDto` in `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs`; save every master-track lane in `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs` (same writer as track lanes, target name `tempo` for 6) and load them with `AddAutomationLane(masterId, …)` + `SetAutomationPoints` before clips are placed; save `bpm` as the Tempo lane value at beat 0 when automated; bump `CurrentFormatVersion` to 34
- [X] T016 [US1] Generalize the master footer row in `src/managed/Nota.App/ArrangementView.Controls.cs` and `src/managed/Nota.App/ArrangementView.Automation.cs`: a `MasterLaneVM` (target, device/param address, height) replaces the volume-only `_masterAuto`; Volume keeps reading/writing `Get/SetMasterVolumeAutomation`; other targets use the master track id lane API; reuse the track lane editor (add, drag, curve handle, delete, marquee, copy/paste, Delete, `SetAutomationPointsLive` while dragging, one undo step per gesture)
- [X] T017 [US1] Target picker on the master row label, reusing the track lane-header menu builder in `src/managed/Nota.App/ArrangementView.Automation.cs`: entries Volume and Tempo (Pan and devices come in T040); label shows the current target
- [X] T018 [US1] Tempo lane presentation: BPM axis labels and hover/drag readout (two decimals) in `src/managed/Nota.App/ArrangementView.Controls.cs`; value range from `MasterLaneVM.RangeMin/RangeMax`; points outside the range drawn clamped to the edge; new points/drags clamp to [20, 300]
- [X] T019 [US1] Tempo range fields (FR-003a): two small numeric fields at the lane's top/bottom edge in `src/managed/Nota.App/ArrangementView.Controls.cs`; "min < max", both within 20–300; default on first open = project tempo ±40 clamped to 20–300 and widened to cover existing points; stored as `tempoLaneMin`/`tempoLaneMax` in the arrangement view state in `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs` / `ProjectService.cs` (absent = default); not an undo step
- [X] T020 [US1] Transport field (FR-009a) in `src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs`: `IsTempoAutomated` refreshed in the existing engine-follow tick; when true `Bpm` follows `TempoAt(playhead beat)` (no echo to `SetBpm`), `BpmEditable = false`; bind read-only + tooltip `transport.tempo_automated` in the transport view XAML; tap tempo and MIDI Learn on the field are no-ops while read-only (`src/managed/Nota.Application/TapTempo.cs` caller)
- [X] T021 [P] [US1] MCP (contracts/ui-and-mcp.md): `masterTrackId` in `get_overview` (`src/managed/Nota.Mcp/Tools/ProjectTools.cs`); target name `tempo` in `src/managed/Nota.Mcp/Tools/AutomationTools.cs` (add/list/get/set/remove on the master id); `set_tempo` in `src/managed/Nota.Mcp/Tools/TransportTools.cs` returns the error "Tempo is automated on the master track; edit its Tempo lane" while automated
- [X] T022 [P] [US1] Strings in `src/managed/Nota.Application/Localization/en.json`: `automation.target_tempo`, `transport.tempo_automated`, `tempo.range_min`, `tempo.range_max`, `project.tempo_clamped`

**Checkpoint**: Tempo can be drawn on the master and drives playback; the old row still exists until US2.

---

## Phase 4: User Story 2 — Get the arrangement space back (P1)

**Goal**: No separate tempo row; tracks start under the ruler/sections.

**Independent Test**: spec US2 — new project and a migrated #568 project show no tempo band (SC-001).

- [X] T023 [US2] Move the time-axis helpers (`TempoAxis`, `TempoSeconds`, `TempoBeat`, `AxisOriginSeconds`, `SecondsToX`, `XToSeconds`, `XToBeat`, `ScrollBeatForX`, `TimeSignatureDenominator`, `TempoBarLength`, `GridBarLength`) from `src/managed/Nota.App/ArrangementView.Tempo.cs` into new `src/managed/Nota.App/ArrangementView.TimeAxis.cs` (SPDX header); `_hasTempoMap` becomes `_engine?.IsTempoAutomated`; drop `_tempoPreview` (no preview any more) — the axis stays time-linear (FR-005a)
- [X] T024 [US2] Delete `src/managed/Nota.App/ArrangementView.Tempo.cs` and remove `BuildTempoRow`/`_tempoRow`/`_tempoLane` uses from `src/managed/Nota.App/ArrangementView.cs`, `ArrangementView.LaneControl.cs`, `ArrangementView.Overview.cs`, `ArrangementView.Controls.cs`; remove the row from the arrangement grid so the first track sits where it did before #568
- [X] T025 [US2] Remove `tests/Nota.TempoUiTest/` and its references (solution file, build scripts — `grep -rn TempoUiTest *.sln scripts .github`); move its still-relevant axis/waveform alignment checks to the headless harness check list in `specs/099-master-tempo-automation/quickstart.md`
- [X] T026 [US2] Headless UI check: new project and a project with tempo automation — first track top equals the pre-#568 value; master row showing Volume or collapsed takes no extra height (SC-001)

**Checkpoint**: Arrangement layout is back to pre-#568; tempo lives only in the master row.

---

## Phase 5: User Story 3 — Open projects made with the #568 tempo lane (P1)

**Goal**: v33 projects open with the same timing, tempo on the master lane.

**Independent Test**: spec US3 — fixtures from T001 open within 1 ms per bar (SC-002).

### Tests for User Story 3

- [X] T027 [P] [US3] Smoke test `tests/Nota.SmokeTest/TempoMigrationTests.cs`: load `stepped-linear-smooth.nota` → master Tempo lane exists, `BeatToSeconds(bar start)` within 1 ms of `LegacyTempoReference` for bars 1–200 (SC-002); load `out-of-range.nota` → values clamped to 20/300 and the clamp flag reported; a pre-#568 project (no `tempoPoints`) → no lane, plays at `bpm` (US3 #3); a malformed `tempoPoints` → no lane, stored `bpm`, no exception

### Implementation for User Story 3

- [X] T028 [US3] `src/managed/Nota.Infrastructure/Persistence/TempoMigration.cs` (SPDX header): pure conversion of legacy points to lane points per research R3 — step → same-beat pair; linear → curve 0; Bezier → split into ≤ 64 sub-segments, each fitted with the curve in [-1, 1] minimizing integrated beat→seconds error, splitting until segment error < 0.25 ms; `incomingBpm` → same-beat pair; clamp to [20, 300] and report `clamped`; invalid input → empty result
- [X] T029 [US3] Migration step v33 → v34 in `src/managed/Nota.Infrastructure/Persistence/ProjectMigrations.cs`: read `tempoPoints`; run `TempoMigration`; append `{target: "tempo", points}` to `master.automation`; set `bpm` to the first value; set root `tempoClamped` when clamped; remove `tempoPoints`
- [X] T030 [US3] One-time notice: after load, if `tempoClamped` show the info toast `project.tempo_clamped` (`src/managed/Nota.App/MainWindow.Files.cs`) and drop the flag so the next save omits it; remove `TempoPoints` from `ProjectModel.cs` and the `SetTempoPoints` load call in `ProjectService.cs`

**Checkpoint**: #568 projects open with identical timing; PR 1 functional scope complete except US5.

---

## Phase 6: User Story 5 — Ctrl-drag time-scale mapping removed (P2)

**Goal**: No tempo-only gesture remains (FR-013).

**Independent Test**: spec US5 — Ctrl-hover/drag over the arrangement changes nothing tempo-related; Shortcuts has no tempo group.

- [X] T031 [US5] Delete `src/managed/Nota.Application/Editing/TempoMapping.cs` and any remaining reference (marker-on-release, transient snap helpers used only by it) — `grep -rn "TempoMapping\|MapBar" src`
- [X] T032 [US5] Remove the "tempo.shortcuts" group (`tempo.shortcut_*`) from `src/managed/Nota.Application/Input/ShortcutLayout.cs` and every `tempo.*` string no longer used from `src/managed/Nota.Application/Localization/en.json` (keep the ones added in T022); run the `nota-shortcuts` skill checks
- [X] T033 [US5] Verify Ctrl-hover/drag on the arrangement timeline behaves as before #568 (headless harness pointer test) and that `SnapGrid`/`PhraseLines` edits from #568 that existed only for mapping are reverted (`src/managed/Nota.Application/Editing/SnapGrid.cs`, `PhraseLines.cs` — keep mapped-grid correctness fixes)

**Checkpoint**: PR 1 complete — run Phase 8 polish for PR 1, then open PR 1 (`Refs #93`).

---

## Phase 7: User Story 4 — Other master automation targets (P2) — PR 2

**Goal**: Master Pan and master device parameters are automatable (FR-012, SC-007).

**Independent Test**: spec US4 — automate master pan and a master EQ band gain; play, export, reload; delete the EQ.

### Tests for User Story 4

- [X] T034 [P] [US4] Smoke test `tests/Nota.SmokeTest/MasterAutomationTests.cs`: master pan lane −1 → 1 over 4 bars renders L/R balance per pan law; master EQ8 band gain lane audibly changes level of a sine at that band; export matches render; save/load round-trip; removing the EQ removes its lane and undo restores both; master pan knob value persists (`master.pan`)

### Implementation for User Story 4

- [X] T035 [US4] `applyAutomation` walks `g->masterTrack->automation` in `src/native/nota.engine/src/engine/Engine_Automation.cpp`: Pan → master pan atomic; DeviceParam / PluginParam / nested rack lanes as for tracks (share the per-lane body via a helper); Tempo skipped
- [X] T036 [US4] Master pan stage in `src/native/nota.engine/src/engine/Engine_Render.cpp`: after the master device chain, before the master fader, using `Graph::panLaw` and the existing pan-smoothing step; value in the master `Track` pan (`setPan`), allocation-free
- [X] T037 [US4] ABI `nota_engine_set_master_pan` / `nota_engine_master_pan` (−1..1; a plain set, undo via the mix gesture as for master volume) in `nota_engine.h` + `src/native/nota.engine/src/abi/nota_engine_core.cpp`; extend `nota_engine_tempo_lane_selftest` or add `nota_engine_master_automation_selftest` covering master lanes in `applyAutomation`
- [X] T038 [US4] Managed: `MasterPan` get/set in `src/managed/Nota.Infrastructure/Interop/` and `IAudioEngine.cs`; persist `MasterDto.Pan` (absent = 0) in `ProjectService.cs`
- [X] T039 [P] [US4] Mixer master strip pan knob like a track strip's in `src/managed/Nota.App/MixerView.cs` and `MixerView.Compact.cs` (follow DESIGN.md / nota-design); automation-touch gesture selects the master Pan lane like track knobs do
- [X] T040 [US4] Picker entries: Pan and one submenu per master device (built-in params; plug-ins via the plugin-param path) in `src/managed/Nota.App/ArrangementView.Automation.cs`; master device knob touches in automation mode select their master lane (`OnAutomationTouched` handles the master id)
- [X] T041 [US4] Device removal on the master drops its lanes and undo restores them — reuse the track rule in `src/native/nota.engine/src/engine/Engine_Devices.cpp` (verify it already applies via `findTrackAuthoring(kMasterTrackId)`; fix if not)
- [X] T042 [P] [US4] MCP: master `pan`/`device`/`plugin` targets on `masterTrackId` in `src/managed/Nota.Mcp/Tools/AutomationTools.cs`; a `set_master_pan` tool next to `set_master_volume`

**Checkpoint**: PR 2 complete — run Phase 8 polish for PR 2, then open PR 2 (`Closes #93`).

---

## Phase 8: Polish & Cross-Cutting Concerns (per PR)

- [X] T043 [P] `CHANGELOG.md` `[Unreleased]`: PR 1 — Changed "Tempo is now automation on the master track…", Removed "the separate tempo row and Ctrl-drag time-scale mapping"; PR 2 — Added "master pan and master device automation"
- [X] T044 [P] `FEATURES.md` via the `nota-features` skill (replace the #568 tempo-track lines)
- [X] T045 [P] User docs via the `nota-docs` skill in `../nota-docs` (EN + RU): rewrite the tempo-track pages from #568 as "Tempo automation on the master", automation page gets master targets, shortcuts page regenerated; new screenshots
- [X] T046 Performance gate locally (memory: Nota perf gate local) — fixed-tempo scenarios unchanged (SC-006); time map re-derivation for 256 points < 2 ms
- [X] T047 Run `specs/099-master-tempo-automation/quickstart.md` manual/headless steps; capture before/after screenshots for the PR via the headless harness
- [ ] T048 `scripts/build.sh` green (smoke incl. all new tests); then `/speckit-converge`

---

## Dependencies & Execution Order

- **Phase 1** → **Phase 2** (T002 is independent of T001).
- **Phase 2** blocks all stories (T003 → T004 → T005 → T006/T007/T008 → T009; T010 after T006/T008; T011 → T012; T013 last).
- **US1 (Phase 3)** after Phase 2. T015 before T016–T019; T020, T021, T022 independent of the UI tasks.
- **US2 (Phase 4)** after US1's T016 (the master row must host Tempo before the old row goes).
- **US3 (Phase 5)** after T015 (master lane persistence) — independent of US1 UI and US2.
- **US5 (Phase 6)** after US2's T024 (gesture code lives in the deleted file).
- **US4 (Phase 7)** after PR 1 is merged (or rebased on it); independent of US3/US5.
- **Phase 8** per PR.

## Parallel Opportunities

- Phase 1: T001 ‖ T002.
- Phase 3: T014 ‖ T021 ‖ T022 while T015–T020 proceed.
- Phase 5: T027 written in parallel with T028.
- After Phase 4: US3 (T027–T030) ‖ US5 (T031–T033).
- Phase 7: T034 ‖ T039 ‖ T042 once T035–T038 land.
- Phase 8: T043 ‖ T044 ‖ T045.

## Implementation Strategy

1. **MVP = Phases 1–3**: tempo drawn on the master lane drives playback (the core complaint's
   fix for smooth transitions).
2. Phases 4–6 complete PR 1 (space back, migration, gesture removed) — ship PR 1.
3. Phase 7 = PR 2 (master pan + devices) — closes #93.
