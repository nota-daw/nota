# Implementation Plan: Engine Performance Optimizations for Large Projects

**Branch**: `perf/engine-large-project-optimizations` | **Date**: 2026-11-26 | **Spec**: [spec.md](spec.md)
**Issue**: [`nota-daw/nota#1050`](https://github.com/nota-daw/nota/issues/1050)

## Summary

Improve audio engine CPU usage, cache locality, multi-core scaling, memory footprint, and UI meter responsiveness on large arrangements and dense mixing graphs without altering audio output or breaking real-time safety (`AR-4`–`AR-7`). The work introduces focused, domain-named engine abstractions — `DeviceTelemetry`, `SilenceGate`, `RenderBusState` (`ActiveBusMask`), `TimelineIndex`, `RenderRoutingPlan`, `TrackContent` (`CowSlice<T>`), `ScratchArena` (`StereoScratchArena`, `ChainScratchBank`), `SimdDispatch` + cached `double2` `Eq` filtering, `RenderGraphExecutor`, `RenderExecutor` + `PolyVoiceScheduler` + `PlanarStereoBuffer`, `StreamingAudioSource`, and `HybridBufferController` — while keeping `Engine_Render.cpp` smaller and cleaner (`4,019` lines, down from `5,066`).

## Technical Context

**Language/Version**: C++20 (`nota_engine`, `nota_pluginhost`), C# / .NET 10 (`Nota.Application`, `Nota.Infrastructure`, `Nota.App`, `Nota.SmokeTest`)
**Primary Dependencies**: None added (`dr_wav`/`dr_flac`/`dr_mp3` already vendored; JUCE confined strictly to `pluginhost/`)
**Audio-Thread Impact** *(required)*: Strict `AR-6` compliance — zero heap allocations, locks, or blocking I/O on hardware callbacks or render pool worker threads. Lease counters, epochs, `SilenceGate`, `ActiveBusMask`, `SubSlot`, seqlock page rings, and `HybridBufferController` ring copies use lock-free atomics; all heap buffers and indices allocate off-RT (`open()`, `prepare()`, `publishRaw()`, `renderOffline()` pre-sizing).
**Testing**: `./scripts/build.sh` (`nota-scanner-selftest`, `Nota.SmokeTest` including `TrackMeterAndTelemetryTests`, `SilenceGateAndBusMaskTests`, `TimelineIndexAndRoutingTests`, `TrackSnapshotArenaSimdTests`, `RenderDagParallelTests`, `DiskStreamingAndHybridBufferTests`, `HostedRenderPoolTests`), `-- --perf`, `tests/perf/nota_idle_cpu`, `scripts/bench-engine.sh`
**Target Platform**: macOS (`arm64` + `x86_64` universal), Linux (`x86_64`), Windows (`x86_64`)
**Constraints**: Pure C ABI (`AR-7`), `0` vs `N` thread bit-exact parity (`maxdiff == 0.0`), `Eq` scalar parity (`< -120 dBFS`), `0.0%` idle CPU, universal x86_64 safety (`-mavx2 -mfma` isolated to `SimdAvx2.cpp`), AGPL-3.0-only headers.

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| 0. Approved Issue First | Pass | `nota-daw/nota#1050` is OPEN and carries `approved`. |
| I. Real-Time Audio Safety (`AR-4`–`AR-6`) | Pass | Lock-free atomics & immutable snapshot indices on RT threads; all allocations off-RT. |
| II. Boundary Discipline (`AR-7`, JUCE confinement) | Pass | Pure C types/functions in `nota_engine.h`; JUCE remains inside `pluginhost/` only. |
| III. Licensing Hygiene | Pass | Every new `.h`/`.cpp`/`.cs` carries the two-line `SPDX-License-Identifier: AGPL-3.0-only` header; zero new dependencies. |
| IV. Verification Gate | Pass | Six dedicated feature test suites + `HostedRenderPoolTests` + `--perf` + `nota_idle_cpu` + `bench-engine.sh`. |
| V. Scope Control | Pass | All work stays strictly within approved optimization & open-editor keep-awake scope of `#1050`. |
| VI. Documentation Stays in Sync | Pass | `CHANGELOG.md` `[Unreleased]` and `.specify/bugs/plugin-editor-sleep-freeze/` updated alongside code. |
| VII. Spec-First | Pass | Consolidated under `specs/115-engine-large-project-optimizations/` (avoiding collision with `specs/114-remote-led-display/`). |
| VIII. Safety Rules Never Weakened | Pass | `AR-1`–`AR-8`, universal x86_64 baseline, and GCC math semantics preserved. |

## Project Structure

### Documentation (this feature)

```text
specs/115-engine-large-project-optimizations/
├── checklists/requirements.md
├── contracts/
│   ├── c-abi.md
│   └── internal-engine.md
├── data-model.md
├── plan.md
├── quickstart.md
├── research.md
├── spec.md
└── tasks.md
```

Related bugfix record for open plugin editor wake handling during silence:
- `.specify/bugs/plugin-editor-sleep-freeze/{assessment.md,fix.md,test.md}`

### Source Code (repository root)

```text
src/native/nota.engine/
├── CMakeLists.txt                                      # Register new TUs, AU target on nota-render-probe (APPLE), Clang -fno-math-errno, Clang/MSVC Release IPO
├── include/nota/nota_engine.h                          # Batch meters, TelemetryMode, PluginSleepMode + selftest, Streaming & Hybrid C ABI
├── src/abi/
│   ├── nota_engine_core.cpp                            # Batch meters, telemetry, streaming & hybrid C ABI handlers
│   └── nota_engine_device.cpp                          # Sleep mode & editor sleep selftest C ABI handlers
├── src/dsp/
│   ├── core/
│   │   ├── AudioBufferView.h                           # [NEW] PlanarStereoBuffer + PlanarBufferView atop AudioSpan.h
│   │   ├── DeviceTelemetry.h                           # [NEW] TelemetryMode, DeviceTelemetry, ScopedBlockTimer
│   │   ├── RenderExecutor.h                            # [NEW] Lock-free scoped parallel collaborator over AudioThreadPool
│   │   ├── ScratchArena.h                              # [NEW] StereoScratchArena<N> + ChainScratchBank
│   │   ├── SilenceDetector.cpp                         # Skip zero-energy sqrt on silent blocks
│   │   └── SilenceGate.h                               # [NEW] PluginSleepMode, lock-free SilenceGate (hangover + holdAwake)
│   ├── math/
│   │   ├── SimdAudio.h                                 # Planar/interleaved helpers + SIMD peak/mix utilities
│   │   ├── SimdAvx2.cpp                                # [NEW] Per-TU -mavx2 -mfma kernels on x86_64
│   │   └── SimdDispatch.{h,cpp}                        # [NEW] Runtime CPUID/NEON capability detection & dispatch table
│   └── poly/
│       └── PolyVoiceScheduler.h                        # [NEW] Deterministic active-voice batch partitioning + PolyVoiceScratchBank
├── src/devices/
│   ├── Device.h, Instrument.h, MidiDevice.h            # Telemetry, SilenceGate, planar hooks, needsIdleRender, outputAlwaysSilent, requiresActiveProcessing
│   ├── effects/*.{h,cpp} (22 timed FX + Eq/AutoGain)   # Shared DeviceTelemetry; Eq BandCache + double2 stereo SIMD; AutoGain idle refinement
│   ├── instruments/{BassSynth,Consort,FluxSynth,Keys,Monolith,Mosaic,OperatorSynth,PendulumSynth,Pentad,RhythmMachine,Sampler,Synth,Twin,VoltSynth,WavetableSynth}.{h,cpp}
│   └── racks/{RackCore,RackDevice,RackInstrument,DrumRack,MidiRack}.{h,cpp} # Recursive idle/editor propagation + parallel chain scratch
├── pluginhost/hosting/
│   ├── PluginCommon.{h,cpp}                            # isParallelRenderSafe(const AudioPluginInstance*) for VST3 & vetted macOS AU
│   ├── PluginEditorWindow.{h,cpp}                      # Audio-thread-safe EditorHost::open_ + onOpenChanged
│   └── PluginInstrument.{h,cpp}, PluginEffect.{h,cpp}  # Tail-aware SilenceGate, planar I/O, AU parallel opt-in, editor keep-awake
├── src/media/
│   ├── AudioSource.h                                   # [NEW] AudioSource, SampleLoadPolicy, MemoryAudioSource
│   ├── StreamingAudioSource.{h,cpp}                    # [NEW] Wait-free seqlock page ring + StreamingIoScheduler + dr_libs sync/async decode
│   └── SampleBuffer.{h,cpp}, AudioFile.{h,cpp}, AudioImport.{h,cpp} # Streaming source integration + bounded peak/RMS scan
└── src/engine/
    ├── AudioThreadPool.{h,cpp}                         # 64B JobSlot alignment + lock-free cooperative SubSlot
    ├── HybridBufferController.{h,cpp}                  # [NEW] Non-live lookahead ring scheduler, live bypass, epoch invalidation
    ├── RenderBusState.h                                # [NEW] ActiveBusMask<N> for Group/Return skip
    ├── RenderGraphExecutor.{h,cpp}                     # [NEW] Leaf/Group/Return DAG waves, planar segments, Hybrid consumption
    ├── RenderRoutingPlan.{h,cpp}                       # [NEW] Sorted track lookup, ancestry, MIDI cache, compiled automation/CV/CC endpoints
    ├── TimelineIndex.{h,cpp}                           # [NEW] Interval/prefix-max clip & padded note index + storage tokens
    ├── TrackContent.h                                  # [NEW] CowSlice<T> Copy-on-Write track snapshot containers
    └── Engine.{h,cpp,_*.cpp}, Track.{h,cpp}, Graph.h, SnapshotHistory.h, Automation.cpp, MidiController.cpp, Modulation.{h,cpp}

src/managed/
├── Nota.Application/Engine/{EngineContracts,IAudioEngine}.cs
├── Nota.Infrastructure/Interop/{NativeMethods,NotaEngine}{,.Devices}.cs
└── Nota.App/{ArrangementView,MixerView,SessionView,SessionView.Mixer,DeviceCards/Bodies/AutoFilterDeviceBody}.cs

tests/Nota.SmokeTest/
├── Program.cs                                          # Wires all 6 suites into full run & --perf
├── HostedRenderPoolTests.cs                            # Extended VST3 sleep/wake & macOS AU parallel tests
├── TrackMeterAndTelemetryTests.cs                      # [NEW] Batch meters & on-demand device telemetry
├── SilenceGateAndBusMaskTests.cs                       # [NEW] Synth/rack sleep gates & active submix bus masks
├── TimelineIndexAndRoutingTests.cs                     # [NEW] TimelineIndex, binary curves & RenderRoutingPlan
├── TrackSnapshotArenaSimdTests.cs                      # [NEW] CowSlice, StereoScratchArena, Eq SIMD & SimdDispatch
├── RenderDagParallelTests.cs                           # [NEW] Group/Return DAG, racks, poly synths, planar chains & hysteresis
└── DiskStreamingAndHybridBufferTests.cs                # [NEW] StreamingAudioSource & HybridBufferController
```

## Complexity Tracking

No constitution violations; no complexity exceptions required.
