---
description: "Consolidated task list for nota-daw/nota#1050 (specs/115-engine-large-project-optimizations)"
---

# Tasks: Engine Performance Optimizations for Large Projects (`#1050`)

**Input**: Design documents from `/specs/115-engine-large-project-optimizations/`
**Prerequisites**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/c-abi.md`, `contracts/internal-engine.md`, `quickstart.md`

## Foundational C ABI & Shared DSP Primitives

- [x] T001 Define C ABI structs, enums, and declarations in `src/native/nota.engine/include/nota/nota_engine.h` (`NotaTrackMeterEntry`, `NotaTelemetryMode`, `NotaPluginSleepMode`, `NotaSampleLoadPolicy`, `NotaStreamingStats`, `NotaHybridBufferMode`, `NotaHybridBufferStats`, and corresponding `nota_*` entry points)
- [x] T002 Implement shared real-time DSP headers under `src/native/nota.engine/src/dsp/`: `core/DeviceTelemetry.h`, `core/SilenceGate.h`, `core/ScratchArena.h` (`StereoScratchArena`, `ChainScratchBank`), `core/AudioBufferView.h` (`PlanarStereoBuffer`), `core/RenderExecutor.h`, `poly/PolyVoiceScheduler.h`, `math/Simd.h`, and `math/SimdAudio.h`
- [x] T003 [P] Add managed C# contracts and `[LibraryImport]` P/Invoke wrappers across `src/managed/Nota.Application/Engine/{EngineContracts,IAudioEngine}.cs` and `src/managed/Nota.Infrastructure/Interop/{NativeMethods,NotaEngine}{,.Devices}.cs`

---

## US1 — Batch Track Meter Polling & On-Demand Device Telemetry

- [x] T004 Implement `Engine::readTrackMeters` (`O(T)` snapshot) and `Engine::setTelemetryMode` / propagation in `src/native/nota.engine/src/engine/Engine.{h,cpp}` and `src/native/nota.engine/src/abi/nota_engine_core.cpp`
- [x] T005 [P] Update `src/managed/Nota.App/{ArrangementView,MixerView,SessionView.Mixer}.cs` to fetch `ReadTrackMeters(Span<TrackMeterReading>)` once per UI refresh tick
- [x] T006 Migrate all 24 timed built-in devices (`22` effects in `src/devices/effects/` + `Consort` and `Pentad` in `src/devices/instruments/`) to `DeviceTelemetry::ScopedBlockTimer` via `Device.h` / `Instrument.h`, and propagate modes across `RackCore`, `RackDevice`, `RackInstrument`, and `RhythmMachine`
- [x] T007 [P] Update `AutoFilterDeviceBody.cs` to gate active LFO view display on `Sc(S_SampleRate) > 0` instead of `Sc(S_Cpu) > 0`

---

## US2 — Idle Sleep Gating, Open-Editor Keep-Awake & Active Submix Buses

- [x] T008 Migrate built-in synths (`BassSynth`, `Consort`, `FluxSynth`, `Monolith`, `OperatorSynth`, `PendulumSynth`, `Pentad`, `Synth`, `VoltSynth`, `WavetableSynth`) to `dsp::SilenceGate` early-out; preserve clock/silent invariants (`needsIdleRender()`, `outputAlwaysSilent()`) on `Twin`, `Keys`, `Mosaic`, `RhythmMachine`, and `Sampler`
- [x] T009 Implement recursive `hasTail()`, `isIdle()`, `requiresActiveProcessing()`, and `wake()` across `RackDevice`, `RackInstrument`, `DrumRack`, `RackCore`, and `MidiDevice` subclasses (`Arpeggiator`, `MidiChord`, `MidiNoteLength`, `MidiRandom`, `MidiRack`)
- [x] T010 Wire tail-aware `SilenceGate`, held-note/sustain tracking, sidechain input wake, `NotaPluginSleepMode` (`Auto`/`Never`), and `EditorHost::open_` keep-awake (`PluginEditorWindow.{h,cpp}`, `nota_engine_plugin_editor_sleep_selftest`) into `PluginEffect.{h,cpp}`, `PluginInstrument.{h,cpp}`, and `Engine_Devices.cpp` / `nota_engine_device.cpp`
- [x] T011 Implement `RenderBusState` (`src/engine/RenderBusState.h` `ActiveBusMask`) and skip idle settled synths in `playEvents()` / `prepareLeafWork()` plus silent `ChainRest::Settled` Group (`Pass 1.5`) and Return (`Pass 2`) buses

---

## US3 — Indexed Timeline Lookups & Precompiled Render Routing Plans

- [x] T012 [P] Upgrade `AutomationLane::valueAt()` (`Automation.cpp`) and `ControllerCurve::movesIn()` (`MidiController.cpp`) to `O(log P)` `std::upper_bound`, and add monotonic `uint64_t generation` + `SnapshotHistory::hasPendingRetire()` (`RackCore.h`, `SnapshotHistory.h`)
- [x] T013 Implement `nota::TimelineIndex` (`src/engine/TimelineIndex.{h,cpp}`) and `nota::RenderRoutingPlan` (`src/engine/RenderRoutingPlan.{h,cpp}`), attach to `Track.h` / `Graph.h`, register in `CMakeLists.txt`, build/reuse during `Engine::publishRaw()`, and bump `routingEpoch_` in `recomputeRouting()`
- [x] T014 Wire `TimelineIndex` and `RenderRoutingPlan` into `Engine_Render.cpp`, `Engine_Automation.cpp`, and `Engine_Modulation.cpp` (clip/note/CC/audio lookup, Group ancestry, MIDI-In cache with atomic override fallback, sidechain key cache, compiled automation/CV/Arp-CC targets)

---

## US4 — Copy-on-Write Track Snapshots, Session-Sized Scratch Arenas & EQ SIMD

- [x] T015 Convert `Track` heavy snapshot vectors (`clips`, `takes`, `midiClips`, `sessionSlots`, `automation`, `modulators`, `cvLinks`) in `Track.h` to `CowSlice<T>` (`TrackContent.h`), update `.write()` mutators across `Engine*.cpp`, reuse `TimelineIndex` via storage tokens, seal on `publishRaw()`, skip no-op `recomputeRouting()` / telemetry sync, and optimize `recomputePdc()` to a single pass
- [x] T016 Migrate `RackDevice`, `RackInstrument`, `DrumRack`, and `Engine::RenderScratch::leafBuf` to `StereoScratchArena` session-sized capacity (`2048` live frames, expanding up to `kMaxBlock` prior to offline bounces)
- [x] T017 Upgrade `Eq.{h,cpp}` with per-band `BandCache` coefficients, SIMD `peakAbs`, and flat-stereo `double2` NEON/SSE2 filtering; consolidate pure vector math in `Simd.h` (`complexMulAdd`, `dot`, `maxAbs`, `peakAndSumSquares`), delegate `SimdAudio.h` peak/RMS helpers cleanly, share `accumulatePackedSpectrum` (`Convolution.h`) + `dot` (`Chamber`, `Convolution`), and enable Clang `-fno-math-errno` and Clang/MSVC Release IPO in `CMakeLists.txt`

---

## US5 — Parallel Render DAG, Intra-Track Parallelism & Planar Plugin Chains

- [x] T018 Cache-line align `AudioThreadPool::JobSlot` and `Engine::LeafWork`, add lock-free cooperative `SubSlot` to `AudioThreadPool`, extract `RenderGraphExecutor.{h,cpp}` from `Engine_Render.cpp` (`5,066` -> `4,019` lines), gate offline per-task `steady_clock::now()`, add RT pool hysteresis + 2-block fast attack, and execute Group depth/key waves & Return key waves in parallel with deterministic reduction
- [x] T019 Wire `dsp::RenderExecutor` + `ChainScratchBank` into `RackInstrument`, `DrumRack`, and `RackDevice` for parallel-safe multi-chain execution, and wire `dsp::PolyVoiceScheduler` into `WavetableSynth`, `Twin`, and `Consort` (`0` vs `N` bit-exact parity)
- [x] T020 Implement contiguous `prefersPlanar()` / `processPlanar()` chain execution on `PluginEffect` & `RenderGraphExecutor` via `PlanarStereoBuffer`, add vetted macOS AudioUnit worker opt-in via `hosting::isParallelRenderSafe()` (`PluginCommon.{h,cpp}`), and add AU build output on Apple to `nota-render-probe` (`CMakeLists.txt`)

---

## US6 — Disk Streaming & Hybrid Anticipative Buffering

- [x] T021 Implement `AudioSource.h`, `StreamingAudioSource.{h,cpp}` (`kPageFrames = 8192`, `kRingPages = 32`, `kHeadPages = 2`, wait-free seqlock rings, sleep-on-idle `StreamingIoScheduler`, `dr_libs` decoders, sync offline fallback), and integrate into `SampleBuffer`, `AudioFile`, `AudioImport`, `Engine_Tracks`, `Engine_Session`, and `Engine.cpp` (`seekBeats` / `sampleRead`)
- [x] T022 Implement `HybridBufferController.{h,cpp}` (`kChunkFrames = 1024`, `kLookaheadFrames = 4096`, `kRingFrames = 8192`), integrate anticipative ring lookup and live/editor/armed/sidechain bypass + invalidation hooks cleanly into `RenderGraphExecutor`, `Engine_Render`, `Engine_Automation`, and `Engine*.cpp`

---

## Verification, Documentation & Consolidation

- [x] T023 Add and wire concrete feature smoke test suites (`TrackMeterAndTelemetryTests.cs`, `SilenceGateAndBusMaskTests.cs`, `TimelineIndexAndRoutingTests.cs`, `TrackSnapshotArenaSimdTests.cs`, `RenderDagParallelTests.cs`, `DiskStreamingAndHybridBufferTests.cs`, plus `HostedRenderPoolTests.cs` AU/sleep extensions) into `tests/Nota.SmokeTest/Program.cs` (full run & `--perf`)
- [x] T024 Update `CHANGELOG.md` `[Unreleased]` and `.specify/bugs/plugin-editor-sleep-freeze/` and consolidate all specification artifacts under `specs/115-engine-large-project-optimizations/`
- [x] T025 Verify `./scripts/build.sh`, `-- --perf`, `tests/perf/nota_idle_cpu`, `nota_engine_plugin_editor_sleep_selftest`, and `scripts/bench-engine.sh` across the final implementation
