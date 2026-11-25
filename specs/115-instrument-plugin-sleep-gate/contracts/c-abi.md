# Contract: C ABI Extensions (Phase 2 — Sleep Gating)

**Phase**: Phase 2 of [nota-daw/nota/issues/1050](https://github.com/nota-daw/nota/issues/1050)

## Header Additions (`src/native/nota.engine/include/nota/nota_engine.h`)

### 1. `NotaPluginSleepMode` Enum

```c
/* Auto-sleep policy for hosted plugins (Phase 2).
 * AUTO wakes automatically on MIDI, audio, sidechain, or parameter changes and
 * sleeps once silent past the declared tail length + hangover. NEVER keeps the
 * plugin rendering every block (e.g. free-running generators or noise units). */
typedef enum NotaPluginSleepMode {
    NOTA_PLUGIN_SLEEP_AUTO  = 0,
    NOTA_PLUGIN_SLEEP_NEVER = 1
} NotaPluginSleepMode;
```

### 2. Sleep Mode Control Functions

Addressing follows `nota_track_set_device_bypassed`:
- `device_index == -1`: targets the track's instrument.
- `device_index >= 0`: targets the insert effect at `device_index`.

```c
/* Controls auto-sleep policy for a device or instrument on a track (NotaPluginSleepMode).
 * device_index == -1 targets the track's instrument; >= 0 targets an insert device.
 * Built-in devices accept NOTA_PLUGIN_SLEEP_AUTO or NOTA_PLUGIN_SLEEP_NEVER uniformly.
 * Returns NOTA_ERR_INVALID_ARG if engine is NULL, mode is invalid, or track/device is missing.
 * Message-thread safe and lock-free across the audio thread. */
NOTA_API NotaResult nota_plugin_set_sleep_mode(
    NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t mode);

/* Returns the current NotaPluginSleepMode (defaults to NOTA_PLUGIN_SLEEP_AUTO). */
NOTA_API int32_t nota_plugin_sleep_mode(
    const NotaEngine* engine, int32_t track_id, int32_t device_index);
```

## Managed Boundary (`IAudioEngine` / `NotaEngine`)

- `Nota.Application/Engine/EngineContracts.cs`: `public enum PluginSleepMode { Auto = 0, Never = 1 }`
- `Nota.Application/Engine/IAudioEngine.cs`:
  - `void SetPluginSleepMode(int trackId, int deviceIndex, PluginSleepMode mode);`
  - `PluginSleepMode GetPluginSleepMode(int trackId, int deviceIndex);`
- `Nota.Infrastructure/Interop/NativeMethods.Devices.cs`:
  - `PluginSetSleepMode(IntPtr engine, int trackId, int deviceIndex, int mode)`
  - `PluginSleepMode(IntPtr engine, int trackId, int deviceIndex)`
- `Nota.Infrastructure/Interop/NotaEngine.Devices.cs`:
  - Implements both via `ThrowIfDisposed()` and `Check(...)`.
