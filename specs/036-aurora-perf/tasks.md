# Tasks: Aurora CPU Optimization

**Input**: Design documents from `/specs/036-aurora-perf/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: YES — explicitly requested (FR-003/FR-004 null tests, FR-005 benchmark; spec acceptance scenarios are test tasks).

**Organization**: Grouped by user story. Execution order differs from priority order on purpose: US3 (benchmark) and US2 (null tests) are measurement prerequisites for US1 (optimization) — see Dependencies.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Reference environment recorded, toolchain verified on the x86 Linux reference machine

- [X] T001 [P] Record reference machine config (CPU model, audio backend, bufferFrames, sample rate) in `specs/036-aurora-perf/baseline-env.md`
- [X] T002 [P] Verify toolchain: build `tests/perf` (`cmake -G Ninja -S tests/perf -B build/perf-tools -DCMAKE_BUILD_TYPE=Release`) and `dotnet build Nota.sln -c Release` green on unmodified code

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared Super Saw preset param map used by both the benchmark (US3) and the null tests (US2)

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Extract Super Saw factory preset param id→value map from `src/managed/Nota.Infrastructure/Persistence/FactoryPresetCatalog.cs` into `specs/036-aurora-perf/supersaw-params.md`

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 3 - Repeatable performance measurement (Priority: P2) 🔧 MEASURE FIRST

**Goal**: One-command benchmark of the reference scenario on x86 Linux, reproducible within ±10%

**Independent Test**: Run `nota_perf run aurora-supersaw-2x` twice on an idle machine; both lines agree within ±10% with non-zero peak (spec US3 acceptance)

### Tests for User Story 3

> Benchmark IS the test artifact for this story — no separate test file.

### Implementation for User Story 3

- [X] T010 [US3] Implement `aurora-supersaw-2x` scenario in `tests/perf/nota_perf.cpp` per `specs/036-aurora-perf/contracts/benchmark-scenario.md` (two kind-5 tracks, params via `nota_plugin_param_set`, 6-voice clips, 256-frame blocks, same output schema)
- [X] T011 [US3] Run scenario twice on reference machine, verify ±10% agreement and non-silent peak, record baseline `wall_ms_per_s` in `specs/036-aurora-perf/baseline-env.md` (depends on T010)
- [X] T012 [P] [US3] Profile with `perf record -g` and confirm the `research.md` R3 hotspot ranking, record top symbols in `specs/036-aurora-perf/baseline-env.md` (depends on T010; runs after T011 to avoid distorting its numbers)

**Checkpoint**: Baseline numbers + confirmed hotspots — US1/US2 work is now provable

---

## Phase 4: User Story 2 - Existing Aurora sound is preserved (Priority: P1)

**Goal**: Null-test suite over all factory Aurora presets; green on unmodified code

**Independent Test**: Render presets offline before/after a change and compare (spec US2 acceptance); suite passes on the unmodified engine

### Tests for User Story 2

> The suite itself is the deliverable — write it first, watch it pass on unmodified code.

### Implementation for User Story 2

- [X] T020 [US2] Create `tests/Nota.SmokeTest/AuroraNullTests.cs` reusing the `NullDb` helper pattern from `tests/Nota.SmokeTest/PanLawTests.cs`: render every factory Aurora preset offline, assert determinism (double render `< -120 dB`) and persist reference renders with per-preset residual report and parameterized threshold (default −120 dB)
- [X] T021 [US2] Run `AuroraNullTests` on unmodified code until green; commit reference renders (depends on T020)
- [X] T022 [US2] Document the Tier-1 (`< -120 dB`) vs Tier-2 (`< -50 dB`, revert otherwise) gates in `specs/036-aurora-perf/quickstart.md` step 3 (depends on T020)

**Checkpoint**: Sound-preservation gate exists and passes — every later DSP edit is verified against it

---

## Phase 5: User Story 1 - Dropout-free playback, Tier-1 hoisting (Priority: P1) 🎯 MVP

**Goal**: Remove block-constant recomputation from the sample loop; bit-identical output; measured speedup

**Independent Test**: Reference passage plays without dropouts; null suite `< -120 dB`; benchmark `wall_ms_per_s` lower than Phase 3 baseline

### Implementation for User Story 1 (Tier-1)

> Same-file edits — run sequentially, one commit per task, null suite green after each.

- [X] T030 [US1] Hoist block-constant unison data in `src/native/nota.engine/src/devices/instruments/WavetableSynth.cpp`: precompute detune factors, pan gains and `norm` once per block for `oscStack` call sites (`render()` lines ~466-469, `oscStack()` ~649-678); null suite must stay `< -120 dB`
- [X] T031 [US1] Early-out unmodulated pitch (`pMul=1` skips `exp2`), hoist chorus `expMap(rate)` to block rate, compute mono reverb once in `src/native/nota.engine/src/devices/instruments/WavetableSynth.cpp`; null suite must stay `< -120 dB` (depends on T030)
- [X] T032 [US1] Re-run benchmark + null suite, record improvement vs baseline in `specs/036-aurora-perf/baseline-env.md`; verdict whether p99 is under 50% budget — if YES, skip Phase 6 (depends on T031)

**Checkpoint**: MVP complete — provably faster, provably identical sound. STOP and VALIDATE before Tier-2.

---

## Phase 6: User Story 1 - Tier-2 fast-math (Priority: P1, CONDITIONAL)

**Goal**: Swap remaining transcendentals for approximations ONLY if Phase 5 did not close the budget; each step revertible at `< -50 dB`

**Independent Test**: Same as Phase 5, with residual gate `< -50 dB` per step (FR-004)

### Implementation for User Story 1 (Tier-2)

> Execute ONLY if T032 verdict says the budget is still open. Same-file edits — sequential, one commit per task, revert the task on gate failure.

- [X] T040 [US1] SKIPPED per T032 verdict (budget closed by Tier-1) — was: Replace `std::tan` with `fast_math::tanPrewarp` in `setSvf` in `src/native/nota.engine/src/devices/instruments/WavetableSynth.cpp`; null suite `< -50 dB` or REVERT
- [X] T041 [US1] SKIPPED per T032 verdict (budget closed by Tier-1) — was: Replace pitch `std::exp2` with `fast_math::exp2f` in `src/native/nota.engine/src/devices/instruments/WavetableSynth.cpp`; null suite `< -50 dB` or REVERT (depends on T040)
- [X] T042 [US1] SKIPPED per T032 verdict (budget closed by Tier-1) — was: Replace LFO/chorus `std::sin` with LUT+lerp in `src/native/nota.engine/src/devices/instruments/WavetableSynth.cpp`; null suite `< -50 dB` or REVERT; sub-oscillator sine stays `std::` (depends on T041)
- [X] T043 [US1] SKIPPED per T032 verdict (budget closed by Tier-1) — was: Convert `readMorph` to float without `floor` in `src/native/nota.engine/src/devices/instruments/WavetableSynth.h`; null suite `< -50 dB` or REVERT (depends on T042)
- [X] T044 [US1] SKIPPED per T032 verdict (budget closed by Tier-1) — was: Re-run benchmark + `scripts/perf-gate.sh` base-vs-feature, record in `specs/036-aurora-perf/baseline-env.md` (depends on T040-T043)

**Checkpoint**: Budget closed with inaudible changes, or documented evidence for the drawing board (Eco stays out of scope)

---

## Phase 7: User Story 1 - Engine playback robustness (Priority: P1)

**Goal**: No stutter by design on slower machines (FR-008, US1 acceptance scenario 2)

**Independent Test**: Reference scenario plays cleanly from the first seconds on the reference machine; cold-start playback shows no serial-first ramp-up penalty

### Implementation for User Story 1 (engine)

- [X] T050 [P] [US1] Engage `rtParallel_` without waiting for first overload (≥2 instrument tracks) in `src/native/nota.engine/src/engine/Engine_Render.cpp` — MOVED to follow-up branch `036-aurora-perf-engine` per PR review (needs small-buffer gating + own benchmark)
- [X] T051 [P] [US1] Ensure effective default buffer ≥256 frames on Linux in `src/native/nota.engine/src/audio_io/desktop/MiniaudioBackend.cpp` (document `audio.json` setting) — DROPPED per PR review (miniaudio default is already ~10 ms/480 frames; premise unmeasured, Windows untested)
- [X] T052 [US1] Verify cold-start playback + full benchmark on reference machine; record in `specs/036-aurora-perf/baseline-env.md` (depends on T050, T051)

**Checkpoint**: US1 fully functional — dropout-free playback including session start

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: PR gates, docs, traceability

- [X] T060 [P] Add `CHANGELOG.md` `[Unreleased]` entry describing the user-facing effect (dropout-free heavy Aurora arrangements, no sound change)
- [X] T061 [P] AGPL-3.0-only SPDX two-line header on every new source file (`tests/Nota.SmokeTest/AuroraNullTests.cs`; benchmark scenario needs none — same file)
- [X] T062 Run `scripts/build-linux.sh` until native + managed + smoke test are green (run alone — shares the machine with perf runs)
- [X] T063 Run `scripts/perf-gate.sh` base-vs-feature; confirm `aurora-supersaw-2x` improved and nothing else regressed (depends on T062; run alone) — NOTE: valgrind/ccache absent on this machine so the script cannot run here (it exits 2); equivalent wall-time base-vs-feature comparison done via `nota_perf` runs and recorded in `baseline-env.md` (71.4→27.5 ms/s); CI runs the full Callgrind gate
- [X] T064 Run `specs/036-aurora-perf/quickstart.md` end to end as final validation (depends on T062, T063)

### Nota closing tasks *(PR gates)*

- [X] T065 Confirm no new dependency (or license check + `LICENSES/third-party.md` row); none expected → mark N/A with justification
- [X] T066 Confirm no user-visible capability change → FEATURES.md / user docs N/A (invisible optimization); `nota-docs`/`nota-features` skills only if anything surfaced
- [X] T067 Engine behaviour change covered by `AuroraNullTests` in `tests/Nota.SmokeTest` (closes the smoke-test gate)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup - BLOCKS all user stories (shared preset param map)
- **US3 Benchmark (Phase 3)**: After Foundational - BLOCKS US1 verification (no baseline, no proof)
- **US2 Null tests (Phase 4)**: After Foundational - BLOCKS US1 DSP edits (no sound gate, no edits)
- **US1 Tier-1 (Phase 5, MVP)**: After US3 + US2 harnesses green
- **US1 Tier-2 (Phase 6)**: After Phase 5 AND only if T032 says budget still open
- **US1 Engine (Phase 7)**: After Foundational; independent of Tier-1/Tier-2 files — may run parallel to Phases 5-6 if staffed (different files)
- **Polish (Phase 8)**: After all included phases complete

### User Story Dependencies

- **US3 (P2)**: No story dependencies — but executes FIRST (measurement prerequisite)
- **US2 (P1)**: No story dependencies — executes SECOND (sound gate prerequisite)
- **US1 (P1)**: Depends on US3 + US2 harnesses; delivers the user-facing fix

### Within Each Story

- Same-file DSP tasks (T030→T032, T040→T044) run SEQUENTIALLY with a null-suite run after each — never parallel (same file, ordered commits, revert discipline)
- Perf-number runs (T011, T012, T044, T052, T063) never overlap each other — machine must be idle

### Parallel Opportunities

- T001 + T002 (different files, no deps)
- T011 + T012 after T010 completes (different outputs; T012 after T011 to keep numbers clean)
- T050 + T051 (different files: `Engine_Render.cpp` vs `MiniaudioBackend.cpp`)
- T060 + T061 (different files)
- Phase 7 may overlap Phases 5-6 when staffed (disjoint file sets)

---

## Parallel Example: Setup

```bash
# Launch together (different files, no dependencies):
Task: "Record reference machine config in specs/036-aurora-perf/baseline-env.md"
Task: "Verify toolchain: build tests/perf and Nota.sln green on unmodified code"
```

## Parallel Example: Engine robustness (Phase 7)

```bash
# Launch together (different files, no dependencies):
Task: "Engage rtParallel_ early in src/native/nota.engine/src/engine/Engine_Render.cpp"
Task: "Effective default buffer >=256 frames in src/native/nota.engine/src/audio_io/desktop/MiniaudioBackend.cpp"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 (Setup) + Phase 2 (Foundational)
2. Complete Phase 3 (US3 benchmark + baseline + confirmed hotspots)
3. Complete Phase 4 (US2 null suite green on unmodified code)
4. Complete Phase 5 (US1 Tier-1 hoisting)
5. **STOP and VALIDATE**: dropout-free? p99 < 50% budget? null `< -120 dB`? If YES — MVP ships, Phases 6-7 are follow-up PRs.

### Incremental Delivery

1. Setup + Foundational → shared vocabulary ready
2. + US3 → numbers exist (prove the problem)
3. + US2 → sound gate exists (prove no regression)
4. + US1 Tier-1 → fix proven (MVP)
5. + US1 Tier-2 (conditional) → further headroom, same gates
6. + US1 Engine → cold-start robustness
7. Each increment is independently testable and shippable as its own PR (Constitution V)

### Parallel Team Strategy

1. Team completes Setup + Foundational together
2. Developer A: US3 benchmark → US1 Tier-1/Tier-2 (same DSP area)
3. Developer B: US2 null suite → US1 engine tweaks (disjoint files from A after Phase 4)
4. Integrate at Phase 8 gates

---

## Notes

- [P] tasks = different files, no dependencies (perf runs excluded even when file-disjoint — idle-machine rule)
- [Story] label maps task to spec user story for traceability
- Each DSP task carries its revert/measurement criterion inline — no separate test tasks needed beyond the harnesses
- Commit after each task; stop at any checkpoint to validate independently
- Task count: 25 (Setup 2, Foundational 1, US3 3, US2 3, US1 Tier-1 3, US1 Tier-2 5, US1 Engine 3, Polish 5)
