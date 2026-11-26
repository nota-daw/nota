# Implementation Plan: Render DAG Execution, Planar Effect Chains & Intra-Track Parallelism

**Branch**: `perf/render-dag-planar-chains` | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/118-render-dag-planar-chains/spec.md`

## Summary

Eliminate the remaining serial bottlenecks across groups (`Pass 1.5`), return buses (`Pass 2`), multi-chain racks (`RackInstrument`, `DrumRack`, `RackDevice`), heavy polyphonic synths (`Twin`, `Consort`, `WavetableSynth`), hosted plugin interleaved round-trips, and macOS AudioUnit serialization — while extracting parallel DAG orchestration out of `Engine_Render.cpp` (`5,066` lines) into clean, cache-aligned, `AR-6`-compliant domain abstractions (`RenderGraphExecutor`, `RenderExecutor`, `AudioBufferView`, `PolyVoiceScheduler`) with strict bit-exact determinism (`0` vs `N` threads).

## Technical Context

**Language/Version**: C++20 (`nota_engine`, `nota_pluginhost`), C# / .NET 10 (`Nota.SmokeTest`, managed shell)  
**Primary Dependencies**: JUCE (confined to `pluginhost/` per Constitution II), CoreAudio/AudioToolbox on macOS (`WorkgroupLink` + `AudioUnit`)  
**Testing**: `./scripts/build.sh` (`Nota.SmokeTest`, `HostedRenderPoolTests`, `nota-scanner-selftest`), standalone `-- --perf`, `nota_idle_cpu`, `./scripts/bench-engine.sh -r 3`  
**Target Platform**: macOS (`arm64` + `x86_64` universal Release), Linux (`x86_64`), Windows (`x86_64`)  
**Performance Goals**: Multi-core scaling across Pass 1.5/Pass 2, racks, poly synths, planar VST3/AU chains; `0.0%` idle CPU; `< 2%` light RT worker overhead; zero probe-burst regressions under RT overload  
**Constraints**: Strict `AR-6` (zero RT heap allocation/locks/I/O), `0` vs `N` thread bit-exact parity (`maxdiff == 0.0`), AGPL-3.0 two-line SPDX headers, `#1050` Hard Readability Gate (`Engine_Render.cpp` shrinks meaningfully)

## Constitution Check

- **0. Approved Issue First**: Satisfied (`nota-daw/nota#1050`, labels `approved`, `area: audio-engine`, `area: devices`).
- **I. Real-Time Audio Safety (`AR-4`–`AR-6`)**: Pass — all DAG wave buffers, multi-slot rack chain buffers, planar scratch buffers, and voice-batch scratch buffers are pre-allocated during `RenderScratch` publish or non-RT device preparation. Workers use lock-free `AudioThreadPool` atomics only.
- **II. Boundary Discipline (`AR-7`, JUCE confinement)**: Pass — `dsp::RenderExecutor`, `AudioBufferView`, `PolyVoiceScheduler`, and `RenderGraphExecutor` contain zero JUCE headers; AU thread-safety vetting lives inside `pluginhost/hosting/PluginCommon.*` + `PluginInstrument/PluginEffect`.
- **III. Licensing Hygiene**: Pass — every new header/source carries `// SPDX-License-Identifier: AGPL-3.0-only` + copyright line, zero third-party dependencies added.
- **IV. Verification Gate & VII. Spec-First**: Pass — `specs/118-render-dag-planar-chains/` artifacts complete; full `Nota.SmokeTest` + `RenderDagParallelTests` + `HostedRenderPoolTests` extensions planned cleanly.

## Project Structure

### Documentation (this feature)

```text
specs/118-render-dag-planar-chains/
├── checklists/requirements.md
├── contracts/internal-engine.md
├── data-model.md
├── plan.md
├── quickstart.md
├── research.md
├── spec.md
└── tasks.md
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── CMakeLists.txt                                      # register RenderGraphExecutor.cpp; optional AU target for nota-render-probe on APPLE
├── src/dsp/core/
│   ├── AudioBufferView.h                              # PlanarStereoBuffer + PlanarBufferView (layers atop AudioSpan.h)
│   ├── RenderExecutor.h                               # Lock-free scoped parallel collaborator for racks/synths
│   └── ScratchArena.h                                 # Dynamic multi-slot chain arena support alongside StereoScratchArena<N>
├── src/dsp/poly/
│   └── PolyVoiceScheduler.h                           # Deterministic active-voice batch partitioning + aligned scratch accumulators
├── src/engine/
│   ├── AudioThreadPool.h / .cpp                       # 64B aligned JobSlot + lock-free cooperative sub-task slot
│   ├── Engine.h / .cpp / _Render.cpp                  # Shrink mixGraph/processLeafDevices via RenderGraphExecutor, 64B LeafWork, hysteresis
│   ├── RenderGraphExecutor.h / .cpp                   # Leaf waves, Group depth/key DAG waves, Return DAG waves, bus chains + planar run
│   └── RenderScratch (in Engine.h / Engine.cpp)       # Preallocated groupScratch_, returnScratch_, planarTrackBuf_ per slot/track
├── src/devices/
│   ├── Device.h / Instrument.h                        # prefersPlanar(), renderPlanar(), processPlanar() virtual hooks
│   ├── racks/ (RackCore.h/.cpp, RackInstrument.h/.cpp, DrumRack.cpp, RackDevice.h/.cpp)
│   └── instruments/ (Twin.h/.cpp, Consort.h/.cpp, WavetableSynth.h/.cpp)
└── pluginhost/hosting/
    ├── PluginCommon.h / .cpp                          # isParallelRenderSafe(const AudioPluginInstance&) w/ AU denylist
    ├── PluginEffect.h / .cpp                          # Planar I/O path + AU parallel opt-in
    └── PluginInstrument.h / .cpp                      # Planar output path + AU parallel opt-in

tests/Nota.SmokeTest/
├── HostedRenderPoolTests.cs                           # Add real AU parallel & planar chain tests (APPLE)
├── RenderDagParallelTests.cs                          # Multi-group/return DAG, racks, poly synths, planar chains (0 vs N bit-exact)
└── Program.cs                                         # Wire RenderDagParallelTests
```
