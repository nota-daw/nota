# Feature Specification: Engine Performance Optimizations for Large Projects

**Feature Branch**: `perf/engine-large-project-optimizations`
**Created**: 2026-11-24
**Consolidated**: 2026-11-26
**Status**: Approved (`nota-daw/nota#1050`)
**Issue**: [`nota-daw/nota#1050`](https://github.com/nota-daw/nota/issues/1050)

## Nota Context *(fill in — drives plan and tasks)*

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Ember)
- [x] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind**:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR expectations),
[`BUILD.md`](../../BUILD.md),
[`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Batch Track Meter Polling & On-Demand Device CPU Telemetry (Priority: P1)

When working on a large session (64–100+ tracks) with the Arrangement headers, Mixer view, or Session mixer open, level meters refresh in a single linear pass (`O(T)`, one native boundary crossing) rather than repeating per-track lookups across all tracks every visual tick. Simultaneously, built-in effects and instruments whose device cards are closed skip per-block OS monotonic wall-clock calls while automatically resuming live CPU load reporting as soon as a user or diagnostic tool inspects a card, and first-frame card visuals render accurately without relying on CPU timing side effects.

**Why this priority**: Refreshing meters individually at 30 Hz turned `T` strips into `O(T^2)` native track scans per tick, while unconditional `steady_clock::now()` pairs across dozens of closed built-in devices burned hundreds of clock queries per audio buffer.

**Independent Test**: `TrackMeterAndTelemetryTests.cs` verifies single-call batch meter parity against `TryGetTrackMeter` across 100 tracks, partial capacity/null safety, track deletion/reordering, `TelemetryMode` (`AlwaysOff`, `OnDemand`, `AlwaysOn`) transitions, lock-free lease renewal via device/instrument scope reads, and `AutoFilterDeviceBody` first-frame visualization without prior CPU timing.

**Acceptance Scenarios**:

1. **Given** a multi-track project producing distinct levels, **When** `ArrangementView`, `MixerView`, or `SessionView` calls `ReadTrackMeters(Span<TrackMeterReading>)` (`nota_engine_read_track_meters`), **Then** all post-fader track meters are returned in graph order in one call matching individual per-track meter queries, and undersized/null buffers safely return total track count without out-of-bounds writes.
2. **Given** built-in effects and instruments under default `TelemetryMode::OnDemand` with closed cards, **When** audio blocks render, **Then** DSP output is bit-identical and wall-clock timing is skipped on the audio thread (`0` clock calls).
3. **Given** a user opens a device card or a diagnostic tool reads `scopeRead`, **When** subsequent blocks render within the 3-second audio-frame lease window, **Then** live CPU telemetry populates via `DeviceTelemetry` (`always-off`, `on-demand`, `always-on` switchable lock-free) and automatically drops back to zero cost after the lease expires.
4. **Given** an `AutoFilter` card opened on an active track whose CPU timing was unobserved prior to open, **When** the UI updates its LFO view, **Then** `AutoFilterDeviceBody` gates active LFO visuals on `S_SampleRate > 0` rather than stale `S_Cpu > 0`.

---

### User Story 2 — Idle Sleep Gating, Open-Editor Keep-Awake & Active Submix Bus Skip (Priority: P1)

Silent built-in instruments, nested racks, hosted VST3/AudioUnit plug-ins, muted/inactive tracks, and unused Group or Return buses automatically stop burning CPU once held notes, sustain, and declared tails finish — while any open native VST3/AudioUnit editor window holds its plug-in and enclosing track/rack/bus path awake cleanly, and free-running third-party plug-ins can opt out per device via `PluginSleepMode.Never`.

**Why this priority**: Previously 10 of 17 built-in synths computed idle flags without early-outing, `RackInstrument`/`DrumRack`/`RackDevice` and `PluginInstrument`/`PluginEffect` never slept, and `mixGraph()` unconditionally cleared and processed every Group and Return bus regardless of signal activity.

**Independent Test**: `SilenceGateAndBusMaskTests.cs` and `HostedRenderPoolTests.cs` verify sleep/wake parity across all gated built-in synths (`BassSynth`, `Consort`, `FluxSynth`, `Monolith`, `OperatorSynth`, `PendulumSynth`, `Pentad`, `Synth`, `VoltSynth`, `WavetableSynth`, plus `needsIdleRender()` clock continuity on `Twin`, `Keys`, `Mosaic`), racks (`RackInstrument`, `DrumRack`, `RackDevice`), `MidiDevice::isIdle()`, hosted VST3/AU probe sleep past tail + `0.25s` hangover, `PluginSleepMode.Never`, open-editor keep-awake (`nota_engine_plugin_editor_sleep_selftest`), and idle Group/Return bus skipping (`0` vs `N` threads bit-identical).

**Acceptance Scenarios**:

1. **Given** a track holding any quiet built-in synth or rack with no incoming MIDI/automation/sidechain activity and decayed tails, **When** blocks render past hangover, **Then** `SilenceGate` marks `isIdle() == true` (`requiresActiveProcessing() == false`), `playEvents()` and `prepareLeafWork()` skip heavy DSP, and exact zero silence is emitted; `wake()` restores rendering sample-accurately on note, sustain, parameter, state, or structural changes.
2. **Given** a hosted `PluginEffect` or `PluginInstrument` in `PluginSleepMode.Auto`, **When** main input, sidechain input, held MIDI/GUI notes, sustain, and `getTailLengthSeconds()` tail + `0.25s` hangover settle silently, **Then** `processBlock()` is skipped until audio, sidechain, MIDI, parameter, state, or sample-rate changes occur, or while `PluginSleepMode.Never` (or infinite tail `>= 1e6s`) is set.
3. **Given** a native VST3 or AudioUnit editor window currently open (`EditorHost::open()`), **When** audio stops or sits on a silent, muted, group/return, or nested-rack track, **Then** `SilenceGate::holdAwake_` and `requiresActiveProcessing()` propagation keep `processBlock()` alive for meter/UI updates without overwriting `PluginSleepMode`.
4. **Given** a Group (`Pass 1.5`) or Return (`Pass 2`) bus receiving no non-silent upstream audio in the current block whose own chain is `ChainRest::Settled` and requires no active processing, **When** `RenderGraphExecutor` runs, **Then** `ActiveBusMask` skips clearing, chain execution, panning, and metering while resetting meter atomics to zero.

---

### User Story 3 — Indexed Timeline Lookups & Precompiled Render Routing Plans (Priority: P1)

Long multi-thousand-note arrangements and 100+ track mixing graphs avoid linear clip/note/curve scans and `O(T^2)` graph routing/target searches on the audio thread. Per-track `TimelineIndex` structures prune clips and notes in logarithmic/prefix-max time, dense automation/CC curves use binary search, and per-graph `RenderRoutingPlan` precompiles sorted track IDs, Group ancestry, MIDI-In caches, sidechain routing epochs, and generation-guarded automation/CV/Arp-CC endpoints on publish.

**Why this priority**: Linear scans across every clip, note, breakpoint, track ID, and nested rack chain inside every 64-sample audio callback cause CPU cost to scale quadratically with song length and track count.

**Independent Test**: `TimelineIndexAndRoutingTests.cs` exercises long timelines with overlapping sustained notes, pickup notes, deactivated clips, track/articulation offsets, tempo changes, loop seams, warped/unwarped/reversed audio clips, dense automation/CC lanes, nested groups (`depth >= 3`), live lock-free `setMidiFromTrackId()`, cross-track CV links, and live nested-rack mutations against bit-exact serial baselines.

**Acceptance Scenarios**:

1. **Given** a MIDI or audio track with hundreds of clips and thousands of notes/events, **When** `gatherInstrumentNotes()`, MIDI clip volume envelopes, `clipCtlEvents()`, `renderAudioClipsRaw()`, or `renderAudioClipsAt()` run, **Then** `TimelineIndex` returns only overlapping candidates in deterministic original `(originalClipIndex, originalNoteIndex)` order with symmetric offset padding and zero audio-thread allocations (`AR-6`).
2. **Given** dense breakpoints on `AutomationLane::valueAt()` or `ControllerCurve::movesIn()`, **When** queried during rendering, **Then** lookup executes via $O(\log P)$ `std::upper_bound` with bit-identical results.
3. **Given** a published `Graph`, **When** `RenderRoutingPlan` is active, **Then** track ID lookups resolve in $O(\log T)$, Group mute/solo ancestry and default MIDI-In sources resolve in $O(1)$ (falling back to $O(\log T)$ when atomically mutated via `setMidiFromTrackId()`), `routingEpoch_` avoids rebuilding sidechain key levels on unchanged routing, and automation/CV/Arp-CC targets access top-level or `RackState::generation`-matched nested rack devices directly without linear track/chain walks.

---

### User Story 4 — Copy-on-Write Track Snapshots, Session-Sized Scratch Arenas & Vectorized Filtering (Priority: P1)

Editing a track or automation lane in a large song no longer deep-copies every clip, note, take, automation, modulator, and CV vector across `cloneTrack()`. Heavy `Track` containers use Copy-on-Write (`CowSlice<T>`) preserving `TimelineIndex` across non-clip edits; rack and leaf scratch buffers allocate session-sized memory (`2048` live frames, growing before offline renders up to `kMaxBlock`) for L2/L3 cache residency; `Nota EQ-8` caches static biquad coefficients and runs a vectorized `double2` stereo path; and runtime `SimdDispatch` unlocks AVX2+FMA kernels on x86_64 alongside NEON on ARM64 and Clang/MSVC ThinLTO/`-fno-math-errno`.

**Why this priority**: Deep-cloning 7 vectors per track edit stalled UI interactions, 384 KB fixed `16384`-frame inline rack arrays plus 256 KB per-track scratch thrashed CPU caches at 64–256 frame buffers, and per-block `Eq` trigonometry ran redundantly on stationary bands.

**Independent Test**: `TrackSnapshotArenaSimdTests.cs` verifies multi-edit CoW snapshot isolation (undo/redo integrity, `TimelineIndex` storage-token reuse, single-pass `recomputePdc()`), live `128`-frame to offline `8192`-frame scratch growth, static/gliding/MS/LR `Eq` accuracy (`< -120 dBFS` vs scalar double), `SimdDispatch` capabilities, and `0` vs `N` thread bit-identity.

**Acceptance Scenarios**:

1. **Given** a published `Track` whose `CowSlice` members were sealed by `publishRaw()`, **When** an edit clones the track to modify one container (`.write()`), **Then** untouched containers share immutable `std::shared_ptr<const std::vector<T>>` storage without copying, and `timelineIndex` is reused in $O(1)$ whenever clip/articulation backing tokens match.
2. **Given** live playback (`<= 2048` frames) vs offline bounce (`> 2048` frames, up to `16384`), **When** preparing racks (`StereoScratchArena`) and `Engine::RenderScratch::leafBuf`, **Then** live sessions stay compact (`8×` smaller) with zero RT allocations (`AR-6`), while `renderOffline()` expands capacity cleanly before invoking workers.
3. **Given** stationary or modulated `Eq` bands, **When** processing stereo audio, **Then** unchanged bands reuse cached `BiquadCoeffs`, flat stereo runs in 128-bit `double2` SIMD (`float64x2_t` NEON / `__m128d` SSE2), and `SimdDispatch` selects AVX2+FMA on capable x86_64 CPUs without raising baseline ISA requirements.

---

### User Story 5 — Parallel Render DAG Execution, Intra-Track Parallelism & Planar Plugin Chains (Priority: P1)

Group buses (`Pass 1.5`), Return buses (`Pass 2`), multi-chain racks (`RackInstrument`, `DrumRack`, parallel/select `RackDevice`), heavy polyphonic synths (`Twin`, `Consort`, `WavetableSynth`), and consecutive hosted VST3/AudioUnit plug-ins scale across CPU cores without intermediate interleaved buffer conversions or monolithic render code inside `Engine_Render.cpp`. On macOS, vetted independent AudioUnit instances safely render in parallel across pool workers.

**Why this priority**: Serial Group/Return passes, single-core execution of heavy single-track racks and high-polyphony chords, repeated deinterleave/interleave passes between adjacent hosted plugins, and blank AudioUnit serialization bottlenecked multi-core scaling.

**Independent Test**: `RenderDagParallelTests.cs` and `HostedRenderPoolTests.cs` (`--hosted-au-pool` on macOS) verify bit-exact parity (`maxdiff == 0.0` for `0` vs `N` threads) across multi-depth Group/Return sidechain DAGs, multi-chain racks, heavy 16-voice `Twin`/`Consort`/`WavetableSynth` chords, planar multi-plugin chains (`PluginInstrument -> PluginEffect x 3`), macOS AudioUnit worker safety, and RT hysteresis (`0.0%` idle / light RT worker spin).

**Acceptance Scenarios**:

1. **Given** active Group (`maxDepth..1`) and Return buses (including intra-level sidechain keys), **When** `RenderGraphExecutor` executes waves, **Then** independent buses render concurrently into pre-allocated scratch buffers and fold in strict ascending track index order (`0` vs `N` threads identical), while shrinking `Engine_Render.cpp`.
2. **Given** a multi-chain rack with `>= 2` parallel-safe active chains or a `Twin`/`Consort`/`WavetableSynth` voice load meeting deterministic batch thresholds, **When** `dsp::RenderExecutor` / `PolyVoiceScheduler` runs, **Then** top-level single-track dispatch or the lock-free `AudioThreadPool` cooperative `SubSlot` accelerates chains/voices concurrently and reduces in canonical index order.
3. **Given** adjacent `prefersPlanar()` devices (`PluginInstrument` / `PluginEffect`), **When** `RenderGraphExecutor` executes the chain, **Then** audio stays in `PlanarStereoBuffer` / `PlanarAudioSpan` across contiguous planar inserts without redundant interleaved round-trips, and vetted macOS AudioUnits opt into worker execution via `hosting::isParallelRenderSafe()` (with denylist and `NOTA_SERIAL_AUDIOUNIT=1` override).
4. **Given** real-time device rendering vs offline bounces, **When** load fluctuates around single-core capacity, **Then** RT pool exit hysteresis (`0.72` / `0.75`) plus 2-block fast attack avoid late-block probe bursts, while offline renders skip per-task `steady_clock::now()`.

---

### User Story 6 — Bounded-Memory Disk Streaming & Hybrid Anticipative Buffering (Priority: P2)

Long multi-minute or hour-long audio stems (`WAV`, `FLAC`, `MP3`) whose uncompressed float32 PCM exceeds a configurable byte threshold (`default 16 MiB`) stream directly from disk via a wait-free seqlock page ring (`StreamingAudioSource`) fed by a sleep-on-idle I/O scheduler — preserving waveforms, warping, reversing, seeking, `nota_sample_read`, and bit-exact offline rendering with a fraction of full-RAM memory. Optionally (`HybridBufferMode.Auto`), `HybridBufferController` pre-renders deterministic non-live Arrangement tracks in `1024`-frame background chunks while keeping armed, monitored, live-MIDI, Session, sidechain, open-editor, and fader/mute/solo controls at hardware buffer latency.

**Why this priority**: Decoding full float32 PCM for long multitrack stems exhausted RAM (`~1.38 GB` per 1-hour stereo stem), while tiny `64–128` sample driver buffers left non-live tracks starved for DSP headroom.

**Independent Test**: `DiskStreamingAndHybridBufferTests.cs` tests large WAV/FLAC streaming (`>= 80%` RAM savings vs memory mode), `getClipPeaks` / `getClipSourcePeaks`, `nota_sample_read`, seeks/offsets/warps/reverse playback (`0` vs `N` offline bit-exact), `0.0%` idle CPU (`nota_idle_cpu 5 5`), plus `HybridBufferController` live-track bypass, `cacheHits > 0` on non-live tracks, instant epoch invalidation on edits/seeks/params/arming, and audio parity against `HybridBufferMode.Off`.

**Acceptance Scenarios**:

1. **Given** a WAV/FLAC/MP3 file exceeding `streamThresholdBytes` under `SampleLoadPolicy.Auto` (or any seekable file under `Stream`), **When** loaded onto a track or via `AudioImportJob`, **Then** `SampleBuffer` attaches a `StreamingAudioSource` (`kPageFrames = 8192`, `kRingPages = 32`, `kHeadPages = 2` pinned, bounded peak/RMS scan, `samples` empty), keeping audio-thread reads `AR-6` wait-free while serving offline bounces and `nota_sample_read` synchronously.
2. **Given** `HybridBufferMode::Auto` with background render threads active in real-time device mode, **When** deterministic non-live Arrangement tracks run alongside armed/monitored/live-MIDI/Session/sidechain/open-editor tracks, **Then** live tracks and mixer fader/pan/mute/solo/sends run directly on the audio callback, eligible tracks serve `1024`-frame lookahead chunks (`4096`-frame target, `8192`-frame ring), and edits/seeks/param changes/arming increment `epoch_` for race-free handback.
3. **Given** transport is stopped/idle, **When** streaming sources or `HybridBufferController` are present, **Then** background threads sleep on condition variables consuming `0.0%` CPU and join cleanly on teardown.

---

### Edge Cases & Invariants

- **Strict `AR-6` (Zero RT Allocations / Locks / OS I/O)**: All telemetry leases, `SilenceGate`, `ActiveBusMask`, `TimelineIndex` queries, `RenderRoutingPlan` lookups, `CowSlice::read()`, `StereoScratchArena` reads, `RenderExecutor` / `SubSlot`, `PlanarStereoBuffer` runs, `StreamingAudioSource` RT page reads, and `HybridBufferController` cache copies run lock-free and allocation-free on audio/worker threads.
- **Deterministic IEEE-754 Reduction (`0` vs `N` Threads)**: Multi-group waves, return waves, multi-chain racks, and polyphonic voice batches determine splitting strictly from deterministic DSP state and accumulate strictly in ascending index order (`maxdiff == 0.0`).
- **Concurrent Mutation Safety**:
  - `SilenceGate` epoch guard prevents a finishing audio block from sleeping over a concurrent UI/MIDI `wake()`.
  - `EditorHost::open_` atomic state keeps silent/muted/group/return/nested-rack/hybrid plugin editors awake without mutating `PluginSleepMode`.
  - `RenderRoutingPlan` checks live atomic `Track::midiFromTrackId()` ($O(\log T)$ fallback) and `RackCore::RackState::generation` (`resolveNested()` fallback on async rack edits).
  - `TimelineIndex` stores clip/articulation backing storage tokens (`matchesTrack`) so stale indices are never reused after edits.
  - `HybridBufferController` enforces single-owner `workerInFlight` handback and `drainAndFence()` on structural publish/stop.
- **Special Instrument / Timing Cases**: `Twin`, `Keys`, `Mosaic`, and `RhythmMachine` keep lightweight `needsIdleRender()` continuity where required (`Mosaic`/`Sampler` declare `outputAlwaysSilent()` when empty/idle); `WavetableSynth`, `RackInstrument`, `DrumRack`, and `RackDevice` never strand retired `SnapshotHistory` allocations (`hasPendingRetire()`); unwarped audio clip index bounds conservatively handle variable `TempoMap`/sample rates; MIDI index search windows pad for track/articulation shifts, pickup notes (`< 0.0`), deactivated clip note-offs, and loop seams.
- **Universal Binary Safety**: `-mavx2 -mfma` applies solely to `SimdAvx2.cpp` on x86_64; Clang/MSVC Release builds enable ThinLTO/LTCG and `-fno-math-errno` (Clang) while keeping GCC on baseline `-O3` math semantics.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001 (Batch Meters)**: Engine and desktop views (`ArrangementView`, `MixerView`, `SessionView`) MUST support and use a single-call $O(T)$ post-fader meter snapshot (`nota_engine_read_track_meters` / `ReadTrackMeters`), keeping single-track `nota_engine_track_meter` compatible and null/undersized buffers safe.
- **FR-002 (On-Demand Device Telemetry)**: Built-in timed effects and instruments (`22` effects + `Consort` & `Pentad`) MUST delegate timing through `dsp::DeviceTelemetry`, skipping OS clock queries under `TelemetryMode::OnDemand` outside an active ~3s `scopeRead` observation lease, exposing `nota_engine_set_device_telemetry` / `nota_engine_device_telemetry`, and keeping `AutoFilterDeviceBody` LFO visuals gated on `S_SampleRate > 0`.
- **FR-003 (Sleep Gating & Keep-Awake)**: `dsp::SilenceGate`, all gated built-in synths, `RackDevice`/`RackInstrument`/`DrumRack`, `MidiDevice`, `PluginInstrument`, and `PluginEffect` MUST auto-sleep past declared tails (`>= 0.25s`, capped `10s`, infinite opt-out), wake lock-free on signal/MIDI/param/state/structural events, respect `NotaPluginSleepMode` (`nota_plugin_set_sleep_mode`, `nota_plugin_sleep_mode`), and stay awake while any native plugin editor window is open (`EditorHost::open_`, propagated through racks, buses, and Hybrid mode, verified by `nota_engine_plugin_editor_sleep_selftest`).
- **FR-004 (Active Submix Buses)**: `RenderBusState` (`ActiveBusMask`) MUST track bus writes lock-free across worker/level barriers so idle settled Group (`Pass 1.5`) and Return (`Pass 2`) buses skip buffer clearing, effect processing, panning, and metering.
- **FR-005 (Timeline Indexing & Curve Search)**: `nota::TimelineIndex` MUST index MIDI clips, padded MIDI notes, and warped/unwarped Audio clips at `publishRaw()` and query in $O(\log N + k)$ deterministic original order; `AutomationLane::valueAt()` and `ControllerCurve::movesIn()` MUST use $O(\log P)$ binary search.
- **FR-006 (Precompiled Routing & Generation Guards)**: `nota::RenderRoutingPlan` on `Graph` MUST provide sorted $O(\log T)$ track lookup, Group ancestry, MIDI-In fast cache with atomic override fallback, `routingEpoch_` sidechain key caching, indexed automation/modulator/CV/Arp-CC targets, and `RackState::generation`-validated nested rack endpoints.
- **FR-007 (Copy-on-Write Track Snapshots)**: `Track` heavy snapshot vectors (`clips`, `takes`, `midiClips`, `sessionSlots`, `automation`, `modulators`, `cvLinks`) MUST use `CowSlice<T>` (`TrackContent.h`) sealed on publish, preserving `TimelineIndex` across non-clip edits, skipping unchanged `recomputeRouting()` / telemetry propagation, and computing `recomputePdc()` in a single pass.
- **FR-008 (Session Arenas, EQ SIMD & Runtime Dispatch)**: `RackDevice`, `RackInstrument`, `DrumRack`, and `Engine::RenderScratch::leafBuf` MUST size scratch arenas cleanly to the negotiated session frame budget (`2048` live frames, growing up to `kMaxBlock = 16384` for offline bounces); `Eq` MUST cache unchanged per-band `BiquadCoeffs` and run flat stereo in `double2` NEON/SSE2 SIMD; `SimdDispatch` / `SimdAvx2.cpp` MUST select AVX2+FMA/SSE2/NEON safely at runtime; and `CMakeLists.txt` MUST configure Clang `-fno-math-errno` and Clang/MSVC Release IPO without regressing GCC or older x86_64 CPUs.
- **FR-009 (Render DAG, Intra-Track Parallelism & Planar AU Chains)**: `RenderGraphExecutor` MUST orchestrate leaf waves, Group depth/key waves, Return key waves, and planar/interleaved chains outside `Engine_Render.cpp` with `64`-byte aligned `JobSlot`/`LeafWork`, RT hysteresis + 2-block fast attack, lock-free `dsp::RenderExecutor`, `ChainScratchBank` parallel racks, `dsp::PolyVoiceScheduler` (`Twin`, `Consort`, `WavetableSynth`), `PlanarStereoBuffer` contiguous `prefersPlanar()` plugin chains, and vetted macOS AudioUnit worker parallelism (`hosting::isParallelRenderSafe()`).
- **FR-010 (Disk Streaming & Hybrid Buffering)**: `AudioSource` / `StreamingAudioSource` MUST stream WAV/FLAC/MP3 files above configurable PCM byte threshold (`default 16 MiB`) via wait-free seqlock page rings + sleep-on-idle `StreamingIoScheduler` with bounded peak/RMS overviews and sync offline/`nota_sample_read` fallback; `HybridBufferController` MUST provide opt-in (`HybridBufferMode::Auto`) lookahead buffering for deterministic non-live tracks with live/armed/editor/sidechain bypass, live faders/sends, single-owner DSP safety, and C ABI / `IAudioEngine` configuration & stats.

### Key Entities

- **`TrackMeterReading` / `NotaTrackMeterEntry` & `DeviceTelemetry` / `TelemetryMode`**: Batch meter entry and shared lease-gated per-block CPU timer.
- **`SilenceGate`, `PluginSleepMode`, `ActiveBusMask` (`RenderBusState`)**: Lock-free sleep gate, per-device sleep policy, and cross-barrier bus activity masks.
- **`TimelineIndex` & `RenderRoutingPlan`**: Immutable per-track spatial/interval clip/note index and per-graph compiled routing, ancestry, and rack-generation-guarded endpoint tables.
- **`CowSlice<T>`, `StereoScratchArena`, `ChainScratchBank`, `Eq::BandCache`, `SimdDispatch`**: Copy-on-Write track snapshot containers, session-sized aligned scratch banks, cached SIMD filter kernels, and runtime CPU dispatch.
- **`RenderGraphExecutor`, `RenderExecutor`, `PolyVoiceScheduler`, `PlanarStereoBuffer`**: Modular DAG wave scheduler, lock-free cooperative sub-task bridge, polyphonic synth batcher, and aligned planar stereo chain buffer.
- **`AudioSource`, `StreamingAudioSource` / `StreamingIoScheduler`, `HybridBufferController`**: Polymorphic memory/disk audio sources with wait-free seqlock page rings and opt-in hybrid lookahead scheduler.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001 (Full Test & Self-Test Pass)**: `./scripts/build.sh` compiles universal native (`arm64 + x86_64`) and .NET 10 targets cleanly and passes `nota-scanner-selftest`, `nota_engine_plugin_editor_sleep_selftest`, and all `Nota.SmokeTest` suites including `TrackMeterAndTelemetryTests`, `SilenceGateAndBusMaskTests`, `TimelineIndexAndRoutingTests`, `TrackSnapshotArenaSimdTests`, `RenderDagParallelTests`, `DiskStreamingAndHybridBufferTests`, and `HostedRenderPoolTests` (`--hosted-au-pool` on macOS).
- **SC-002 (Idle CPU & RT Gate)**: `dotnet run --project tests/Nota.SmokeTest --no-build -c Release -- --perf` exits with `PERF PASSED`, and `nota_idle_cpu 5 5` measures `0.0%` idle CPU even with streaming stems and hybrid buffering configured.
- **SC-003 (Bit-Exact Parallel Parity & Filter Fidelity)**: `0` vs `N` render threads remain sample-identical (`maxdiff == 0.0`) across built-in/hosted instruments, racks, polyphonic synths, groups/returns, CoW/arena transitions, planar chains, and offline streamed bounces; vectorized `Eq` stays within `< -120 dBFS` of scalar double reference.
- **SC-004 (Memory & Scaling Wins)**: `StreamingAudioSource` cuts resident sample PCM RAM on streamed stems by `>= 80%` with accurate waveforms and `nota_sample_read`; `Engine_Render.cpp` shrinks from `5,066` lines to `4,019` lines via `RenderGraphExecutor`; `./scripts/bench-engine.sh` and scenario benchmarks confirm large-project CPU/wall gains while respecting project CI performance thresholds.
- **SC-005 (Code Quality & Readability)**: All new headers/sources carry two-line `SPDX-License-Identifier: AGPL-3.0-only` headers, zero third-party dependencies are added, `AR-4`–`AR-7` rules hold cleanly, and engine/ABI boundaries expose clear domain abstractions.

## Nota Compliance Checklist

- [x] Every new source and test file carries the two-line AGPL-3.0-only SPDX header (`CONTRIBUTING.md`)
- [x] Zero new third-party dependencies (`dr_libs` & existing JUCE confinement inside `pluginhost/` reused)
- [x] Audio-thread rules (`AR-4`, `AR-5`, `AR-6`) verified: zero RT allocations, locks, or blocking I/O across all fast paths
- [x] Managed/native crossings strictly go through pure C ABI types/functions in `nota_engine.h` (`AR-7`)
- [x] Concrete feature smoke test suites added under `tests/Nota.SmokeTest/` and registered in `Program.cs` / `--perf`
- [x] `CHANGELOG.md` `[Unreleased]` updated cleanly for `#1050` (`Added`, `Changed`, `Fixed`)
