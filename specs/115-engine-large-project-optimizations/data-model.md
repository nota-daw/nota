# Data Model: Engine Performance Optimizations (`#1050`)

**Spec**: [spec.md](spec.md)

## 1. Meters & Telemetry (`DeviceTelemetry.h`, `nota_engine.h`, `EngineContracts.cs`)

- **`NotaTrackMeterEntry` / `TrackMeterReading`**: `{ int32_t track_id; NotaMeter meter; }` — post-fader stereo `peak_l`, `peak_r`, `rms_l`, `rms_r` in graph order.
- **`dsp::TelemetryMode` / `NotaTelemetryMode` / `DeviceTelemetryMode`**: `OnDemand = 0` (default), `AlwaysOff = 1`, `AlwaysOn = 2`.
- **`dsp::DeviceTelemetry`**:
  - Cross-thread: `std::atomic<TelemetryMode> mode_`, `mutable std::atomic<uint32_t> observeRequests_`, `std::atomic<float> cpuLoad_`.
  - Audio-thread: `uint32_t lastSeenObserve_`, `int64_t rtLeaseFrames_` (armed to `clamp(sr * 3.0, 4096.0, 1e8)` when `observeRequests_` advances; zero clock calls while `<= 0`), `double smoothedCpu_`.
  - Guarded by RAII `ScopedBlockTimer`.

## 2. Sleep Gating, Keep-Awake & Active Submix Buses (`SilenceGate.h`, `RenderBusState.h`)

- **`dsp::PluginSleepMode` / `NotaPluginSleepMode`**: `Auto = 0`, `Never = 1`.
- **`dsp::SilenceGate`**:
  - Configuration: `sampleRate_`, `hangoverSeconds_` (`0.25s` minimum; hosted plugins use `max(0.25, min(tail, 10.0))`, infinite `>= 1e6s` opts out), `releaseThreshold_` (`1e-5f`).
  - Cross-thread atomics: `wakeEpoch_`, `seenEpoch_`, `sleeping_`, `neverSleep_`, `holdAwake_` (ref-counted/atomic open-editor keep-awake without altering `PluginSleepMode`).
  - Global atomic counter `dsp::gNeverSleepDevices` avoids scanning on `PluginSleepMode::Never` opt-outs when none exist.
- **`EditorHost` (`PluginEditorWindow.h/.cpp`)**: Atomic `open_` + `onOpenChanged` callback notifying `PluginEffect` / `PluginInstrument` `holdAwake_` across open/close/hide/dtor.
- **`RenderBusState` (`RenderBusState.h`)**: Fixed-capacity `ActiveBusMask<kMaxGroups>` and `ActiveBusMask<kMaxReturns>` using wait-free byte-atomic `active_` flags written during Pass 1 / Pass 1.5 and `prevActive_` tracking to clear only previously dirtied buses before each block.

## 3. Timeline Indexing & Precompiled Routing (`TimelineIndex.h`, `RenderRoutingPlan.h`)

- **`TimelineIndex`**:
  - `NoteSpan`: sorted by `startBeat` within each `MidiClipEntry` alongside `originalIndex`, cumulative `notePrefixMaxEnd`, and symmetric track/articulation offset padding (`maxNoteAbsOffsetMs`).
  - `MidiClipEntry`: original clip index, beat span, sorted notes, envelope flag.
  - `AudioClipEntry`: original clip index, `startBeat`, warped flag / `warpLenBeats`, conservative `unwarpedSec`, interval/segment tree (`AudioTreeNode`) for $O(\log N + k)$ queries in ascending original clip index order.
  - Backing storage tokens (`clipsStorage_`, `midiClipsStorage_`, `artTableStorage_`) supporting $O(1)$ reuse in `matchesTrack()`.
- **`RenderRoutingPlan`** (immutable on `Graph`):
  - `tracksById_` (`TrackIdSlot`: sorted `{ trackId, trackIndex }` for $O(\log T)$ binary search).
  - `groups_` (`CompiledGroupSlot`: parent slot, depth, fixed `ancestors[kMaxGroups]` for $O(\text{depth})$ mute/solo folding).
  - `leafRoutes_` (`CompiledLeafRoute`: `parentGroupSlot`, `expectedMidiFromId`, `midiFromTrackIndex` with lock-free atomic override fallback).
  - `monitorFeeds`, `automatedTracks`, `modulatorTracks`, `cvTracks`, `ccRoutePlans`, `masterLanes`, `masterPanLane`.
  - `CompiledParamEndpoint`: resolves direct `Track`/`Device`/`MidiDevice`/`Instrument` pointers or `(rack, chainUid, host, slot, expectedRackGen)` verified in $O(1)$ against `RackCore::RackState::generation` before falling back to `resolveNested()`.

## 4. Track CoW Containers, Scratch Arenas & SIMD (`TrackContent.h`, `ScratchArena.h`, `Eq.h`, `SimdDispatch.h`)

- **`CowSlice<T>` (`TrackContent.h`)**:
  - `std::shared_ptr<const std::vector<T>> data_` (copied across `cloneTrack()` snapshots) + `std::shared_ptr<std::vector<T>> mutable_` (cleared by `seal()`, allocated/detached on `.write()` when `!mutable_ || data_.use_count() > 1`).
  - Backing fields on `Track`: `clips`, `takes`, `midiClips`, `sessionSlots`, `automation`, `modulators`, `cvLinks`.
- **`dsp::StereoScratchArena<N>` & `dsp::ChainScratchBank` (`ScratchArena.h`)**:
  - Live capacity defaults to `kDefaultLiveFrames = 2048` (`8×` smaller than `16384`-frame `kMaxBlock`), prepared outside RT to handle offline chunks cleanly (`Engine::ensureRenderScratch` + `ensureBlockCapacity`).
  - `ChainScratchBank` dynamically prepares aligned multi-chain scratch buffers for `RackInstrument`, `DrumRack`, and `RackDevice`.
- **`Eq::BandCache` & `SimdKernels`**:
  - Per-band cached `BiquadCoeffs[kMaxSec]` + inputs (`sr, freq, gain, q, scale, type, slope, ch`) and flat-stereo `double2` L/R SIMD biquad runner.
  - `nota::simd::SimdIsa` + `SimdKernels` (`SimdDispatch.h` layered strictly over `SimdAudio.h`) selecting runtime AVX2+FMA (`SimdAvx2.cpp`) or baseline SSE2/NEON kernels specifically for heavy block operations (`nota::simd::convCmac` in `Chamber`/`Convolution` for `bins >= 33`, and `nota::simd::peakAbs`/`peakAndSumSquares` for `count >= 512`), alongside inlined `nota::simd::dot` for multi-tap FIR heads.

## 5. Render DAG, Intra-Track Parallelism & Planar Chains (`AudioThreadPool.h`, `RenderExecutor.h`, `RenderGraphExecutor.h`, `PolyVoiceScheduler.h`, `AudioBufferView.h`)

- **`AudioThreadPool`**: Cache-aligned `alignas(64) JobSlot` + lock-free cooperative `SubSlot` (`subState_` packing `[gen:32][publishing:1][active:1][refCount:30]`).
- **`dsp::RenderExecutor`**: Thread-local scoped bridge choosing between inline serial execution, single-track top-level pool dispatch, or cooperative `SubSlot` helping inside active pool workers without reentrant `dispatch()`.
- **`RenderGraphExecutor`**: Owns `MixContext`, `initGroupsAndBuses`, `classifyLeaves`, `runLeafWaves`, `reduceLeavesAndMeterInputs`, parallel Group depth/key DAG waves (`runGroupWaves`), parallel Return key DAG waves (`runReturnWaves`), `finalizeMixBlock`, and `processDeviceChain` planar-run batching.
- **`dsp::PolyVoiceScheduler` & `PolyVoiceScratchBank`**: Pre-allocated `kMaxBatches = 8` scratch bank partitioning active polyphonic voices deterministically by DSP state in `WavetableSynth`, `Twin`, and `Consort`.
- **`dsp::PlanarStereoBuffer` (`AudioBufferView.h`)**: Aligned L/R planar buffer and `PlanarAudioSpan` bridges eliminating intermediate deinterleave/interleave conversions across adjacent `prefersPlanar()` nodes.

## 6. Disk Streaming & Hybrid Buffering (`AudioSource.h`, `StreamingAudioSource.h`, `HybridBufferController.h`, `nota_engine.h`)

- **`SampleLoadPolicy` / `NotaSampleLoadPolicy`**: `Auto = 0` (`> 16 MiB` decoded PCM streams), `Memory = 1`, `Stream = 2`.
- **`StreamingAudioSource`**: `kPageFrames = 8192`, `kRingPages = 32` (~2 MiB stereo ring), `kHeadPages = 2` pinned start frames, cache-aligned `PageSlot` with atomic `seq` seqlock (`even` = stable, `odd` = filling), `StreamingIoScheduler` sleep-on-idle prefetch thread (`condvar`), sync offline / `nota_sample_read` decoder path, and `NotaStreamingStats`.
- **`HybridBufferMode` / `NotaHybridBufferMode` & `HybridBufferController`**: `Off = 0`, `Auto = 1`; `kChunkFrames = 1024`, `kLookaheadFrames = 4096`, `kRingFrames = 8192`; cache-aligned per-track `TrackSlot` (`Owner` `Realtime` vs `Worker`, `workerInFlight`, `ringEpoch`, `epoch_` fencing, background synth/FX scratch workspace) and `NotaHybridBufferStats`.
