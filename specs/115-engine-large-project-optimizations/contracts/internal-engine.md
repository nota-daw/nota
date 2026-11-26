# Internal Engine Contracts (`nota-daw/nota#1050`)

**Spec**: [../spec.md](../spec.md)

## 1. Telemetry & Sleep Gate Contracts (`AR-6`)

- `DeviceTelemetry::ScopedBlockTimer` performs only a relaxed atomic epoch load + thread-local `rtLeaseFrames_` decrement while closed under `TelemetryMode::OnDemand`; `steady_clock::now()` executes solely when actively observed (`scopeRead`) or under `AlwaysOn`.
- `SilenceGate` uses an epoch guard (`wakeEpoch_` vs `startEpoch`) so block-end sleep transitions never clobber concurrent cross-thread `wake()` calls.
- Hosted plugin editors track `EditorHost::open_` atomically and hold `SilenceGate::holdAwake_` active while visible; `requiresActiveProcessing()` propagates through `RackCore`, `RackDevice`, `RackInstrument`, `DrumRack`, `RenderGraphExecutor`, and `HybridBufferController` without overwriting `PluginSleepMode`.

## 2. `TimelineIndex` & `RenderRoutingPlan` Contracts

- `TimelineIndex::buildForTrack(const Track& t)` executes off-RT during `publishRaw()`. Query helpers (`forEachMidiClipCandidate`, `forEachMidiNoteInClip`, `forEachAudioClipCandidate`) are lock-free, `const`, zero-allocation (`AR-6`), pad beat windows symmetrically for track/articulation offsets, bound unwarped audio clips conservatively under dynamic tempo maps/sample rates, and preserve deterministic `(originalClipIndex, originalNoteIndex)` traversal order.
- `RenderRoutingPlan::build(const Graph& g)` precomputes sorted `TrackId -> trackIndex` ($O(\log T)$), Group ancestry chains, `leafRoutes` (`expectedMidiFromId` checked at runtime with $O(\log T)$ `findTrackIndex` fallback), sidechain key consumers, and compiled automation/CV/Arp-CC targets.
- Nested rack endpoints verify `rack->liveGeneration() == target.expectedRackGen` before direct access, falling back safely to `resolveNested()` if a rack mutated asynchronously between graph publishes.

## 3. Copy-on-Write Track & Scratch Arena Contracts

- Every `Track` inside a published `Graph` has `sealContent()` invoked by `publishRaw()`, dropping `CowSlice::mutable_`. `cloneTrack()` copies shared immutable storage pointers and preserves `TimelineIndex` when clip/articulation backing storage tokens match.
- Every mutating operation on `clips`, `takes`, `midiClips`, `sessionSlots`, `automation`, `modulators`, or `cvLinks` accesses storage via `.write()` before publishing, isolating concurrent audio renders and undo/redo history snapshots.
- `StereoScratchArena<N>` and `Engine::RenderScratch::leafBuf` pre-size for `<= 2048` live frames and expand up to `kMaxBlock` (`16384`) inside `renderOffline()` / `ensureBlockCapacity()` before rendering starts.

## 4. `Eq` Scalar Parity & SIMD Safety Contract

- Cached `Eq::BandCache` coefficients match uncached `buildBand` identically for unchanged `(sr, freq, gain, q, scale, type, slope, ch)`.
- Flat-stereo `double2` SIMD biquad loops preserve scalar DF2T multiply-add semantics within `< -120 dBFS` (`< -240 dBFS` on double2 SIMD) and produce `0.0` sample diff across `0` vs `N` threads.
- `-mavx2 -mfma` compiler flags apply strictly to `src/dsp/math/SimdAvx2.cpp` on `x86_64`; Clang uses `-fno-math-errno`, and Clang/MSVC Release targets enable IPO while keeping GCC on baseline `-O3`.

## 5. Cooperative Parallel Execution & Determinism Contracts (`0` vs `N` == `0.0`)

- Inside `AudioThreadPool`, workers or the calling audio thread inside `dispatch()` MUST NOT invoke `dispatch()` reentrantly. Intra-rack and intra-synth sub-tasks route through `dsp::RenderExecutor::parallelFor()`: using top-level `dispatch()` when running standalone (`m == 1`), or claiming the lock-free cooperative `SubSlot` when inside an active pool dispatch.
- Parallel splits (`Group` depth/key waves, `Return` key waves, `>= 2` parallel-safe rack chains, `PolyVoiceScheduler` batches) depend solely on deterministic DSP/scene state (never thread count or runtime queue timing) and reduce buffers strictly in ascending index order (`maxdiff == 0.0`).
- Light real-time playback (`!offline && !rtParallel_`) disables parallel sub-tasks and applies pool exit hysteresis + 2-block fast-attack detection so idle/light worker usage stays near `0%` without late-block bursts; offline renders omit per-task `steady_clock::now()`.
- Contiguous `prefersPlanar()` devices stay in `PlanarStereoBuffer` / `PlanarAudioSpan` until an interleaved boundary/tap converts back; `hosting::isParallelRenderSafe()` vets VST3 & non-denylisted macOS AudioUnits (`NOTA_SERIAL_AUDIOUNIT=1` override).

## 6. Disk Streaming & Hybrid Lookahead Contracts

- `renderOffline()` and `nota_sample_read()` permit synchronous decoder page fills (`allowSyncFill = true`) and bypass `HybridBufferController` so offline bounces stay 100% deterministic (`maxdiff == 0.0` vs in-memory decoding and `0` vs `N` threads).
- Real-time device callbacks read `StreamingAudioSource` pages wait-free via `PageSlot` seqlocks (`allowSyncFill = false`, `0` OS/disk syscalls on RT thread) and serve pre-rendered `1024`-frame chunks for eligible non-live Arrangement tracks under `HybridBufferMode::Auto` while executing live/armed/monitored/open-editor/sidechain tracks and mixer controls (fader/pan/mute/solo/sends) at driver block latency.
- `HybridBufferController` enforces single-owner DSP access per track (`workerInFlight` handback + `epoch_` increment + `drainAndFence()` on `publishRaw()` / `stop()`) and parks background workers completely when stopped (`0.0%` idle CPU).
