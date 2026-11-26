---
description: "Task list for Phase 5 of nota-daw/nota#1050"
---

# Tasks: Render DAG (`RenderGraphExecutor`), Planar Chains & Intra-Track Parallelism

## Phase 1: Core Abstractions (`AudioBufferView`, `RenderExecutor`, `PolyVoiceScheduler`, `AudioThreadPool`)

- [x] T001 [P] Create `src/native/nota.engine/src/dsp/core/AudioBufferView.h` (`PlanarStereoBuffer` + `PlanarBufferView` atop `AudioSpan.h`) and add planar virtual hooks (`prefersPlanar`, `processPlanar`) to `Device.h`
- [x] T002 [P] Create `src/native/nota.engine/src/dsp/core/RenderExecutor.h` and upgrade `src/native/nota.engine/src/engine/AudioThreadPool.h` / `AudioThreadPool.cpp` with `NOTA_CACHE_ALIGNED` `JobSlot` and lock-free ABA-safe cooperative `SubSlot` helper
- [x] T003 [P] Create `src/native/nota.engine/src/dsp/poly/PolyVoiceScheduler.h` and add `ChainScratchBank` multi-slot dynamic arena support in `src/native/nota.engine/src/dsp/core/ScratchArena.h`

## Phase 2: Graph DAG Execution & Pool Hysteresis (US1)

- [x] T004 Align `LeafWork` (`alignas(64)`), pre-allocate `groupScratch` / `returnScratch` / planar chain buffers in `Engine::RenderScratch`, and create `src/native/nota.engine/src/engine/RenderGraphExecutor.h` & `RenderGraphExecutor.cpp` registered in `CMakeLists.txt`
- [x] T005 Migrate `Engine_Render.cpp` (`mixGraph`, `processLeafDevices`, `rtParallel_` hysteresis + fast attack, gating `steady_clock::now()` in offline mode) onto `RenderGraphExecutor` (shrinking `Engine_Render.cpp` materially) and execute independent `Group` depth/key waves & `Return` key waves in parallel with deterministic reduction

## Phase 3: Intra-Track Rack & Polyphonic Synth Parallelism (US2)

- [x] T006 Wire `dsp::RenderExecutor` + `ChainScratchBank` into `RackInstrument` (`RackInstrument.h/.cpp`), `DrumRack` (`DrumRack.cpp`), `RackDevice` (`RackDevice.h/.cpp`), and `RackCore.cpp` for `>= 2` active parallel-safe chains with deterministic reduction
- [x] T007 Integrate `dsp::PolyVoiceScheduler` cleanly into `WavetableSynth` (`WavetableSynth.h/.cpp`, converting sample loop to hoisted block arrays + voice-outer), `Twin` (`Twin.h/.cpp`), and `Consort` (`Consort.h/.cpp`) preserving exact `0` vs `N` thread output

## Phase 4: Planar Chain Pass-Through & Safe macOS AudioUnit Parallelism (US3)

- [x] T008 Implement planar processing (`prefersPlanar`, `processPlanar`) in `PluginEffect` (`PluginEffect.h/.cpp` + `SimdAudio::mixPlanarWetDry`) and execute contiguous `prefersPlanar()` device chains without intermediate interleaved round-trips inside `RenderGraphExecutor`
- [x] T009 Add `hosting::isParallelRenderSafe()` in `PluginCommon.h/.cpp` enabling vetted macOS AudioUnit parallel rendering with explicit safety denylist (`NOTA_SERIAL_AUDIOUNIT=1` opt-out); add `AU` target alongside `VST3` to `nota-render-probe` in `CMakeLists.txt` (on `APPLE`)

## Phase 5: Verification, Tests, Changelog & Benchmarks

- [x] T010 Create `tests/Nota.SmokeTest/RenderDagParallelTests.cs` (wired in `Program.cs`) and extend `tests/Nota.SmokeTest/HostedRenderPoolTests.cs` for macOS AU parallel parity (`--hosted-au-pool`), multi-plugin planar chains, parallel group/return DAG, multi-chain racks, and heavy polyphonic synths (`0` vs `N` bit-exactness)
- [x] T011 Update `CHANGELOG.md` `[Unreleased]` with Phase 5 entry and audit `Engine_Render.cpp` readability / SPDX headers across all new files
- [x] T012 Run `./scripts/build.sh`, `-- --perf`, `nota_idle_cpu`, Phase 5 targeted microbenchmarks, and `./scripts/bench-engine.sh -r 3`; mark completed tasks `[x]` via `/speckit-converge`
