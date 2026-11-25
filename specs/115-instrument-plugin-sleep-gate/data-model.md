# Data Model: Shared SilenceGate, Built-in/Plugin Sleep Gating, and Active Submix Buses

**Phase**: Phase 2 of [nota-daw/nota#1050](https://github.com/nota-daw/nota/issues/1050)  
**Spec**: [spec.md](spec.md)

## Native Entities (`src/native/nota.engine`)

### 1. `nota::dsp::SilenceGate` (`src/dsp/core/SilenceGate.h`)
Lock-free, real-time-safe sleep gate shared across built-in instruments and hosted VST3 adapters.

- **Configuration**:
  - `sampleRate_` (`double`, default `44100.0`)
  - `hangoverSeconds_` (`double`, default `0.25`)
  - `hangoverSamples_` (`uint64_t`)
  - `releaseThreshold_` (`float`, default `1.0e-5f`)
- **Cross-thread state**:
  - `wakeEpoch_` (`std::atomic<uint32_t>{1}`): incremented by `wake()` / `setNeverSleep()` from any thread.
  - `seenEpoch_` (`std::atomic<uint32_t>{0}`): updated by audio thread as wakes are observed.
  - `sleeping_` (`std::atomic<bool>{false}`): published by audio thread after `quietSamples_ > hangoverSamples_`.
  - `neverSleep_` (`std::atomic<bool>{false}`): explicit user/plugin override (`PluginSleepMode::Never`).
- **Audio-thread state**:
  - `quietSamples_` (`uint64_t{0}`)

### 2. `nota::PluginSleepMode` & `NotaPluginSleepMode`
Explicit per-device sleep policy for `Device` / `Instrument` (used primarily by hosted plugins via `SilenceGate`).

| Enum Value | C ABI Value | Semantics |
|---|---|---|
| `PluginSleepMode::Auto` | `NOTA_PLUGIN_SLEEP_AUTO = 0` | Default. Auto-sleep once input, MIDI, and declared tail finish + hangover. |
| `PluginSleepMode::Never` | `NOTA_PLUGIN_SLEEP_NEVER = 1` | Disables sleep gating (`SilenceGate::setNeverSleep(true)`); device always processes. |

### 3. `nota::RenderBusState` (`src/engine/RenderBusState.h`)
Per-engine bus-activity coordinator eliminating redundant zeroing and silent bus chains in `mixGraph()`.

- `ActiveBusMask<kMaxGroups>` & `ActiveBusMask<kMaxReturns>`:
  - `dirty_` / `active_` byte-atomics: workers mark `markActive(slot)` whenever a non-skipped track, child group, or send writes into a bus.
  - Only buses marked dirty during block `N-1` need `std::fill_n(..., 0.0f)` before block `N` (so permanently silent buses pay zero `memset`/`fill_n` cost too!).
  - Wait-free during parallel leaf / group level passes; queried after the render pool barrier.

## Managed Entities (`src/managed/`)

### 4. `PluginSleepMode` (`Nota.Application.Engine`)
```csharp
public enum PluginSleepMode
{
    Auto = 0,
    Never = 1,
}
```
Exposed on `IAudioEngine` and `NotaEngine` via `SetPluginSleepMode(int trackId, int deviceIndex, PluginSleepMode mode)` and `GetPluginSleepMode(int trackId, int deviceIndex)`.
