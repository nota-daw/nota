# Implementation Plan: Shared SilenceGate, Built-in/Plugin Sleep Gating, and Active Submix Buses

**Branch**: `perf/silence-gate-active-buses` | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Phase 2 of [nota-daw/nota#1050](https://github.com/nota-daw/nota/issues/1050)

## Summary

Eliminate silent-processing overhead across instruments, racks, hosted plugins, and mix buses through cohesive, human-readable abstractions:
1. **`dsp::SilenceGate`** (`src/dsp/core/SilenceGate.h`): epoch-guarded lock-free sleep gate eliminating duplicate `quietSamples_`/`idle_`/non-atomic wake boilerplate.
2. **10 built-in instruments + `RackDevice`/`RackInstrument`/`DrumRack` + `needsIdleRender()`**: add idle early-outs to `BassSynth`, `Consort`, `FluxSynth`, `Monolith`, `OperatorSynth`, `PendulumSynth`, `Pentad`, `Synth`, `VoltSynth`, `WavetableSynth`, recursive `isIdle()`/`hasTail()`/`wake()` on racks, and `needsIdleRender()` on `Twin`/`Keys` for lightweight clock continuity.
3. **Hosted plugin gating & C ABI policy**: wire `PluginInstrument` and `PluginEffect` through `SilenceGate` respecting plugin tail length + `250 ms` hangover, and expose `NotaPluginSleepMode` (`Auto` / `Never`) in `nota_engine.h`, `Engine`, `IAudioEngine`, and `NotaEngine`.
4. **Engine & submix bus gating (`RenderBusState.h`)**: short-circuit settled idle instruments in `playEvents()` & `prepareLeafWork()`, and skip silent `ChainRest::Settled` Group (`Pass 1.5`) & Return (`Pass 2`) buses via `ActiveBusMask`.

## Technical Context

**Language/Version**: C++20 (`nota.engine`), C# / .NET 10 (`Nota.Application`, `Nota.Infrastructure`, `Nota.SmokeTest`)  
**Primary Dependencies**: None — standard C++20 `<atomic>` / `<cmath>`, existing `SimdAudio` helpers  
**Audio-Thread Impact** *(required)*: Zero allocations, zero locks, zero I/O (`AR-6`). `SilenceGate` uses relaxed/acquire-release atomics with epoch protection against concurrent UI wakes; `ActiveBusMask` uses relaxed atomic byte writes joined by render-pool barriers.  
**Testing**: `./scripts/build.sh` (`Nota.SmokeTest`, `HostedRenderPoolTests`), `-- --perf`, `./scripts/bench-engine.sh` vs `develop` baseline  
**Constraints**: Preserve `RenderPoolTests` & `HostedRenderPoolTests` 1- vs `N`-thread bit-identity, `Twin`/`Keys` phase continuity, and `SnapshotHistory::RenderScope` snapshot reclamation.

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| 0. Approved Issue First | Pass | `nota-daw/nota#1050` approved (`approved` label). |
| I. Real-Time Audio Safety | Pass | Lock-free `SilenceGate` and wait-free `ActiveBusMask`; zero allocations/locks on RT path. |
| II. Boundary Discipline | Pass | Pure C enum + `nota_plugin_set_sleep_mode` / `nota_plugin_sleep_mode`; JUCE confined to `pluginhost/`. |
| III. Licensing Hygiene | Pass | AGPL-3.0-only SPDX headers on every new file (`SilenceGate.h`, `RenderBusState.h`, `SilenceGateAndBusMaskTests.cs`). |
| IV. Verification Gate | Pass | Smoke tests verify built-in synths, racks, hosted VST3 probe sleep/wake, sleep modes, and bus skips. |
| V. One Logical Change per PR | Pass | Phase 2 stacked cleanly on Phase 1 branch; Phases 3–6 remain separate. |
| VI. Documentation Stays in Sync | Pass | `CHANGELOG.md` `[Unreleased]` updated once implementation finishes. |
| VII. Spec-First | Pass | Spec, research, data-model, contracts, quickstart, and tasks authored prior to coding. |
| VIII. Safety Rules Never Weakened | Pass | All AR-1..AR-7 guarantees respected cleanly. |

## Project Structure

```text
specs/115-instrument-plugin-sleep-gate/
├── checklists/requirements.md
├── spec.md
├── research.md
├── data-model.md
├── contracts/c-abi.md
├── quickstart.md
├── plan.md
└── tasks.md

src/native/nota.engine/
├── include/nota/nota_engine.h
├── src/abi/nota_engine_device.cpp
├── src/dsp/core/SilenceGate.h                       # [NEW]
├── src/devices/{Device,Instrument}.h
├── src/devices/instruments/{BassSynth,Consort,FluxSynth,Monolith,OperatorSynth,PendulumSynth,Pentad,Synth,VoltSynth,WavetableSynth,Twin,Keys}.{h,cpp}
├── src/devices/racks/{RackDevice,RackInstrument,DrumRack}.{h,cpp}
├── pluginhost/hosting/{PluginInstrument,PluginEffect}.{h,cpp}
└── src/engine/{RenderBusState.h,Engine.h,Engine_Devices.cpp,Engine_Render.cpp}

src/managed/
├── Nota.Application/Engine/{EngineContracts,IAudioEngine}.cs
└── Nota.Infrastructure/Interop/{NativeMethods,NotaEngine}.Devices.cs

tests/Nota.SmokeTest/
├── HostedRenderPoolTests.cs
└── SilenceGateAndBusMaskTests.cs                    # [NEW]
```
