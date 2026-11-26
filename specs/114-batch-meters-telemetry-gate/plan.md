# Implementation Plan: Batch Track Meter Snapshots and On-Demand Device Telemetry

**Branch**: `perf/batch-meters-telemetry-gate` | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/114-batch-meters-telemetry-gate/spec.md` (Phase 1 of [nota-daw/nota#1050](https://github.com/nota-daw/nota/issues/1050))

## Summary

Eliminate two zero-signal overheads present in every large Nota project:
1. Replace `O(T^2)` per-strip track meter polling across `ArrangementView`, `MixerView`, and `SessionView` with a single `O(T)` batch C ABI snapshot (`nota_engine_read_track_meters` / `IAudioEngine.ReadTrackMeters`), returning a clean `TrackMeterReading` span.
2. Consolidate duplicated CPU clock/EMA boilerplate across 24 built-in devices (`22` effects + `Consort` and `Pentad`) into one readable `dsp::DeviceTelemetry` abstraction (`src/dsp/core/DeviceTelemetry.h`) using RAII `ScopedBlockTimer` and an audio-frame-counted observation lease renewed automatically by `scopeRead()`, controlled via a clean `TelemetryMode` (`OnDemand`, `AlwaysOff`, `AlwaysOn`) in the C ABI and `IAudioEngine`.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): `AR-4`, `AR-5`, `AR-6`, `AR-7` are binding.
- **UI work** → [`DESIGN.md`](../../DESIGN.md): preserve layout/behaviour; fix `AutoFilterDeviceBody` sentinel so LFO phase visualization does not depend on CPU timing having run while closed.
- **Hard Readability Gate (#1050)** → Use human-readable abstractions (`DeviceTelemetry`, `TrackMeterReading`, `TelemetryMode`), delete duplicate ad-hoc clock/EMA code across devices, and avoid expanding `Engine.h` / `Engine_Render.cpp` monolith logic.

## Technical Context

**Language/Version**: C++20 (`nota.engine`), C# / .NET 10 (`Nota.Application`, `Nota.Infrastructure`, `Nota.App`, `Nota.SmokeTest`)

**Primary Dependencies**: None — standard C++20 `<chrono>` / `<atomic>`, .NET P/Invoke `[LibraryImport]`

**Storage**: N/A

**Audio-Thread Impact** *(required)*: Zero allocations, zero locks, zero I/O (`AR-6`). `DeviceTelemetry::ScopedBlockTimer` reads a single relaxed atomic request epoch and decrements a thread-local audio-frame lease counter; `steady_clock::now()` is skipped completely while unobserved (`OnDemand`) or disabled (`AlwaysOff`).

**Testing**: `scripts/build.sh` (`Nota.SmokeTest`, including new batch meter and telemetry mode/lease checks), `tests/perf/nota_idle_cpu`, `scripts/bench-engine.sh` vs `develop` baseline

**Target Platform**: macOS (`arm64` + `x86_64`), Windows, Linux

**Project Type**: Native C++20 engine + .NET 10 / Avalonia desktop app

**Performance Goals**: `1` P/Invoke + `O(T)` read per UI meter tick (down from `T` calls and `O(T^2)` track lookups); `0` OS clock queries per audio block on closed device cards.

**Constraints**: Must keep existing `scopeRead()` indices, analyzer buffers, `EqAnalyzerTests`, `VocoderTests`, `AutoFilter` smoke checks, and `RenderPoolTests` bit-identical.

**Scale/Scope**: Core engine ABI + `Device`/`Instrument` bases + 24 built-in devices + 3 UI meter views + `AutoFilterDeviceBody` + `Nota.SmokeTest`

## Constitution Check

*GATE: Passed before design; re-checked after contracts.*

| Principle | Status | Note |
|---|---|---|
| 0. Approved Issue First | Pass | `nota-daw/nota#1050` is OPEN and carries `approved`. |
| I. Real-Time Audio Safety | Pass | `DeviceTelemetry` is lock-free (`relaxed` epoch bump on reader, local frame countdown on RT). `readTrackMeters` reads atomics once without locking or RT participation. |
| II. Boundary Discipline | Pass | Pure C structs and `enum` in `nota_engine.h` (`NotaTrackMeterEntry`, `NotaTelemetryMode`); no C++ types across ABI; `nota_exports.*` already exports `_nota_*` / `nota_*`. |
| III. Licensing Hygiene | Pass | New header `DeviceTelemetry.h` carries two-line AGPL-3.0-only SPDX header; zero new dependencies. |
| IV. Verification Gate | Pass | `Nota.SmokeTest` extended for batch meters & telemetry lease/modes; `scripts/build.sh` + `scripts/bench-engine.sh` verify zero regressions. |
| V. One Logical Change per PR | Pass | Phase 1 only (`Refs #1050`); phases 2–6 deferred to follow-up PRs. |
| VI. Documentation Stays in Sync | Pass | `CHANGELOG.md` `[Unreleased]` entry updated when implemented (`Added`/`Changed` internal perf/API as appropriate). |
| VII. Spec-First | Pass | `spec.md`, `research.md`, `data-model.md`, `contracts/c-abi.md`, `quickstart.md` written before tasks/code. |
| VIII. Safety Rules Never Weakened | Pass | All AR rules preserved cleanly. |

## Project Structure

### Documentation (this feature)

```text
specs/114-batch-meters-telemetry-gate/
├── checklists/requirements.md
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── contracts/c-abi.md
├── quickstart.md
└── tasks.md              # Generated via /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h                  # NotaTrackMeterEntry, NotaTelemetryMode, batch/telemetry APIs
├── src/abi/nota_engine_core.cpp                # C ABI entry points
├── src/dsp/core/DeviceTelemetry.h              # [NEW] TelemetryMode, DeviceTelemetry, ScopedBlockTimer
├── src/devices/Device.h                        # Protected telemetry_ helper + setTelemetryMode / observe
├── src/devices/Instrument.h                    # Protected telemetry_ helper + setTelemetryMode / observe
├── src/devices/effects/*.{h,cpp} (22 effects)  # Delegate timing + S_Cpu to telemetry_
├── src/devices/instruments/{Consort,Pentad}.{h,cpp} # Delegate timing + ScCpu to telemetry_
├── src/devices/racks/{RackDevice,RackInstrument}.{h,cpp} # Propagate TelemetryMode to nested chains
└── src/engine/{Engine.h,Engine.cpp,Engine_Devices.cpp,Engine_Tracks.cpp} # readTrackMeters, telemetry mode sync

src/managed/
├── Nota.Application/Engine/EngineContracts.cs  # TrackMeterReading, DeviceTelemetryMode
├── Nota.Application/Engine/IAudioEngine.cs     # ReadTrackMeters(Span<TrackMeterReading>), DeviceTelemetry
├── Nota.Infrastructure/Interop/NativeMethods.cs # [LibraryImport] bindings
├── Nota.Infrastructure/Interop/NotaEngine.cs   # Marshaling over Span<TrackMeterReading>
└── Nota.App/
    ├── ArrangementView.cs                      # Single batch ReadTrackMeters per UI tick
    ├── MixerView.cs                            # Single batch ReadTrackMeters per UI tick + hidden clip dots
    ├── SessionView.Mixer.cs                    # Single batch ReadTrackMeters per UI tick
    └── DeviceCards/Bodies/AutoFilterDeviceBody.cs # Replace Sc(S_Cpu) > 0 sentinel with Sc(S_SampleRate) > 0

tests/Nota.SmokeTest/
└── TrackMeterAndTelemetryTests.cs            # [NEW] Batch meter & on-demand telemetry verification
```

**Structure Decision**: Keep new native abstraction cleanly in `src/native/nota.engine/src/dsp/core/DeviceTelemetry.h` alongside `SilenceDetector.h` and `AudioSpan.h`, while routing managed callers exclusively through `Nota.Application` → `Nota.Infrastructure` → `nota_engine.h` (`AR-7`).

## Complexity Tracking

No constitution violations; no complexity exceptions required.
