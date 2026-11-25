# Tasks: Batch Track Meter Snapshots and On-Demand Device Telemetry

**Input**: Design documents from `/specs/114-batch-meters-telemetry-gate/`  
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/c-abi.md](contracts/c-abi.md)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: `US1` (Batch Track Meters), `US2` (On-Demand Device Telemetry), `US3` (First-Open UI Visuals)

---

## Phase 1: Setup (Shared Abstractions)

**Purpose**: Create the shared readable telemetry primitive and C ABI contract types.

- [x] T001 [P] Create `TelemetryMode`, `DeviceTelemetry`, and RAII `ScopedBlockTimer` in `src/native/nota.engine/src/dsp/core/DeviceTelemetry.h` with AGPL-3.0-only SPDX header
- [x] T002 [P] Add `NotaTrackMeterEntry`, `NotaTelemetryMode`, `nota_engine_read_track_meters`, `nota_engine_set_device_telemetry`, and `nota_engine_device_telemetry` declarations to `src/native/nota.engine/include/nota/nota_engine.h`

---

## Phase 2: Foundational (Engine & Base Interfaces)

**Purpose**: Wire the new abstractions into core C++ interfaces, C ABI entry points, and managed boundary contracts before migrating individual devices or views.

- [x] T003 [US1] Add `Engine::readTrackMeters(NotaTrackMeterEntry* out, int32_t capacity)` to `src/native/nota.engine/src/engine/Engine.h` and `src/native/nota.engine/src/engine/Engine.cpp`
- [x] T004 [US2] Add `telemetry_` member, `observeTelemetry()`, and virtual `setTelemetryMode(dsp::TelemetryMode)` to `src/native/nota.engine/src/devices/Device.h` and `src/native/nota.engine/src/devices/Instrument.h`
- [x] T005 [US2] Implement `Engine::setTelemetryMode` / `telemetryMode` propagation (including in `publishRaw`) in `src/native/nota.engine/src/engine/Engine.h`, `src/native/nota.engine/src/engine/Engine.cpp`, `src/native/nota.engine/src/devices/racks/RackCore.h`, `RackDevice.{h,cpp}`, `RackInstrument.{h,cpp}`, and `RhythmMachine.{h,cpp}`
- [x] T006 [US1][US2] Expose `nota_engine_read_track_meters`, `nota_engine_set_device_telemetry`, and `nota_engine_device_telemetry` in `src/native/nota.engine/src/abi/nota_engine_core.cpp`
- [x] T007 [P] [US1][US2] Add `TrackMeterReading`, `DeviceTelemetryMode`, `ReadTrackMeters(Span<TrackMeterReading>)`, and `DeviceTelemetry` to `src/managed/Nota.Application/Engine/EngineContracts.cs`, `src/managed/Nota.Application/Engine/IAudioEngine.cs`, `src/managed/Nota.Infrastructure/Interop/NativeMethods.cs`, and `src/managed/Nota.Infrastructure/Interop/NotaEngine.cs`

**Checkpoint**: Native/managed boundary compiles and exposes clean batch meters & telemetry controls.

---

## Phase 3: User Story 1 — Batch Track Meter Polling in Desktop Views (Priority: P1)

**Goal**: Eliminate per-track `O(T^2)` meter polling across `ArrangementView`, `MixerView`, and `SessionView`.

- [x] T008 [P] [US1] Replace per-track `TryGetTrackMeter` calls in `UpdateMeters()` with a single reusable batch `ReadTrackMeters` lookup map in `src/managed/Nota.App/ArrangementView.cs`
- [x] T009 [P] [US1] Replace per-strip and folded-group `TryGetTrackMeter` calls in `UpdateMeters()` / `UpdateHiddenState()` with a single reusable batch `ReadTrackMeters` lookup map in `src/managed/Nota.App/MixerView.cs`
- [x] T010 [P] [US1] Populate a per-refresh batch `ReadTrackMeters` snapshot once in `src/managed/Nota.App/SessionView.Mixer.cs` for channel strip meters

**Checkpoint**: UI meter refresh uses 1 boundary crossing per view pass instead of `O(T)` calls.

---

## Phase 4: User Story 2 & 3 — On-Demand `DeviceTelemetry` Migration Across Built-In Devices & First-Open UI Fix (Priority: P1/P2)

**Goal**: Remove duplicate clock/EMA code across all 24 timed devices and ensure `AutoFilterDeviceBody` works cleanly when CPU telemetry was unobserved prior to card open.

- [x] T011 [P] [US2] Migrate effects group A (`Amp`, `AutoFilter`, `AutoPan`, `AutoShift`, `BeatRepeat`, `Ceiling`, `Chamber`, `Chorus`) `.h`/`.cpp` in `src/native/nota.engine/src/devices/effects/` to `dsp::DeviceTelemetry::ScopedBlockTimer` and `telemetry_.observeAndReadCpu()`
- [x] T012 [P] [US2] Migrate effects group B (`Compressor`, `Crush`, `Delay`, `DynamicEq`, `Eq`, `Eq3`, `Flanger`, `Forge`) `.h`/`.cpp` in `src/native/nota.engine/src/devices/effects/` to `dsp::DeviceTelemetry`
- [x] T013 [P] [US2] Migrate effects group C (`Phaser`, `Prism`, `Reverb`, `Shutter`, `Utility`, `Vintage`) `.h`/`.cpp` in `src/native/nota.engine/src/devices/effects/` to `dsp::DeviceTelemetry`
- [x] T014 [P] [US2] Migrate instruments (`Consort.{h,cpp}`, `Pentad.{h,cpp}`) in `src/native/nota.engine/src/devices/instruments/` to `dsp::DeviceTelemetry`
- [x] T015 [P] [US3] Update `SyncLfoView()` in `src/managed/Nota.App/DeviceCards/Bodies/AutoFilterDeviceBody.cs` to gate active LFO phase display on `Sc(S_SampleRate) > 0` instead of `Sc(S_Cpu) > 0`

**Checkpoint**: Zero unconditional `steady_clock::now()` calls inside built-in DSP `process()`/`render()` blocks; `AutoFilterDeviceBody` displays cleanly on first frame.

---

## Phase 5: Verification, Tests & Benchmarks

**Purpose**: Extend smoke tests, verify full build, collect before/after benchmarks, and update changelog.

- [x] T016 [US1][US2] Add `Phase1MeterTelemetryTests.cs` in `tests/Nota.SmokeTest/Phase1MeterTelemetryTests.cs` covering 100-track batch meter parity, buffer truncation/null guards, `AlwaysOff`/`OnDemand`/`AlwaysOn` telemetry behavior, lease renewal via `DeviceScope`/`InstrumentScope`, and `Consort`/`Pentad` CPU scope reporting; wire into `tests/Nota.SmokeTest/Program.cs`
- [x] T017 Update `CHANGELOG.md` `[Unreleased]` with Phase 1 entry under `Changed`/`Added`
- [x] T018 Run `./scripts/build.sh` end-to-end and verify all smoke + perf regression tests (`-- --perf`) pass
- [x] T019 Run `./scripts/bench-engine.sh` comparing `develop` baseline against Phase 1 build and verify `git diff` / `/speckit-converge`
