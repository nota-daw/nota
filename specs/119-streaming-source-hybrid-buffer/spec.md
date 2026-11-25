# Feature Specification: Streaming Disk Audio Sources & Hybrid Anticipative Buffering

**Feature Branch**: `perf/streaming-source-hybrid-buffer`  
**Created**: 2026-11-25  
**Status**: Approved (`nota-daw/nota#1050` — Phase 6)  
**Parent Issue**: [`nota-daw/nota#1050`](https://github.com/nota-daw/nota/issues/1050)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Disk-Streamed Long Audio Files & Bounded-Memory `AudioSource` (Priority: P1)

When loading long multitrack stems, full-mix backing tracks, or hour-long recordings (whose uncompressed 32-bit float PCM footprint exceeds a configurable threshold, e.g., `16 MiB`) onto audio tracks, Nota must keep peak/RMS waveform display, clipping/trimming/splitting/reversing, project bundling, and sample-accurate playback fully functional without decoding the entire file's float PCM into RAM. Short one-shots and clips below the threshold remain zero-overhead in-memory sources (`MemoryAudioSource`), while large files use a disk-backed streaming source (`StreamingAudioSource`) fed asynchronously by a background I/O worker into a fixed lock-free ring buffer (`AR-6`: zero audio-thread allocation, locks, or disk syscalls).

**Why this priority**: Currently `decodeAudioFile()` and `AudioImportJob` unconditionally allocate `frames * channels * sizeof(float)` bytes in `SampleBuffer::samples` — a 1-hour 48 kHz stereo stem allocates `~1.38 GB` of heap RAM per clip, and 16 stems exhaust workstation memory before playback even starts.

**Independent Test**: Load a large multi-minute WAV/FLAC file above the configured streaming byte threshold alongside short in-memory clips, render across starts, mid-clip offsets, reverse reads, loops, and seeks with `0` and `N` threads, verify waveforms (`getClipPeaks` / `getClipSourcePeaks`), project sample readback (`nota_sample_read`), `0.0%` idle CPU, bit-exact audio output against forced in-memory decoding, and a large resident-RAM reduction for the streamed sample vs memory-loaded decoding.

**Acceptance Scenarios**:

1. **Given** an uncompressed or lossless audio file (`WAV`/`FLAC`/`MP3`) whose decoded float32 PCM footprint exceeds the configured stream threshold (`default 16 MiB`) and policy `Auto` (or explicit `Stream`), **When** placed on an audio track (`addAudioClip`, `addAudioClipEx`, `AudioImportJob`, or Session slot), **Then** the engine opens a `StreamingAudioSource` holding only a fixed lock-free page ring + head window (`<= ~2.5 MiB` PCM cache per stream) plus precomputed `peakTable`/`rmsTable`, keeping `SampleBuffer::samples` empty (`memoryBytes` reduced by `>= 80%` on large files).
2. **Given** active playback (real-time device callbacks or offline bounce) across starts, mid-clip source offsets, seeks, loop wraps, varispeed, or reverse playback, **When** audio frames are read, **Then** the audio thread consumes pre-fetched resident pages lock-free (`AR-6`, zero allocations/locks/I/O) while the background I/O scheduler tracks the playhead window asynchronously; offline bounces and project sample bundling (`nota_sample_read`) produce sample-exact PCM parity against in-memory decoding.
3. **Given** transport stopped / idle with streaming sources loaded, **When** measuring background I/O scheduler activity over `5` seconds (`nota_idle_cpu`), **Then** worker CPU remains `0.0%` and engine teardown joins background I/O cleanly without hangs.

---

### User Story 2 — Hybrid Low-Latency Live + Background Anticipative Buffering (`HybridBufferController`) (Priority: P2)

When playing a multi-track session at low hardware buffer sizes (e.g., `64`–`256` frames) where some tracks carry live interaction (record-armed tracks, live input monitors, tracks receiving live MIDI/controller input, live Session clips, or sidechain-coupled tracks) and other tracks contain deterministic Arrangement clips and effects, the engine should optionally render eligible non-live timeline tracks ahead in larger background chunks (`1024` frames into a pre-allocated ring buffer) via `HybridBufferController`, leaving only live-interactive tracks, buses, faders/sends, and post-mix stages on the low-latency hardware callback deadline. Any timeline edit, seek, loop change, arm/monitor toggle, or device/clip parameter change immediately invalidates stale lookahead buffers and falls back race-free without clicks or torn state.

**Why this priority**: Even with parallel DAG waves (Phase 5), small driver buffers (`64`–`128` samples ≈ `1.3`–`2.7 ms`) restrict heavy VST3/AU/synth arrangements because every track must synchronize inside every short callback period. Decoupling non-live tracks via larger anticipative background chunks amortizes per-block DSP setup, SIMD warmup, and plugin call overhead while preserving instant hardware latency for armed/live tracks and fader/mute/solo controls.

**Independent Test**: Construct a multi-track arrangement mixing heavy non-live Arrangement synth/effect tracks and an armed/monitored live track; pump real-time device blocks (`nota_engine_render_device_block`) at `128` frames with `HybridBufferMode::Auto` vs `HybridBufferMode::Off`; verify (a) live/armed tracks always bypass anticipative buffering and respond immediately on block 0 to injected live MIDI/input, (b) eligible non-live tracks serve pre-rendered frames (`cacheHits > 0`), (c) seeking, moving/splitting clips, changing device parameters, or arming a track increments `invalidations` cleanly without race or drift (`audioParity` within floating-point tolerance against `HybridBufferMode::Off`), and (d) stopped idle engine keeps `0.0%` CPU.

**Acceptance Scenarios**:

1. **Given** a multi-track arrangement in real-time device playback with `HybridBufferMode::Auto` (and `renderThreads != 0`), **When** non-live deterministic Arrangement tracks play alongside an armed/monitored live track, **Then** the live track always renders live inside each driver block while eligible non-live tracks fill a circular lookahead buffer (`1024`-frame chunks, up to `4096` frames ahead) on a background worker and serve driver callbacks via lock-free slice copies (`cacheHits > 0`).
2. **Given** active lookahead buffers when a user seeks, wraps a loop, modifies a clip/MIDI note (`publishRaw`), changes a device/plugin parameter, toggles arm/monitor, or changes BPM/loop bounds, **When** the next block renders, **Then** an epoch increment immediately rejects stale lookahead frames (`invalidations > 0`), tracks safely render or hand back without concurrent DSP mutation, and lookahead cleanly rebuilds from the new playhead.
3. **Given** `HybridBufferMode::Off`, `renderThreads == 0` in `Auto`, offline exports (`renderOffline`), or stopped transport, **When** rendering or idling, **Then** anticipative workers perform no background rendering (`0.0%` idle CPU) and offline bit-identity (`0` vs `N` threads) remains untouched.

---

### Edge Cases & Invariants

- **Strict `AR-6` Compliance**: Neither `StreamingAudioSource` nor `HybridBufferController` allocates memory, acquires blocking mutexes, or performs disk/OS blocking calls on the hardware audio callback thread. Wait-free seqlock rings (`StreamingAudioSource`) and atomic epoch/range descriptors (`HybridBufferController`) mediate all RT reads cleanly.
- **Single-Owner DSP Safety in Hybrid Mode**: A track's instrument/FX DSP state is never mutated concurrently by the background lookahead worker and the audio callback thread (`workerInFlight` handback guard + epoch fencing on structural edits).
- **Track Fader / Pan / Mute / Solo / Send Responsiveness**: Mixer controls run post-buffer on the audio callback thread so fader rides, mutes, solos, cues, and send throws remain zero-latency even for anticipatively buffered tracks.
- **Backward Compatibility & Project Save Integrity**: `SampleBuffer` preserves all existing API contracts (`peakRange`, `rmsRange`, `at`, `readStereo`, `ClipWarpStream`, `TempoDetect`, `SampleAnalysis`, `nota_sample_read`) transparently regardless of memory vs streaming backing.
- **Hard Readability Gate (`#1050`)**: All new functionality lives in clean, domain-named modules (`AudioSource.h`, `StreamingAudioSource.h/.cpp`, `HybridBufferController.h/.cpp`); `Engine.h` and `Engine_Render.cpp` stay lean via delegation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST define an `AudioSource` abstraction with `MemoryAudioSource` and `StreamingAudioSource` in `src/native/nota.engine/src/media/` integrated into `SampleBuffer`, selecting disk streaming automatically when decoded PCM bytes exceed a configurable threshold (`default 16 MiB`) under `SampleLoadPolicy::Auto` (or always under `Stream`, never under `Memory`).
- **FR-002**: `StreamingAudioSource` MUST stream `WAV`, `FLAC`, and `MP3` files via existing `dr_libs` decoders using a pre-allocated lock-free seqlock page ring fed asynchronously by a dedicated sleep-on-idle I/O scheduler, keeping cached head frames for instant start, building `peakTable`/`rmsTable` overviews with bounded scratch RAM, and supporting synchronous on-demand page fill during offline bounces (`renderOffline`) and sample extraction (`nota_sample_read`).
- **FR-003**: System MUST implement `HybridBufferController` in `src/native/nota.engine/src/engine/` with pre-allocated per-track circular buffers (`8192` frames, `1024`-frame background render chunks, `4096`-frame target lookahead), strict live-track bypass rules (armed, monitor, live/routed MIDI, live Session slots, sidechain-coupled, capture/freeze, unsafe plugins), instant epoch invalidation + safe single-owner DSP handback, and sleep-when-idle behavior.
- **FR-004**: C ABI (`nota_engine.h`) and managed interop (`IAudioEngine` / `NotaEngine`) MUST expose controls and diagnostics for sample streaming policy/threshold, per-sample streaming query, streaming stats, hybrid buffering mode, and hybrid buffer stats without breaking any existing ABI contracts.

### Key Entities

- **`AudioSource` / `MemoryAudioSource` (`media/AudioSource.h`)**: Polymorphic source interface and contiguous RAM implementation backing `SampleBuffer`.
- **`StreamingAudioSource` & `StreamingIoScheduler` (`media/StreamingAudioSource.h/.cpp`)**: Disk-backed lock-free ring page source and demand-driven async reader thread (`AR-6` compliant).
- **`HybridBufferController` (`engine/HybridBufferController.h/.cpp`)**: Non-live track background lookahead scheduler, ring cache, live/anticipative classifier, and epoch invalidation coordinator.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001 (Zero Regressions & Idle Gate)**: `./scripts/build.sh`, `-- --perf`, `nota_idle_cpu 5 5` (`0.0%` idle CPU), and `Phase6StreamingHybridTests` pass completely cleanly on Debug/Release universal builds.
- **SC-002 (Memory Reduction & Fidelity on Long Stems)**: Streaming a large multi-minute WAV/FLAC/MP3 file (`> threshold`) reduces resident sample memory footprint by `>= 80%` relative to full float32 RAM decoding while achieving exact/near-exact (`maxdiff == 0.0` offline) audio & waveform overview parity.
- **SC-003 (Hybrid Lookahead Correctness & Live Latency)**: With `HybridBufferMode::Auto`, live/armed tracks bypass lookahead and reflect live MIDI/input immediately, non-live tracks hit anticipative buffers (`cacheHits > 0`) cleanly during real-time device blocks, edits/seeks trigger clean invalidations (`invalidations > 0`) with accurate re-convergence and audio parity, and `./scripts/bench-engine.sh -r 3` shows no regressions and measurable wins on low-buffer/large-stem workloads.
