# Architectural Research & Design Notes (`nota-daw/nota#1050`)

**Spec**: [spec.md](spec.md)

## 1. Linear UI Meter Polling & On-Demand CPU Clock Gating

- **Root cause**: `ArrangementView`, `MixerView`, and `SessionView` each called `TryGetTrackMeter(id)` per strip at 30 Hz, and C++ `Engine::trackMeter()` performed a linear `authoring_->tracks` scan per call (`O(T^2)` per visual tick). Meanwhile, 24 built-in devices (`22` effects + `Consort`, `Pentad`) queried `steady_clock::now()` twice on every audio block even when zero cards were open.
- **Decision**: Add `nota_engine_read_track_meters` (1 call, `O(T)`) + `TrackMeterBatch` cache in UI views, and centralize device block timing in `dsp::DeviceTelemetry` gated behind a lock-free `observeRequests_` / `rtLeaseFrames_` ~3s window under default `TelemetryMode::OnDemand`. Switch `AutoFilterDeviceBody::SyncLfoView()` to gate on `Sc(S_SampleRate) > 0` instead of `Sc(S_Cpu) > 0`.

## 2. Sleep Gating, Open-Editor Keep-Awake & Active Submix Buses

- **Root cause**: 10 of 17 built-in synths computed `idle_` without returning early; `RackInstrument`, `DrumRack`, `RackDevice`, `PluginInstrument`, and `PluginEffect` never slept (`isIdle() => false`); and `Engine_Render.cpp` cleared and processed every Group and Return bus unconditionally each block. Conversely, auto-sleeping hosted plug-ins while their native editor stayed open froze plug-in meters/UI.
- **Decision**: Extract lock-free epoch-guarded `dsp::SilenceGate` (`hangover >= 0.25s`, capped `10s`, infinite opt-out) plus global atomic `gNeverSleepDevices` counter and `EditorHost::open_` keep-awake flag (`requiresActiveProcessing()` propagated across racks, buses, and `HybridBufferController`). Track bus writes wait-free in `RenderBusState::ActiveBusMask` across thread/level barriers to skip silent `ChainRest::Settled` Group (`Pass 1.5`) and Return (`Pass 2`) buses and skip clearing untouched bus buffers.

## 3. `TimelineIndex`, Binary-Search Curves & `RenderRoutingPlan`

- **Root cause**: `gatherInstrumentNotes()`, `clipCtlEvents()`, `renderAudioClipsRaw()`, and `renderAudioClipsAt()` scanned every clip and note linearly, `AutomationLane::valueAt()` / `ControllerCurve::movesIn()` scanned breakpoints linearly, and `mixGraph()` / modulation / CV / Arp-CC passes repeatedly scanned `g->tracks` and invoked `resolveNested()` multiple times per block.
- **Decision**: Upgrade curve lookups to `std::upper_bound` ($O(\log P)$), add `TimelineIndex` (sorted start-beat intervals with prefix-max pruning, segment tree, offset padding, conservative unwarped clip bounds, and deterministic original index merge) plus `RenderRoutingPlan` ($O(\log T)$ sorted track lookup, Group ancestry, MIDI-In cache with atomic override check, `routingEpoch_` sidechain key cache, and `RackState::generation`-guarded endpoints).

## 4. `CowSlice`, Session-Sized `StereoScratchArena`, `Eq` SIMD & Clean Math Layer

- **Root cause**: `Engine::cloneTrack()` copied 7 heavy `Track` vectors on every edit and dropped `TimelineIndex`; `RackDevice` (`384 KB`), `RackInstrument` (`128 KB`), and `RenderScratch::leafBuf` (`256 KB`/track) permanently held `16384`-frame buffers; `Eq::process()` recomputed transcendental band math every block without stereo vectorization; and convolution/peak SIMD helpers were duplicated or overly complex.
- **Decision**: Wrap `Track`'s 7 containers in `CowSlice<T>` (`seal()` at publish, `.write()` detach on edit, storage-token `TimelineIndex` reuse), shrink live `StereoScratchArena` / `leafBuf` to `2048` frames (`8×` smaller, expanding safely before offline bounces), add `Eq::BandCache` + flat-stereo `double2` NEON/SSE2 filtering, unify vector math in header-only `Simd.h` (`complexMulAdd`, `dot`, `maxAbs`, `peakAndSumSquares`), delegate `SimdAudio.h` peak/RMS helpers cleanly to `Simd.h`, share `nota::dsp::accumulatePackedSpectrum` (`Convolution.h`) across `Chamber`/`Convolution` on baseline SSE2/NEON, and enable Clang `-fno-math-errno` and Clang/MSVC Release IPO while keeping GCC on baseline `-O3` math semantics.

## 5. `RenderGraphExecutor`, Cooperative `SubSlot`, Planar Chains & macOS AU Parallelism

- **Root cause**: `Engine_Render.cpp` (`5,066` lines) executed Group/Return buses serially, worker threads could not safely help inside single-track racks or heavy polyphonic chords without `AudioThreadPool::dispatch()` reentrancy, adjacent hosted plugins paid deinterleave/interleave overhead per node, and macOS AudioUnits were serialized globally.
- **Decision**: Extract DAG wave scheduler and device-chain runner into `RenderGraphExecutor` (`Engine_Render.cpp` shrinks to `4,019` lines), align `JobSlot` / `LeafWork` to 64 bytes, add RT hysteresis + 2-block fast attack and skip offline per-task `steady_clock::now()`, add lock-free cooperative `SubSlot` + `dsp::RenderExecutor` for multi-chain racks (`ChainScratchBank`) and deterministic `PolyVoiceScheduler` (`WavetableSynth`, `Twin`, `Consort`), run adjacent `prefersPlanar()` plugins through `PlanarStereoBuffer`, and opt vetted macOS AudioUnits into parallel pool workers via `hosting::isParallelRenderSafe()` (with denylist & `NOTA_SERIAL_AUDIOUNIT=1` override).

## 6. Wait-Free Disk Streaming & Opt-In Hybrid Anticipative Buffering

- **Root cause**: Full float32 PCM decoding consumed ~1.38 GB RAM per hour-long stereo stem, and tiny `64–128`-frame live callbacks constrained heavy non-live tracks.
- **Decision**: Compose polymorphic `AudioSource` (`MemoryAudioSource` vs `StreamingAudioSource`) inside `SampleBuffer` above `16 MiB` (`SampleLoadPolicy::Auto`), backed by `dr_libs` decoders, pinned 2-page head cache, 32-page `8192`-frame seqlock ring, bounded-RAM peak/RMS scanner, and sleep-on-idle `StreamingIoScheduler` (`0.0%` idle CPU), plus opt-in `HybridBufferController` (`HybridBufferMode::Auto`, `1024`-frame lookahead chunks into `8192`-frame per-track circular rings) preserving live latency for armed/monitored/open-editor/sidechain/Session tracks and post-fader mixer controls with `workerInFlight` + `epoch_` fencing.
