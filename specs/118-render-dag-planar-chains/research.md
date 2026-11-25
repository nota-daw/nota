# Phase 0 Research: Render DAG, Planar Chains & Intra-Track Parallelism (`specs/118-render-dag-planar-chains`)

**Date**: 2026-11-24 | **Parent Issue**: [`nota-daw/nota#1050`](https://github.com/nota-daw/nota/issues/1050)

## Codebase Audit & Verified Decisions

### 1. `AudioThreadPool` & Cooperative Sub-Task Execution
- **Finding (`src/native/nota.engine/src/engine/AudioThreadPool.h/.cpp`)**:
  - `AudioThreadPool` uses a single-publisher monotonic seqlock (`epoch_`, `claimed_`, `done_`) where the calling audio thread acts as sole publisher. Therefore **worker threads inside `dispatch()` cannot recursively call `dispatch()`** without corrupting `slots_` / `epoch_` / `doneBase_`.
  - Additionally, `JobSlot` (line 188) and `Engine::LeafWork` (`Engine.h:2849`) lack `alignas(64)` cache-line padding, allowing cross-core false sharing when multiple workers update `w.wid`/`w.workNs` or read adjacent slots.
  - Meanwhile, `Engine_Render.cpp:3865-3882` calls `std::chrono::steady_clock::now()` per leaf AND per wave even when `offline == true` (then discards the measurements on line 3888!).
  - Furthermore, `rtParallel_` (`Engine_Render.cpp:3815-3822`) checks `parWorkNs_ <= roomNs` with zero hysteresis, which can drop back to 1-thread probing on borderline RT loads (`1.3x` period) and produce short late-block bursts (`> 4` late blocks / `50 ms`), while EWMA ramp-up (`leafWorkNs_ += ... / 16`) waits ~16 blocks before joining on sudden overload.
- **Decision**:
  1. Cache-line align (`NOTA_CACHE_ALIGNED`) both `AudioThreadPool::JobSlot` and `Engine::LeafWork`.
  2. Skip per-task / per-wave `steady_clock::now()` in `RenderGraphExecutor` when `offline == true`.
  3. Add clean hysteresis on RT pool exit (`parWorkNs_ <= 0.72 * roomNs && leafWorkNs_ <= 0.75 * roomNs`) and faster consecutive-miss attack (`2` consecutive over-budget solo blocks join immediately), keeping `0.0%` worker CPU during light RT playback (`RenderPoolTests` test 3).
  4. Introduce `dsp::RenderExecutor` (`src/native/nota.engine/src/dsp/core/RenderExecutor.h`) backed by a lock-free `AudioThreadPool` cooperative sub-task slot (`subState_` packing `(gen << 32) | kPublishing | kActive | refCount`) with single relaxed load when idle and bounded helper participation on worker exit / caller wait loops. When `m == 1` leaf renders on caller directly (outside outer `dispatch()`), sub-tasks dispatch cleanly through `pool_->dispatch()`, waking workers on heavy single-track racks/synths!

### 2. DAG Execution for Pass 1.5 (`Group`) and Pass 2 (`Return`) + `Engine_Render.cpp` Decomposition
- **Finding (`Engine_Render.cpp:3265-4341`)**:
  - `Engine_Render.cpp` is `5,066` lines; `mixGraph()` alone is `1,076` lines combining leaf classification, Pass 1 dispatch, Pass 1.5 serial group loop, and Pass 2 serial return loop.
  - All active `Group` tracks at a hierarchy depth (`maxDepth..1`) read already-summed children (`groupBus_[gi]`) + read-only route taps (`routeBus_` / fresh `routeFresh_`), and fold deterministically into `groupBus_[pslot]` / `out`. Similarly, non-muted active `Return` buses read `returnBus_[bus]` and fold deterministically into `out`.
  - Any intra-level group-to-group or return-to-return sidechain keys (`feedDeviceKey` reading `routeSlot`) require topological level ordering within that depth/return pass, identically to leaf key levels (`kMaxKeyLevels`).
- **Decision**: Extract `RenderGraphExecutor` (`src/native/nota.engine/src/engine/RenderGraphExecutor.h/.cpp`) owning per-leaf wave staging, intra-depth Group DAG waves, Return DAG waves, and deterministic index-order reduction into parent buses / `out` — preserving exact IEEE-754 addition order (`maxdiff == 0.0` for `0` vs `N` threads) and significantly shrinking `Engine_Render.cpp`.

### 3. Intra-Track Parallelism: Multi-Chain Racks & Polyphonic Synths
- **Finding (`RackInstrument.cpp`, `DrumRack.cpp`, `RackDevice.cpp`, `Twin.cpp`, `Consort.cpp`, `WavetableSynth.cpp`)**:
  - Racks (`RackInstrument`, `DrumRack`, `RackDevice` Parallel/Select) hold mutually independent chains (`chainEvs_` / `outHeld[ci]` indexed per chain `ci`, `PartTap` unique per `c.uid`, `c.devices` & `c.instrument` private per chain). Extending `StereoScratchArena` / chain scratch slots for `N` chains enables parallel per-chain execution followed by deterministic `ci = 0..N-1` reduction (`diff == 0.0`).
  - `WavetableSynth::render()` (`WavetableSynth.cpp:715-861`) loops `for (i = 0..frames-1) { for (v : voices_) ... }`, which mixes block control stepping with per-voice oscillators/SVFs. Hoisting per-sample control smoothing arrays (`f1Base`, `f2Base`, `reso1`, `reso2`, `lfo1`, `lfo2`) converts `WavetableSynth` to voice-outer loops ready for `PolyVoiceScheduler`.
  - `Twin::renderSegment()` (`n <= 64` frames, up to `12` voices) and `Consort::renderSpan()` (when `poly` is active, up to `16` voices, provided no active voice is in `pendNote >= 0` voice-steal fade calling `startPoly()`/`focus_`/`orderCtr_`) are completely independent per active voice and can write into per-batch oversampled scratch (`osL_[b], osR_[b]`), reduced deterministically by batch index before decimation & FX.
  - Choosing batching purely from deterministic DSP state (`active >= threshold && frames >= minFrames`), never from thread count, guarantees `diff == 0.0` between `0` and `N` render threads.

### 4. Planar Effect Chains (`AudioBufferView` / `PlanarStereoBuffer`) & macOS AudioUnit Safety
- **Finding (`PluginEffect.cpp`, `PluginInstrument.cpp`, `juce_AudioUnitPluginFormatImpl.h`)**:
  - `PluginEffect::processPiece()` unconditionally runs `deinterleaveStereo` -> `processBlock` -> `interleaveStereoWetDryMix` even between adjacent hosted plugins (`FX1 -> FX2 -> FX3`), while `PluginInstrument` renders planar into `storage_` and immediately interleaves only for `FX1` to deinterleave again.
  - Reading `juce_AudioUnitPluginFormatImpl.h:1329-1436` confirms every `AudioUnitPluginInstance` owns its private `AudioComponentInstance`, `outputBufferList`, `inputBuffer`, `timeStamp`, and `midiInLock`. No global wrapper buffer is shared across separate AudioUnit instances.
  - `nota-render-probe` (`CMakeLists.txt:495-510`) currently builds `VST3` only; enabling `AU` alongside `VST3` on Apple in `nota-render-probe` lets `HostedRenderPoolTests` test real multi-threaded AudioUnit rendering end-to-end.
- **Decision**:
  - Add `src/native/nota.engine/src/dsp/core/AudioBufferView.h` (`PlanarStereoBuffer` + `PlanarBufferView`, layered cleanly onto `AudioSpan.h`). Add optional `prefersPlanar()` / `renderPlanar()` / `processPlanar()` virtual entry points on `Instrument` / `Device` (`PluginInstrument` & `PluginEffect` opt in cleanly); extract contiguous planar-run execution into `RenderGraphExecutor` / device chain helper.
  - Add `isParallelRenderSafe(const juce::AudioPluginInstance&)` in `PluginCommon.h/.cpp`, enabling `"VST3"` and `"AudioUnit"` (outside explicit unsafe denylist / env override `NOTA_SERIAL_AUDIOUNIT=1`), and add macOS AudioUnit regression checks in `HostedRenderPoolTests.cs`.
