# Implementation Plan: Streaming Disk Audio Sources & Hybrid Anticipative Buffering

**Branch**: `perf/streaming-source-hybrid-buffer` | **Date**: 2026-11-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/119-streaming-source-hybrid-buffer/spec.md`

## Summary

Eliminate unbounded PCM memory consumption for long audio stems via a lock-free `AudioSource` / `StreamingAudioSource` disk streaming pipeline (`dr_wav`/`dr_flac`/`dr_mp3` + background prefetch scheduler + wait-free seqlock page ring), and add low-latency `HybridBufferController` anticipative lookahead buffering (`1024`-frame background chunks for non-live arrangement tracks while keeping armed, monitored, live-MIDI, Session, sidechain, and mixer/fader controls at hardware block latency) — obeying `AR-6` (zero RT alloc/locks/I/O), `0.0%` idle CPU, offline bit-identity, and the `#1050` Hard Readability Gate.

## Technical Context

**Language/Version**: C++20 (`nota_engine`), C# / .NET 10 (`Nota.Application`, `Nota.Infrastructure`, `Nota.SmokeTest`)  
**Primary Dependencies**: Existing `dr_wav`, `dr_flac`, `dr_mp3` (`dr_libs`), STL `<atomic>` / `<thread>` / `<condition_variable>`; zero new third-party dependencies (`Constitution III`)  
**Testing**: `./scripts/build.sh` (`Nota.SmokeTest`, `nota-scanner-selftest`), `-- --perf`, `nota_idle_cpu 5 5`, streaming & hybrid microbenchmarks (RAM RSS + CPU/latency vs baseline), `./scripts/bench-engine.sh -r 3`  
**Target Platform**: macOS (`arm64` + `x86_64` universal Release), Linux (`x86_64`), Windows (`x86_64`)  
**Performance Goals**: `>= 80%` RAM footprint reduction on long (`> threshold`) audio stems; `0.0%` idle CPU; deterministic zero-dropout RT lookahead cache hits on non-live heavy tracks with instant live-track response; zero regression in `bench-engine.sh`  
**Constraints**: Strict `AR-6` (zero RT alloc/locks/disk I/O), single-owner DSP thread safety, exact offline bit-identity (`0` vs `N` threads, memory vs stream), 2-line AGPL-3.0 SPDX headers, `#1050` Hard Readability Gate

## Constitution Check

- **0. Approved Issue First**: Satisfied (`nota-daw/nota#1050`, labels `approved`, `area: devices`, `area: audio-engine`).
- **I. Real-Time Audio Safety (`AR-4`–`AR-6`)**: Pass — streaming page ring and hybrid lookahead circular buffers are pre-allocated on non-RT threads (`open()`, `prepare()`, `publishRaw()`); audio thread uses wait-free seqlock / atomic loads/stores only.
- **II. Boundary Discipline (`AR-7`, JUCE confinement)**: Pass — `AudioSource`, `StreamingAudioSource`, and `HybridBufferController` contain zero JUCE headers; C ABI uses pure C enums/POD structs in `nota_engine.h`.
- **III. Licensing Hygiene**: Pass — every new `.h`/`.cpp`/`.cs` carries `// SPDX-License-Identifier: AGPL-3.0-only` + copyright line; reuses existing `dr_libs`.
- **IV. Verification Gate & VII. Spec-First**: Pass — complete spec-kit pipeline before code changes, converged and verified end-to-end.

## Project Structure

### Documentation (this feature)

```text
specs/119-streaming-source-hybrid-buffer/
├── checklists/requirements.md
├── contracts/internal-engine.md
├── data-model.md
├── plan.md
├── quickstart.md
├── research.md
└── tasks.md
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── CMakeLists.txt                                      # register StreamingAudioSource.cpp & HybridBufferController.cpp
├── include/nota/nota_engine.h                          # NotaSampleLoadPolicy, NotaHybridBufferMode, stats structs & C ABI functions
├── src/abi/
│   ├── nota_engine_core.cpp                            # C ABI handlers for streaming policy/threshold & hybrid buffering
│   └── nota_engine_import.cpp                          # Optional streaming import threshold integration
├── src/media/
│   ├── AudioSource.h                                   # AudioSource interface, SampleLoadPolicy, MemoryAudioSource
│   ├── StreamingAudioSource.h / .cpp                   # Lock-free seqlock page ring + StreamingIoScheduler + dr_libs random-access reader
│   ├── SampleBuffer.h / .cpp                           # Integrate std::shared_ptr<AudioSource> delegation & fast block reader
│   ├── AudioFile.h / .cpp                              # AudioDecodeOptions + streaming cutoff decision helper + shared openDecoder()
│   └── AudioImport.h / .cpp                            # Streaming-aware AudioImportJob above threshold (peak table + head cache)
└── src/engine/
    ├── HybridBufferController.h / .cpp                 # Non-live lookahead scheduler, track ring cache, live classifier & epoch fence
    ├── RenderGraphExecutor.cpp                         # Consume anticipative track ring buffers on cache hits / skip RT DSP when served
    ├── Engine.h / .cpp / _Render.cpp / _Tracks.cpp     # Wire streaming decode threshold/stats, prime on addClip/seek, hybrid invalidation & lifecycle
    └── Engine_Automation.cpp                           # Per-track device/plugin automation helper reusable by HybridBufferController

src/managed/
├── Nota.Application/Engine/IAudioEngine.cs             # Managed enums/structs & properties/queries for streaming & hybrid buffering
└── Nota.Infrastructure/Interop/
    ├── NativeMethods.cs                                # P/Invoke LibraryImport declarations
    └── NotaEngine.cs                                   # Managed implementation forwarding cleanly to native C ABI

tests/Nota.SmokeTest/
├── DiskStreamingAndHybridBufferTests.cs                # Streaming RAM/parity/waveform/seek/loop tests + Hybrid live/anticipative/invalidation tests
└── Program.cs                                          # Register under `--perf` + full suite
```
