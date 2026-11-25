# Data Model: Streaming Audio Sources & Hybrid Buffering

## 1. `nota::AudioSource` (`media/AudioSource.h`)

- `enum class SampleLoadPolicy : int32_t`: `Auto = 0`, `MemoryOnly = 1`, `StreamAlways = 2`
- `inline constexpr int64_t kDefaultStreamThresholdBytes = 16LL * 1024 * 1024;` (`16 MiB` decoded float32 PCM)
- Pure virtual query/read surface:
  - `channels() -> int32_t`, `frames() -> int64_t`, `sampleRate() -> double`
  - `isStreaming() -> bool`, `memoryBytes() -> int64_t`, `path() -> const std::string&`
  - `at(frame, ch) const noexcept -> float`
  - `readStereo(frame, l, r) const noexcept -> void`
  - `prepareWindow(centerFrame, reverse, allowSyncFill) const noexcept -> void`
  - `readAllInterleaved(out, capFloats) const -> int64_t`

## 2. `nota::StreamingAudioSource` (`media/StreamingAudioSource.h/.cpp`)

- **Constants**:
  - `kPageFrames = 8192`
  - `kRingPages = 32` (~`262,144` frames ≈ `5.9 s` stereo @ `44.1 kHz` ≈ `2.0 MiB` RAM)
  - `kHeadPages = 2` (`16,384` pinned start frames for instant sample-0 starts & loop wraps)
  - `kLookaheadPages = 20` (`~3.7 s` forward lookahead), `kTrailingPages = 2` (covers backward/reverse reads)
- **Seqlock Page Slot (`alignas(64) PageSlot`)**:
  - `std::atomic<uint64_t> seq{0}` (even = readable, odd = writer mutating)
  - `std::atomic<int64_t> pageIdx{-1}`
  - `std::atomic<uint64_t> stamp{0}` (LRU / distance eviction order)
  - `std::vector<float> data` (sized `kPageFrames * channels` once at `open()`, never resized)
- **Lock-free Scheduler Coordination**:
  - `std::atomic<int64_t> cursorPage_{0}`, `std::atomic<bool> cursorReverse_{false}`, `std::atomic<uint32_t> pendingMask_{0}`
  - Atomic telemetry counters: `pagesFaulted_`, `pagesServedAsync_`, `pagesServedSync_`, `rtUnderruns_`
  - Registered with shared singleton `StreamingIoScheduler` (woken on demand via condition variable; sleeps completely when stopped/idle -> `0.0%` CPU).

## 3. `nota::SampleBuffer` Integration (`media/SampleBuffer.h/.cpp`)

- Adds `std::shared_ptr<AudioSource> source;` alongside existing fields (`samples`, `channels`, `frames`, `sourceSampleRate`, `id`, `peakTable`, `rmsTable`).
- Invariant:
  - Memory buffer: `samples` holds interleaved floats (`source` optional/null or `MemoryAudioSource`); `isStreaming() == false`.
  - Streamed buffer: `samples.empty() == true`, `source` holds `std::shared_ptr<StreamingAudioSource>`, `channels`/`frames`/`sourceSampleRate`/`peakTable`/`rmsTable` mirror file metadata and computed overviews (`kPeakBlock = 512`).
  - `empty()` returns `frames <= 0 || channels <= 0 || (samples.empty() && !source)`.

## 4. `nota::HybridBufferController` (`engine/HybridBufferController.h/.cpp`)

- **Constants**:
  - `kChunkFrames = 1024` (background render chunk size)
  - `kLookaheadFrames = 4096` (target lead ahead of playhead)
  - `kRingFrames = 8192` (circular buffer frames per track)
- **Track Ownership & Eligibility State (`TrackSlot`)**:
  - `trackId` (`int32_t`), `eligible` (`bool`), `ringBuf` (`std::vector<float>`, size `kRingFrames * 2`, pre-allocated outside RT)
  - Wait-free reader metadata (`std::atomic<uint64_t> ringEpoch`, `std::atomic<int64_t> readBaseSample`, `std::atomic<int64_t> writePosSample`, `std::atomic<bool> workerInFlight`)
  - Dedicated background workspace: `MidiEv midiScratch[1024]`, `ExprEv exprScratch[...]`, `float dryScratch[kChunkFrames * 2]`, `float postScratch[kChunkFrames * 2]`, `dsp::PlanarStereoBuffer planarScratch`, `dsp::StereoScratchArena<1> arena` (pre-sized outside RT)
- **Global Atomics & Telemetry**:
  - `std::atomic<int32_t> mode_` (`HybridBufferMode::Off = 0` default opt-in, `Auto = 1`)
  - `std::atomic<uint64_t> epoch_{1}` (incremented on graph swap, seek, transport toggle/loop/BPM change, param edit, live arm/monitor change)
  - `std::atomic<int32_t> liveTracks_`, `anticipativeTracks_`
  - `std::atomic<int64_t> cacheHits_`, `cacheMisses_`, `invalidations_`
- **Invariants**:
  - Audio callback and background worker NEVER call `Device`/`Instrument` `process()` on the same `Track` simultaneously (`workerInFlight` guard + `epoch_` fence + blocking `drainAndFence()` on message-thread structural `publishRaw()` / `stop()`).
  - Offline mode (`!deviceBlock_`) and stopped/zero-render-thread mode park worker immediately (`0.0%` CPU).
