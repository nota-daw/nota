# Contract: C ABI additions (`include/nota/nota_engine.h`)

All new calls sit in the "audio device settings (M7-1)" block. Like the existing setters
they only **stage** values; `nota_audio_apply()` persists and restarts. On macOS/Linux
every ASIO call is a no-op: counts return 0, `nota_audio_asio_available()` returns 0,
setters return `NOTA_OK` and change nothing.

```c
/* Audio system (Windows): 0 = WASAPI (default), 1 = ASIO. */
NOTA_API NotaResult  nota_audio_set_system(NotaEngine* engine, int32_t system);
NOTA_API int32_t     nota_audio_system(const NotaEngine* engine);          /* staged */
NOTA_API int32_t     nota_audio_active_system(const NotaEngine* engine);   /* what actually runs (after fallback) */

/* Installed ASIO drivers (refreshed by nota_audio_refresh_devices). */
NOTA_API int32_t     nota_audio_asio_available(void);                      /* 1 if this build supports ASIO */
NOTA_API int32_t     nota_audio_asio_driver_count(void);
NOTA_API const char* nota_audio_asio_driver_id(int32_t index);             /* CLSID string */
NOTA_API const char* nota_audio_asio_driver_name(int32_t index);           /* display name, de-duplicated */
NOTA_API NotaResult  nota_audio_set_asio_driver(NotaEngine* engine, const char* id);
NOTA_API const char* nota_audio_asio_driver(const NotaEngine* engine);     /* staged id */

/* Channels of the OPEN driver (0 when ASIO is not running). */
NOTA_API int32_t     nota_audio_asio_channel_count(const NotaEngine* engine, int32_t is_input);
NOTA_API const char* nota_audio_asio_channel_name(const NotaEngine* engine, int32_t is_input, int32_t index);
NOTA_API NotaResult  nota_audio_set_asio_pair(NotaEngine* engine, int32_t is_input, int32_t first_channel);
NOTA_API int32_t     nota_audio_asio_pair(const NotaEngine* engine, int32_t is_input);

/* Open the running driver's own control panel (non-blocking for the caller). */
NOTA_API NotaResult  nota_audio_asio_open_control_panel(NotaEngine* engine);

/* Device events since the last call: 0 none, 1 reset requested, 2 driver failed. Cleared on read. */
NOTA_API int32_t     nota_audio_poll_device_event(NotaEngine* engine);
/* Why the last ASIO open failed (UTF-8, engine-owned), "" if it didn't. */
NOTA_API const char* nota_audio_fallback_reason(const NotaEngine* engine);

/* Device latencies of the running backend, in frames (0 if unknown). */
NOTA_API int32_t     nota_audio_input_latency_frames(const NotaEngine* engine);
NOTA_API int32_t     nota_audio_output_latency_frames(const NotaEngine* engine);

/* Device-free ASIO self-test (fake driver): formats, routing, duplex, reset, fallback,
 * latency compensation. 1 = pass. Runs on every platform. */
NOTA_API int32_t     nota_asio_selftest(void);
```

Errors: `nota_audio_apply` returns `NOTA_OK` when ASIO fell back to WASAPI (the app
learns it from `nota_audio_active_system` + `nota_audio_fallback_reason`), and
`NOTA_ERR_AUDIO_DEVICE` only when nothing at all could be opened — same contract as the
WASAPI exclusive fallback today.

Managed mirror: `NativeMethods.Audio.cs` (LibraryImport), `NotaEngine.Audio.cs`,
`IAudioEngine` — properties `AudioSystem`, `ActiveAudioSystem`, `AsioDrivers`,
`AsioChannels(bool input)`, `AsioPair(bool input)`, `OpenAsioControlPanel()`,
`PollDeviceEvent()`, `AudioFallbackReason`, `InputLatencyFrames`, `OutputLatencyFrames`.
