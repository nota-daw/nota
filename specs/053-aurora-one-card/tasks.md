---

description: "Task list for 053-aurora-one-card"
---

# Tasks: Aurora, both wavetables on one card, and custom wavetables from samples

**Input**: Design documents from `specs/053-aurora-one-card/`. These are
[plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/) and [quickstart.md](quickstart.md).

**Prerequisites**: spec committed at 41bd30d3. FR-005 (card width) is still open; the tasks
assume **700** wide. If the answer is 1050, only T019, T020 and T024 change
(plan § Layout at 1050).

**Tests**: These are included. The constitution requires smoke-test coverage for engine
behaviour changes (IV), and SC-008 names the cases. Engine and ABI tests go in
`tests/Nota.SmokeTest/AuroraTableTests.cs` (run alone with `--aurora-tables`). UI budgets are
measured by hand (quickstart § 4).

**Organization**: Tasks are grouped by user story, so each story can be built and tested on
its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 one card · US2 import · US3 performance · US4 live follow
- Every task names its files

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The test harness, fixtures, the measurement probe, and the baseline numbers
taken on the **unchanged** code.

- [ ] T001 Add `tests/Nota.SmokeTest/AuroraTableTests.cs` (SPDX header, empty test groups for conditioning, undo, state, project, preset, rack, scope). Register the `--aurora-tables` mode in `tests/Nota.SmokeTest/Program.cs`, next to `--aurora-null` (`Program.cs:536-551`), and add the group to the full run.
- [ ] T002 [P] Add `tests/Nota.SmokeTest/AuroraFixtures.cs` and the `--aurora-fixtures <dir>` mode. It writes the quickstart § 2 files (cycle-2048, table-64x2048 with a `clm ` chunk, table-300x2048, stereo vowel-2s FLAC, long-90s, silence, broken, x.aiff) through `tests/Nota.SmokeTest/WavWriter.cs` and the engine's encoder.
- [ ] T003 [P] Add the `--aurora-ui-fixture <dir> [--builtin]` mode in `tests/Nota.SmokeTest/AuroraFixtures.cs`. For now it writes the **v27 baseline** project: 4 Auroras, Formant / Chroma, LFO 1 → Osc 1 position, LFO 2 → Osc 2 position at 2 Hz, a 4-bar 6-note chord loop. The scenario variant is added in T043.
- [ ] T004 [P] Add the opt-in UI probe `src/managed/Nota.App/Diagnostics/UiProbe.cs`: `NOTA_UI_PROBE=aurora`, fixed rings of Stopwatch ticks, p50 / p99 to stderr every 10 s, no cost when off. Hook it into the current `src/managed/Nota.App/Controls/AuroraStack.cs` `Render`, and add a "card build → first paint" mark in `src/managed/Nota.App/DeviceChainView.Instruments.cs` (`:120-126`).
- [ ] T005 On the 036 reference machine, record the baselines in `specs/053-aurora-one-card/perf-baseline.md` (format of `specs/036-aurora-perf/baseline-env.md`): `nota_perf run aurora-supersaw-2x 30` twice, plus `pidstat` UI-thread %CPU, paint p99 and card build time with the T003 baseline fixture on the unchanged card (quickstart § 4 step 1).

**Checkpoint**: baselines recorded. Every later number is compared against them.

---

## Phase 2: Foundational (Blocking Prerequisites for US2, US3, US4)

**Purpose**: The engine table model, the hand-over to the audio thread, the state blob, the
C ABI and interop. US1 does not need this phase and can start right after Phase 1.

- [ ] T006 Create `src/native/nota.engine/src/devices/instruments/AuroraTable.h` and `.cpp` (SPDX header): the immutable `AuroraTable` and `AuroraTableSet` types from data-model.md, with a serial counter. Add the .cpp to `src/native/nota.engine/CMakeLists.txt` (list at `:171`).
- [ ] T007 Implement `AuroraTable::buildFromFile` in `AuroraTable.cpp`:
  - `decodeAudioFile` (`src/media/AudioFile.cpp:73`), mono mix, the 60 s cap;
  - classification (a) `clm ` chunk or a multiple of 2048, at most 256 frames, (b) at most
    4096 samples as one cycle by FFT resample, (c) 16 zero-crossing-aligned windows
    (research R3);
  - typed errors (contracts/c-abi.md).
- [ ] T008 Implement conditioning in `AuroraTable.cpp`: DC removal per frame, the 32-sample equal-power seam blend for (c), per-table normalisation to peak 1.0, refusal below −60 dBFS, and `buildFromFrames` (no re-conditioning).
- [ ] T009 Implement mip levels k = 0…5 by FFT truncation with signalsmith-linear `RealFFT` (as in `src/dsp/primitives/SpectrumAnalyzer.h:18`), and the 128-bit content hash of the base level, in `AuroraTable.cpp`.
- [ ] T010 In `src/native/nota.engine/src/devices/instruments/WavetableSynth.h/.cpp`, add:
  - a `SnapshotHistory<const AuroraTableSet>` member and `std::atomic<uint8_t> useCustom_[2]`;
  - a `RenderScope` in `render()`, and a `live()` read next to the block snapshot (`WavetableSynth.cpp:330-345`);
  - `clone()` (`:246-252`) sharing the set and flags;
  - message-thread setters `setTable`, `clearTable`, `setSource`, `setMissing`.
- [ ] T011 In `WavetableSynth.h/.cpp`, add `readMorphSized` and `oscStackCustom`, with the mip pick per voice per block (unison detune and warp bias, research R4). Call it only when `useCustom_` is set and a table is present; leave the built-in `oscStack` / `readMorph` calls at `:497-500` untouched.
- [ ] T012 In `WavetableSynth.cpp`, implement the state trailer v1 in `getState` / `setState` (`:227-244`) per contracts/state-blob.md, with an `includeTables` flag. A blob with no table, flag or missing name stays byte-identical.
- [ ] T013 In `WavetableSynth.h/.cpp`, implement `scopeRead` (5 floats, data-model.md § Scope telemetry, the Keys pattern `Keys.h:199-215`): relaxed atomics written once per block, holding the played position of the most recently started sounding voice.
- [ ] T014 In `src/native/nota.engine/src/engine/Engine.h` and `Engine_Devices.cpp`, add:
  - `auroraSetTable` (checkpoint → clone + `republishWithTrack`; otherwise in place through the history), `auroraClearTable`, `auroraSetSource`, `auroraSetMissing`;
  - `auroraTableInfo`, `auroraTableRead`, `auroraTablePreview`, `auroraState`.
  Model them on `setRhythmVoiceSample` (`Engine_Devices.cpp:328-343`).
- [ ] T015 Declare the contracts/c-abi.md functions in `src/native/nota.engine/include/nota/nota_engine.h`. Implement the builders in `src/native/nota.engine/src/abi/nota_engine_import.cpp` and the track functions in `src/native/nota.engine/src/abi/nota_engine_track.cpp`.
- [ ] T016 [P] Add managed interop:
  - `src/managed/Nota.Infrastructure/Interop/NativeMethods.Tracks.cs` (`[LibraryImport]`, `NotaAuroraTableInfo` struct);
  - `NotaEngine.Tracks.cs` wrappers, with a `SafeHandle` for `NotaAuroraTable*`;
  - `src/managed/Nota.Application/Engine/IAudioEngine.cs` members;
  - any test fakes that implement `IAudioEngine`.
- [ ] T017 In `tests/Nota.SmokeTest/AuroraTableTests.cs`, add the regression tests: an Aurora with no table gives a byte-identical `getState` (compare against the 156-float layout); a factory preset after the change still passes `--aurora-null` (`AuroraNullTests.cs`).

**Checkpoint**: the engine can hold, play, save and report a custom table. Built-in sound is bit-identical.

---

## Phase 3: User Story 1 — See and shape both oscillators at once (Priority: P1) 🎯 MVP

**Goal**: The Osc tab shows both oscillators with their own window, selectors and knobs. There is no 1 / 2 switch.

**Independent Test**: quickstart § 1. Glacier Pad shows both tables, both type and warp selectors and 2 × 6 knobs with zero clicks; editing one leaves the other unchanged; old projects show the same values.

- [ ] T018 [US1] Create `src/managed/Nota.App/Controls/AuroraTableView.cs` (SPDX header):
  - a `NotaGraph` window whose title row carries the caption;
  - the built-in source drawn from `AuroraStack`'s shared shape cache (keep the cache in `Controls/AuroraStack.cs`, remove its view role);
  - one `StreamGeometry` cached per frame per bounds;
  - `Set(source, litPos, dim)` that invalidates only on a change of lit index, source, dim or bounds (FR-018 / FR-019);
  - the probe hook from T004.
- [ ] T019 [US1] In `src/managed/Nota.App/DeviceCards/Instruments/AuroraInstrumentCard.cs`, rebuild the Osc body as two 216 px columns: a header row 20, a window 216 × 60, a knob row 54 with the six existing `PKnob` cells at 34 px; then the shared sub / unison row (`:489-507`) spanning both columns (contracts/ui-osc-tab.md).
- [ ] T020 [US1] In `AuroraInstrumentCard.cs`, give each oscillator its own type drop-down (built-ins) and warp drop-down, bound to `table` / `osc2table` and `osc1warpmode` / `osc2warpmode`, with MIDI learn, readouts and automation follow, as `Chips` does today (`:292-300`).
- [ ] T021 [US1] In `AuroraInstrumentCard.cs`, remove `osel`, `oscPick`, `applyOsc`, `oscStrip`, `oscTableHosts` / `oscWarpHosts` and the single `tableWindow` (`:423-442, 480-487, 534`). Feed both windows in `Refresh` (`:164-176`) instead of `stack.Set` (`:168-169`). Update `Hint()` / `Summary()` and the header comment (`:8-11`).
- [ ] T022 [US1] In `AuroraInstrumentCard.cs`, apply the `Inactive` look to a column when its oscillator is off (FR-004). Each caption names its own position modulation (adapt `:509-526` per oscillator).
- [ ] T023 [P] [US1] Update `DESIGN.md` § Devices with an Aurora note: the Osc tab layout change, decided by spec 053.
- [ ] T024 [US1] Check quickstart § 1 in Studio Dark and Studio Light at 100 % and 150 % scale, and with `DesignTokenCheck*` in the smoke test green. Fix any overlap in `AuroraInstrumentCard.cs` / `AuroraTableView.cs`.

**Checkpoint**: US1 is shippable alone (the UI only; the engine is untouched by this phase).

---

## Phase 4: User Story 2 — Turn my own sample into a wavetable (Priority: P1)

**Goal**: Drop or import a file per oscillator. It plays, saves with the project and presets, survives racks, and handles undo and missing files.

**Independent Test**: quickstart § 2 and § 3. Import the three kinds, save, move the sources away, and reopen: the render is bit-identical. Presets and racks carry the table.

### Tests for User Story 2 (write first; they fail before the implementation)

- [ ] T025 [P] [US2] In `AuroraTableTests.cs`, add the conditioning tests:
  - each of rules (a) / (b) / (c) gives the expected frame count, using the T002 fixtures;
  - the 256-frame clamp and the 60 s cap set their flags;
  - silence gives `ERR_SILENT`; AIFF / broken files give `ERR_FORMAT` / `ERR_DECODE`;
  - each frame's mean is below 1e-6; the table peak equals 1.0;
  - the loop seam jump is at most the median step for (c).
- [ ] T026 [P] [US2] In `AuroraTableTests.cs`, add the undo and source tests: `set_table(checkpoint 1)`, then undo restores the previous table info and redo re-applies it; `clear_table` is one step; `set_source` toggles with no undo entry and keeps the voices active.
- [ ] T027 [P] [US2] In `AuroraTableTests.cs`, add the state tests:
  - the trailer round-trips through `getState` / `setState` (contracts/state-blob.md);
  - a blob without a trailer clears the tables;
  - an unknown trailer version is ignored;
  - `clone()`, track duplicate, device copy/paste, and an Instrument Rack chain / Drum Rack pad round trip keep the table (`RackNestingTests.cs` style).
- [ ] T028 [P] [US2] In `AuroraTableTests.cs`, add the render tests: an imported 2048-sample saw cycle and the built-in Analog at frame 0 differ by less than −40 dB at C3 (sanity); with Warp Off at C7 the alias energy of a bright 64-frame table is at most −60 dB relative to the fundamental (FR-011).
- [ ] T029 [P] [US2] In `AuroraTableTests.cs`, add the project tests:
  - save, reopen and render bit-identically (SC-007);
  - two Auroras with the same table give one `samples/<hash>.wav`;
  - with a deleted sample, the load warns, `table_info.missing` is 1 and the built-in plays;
  - v27 projects load unchanged and migrate to 28.
- [ ] T030 [P] [US2] In `AuroraTableTests.cs`, add the preset tests: a user preset carries `AuroraTables` and restores the frames exactly; a factory preset clears custom tables; the preset apply is one undo group.

### Implementation for User Story 2

- [ ] T031 [US2] Create `src/managed/Nota.Application/Instruments/Aurora/AuroraTableImporter.cs` (SPDX header):
  - check the extension against `SampleIndex.AudioExtensions` (`src/managed/Nota.Infrastructure/SampleStore/SampleIndex.cs:20-21`);
  - run `AuroraTableBuild` on a `Task`, then apply on the UI thread with `AuroraSetTable(checkpoint: true)`;
  - return a typed result (Ok / Shortened / FirstFrames / Unreadable / Format / Silent).
- [ ] T032 [US2] In `AuroraInstrumentCard.cs`, make each column a drop target (`BrowserView.IsAcceptableDrag` / `DroppedItems`, Rhythm pattern `RhythmInstrumentCard.cs:220-235`, ignoring MIDI files and presets). Add an `Import…` file picker filtered to `*.wav;*.flac;*.mp3` (Rhythm `:205-218`, without AIFF / OGG). Both call T031.
- [ ] T033 [US2] In `AuroraInstrumentCard.cs`, extend each type drop-down with the imported table's name (a check mark on the active source, via `AuroraSetSource`), `Remove imported table` (`AuroraClearTable`), and the dimmed built-in value while the imported table is active (FR-010).
- [ ] T034 [US2] In `src/managed/Nota.App/Controls/AuroraTableView.cs`, add the imported source: fetch `AuroraTablePreview` once per (track, osc, serial) into a shared cache; the "n/N" caption; "1 frame"; the drag-over ring and Loading / Error (4 s) / Missing states (contracts/ui-osc-tab.md).
- [ ] T035 [US2] In `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs` (`InstrumentDto` `:316-341`), add `WavetableDto` and `InstrumentDto.Wavetables`. Bump `CurrentFormatVersion` to 28 in `ProjectService.cs:40`, with its doc line, and append the no-op v27→v28 `Migration` in `ProjectMigrations.cs`.
- [ ] T036 [US2] In `ProjectService.cs`, make the save of kind 5 (`:150-153`) store `AuroraState(includeTables: false)`, write each table as float32 `clm ` WAV under `samples/<hash>.wav` via `BundleContent`, and fill `Wavetables` (contracts/persistence.md).
- [ ] T037 [US2] In `ProjectService.cs`, make the load of kind 5 (`:637` area) apply the blob, then for each `WavetableDto` call `nota_file_decode_mono` → `AuroraTableFromFrames` → `AuroraSetTable(checkpoint: false)` → `AuroraSetSource`. On failure, add the warning text from contracts/persistence.md and call `AuroraSetMissing`.
- [ ] T038 [US2] In `src/managed/Nota.Infrastructure/Persistence/BundleContent.cs` and `ProjectHistory.cs`, pin `Wavetables` samples in `PinnedBinaries` / `PruneUnreferenced` so that version history and pruning keep them.
- [ ] T039 [P] [US2] In `src/managed/Nota.Infrastructure/Persistence/PresetModel.cs` and `PresetService.cs`, add `PresetAuroraTable` and `AuroraTables`. Write it in save (`PresetService.cs:62` area) and apply it after the parameters (`:274`, `:313-324` pattern), clearing tables when it is absent.
- [ ] T040 [US2] Add the rack-chain variants (contracts/c-abi.md § Rack chains) in `src/native/nota.engine/src/engine/Engine_Devices.cpp`, `src/abi/nota_engine_rack.cpp`, `nota_engine.h`, and managed interop `NativeMethods.Rack.cs` / `NotaEngine.Rack.cs` / `IAudioEngine.cs`.
- [ ] T041 [US2] In `src/managed/Nota.App/DeviceCards/Rack/RackCardView.cs` (proxy engine, `:211`), forward the Aurora table calls to the chain variants, so an Aurora card opened inside a rack can import, switch source and remove.

**Checkpoint**: US2 complete. T025–T030 pass; quickstart § 2 and § 3 pass.

---

## Phase 5: User Story 3 — Two tables on screen without a slower app (Priority: P1)

**Goal**: Prove and hold the budgets in SC-003 to SC-006.

**Independent Test**: quickstart § 4 and § 5 on the reference machine, compared with the T005 baselines.

- [ ] T042 [P] [US3] In `tests/perf/nota_perf.cpp`, add the `aurora-imported-2x` scenario next to `aurora-supersaw-2x` (`:195`, `:254`): the same load, with both oscillators on generated 256-frame tables.
- [ ] T043 [P] [US3] In `tests/Nota.SmokeTest/AuroraFixtures.cs`, extend `--aurora-ui-fixture` with the v28 scenario project (256-frame generated tables on both oscillators of all 4 Auroras).
- [ ] T044 [US3] Check that no table-view work runs while the device panel is hidden or the window is minimised (FR-018). Gate `RefreshSynthLive` / the probe in `src/managed/Nota.App/DeviceChainView.Instruments.cs` if needed; the probe should show 0 paints (quickstart § 4 step 4).
- [ ] T045 [US3] Run quickstart § 4 and § 5 twice on the reference machine. Record UI-thread %CPU, paint p50 / p99, card build time and `nota_perf` numbers in `specs/053-aurora-one-card/perf-baseline.md`, against T005.
- [ ] T046 [US3] Only if T045 misses a budget: profile (`perf record` on the UI thread, or `dotnet-trace`). Fix in `AuroraTableView.cs` / `AuroraInstrumentCard.cs` (geometry cache, invalidation gate) or in `oscStackCustom`, then re-run T045. The budgets themselves are not loosened.

**Checkpoint**: SC-003 to SC-006 recorded as met.

---

## Phase 6: User Story 4 — See where modulation has taken the table (Priority: P3, in scope per Clarification 2)

**Goal**: The lit frame follows the played position while notes sound.

**Independent Test**: LFO 1 → Position at 1 Hz with a held note: the lit frame travels once a second. With no note, it rests at the set Position and repaints stop.

- [ ] T047 [P] [US4] In `AuroraTableTests.cs`, add the scope tests: with LFO 1 routed to Position and a held note, `nota_track_instrument_scope` floats [0] / [1] move over 1 s and stay in 0..1; with no voice they are −1; [2] / [3] change on `set_table`.
- [ ] T048 [US4] In `AuroraInstrumentCard.cs`, read the 5-float scope on every tick **before** the `ParamsMoved` gate (`:151-166`). Push the played position to both `AuroraTableView`s (the set Position when −1). Refetch the preview when a serial changes. Leave the readouts and graphs gated as today.
- [ ] T049 [US4] In `AuroraTableView.cs`, use the 64-frame lit grid of the preview for imported tables (built-ins: the 16 frames), and quantise the lit index so repaints happen only on a grid step.

**Checkpoint**: US4 complete. Re-run T045 with live follow active; it is part of the SC-003 scenario.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T050 [P] Add a `CHANGELOG.md` `[Unreleased]` entry: both oscillators on the Osc tab, custom wavetable import (formats, rules, limits), project format 28, and that held notes stop on import.
- [ ] T051 [P] Update `FEATURES.md` with the nota-features skill.
- [ ] T052 [P] Update the user manual (EN + RU) page `instruments/aurora` with the nota-docs skill (`src/managed/Nota.App/NotaDocs.cs:23` lists it).
- [ ] T053 [P] Check that every new file has the two-line SPDX header (`AuroraTable.h/.cpp`, `AuroraTableView.cs`, `AuroraTableImporter.cs`, `UiProbe.cs`, `AuroraFixtures.cs`, `AuroraTableTests.cs`), and that `LICENSES/third-party.md` needs no change (no new dependency).
- [ ] T054 Run `scripts/build-linux.sh` (and `scripts/build.sh` on macOS) and the whole of quickstart.md. Re-check the plan's Constitution Check and the spec's Nota Compliance Checklist.
- [ ] T055 Run `/speckit-converge` until it reports **Converged**.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** has no dependencies. T005 needs T003 and T004 and must run on the **unchanged** code.
- **Foundational (Phase 2)** depends on Phase 1 (T001). It blocks US2, US3 and US4.
- **US1 (Phase 3)** depends on Phase 1 only. It can run in parallel with Phase 2.
- **US2 (Phase 4)** depends on Phase 2 and on US1's T018–T021 (the card it extends).
- **US3 (Phase 5)** depends on US1 and US2: it measures the finished views with imported tables. T042 can start after Phase 2.
- **US4 (Phase 6)** depends on Phase 2 (T013) and US1 (T018). T045 is re-run after it.
- **Polish (Phase 7)** depends on all stories.

### Within each phase

- T006 → T007 → T008 → T009; T010 → T011 → T012 → T013; T014 → T015 → T016; T017 last.
- Tests T025–T030 are written before T031–T041 and fail before them.
- T035 → T036 → T037 → T038 (same persistence files, in order). T039 is independent ([P]).

### Parallel Opportunities

- Phase 1: T002, T003 and T004 run in parallel after T001.
- Phase 2 and Phase 3 run side by side (engine and UI files are disjoint).
- US2 tests T025–T030 are all [P] (separate test groups in one new file; merge in order).
- T039 (presets) runs in parallel with T035–T038 (project). T042 and T043 run in parallel.
- Polish T050–T053 run in parallel.

---

## Parallel Example: User Story 2

```bash
# Tests first, side by side:
Task: "T025 conditioning tests in tests/Nota.SmokeTest/AuroraTableTests.cs"
Task: "T027 state / rack round-trip tests in tests/Nota.SmokeTest/AuroraTableTests.cs"
Task: "T030 preset tests in tests/Nota.SmokeTest/AuroraTableTests.cs"

# Then persistence in two lanes:
Task: "T035–T038 project v28 in src/managed/Nota.Infrastructure/Persistence/{ProjectModel,ProjectService,ProjectMigrations,BundleContent,ProjectHistory}.cs"
Task: "T039 presets in src/managed/Nota.Infrastructure/Persistence/{PresetModel,PresetService}.cs"
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 (baselines on the unchanged code).
2. Phase 3 (US1): the two-column Osc tab with built-in tables only. Stop and validate with
   quickstart § 1 and a first UI CPU check (the T045 method with the baseline fixture).
   This answers Egor's main complaint, and it can ship as its own PR if review prefers.

### Incremental delivery

1. Phase 2 (engine and ABI) in parallel with US1.
2. US2: import, persistence, presets, racks.
3. US4: live follow.
4. US3: full measurement with everything on, then fixes if needed.
5. Polish, the build gate, converge.

### Task count

| Phase | Tasks |
|---|---|
| 1 Setup | 5 (T001–T005) |
| 2 Foundational | 12 (T006–T017) |
| 3 US1 | 7 (T018–T024) |
| 4 US2 | 17 (T025–T041) |
| 5 US3 | 5 (T042–T046) |
| 6 US4 | 3 (T047–T049) |
| 7 Polish | 6 (T050–T055) |
| **Total** | **55** |
