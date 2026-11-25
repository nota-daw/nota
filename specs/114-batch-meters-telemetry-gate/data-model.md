# Data Model: Batch Track Meter Snapshots and On-Demand Device Telemetry

**Phase**: Phase 1 of [nota-daw/nota#1050](https://github.com/nota-daw/nota/issues/1050)  
**Spec**: [spec.md](spec.md)

## Native Entities (`src/native/nota.engine`)

### 1. `nota::dsp::TelemetryMode`
Represents how built-in devices collect block-execution CPU telemetry (`src/dsp/core/DeviceTelemetry.h`).

| Value | C ABI Enum | Meaning |
|---|---|---|
| `OnDemand = 0` | `NOTA_TELEMETRY_ON_DEMAND` | Default. Measures `steady_clock::now()` only while a card/tool recently called `scopeRead()`. |
| `AlwaysOff = 1` | `NOTA_TELEMETRY_ALWAYS_OFF` | Never queries wall-clock time on the audio thread; `cpuLoad()` remains `0.0f`. |
| `AlwaysOn = 2` | `NOTA_TELEMETRY_ALWAYS_ON` | Measures every rendered audio block regardless of UI observation (benchmarks/profiling). |

### 2. `nota::dsp::DeviceTelemetry`
Self-contained timing state held by `Device` and `Instrument` base classes, eliminating 24 duplicate per-device timing implementations.

- **Fields**:
  - `mode_` (`std::atomic<TelemetryMode>`): defaults to `TelemetryMode::OnDemand`.
  - `observeRequests_` (`std::atomic<uint32_t>`): relaxed counter bumped by `observe()` / `observeAndRead()` on reader threads (`const` methods mark it `mutable`).
  - `lastSeenObserve_` (`uint32_t`): audio-thread-owned sequence tracker.
  - `rtLeaseFrames_` (`int64_t`): audio-thread-owned remaining lease frames (seeded to `clamp(sr * 3.0, 4096.0, 1e8)` when `observeRequests_` changes; decremented by `frames` per block).
  - `smoothedCpu_` (`double`): audio-thread smoothed render fraction (`elapsed / blockSeconds`).
  - `cpuLoad_` (`std::atomic<float>`): lock-free published CPU fraction for `scopeRead()`.
- **Lifecycle**:
  - Closed card (`rtLeaseFrames_ == 0` in `OnDemand`): `ScopedBlockTimer` does 1 relaxed atomic load, skips `Clock::now()`, and avoids OS clock calls completely.
  - Card opens / test calls `scopeRead()`: `observeAndRead()` bumps `observeRequests_` and returns `cpuLoad_`. On the next audio block, `ScopedBlockTimer` arms `rtLeaseFrames_` for ~3 seconds of audio and times the block until return via RAII destructor.
  - Card closes: after 3 seconds without `observeAndRead()`, `rtLeaseFrames_` reaches `0`, clears `smoothedCpu_` / `cpuLoad_`, and returns to zero clock overhead.

### 3. `NotaTrackMeterEntry` (C ABI struct)
- **Fields**:
  - `int32_t track_id` — stable track identifier matching `NotaTrackInfo::id`.
  - `NotaMeter meter` — post-fader `peak_l`, `peak_r`, `rms_l`, `rms_r`.

## Managed Entities (`src/managed/`)

### 4. `TrackMeterReading` (`Nota.Application.Engine`)
Blittable C# `readonly record struct` (`[StructLayout(LayoutKind.Sequential)]`) binary-compatible with `NotaTrackMeterEntry`:
- `int TrackId`
- `NotaMeter Meter`

### 5. `DeviceTelemetryMode` (`Nota.Application.Engine`)
C# enum matching `NotaTelemetryMode`:
- `OnDemand = 0`
- `AlwaysOff = 1`
- `AlwaysOn = 2`
