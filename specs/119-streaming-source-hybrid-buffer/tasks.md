---
description: "Task list for Phase 6 of nota-daw/nota#1050"
---

# Tasks: Streaming Disk Audio Sources & Hybrid Anticipative Buffering

## Phase 1: Setup & C ABI Surface

- [x] T001 Define C ABI enums (`NotaSampleLoadPolicy`, `NotaHybridBufferMode`), stats structs (`NotaStreamingStats`, `NotaHybridBufferStats`), and functions in `src/native/nota.engine/include/nota/nota_engine.h`

## Phase 2: Disk Streaming Audio Sources (US1)

- [x] T002 Create `src/native/nota.engine/src/media/AudioSource.h` defining `AudioSource`, `SampleLoadPolicy`, `kDefaultStreamThresholdBytes`, and `MemoryAudioSource`
- [x] T003 Create `src/native/nota.engine/src/media/StreamingAudioSource.h` and `src/native/nota.engine/src/media/StreamingAudioSource.cpp` implementing wait-free seqlock page ring (`kPageFrames = 8192`, `kRingPages = 32`), pinned head cache, sleep-on-idle `StreamingIoScheduler` (`0.0%` idle CPU), bounded-memory peak/RMS scan, and sync offline fallback for `WAV`/`FLAC`/`MP3` via `dr_libs`; register in `src/native/nota.engine/CMakeLists.txt`
- [x] T004 Update `src/native/nota.engine/src/media/SampleBuffer.h` and `SampleBuffer.cpp` to compose `std::shared_ptr<AudioSource>`, support fast sequential/random block access and RAM accounting (`memoryBytes()`, `isStreaming()`, `readInterleaved()`), and keep `at`/`readStereo`/`scanRange`/`scanSquares` streaming-safe
- [x] T005 Update `src/native/nota.engine/src/media/AudioFile.h/.cpp` and `AudioImport.h/.cpp` with `AudioDecodeOptions`, streaming threshold routing, and streaming `AudioImportJob` support; update `Engine_Render.cpp::mixAudioClip`, `Engine_Tracks.cpp` (`addAudioClip`, `addAudioClipEx`, `sampleRead`, `primeStreamingClip`), `Engine_Session.cpp`, and `Engine.cpp` (`seekBeats`) for fast RT streamed mixing, initial priming, and synchronous sample readback

## Phase 3: Hybrid Anticipative Buffering (US2)

- [x] T006 Create `src/native/nota.engine/src/engine/HybridBufferController.h` and `src/native/nota.engine/src/engine/HybridBufferController.cpp` implementing pre-allocated per-track circular lookahead buffers (`kChunkFrames = 1024`, `kLookaheadFrames = 4096`, `kRingFrames = 8192`), live-track bypass classification, single-owner `workerInFlight` handback + epoch fencing, and sleep-on-idle background render loop; register in `src/native/nota.engine/CMakeLists.txt`
- [x] T007 Integrate `HybridBufferController` cleanly into `Engine.h`, `Engine.cpp`, `Engine_Render.cpp`, `Engine_Automation.cpp` (reusable per-track device automation evaluation + skip double-application when anticipative-owned), and `RenderGraphExecutor.cpp` (`deviceBlock_` live consumption, post-fader live mix preservation, invalidation hooks on `publishRaw`, `seekBeats`, `setBpm`, `setLoop`, `deviceSetParam`, `midiEffectSetParam`, `setTrackArmed`, `setTrackMonitor`, `noteOn`/`noteOff`)

## Phase 4: C ABI Handlers, Managed Interop & Tests

- [x] T008 [P] Implement C ABI handlers in `src/native/nota.engine/src/abi/nota_engine_core.cpp` and `nota_engine_import.cpp` for streaming threshold/policy/stats and hybrid mode/stats
- [x] T009 [P] Add managed interfaces and P/Invoke wrappers in `src/managed/Nota.Application/Engine/IAudioEngine.cs`, `src/managed/Nota.Infrastructure/Interop/NativeMethods.cs`, and `src/managed/Nota.Infrastructure/Interop/NotaEngine.cs`
- [x] T010 Create `tests/Nota.SmokeTest/DiskStreamingAndHybridBufferTests.cs` (registered in `tests/Nota.SmokeTest/Program.cs` under `--perf` and full run) verifying WAV/FLAC streaming RAM reduction, peak/RMS waveform & `nota_sample_read` parity, offline `0` vs `N` bit-exactness across seeks/offsets/reverses/warps, RT async streaming, hybrid live-bypass vs anticipative hits, invalidation on seek/edit/param/arm, and parity vs `HybridOff`

## Phase 5: Verification, Documentation & Local Commit

- [x] T011 Update `CHANGELOG.md` `[Unreleased]`, audit SPDX headers and file line counts (`Engine_Render.cpp`, `Engine.h`)
- [x] T012 Run `./scripts/build.sh`, `-- --perf`, `nota_idle_cpu 5 5`, targeted streaming RAM & CPU microbenchmarks (`phase5-base` vs `phase6`), and `./scripts/bench-engine.sh -r 3`; converge `tasks.md` and commit locally on `perf/streaming-source-hybrid-buffer`
