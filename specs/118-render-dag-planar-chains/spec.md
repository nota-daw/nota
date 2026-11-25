# Feature Specification: Render DAG Execution, Planar Effect Chains & Intra-Track Parallelism

**Feature Branch**: `perf/render-dag-planar-chains`  
**Created**: 2026-11-24  
**Status**: Approved (`nota-daw/nota#1050` — Phase 5)  
**Parent Issue**: [`nota-daw/nota#1050`](https://github.com/nota-daw/nota/issues/1050)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Multi-Core Scaling for Groups, Returns & Cache-Clean Task Scheduling (`RenderGraphExecutor`) (Priority: P1)

When mixing a project with multiple independent Group buses, nested group hierarchies, Return buses (reverbs, delays, parallel compression), and sidechain-linked buses, real-time and offline renders must execute independent Group and Return workloads concurrently across render pool workers rather than funneling all groups (`Pass 1.5`) and returns (`Pass 2`) serially through one core. Furthermore, the audio thread pool must avoid cache-line false sharing, skip redundant per-task clock reads in offline renders, and avoid periodic single-thread probing bursts that cause avoidable glitches under live real-time load, while extracting parallel graph orchestration out of the 5,000-line `Engine_Render.cpp` monolith into dedicated domain modules.

**Why this priority**: Currently only leaf tracks (`Pass 1`) use the worker pool; Pass 1.5 (`groups`) and Pass 2 (`returns`) run sequentially on the audio callback thread even when 8–16 groups/returns carry expensive effects or hosted plug-ins. Meanwhile `LeafWork` and `AudioThreadPool::JobSlot` are unaligned (`< 64 B` stride), per-task `steady_clock::now()` timers run unconditionally even during untimed offline exports, and `Engine_Render.cpp` mixes mixer logic with pool scheduling and probing state machines.

**Independent Test**: Build a 32-track session routed through 6 independent `Group` tracks (plus nested sub-groups) and 4 `Return` buses loaded with heavy DSP effects (reverb/delay/EQ); verify bit-identical output (`diff == 0.0`) across `0` vs `N` worker threads, zero glitches in real-time and offline modes, `0.0%` idle CPU, and measurable wall-clock/CPU reduction on multi-core offline bounces and real-time overload.

**Acceptance Scenarios**:
1. **Given** multiple active `Group` tracks at the same hierarchy depth (or independent `Return` buses) with no unrendered intra-level sidechain consumer edge between them, **When** rendering in offline export or overloaded real-time mode, **Then** eligible buses process concurrently on `AudioThreadPool` workers into per-bus scratch buffers and accumulate into parent groups/master in fixed deterministic track order (`diff == 0.0` vs `0` threads).
2. **Given** `AudioThreadPool` slots (`JobSlot`) and per-leaf task descriptors (`LeafWork`), **When** worker threads run concurrently, **Then** slots/descriptors are cache-line aligned (`alignas(64)`) to prevent false sharing, offline renders bypass redundant per-task `steady_clock::now()` calls, and real-time mode-switching applies hysteresis so steady overload never oscillates into late-block bursts while light playback stays parked (`< 2%` worker CPU).
3. **Given** a multi-core refactor of `Engine_Render.cpp`, **When** inspecting source modularity, **Then** graph render staging and execution live in concise, domain-named modules (`RenderGraphExecutor.h/.cpp`) rather than further inflating `Engine_Render.cpp`.

---

### User Story 2 — Intra-Track Parallelism for Multi-Chain Racks & Heavy Polyphonic Synths (Priority: P1)

When a project plays a single heavy track — or a small track count where idle render threads sit waiting at the pool barrier — carrying a multi-chain `Instrument Rack`, `Drum Rack`, or `Audio Effect Rack` (parallel/select modes), or a heavy polyphonic synthesizer (`Twin`, `Consort`, `WavetableSynth`) sustaining high polyphony, idle worker threads must assist with chain/voice sub-tasks so heavy single-track or few-track workloads scale beyond one CPU core while maintaining sample-exact determinism.

**Why this priority**: Assigning whole tracks to workers cannot accelerate a single track playing a 16-voice `Twin`/`Consort`/`WavetableSynth` chord or a 12-pad `DrumRack` / `RackInstrument` layering heavyweight synths and insert chains. Those voices and chains are naturally data-parallel across the audio callback.

**Independent Test**: Render a heavy 16-voice polyphonic chord on `Twin`, `Consort`, and `WavetableSynth`, plus multi-chain `RackInstrument`, `DrumRack`, and parallel `RackDevice` sessions on `0` vs `N` threads; verify sample-exact parity (`diff == 0.0`), zero worker CPU spin when light/real-time serial mode is active, and measurable multi-core speedup on `1`-track / few-track multi-core exports.

**Acceptance Scenarios**:
1. **Given** a `RackInstrument`, `DrumRack`, or parallel/select `RackDevice` with `>= 2` active parallel-capable chains (all chain devices/instruments parallel-safe) executing in an active parallel context, **When** chains render, **Then** independent chains execute concurrently via the lock-free `dsp::RenderExecutor` abstraction into pre-allocated multi-slot rack arenas and sum in deterministic chain index order (`diff == 0.0` vs `0` threads).
2. **Given** `Twin`, `Consort`, or `WavetableSynth` rendering at least `>= kParallelVoiceThreshold` active voices over sufficient block length, **When** synthesizing voices, **Then** `PolyVoiceScheduler` partitions active voice batches deterministically into pre-allocated scratch buffers so idle pool workers collaborate cleanly (`diff == 0.0` vs `0` threads, zero `AR-6` allocations).
3. **Given** a light real-time session fitting on a single core or a single-chain hosted rack, **When** rendering, **Then** intra-track sub-tasks execute inline on the calling thread with zero worker wakeups and preserve existing serial fallback expectations.

---

### User Story 3 — Planar Chain Buffers (`AudioBufferView` / `PlanarStereoBuffer`) & Safe macOS AudioUnit Parallelism (Priority: P2)

When a track or bus chains multiple planar-native devices (such as consecutive hosted VST3/AU effects or a hosted instrument followed by hosted effects), audio buffers must stay in planar L/R layout across adjacent planar devices instead of converting planar → interleaved → planar between every plug-in. On macOS, audited independent AudioUnit plug-in instances must safely execute in parallel on worker threads (with an explicit opt-out / thread-safety denylist for legacy or non-reentrant AudioUnits).

**Why this priority**: Currently `PluginInstrument` renders planar output, calls `interleaveStereoAdd()`, and immediately the next `PluginEffect` calls `deinterleaveStereo()`, processes planar `AudioBuffer`, calls `interleaveStereoWetDryMix()`, and the next `PluginEffect` deinterleaves it back again. Additionally, macOS AudioUnits are hard-restricted to serial execution (`pluginFormatName == "VST3"`) even though independent `AudioUnitPluginInstance` objects own separate CoreAudio handles and buffers.

**Independent Test**: Chain 3 consecutive hosted probe plug-ins (and an instrument + 3 hosted effects) on tracks; verify sample accuracy against serial interleaved execution (`<= -120 dBFS` or exact parity depending on FMA), verify planar buffer transitions work seamlessly alongside interleaved built-in effects, and test real macOS AudioUnit instances in parallel `HostedRenderPoolTests` (`0` vs `N` thread output parity and zero reentrancy/NaNs).

**Acceptance Scenarios**:
1. **Given** a contiguous sub-sequence of `prefersPlanar()` devices (or `PluginInstrument` → `PluginEffect`s) on a track or bus, **When** `processLeafDevices` / bus device chain executes, **Then** audio enters `PlanarStereoBuffer` once, passes directly between consecutive planar devices as `PlanarAudioSpan` without per-plugin interleaved conversion round-trips, and converts back cleanly before subsequent interleaved devices or fader/taps.
2. **Given** a macOS AudioUnit plug-in instance without known shared-state restrictions, **When** checking `supportsParallelRender()`, **Then** independent AU instances opt into parallel worker execution (verified by `nota-render-probe` AU/VST3 and system AU multi-thread stress tests), while any denylisted or single-instance restricted AU safely retains serial fallback.

---

### Edge Cases & Invariants

- **Zero Reentrant Pool Corruption**: Cooperative sub-task helpers (`RenderExecutor` / `AudioThreadPool` sub-slot) never allow a worker thread inside a top-level `dispatch()` to mutate outer job generation `epoch_` or corrupt completion counts; atomic CAS state guarantees zero ABA hazards and bounded single-load overhead when unused.
- **Deterministic IEEE-754 Reduction Order (`RenderPoolTests` bit-identity)**: Groups, Returns, multi-chain racks, and partitioned polyphonic voices reduce batch/child outputs strictly in deterministic ascending index order regardless of worker completion timing, using pre-determined conditions independent of thread count so `0` threads == `N` threads (`maxdiff == 0.0`).
- **Strict `AR-6` (Zero RT Allocations / Locks)**: Group/Return parallel scratch buffers, multi-slot rack arenas, planar track buffers, and synth voice-batch scratch buffers are sized during graph/device preparation or `RenderScratch` publish on non-RT threads, never on hardware or worker threads.
- **AudioUnit & Sidechain Safety**: AU instances sharing components known to be unsafe or non-parallel-safe (or when disabled via diagnostics flag) keep serial execution; sidechain key edges within Groups or Returns are ordered across DAG waves before parallel execution so no same-block key hazard occurs.
- **Hard Readability Gate (`#1050`)**: All new mechanisms use concise, domain-named modules (`AudioBufferView.h`, `RenderExecutor.h`, `PolyVoiceScheduler.h`, `RenderGraphExecutor.h/.cpp`), with zero new monolithic sprawl in `Engine_Render.cpp`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST decouple graph parallel orchestration and bus reduction from `Engine_Render.cpp` into clean, readable `RenderGraphExecutor` abstractions covering leaf levels, group depth/key waves, and return key waves, reducing code volume inside `Engine_Render.cpp::mixGraph()`.
- **FR-002**: System MUST align `AudioThreadPool::JobSlot` and `Engine::LeafWork` to cache lines (`64` bytes), gate per-task timing measurements off in untimed offline renders, and enforce hysteresis + fast-attack detection on real-time pool engagement to prevent late-block probe bursts.
- **FR-003**: System MUST provide a lock-free cooperative `dsp::RenderExecutor` abstraction backed by `AudioThreadPool` (handling both single-track top-level dispatch and multi-worker idle helping) with zero allocations on audio/worker threads (`AR-6`).
- **FR-004**: `RackInstrument`, `DrumRack`, and parallel/select `RackDevice` MUST render `>= 2` active parallel-safe chains via `RenderExecutor` into multi-slot `StereoScratchArena` buffers and reduce deterministically in chain order (`diff == 0.0` for `0` vs `N` threads).
- **FR-005**: Heavy polyphonic synthesizers (`Twin`, `Consort`, `WavetableSynth`) MUST expose a shared `dsp::PolyVoiceScheduler` utility enabling parallel voice-batch synthesis above an active-voice + block-frame threshold with deterministic summation (`diff == 0.0` for `0` vs `N` threads).
- **FR-006**: System MUST provide `PlanarStereoBuffer` / `AudioBufferView` in `src/native/nota.engine/src/dsp/core/` and run contiguous `prefersPlanar()` device/instrument segments (including `PluginInstrument` and `PluginEffect`) without redundant interleaved conversions between adjacent planar nodes.
- **FR-007**: `PluginInstrument` and `PluginEffect` MUST enable `parallelRender_` for vetted macOS AudioUnit instances via a shared `isParallelRenderSafe()` check with an explicit thread-safety denylist and real multi-threaded AU stress verification.

### Key Entities

- **`dsp::RenderExecutor` (`dsp/core/RenderExecutor.h`)**: Scoped lock-free task collaborator connecting DSP components and racks cleanly to `AudioThreadPool` without layer-boundary violations.
- **`RenderGraphExecutor` (`engine/RenderGraphExecutor.h/.cpp`)**: Graph execution engine coordinating leaf waves, group depth/sidechain waves, return waves, and deterministic bus accumulation.
- **`PlanarStereoBuffer` / `dsp::PlanarBufferView` (`dsp/core/AudioBufferView.h`)**: Aligned pre-allocated L/R planar buffer and non-owning views complementing `AudioSpan.h` for adjacent planar plugins/DSP chains.
- **`dsp::PolyVoiceScheduler` (`dsp/poly/PolyVoiceScheduler.h`)**: Reusable batch partitioner and deterministic scratch-accumulator for polyphonic synth voices (`Twin`, `Consort`, `WavetableSynth`).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001 (Zero Regressions & Idle Gate)**: `./scripts/build.sh`, `-- --perf` (`RenderPoolTests`, `HostedRenderPoolTests`), and `nota_idle_cpu` exit `0` with `0.0%` idle CPU and `< 2%` worker usage on light real-time playback.
- **SC-002 (Determinism / Bit-Identity)**: Full bit-identical parity (`maxdiff == 0.0`) holds across `0` vs `N` render threads for multi-group/return graphs, multi-chain racks (`RackInstrument`, `DrumRack`, `RackDevice`), heavy polyphony (`Twin`, `Consort`, `WavetableSynth`), planar chains (`VST3`/`AU`), and built-in/hosted FX chains.
- **SC-003 (Multi-Core Speedup & Readability)**:
  - Microbenchmarks and `./scripts/bench-engine.sh -r 3` show significant speedups on multi-group/return sessions, multi-chain racks, single-track heavy polyphony, and multi-plugin planar insert chains.
  - `Engine_Render.cpp` shrinks meaningfully relative to Phase 4 baseline (`5,066` lines) via extracted `RenderGraphExecutor` / device-chain helpers.
