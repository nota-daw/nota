# Public C ABI & Managed Boundary Contracts (`nota-daw/nota#1050`)

**Spec**: [../spec.md](../spec.md)

## 1. Public C ABI (`src/native/nota.engine/include/nota/nota_engine.h`)

### Batch Track Meters & On-Demand Device Telemetry

```c
/* Single track's post-fader level meter entry returned by nota_engine_read_track_meters. */
typedef struct NotaTrackMeterEntry {
    int32_t   track_id;
    NotaMeter meter;
} NotaTrackMeterEntry;

/* Controls how built-in devices measure per-block CPU usage on the audio thread.
 * On-demand (default) skips OS clock reads whenever no device card or scope is observing. */
typedef enum NotaTelemetryMode {
    NOTA_TELEMETRY_ON_DEMAND = 0,
    NOTA_TELEMETRY_ALWAYS_OFF = 1,
    NOTA_TELEMETRY_ALWAYS_ON = 2
} NotaTelemetryMode;

NOTA_API int32_t nota_engine_read_track_meters(
    const NotaEngine* engine,
    NotaTrackMeterEntry* out_entries,
    int32_t capacity);

/* Controls per-device CPU telemetry on the audio thread (NotaTelemetryMode).
 * Safe to call while running; updates are lock-free (AR-6). */
NOTA_API NotaResult nota_engine_set_device_telemetry(NotaEngine* engine, int32_t mode);
NOTA_API int32_t    nota_engine_device_telemetry(const NotaEngine* engine);
```

### Plugin Auto-Sleep Policy & Editor Keep-Awake Self-Test

```c
typedef enum NotaPluginSleepMode {
    NOTA_PLUGIN_SLEEP_AUTO  = 0,
    NOTA_PLUGIN_SLEEP_NEVER = 1
} NotaPluginSleepMode;

/* Controls auto-sleep policy for a device or instrument (device_index == -1 targets the
 * track's instrument; >= 0 targets an insert device). Lock-free across the audio thread. */
NOTA_API NotaResult nota_plugin_set_sleep_mode(
    NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t mode);
NOTA_API int32_t    nota_plugin_sleep_mode(
    const NotaEngine* engine, int32_t track_id, int32_t device_index);

/* Device-free selftest verifying open editors hold SilenceGate and audio/rack paths active. 0 = pass. */
NOTA_API int32_t    nota_engine_plugin_editor_sleep_selftest(int32_t catalog_index);
```

### Disk Audio Streaming & Hybrid Anticipative Buffering

```c
typedef enum NotaSampleLoadPolicy {
    NOTA_SAMPLE_LOAD_AUTO = 0,   /* Stream if decoded float32 PCM > threshold, else RAM */
    NOTA_SAMPLE_LOAD_MEMORY = 1, /* Decode full PCM into RAM regardless of size */
    NOTA_SAMPLE_LOAD_STREAM = 2  /* Stream all seekable WAV/FLAC/MP3 files from disk */
} NotaSampleLoadPolicy;

typedef struct NotaStreamingStats {
    int32_t active_streams;
    int64_t stream_threshold_bytes;
    int64_t stream_cache_bytes;
    int64_t resident_sample_bytes;
    int64_t pages_faulted;
    int64_t pages_served_async;
    int64_t pages_served_sync;
    int64_t rt_underruns;
} NotaStreamingStats;

typedef enum NotaHybridBufferMode {
    NOTA_HYBRID_OFF = 0,  /* Every track renders inside the audio callback / pool */
    NOTA_HYBRID_AUTO = 1  /* Live tracks run low-latency; non-live arrangement tracks look ahead */
} NotaHybridBufferMode;

typedef struct NotaHybridBufferStats {
    int32_t active;
    int32_t live_tracks;
    int32_t anticipative_tracks;
    int32_t lookahead_frames;
    int32_t chunk_frames;
    int64_t cache_hits;
    int64_t cache_misses;
    int64_t invalidations;
} NotaHybridBufferStats;

NOTA_API NotaResult nota_engine_set_stream_threshold_bytes(NotaEngine* engine, int64_t bytes);
NOTA_API int64_t    nota_engine_stream_threshold_bytes(const NotaEngine* engine);
NOTA_API NotaResult nota_engine_set_sample_load_policy(NotaEngine* engine, int32_t policy);
NOTA_API int32_t    nota_engine_sample_load_policy(const NotaEngine* engine);
NOTA_API int32_t    nota_sample_is_streaming(const NotaEngine* engine, int64_t sample_id);
NOTA_API NotaResult nota_engine_streaming_stats(const NotaEngine* engine, NotaStreamingStats* out);

NOTA_API NotaResult nota_engine_set_hybrid_buffering(NotaEngine* engine, int32_t mode);
NOTA_API int32_t    nota_engine_hybrid_buffering(const NotaEngine* engine);
NOTA_API NotaResult nota_engine_hybrid_stats(const NotaEngine* engine, NotaHybridBufferStats* out);
```

### C ABI Semantics & Compatibility

- `nota_engine_read_track_meters(e, nullptr, 0)` safely returns total track count (`0` if `e == nullptr`); partial `capacity < total` writes the first `capacity` entries in graph order and returns full `total`.
- `nota_engine_set_device_telemetry`, `nota_plugin_set_sleep_mode`, `nota_engine_set_sample_load_policy`, and `nota_engine_set_hybrid_buffering` validate enum bounds cleanly and return `NOTA_ERR_INVALID_ARG` on invalid arguments.
- Existing `nota_engine_track_meter`, `nota_device_scope`, `nota_instrument_scope`, and `nota_sample_read` signatures and slot layouts stay backwards-compatible.

## 2. Managed Contracts (`src/managed/Nota.Application` & `Nota.Infrastructure`)

- **`EngineContracts.cs`**:
  - `DeviceTelemetryMode` (`OnDemand = 0`, `AlwaysOff = 1`, `AlwaysOn = 2`)
  - `SampleLoadPolicy` (`Auto = 0`, `MemoryOnly = 1`, `StreamAlways = 2`)
  - `HybridBufferMode` (`Off = 0`, `Auto = 1`)
  - `PluginSleepMode` (`Auto = 0`, `Never = 1`)
  - `NotaStreamingStats`, `NotaHybridBufferStats` (`[StructLayout(LayoutKind.Sequential)]`)
  - `TrackMeterReading(int TrackId, NotaMeter Meter)` and reusable per-view `TrackMeterBatch`
- **`IAudioEngine.cs` / `NotaEngine.cs` / `NotaEngine.Devices.cs`**:
  - `int ReadTrackMeters(Span<TrackMeterReading> destination)`
  - `DeviceTelemetryMode DeviceTelemetry { get; set; }`
  - `void SetPluginSleepMode(int trackId, int deviceIndex, PluginSleepMode mode)` / `PluginSleepMode GetPluginSleepMode(int trackId, int deviceIndex)`
  - `long StreamThresholdBytes { get; set; }`, `SampleLoadPolicy SampleLoadPolicy { get; set; }`, `bool IsSampleStreaming(long sampleId)`, `NotaStreamingStats GetStreamingStats()`
  - `HybridBufferMode HybridBuffering { get; set; }`, `NotaHybridBufferStats GetHybridStats()`
