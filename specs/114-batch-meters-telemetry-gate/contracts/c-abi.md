# C ABI & Managed Boundary Contract: Phase 1 (#1050)

## Public C ABI (`src/native/nota.engine/include/nota/nota_engine.h`)

```c
/* Single track's post-fader level meter entry returned by nota_engine_read_track_meters. */
typedef struct NotaTrackMeterEntry {
    int32_t   track_id;
    NotaMeter meter;
} NotaTrackMeterEntry;

/* Controls how built-in devices measure per-block CPU usage on the audio thread.
 * On-demand (default) skips OS clock reads whenever no device card/scope is observing. */
typedef enum NotaTelemetryMode {
    NOTA_TELEMETRY_ON_DEMAND = 0,
    NOTA_TELEMETRY_ALWAYS_OFF = 1,
    NOTA_TELEMETRY_ALWAYS_ON = 2
} NotaTelemetryMode;

/* Reads post-fader peak/RMS meters for all non-master tracks in graph order in a single
 * snapshot pass. Writes at most `capacity` entries into `out_entries` (ignored when
 * out_entries == NULL or capacity <= 0). Returns the total track count (0 if engine is NULL). */
NOTA_API int32_t nota_engine_read_track_meters(
    const NotaEngine* engine,
    NotaTrackMeterEntry* out_entries,
    int32_t capacity);

/* Sets / queries the built-in device CPU telemetry mode (`NOTA_TELEMETRY_*`).
 * Safe to call while running; updates are lock-free on the audio thread (AR-6). */
NOTA_API NotaResult nota_engine_set_device_telemetry(NotaEngine* engine, int32_t mode);
NOTA_API int32_t    nota_engine_device_telemetry(const NotaEngine* engine);
```

### Semantics & Invariants (`AR-6` / `AR-7`)
- `nota_engine_read_track_meters(e, nullptr, 0)` returns `TrackCount` safely without dereferencing `out_entries`.
- When `capacity < trackCount`, the first `capacity` tracks in graph order are written and full `trackCount` is still returned so callers can resize once if needed.
- `nota_engine_set_device_telemetry` validates `mode` in `[0, 2]`, returning `NOTA_ERR_INVALID_ARG` otherwise.
- Existing `nota_engine_track_meter` and `nota_device_scope` / `nota_instrument_scope` signatures remain 100% backwards-compatible.

## Managed Boundary (`IAudioEngine` & `NotaEngine`)

```csharp
public enum DeviceTelemetryMode
{
    OnDemand = 0,
    AlwaysOff = 1,
    AlwaysOn = 2
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct TrackMeterReading(int TrackId, NotaMeter Meter);

// On IAudioEngine:
int ReadTrackMeters(Span<TrackMeterReading> destination);
DeviceTelemetryMode DeviceTelemetry { get; set; }
```
