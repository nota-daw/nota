# Contracts: Phase 6 C ABI, Managed Interop & Engine Lifecycle

## 1. Public C ABI Extensions (`src/native/nota.engine/include/nota/nota_engine.h`)

```c
typedef enum NotaSampleLoadPolicy {
    NOTA_SAMPLE_LOAD_AUTO = 0,   /* Stream if decoded PCM > threshold, else RAM */
    NOTA_SAMPLE_LOAD_MEMORY = 1, /* Always decode full float32 PCM into RAM */
    NOTA_SAMPLE_LOAD_STREAM = 2  /* Stream all seekable WAV/FLAC/MP3 files regardless of threshold */
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
    NOTA_HYBRID_OFF = 0,  /* Every track renders live on the audio callback / pool */
    NOTA_HYBRID_AUTO = 1  /* Live tracks run low-latency; eligible timeline tracks render ahead in background */
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

## 2. Managed Interop (`Nota.Application/Engine/IAudioEngine.cs` & `NotaEngine.cs`)

- Expose `SampleLoadPolicy`, `HybridBufferMode`, `NotaStreamingStats`, `NotaHybridBufferStats` cleanly in `Nota.Application`.
- Expose `StreamThresholdBytes`, `SampleLoadPolicy`, `IsSampleStreaming(long sampleId)`, `GetStreamingStats()`, `HybridBuffering`, `GetHybridStats()` on `IAudioEngine` / `NotaEngine` with exact P/Invoke bindings in `NativeMethods.cs`.

## 3. RT Safety & Determinism Contracts

1. **`RenderOffline` vs Device Block**:
   - `renderOffline()` sets `allowSyncFill = true` for `StreamingAudioSource` and bypasses `HybridBufferController` so offline bounces are 100% deterministic (`maxdiff == 0.0` across `0` vs `N` render threads and streaming vs memory decoding).
   - `render()` / `nota_engine_render_device_block()` runs strict `AR-6` (`allowSyncFill = false` for streaming sources, using cached head/ring pages populated asynchronously) and queries `HybridBufferController` per eligible leaf track.
2. **Invalidation Contract**:
   - `Engine::publishRaw()`, `seekBeats()`, `setBpm()`, `setLoop()`, `deviceSetParam()`, `midiEffectSetParam()`, `setTrackArmed()`, `setTrackMonitor()`, `noteOn()`/`noteOff()`, and `stop()` immediately notify `HybridBufferController::invalidate()` (and message-thread structural mutations fence any active worker call before publishing a track mutation).
   - `Engine::seekBeats()`, `addAudioClip()`, `addAudioClipEx()`, and `play()` notify `StreamingAudioSource` instances to pre-warm around the active playhead / clip offset.
