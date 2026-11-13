# Contract: C ABI (`nota_engine.h`)

All additions are additive (AR-7); every existing entry point keeps its meaning.
`nota_audio_set_asio_pair(is_input = 0)` keeps working and now sets the Main pair's first
channel (FR-005).

## Device outputs

```c
/* Output channels of the open playback device (0 = none), at most 64, and the UTF-8 name
 * the device gives one ("" when it gives none; copied into `out`, returns its length).
 * Message thread; valid after nota_engine_start / nota_audio_apply. */
NOTA_API int32_t nota_audio_output_channel_count(const NotaEngine* engine);
NOTA_API int32_t nota_audio_output_channel_name(const NotaEngine* engine, int32_t index, char* out, int32_t cap);

/* Where Main and Cue go: 0-based first channel and width (1 or 2; width 2 needs an even
 * first). cue_first = -1: no Cue output. Takes effect on the next audio block — no device
 * restart. NOTA_ERR_INVALID_ARG for a bad width / odd stereo first / overlapping pairs.
 * The pairs are kept even when the device lacks them (see routing_state). */
NOTA_API NotaResult nota_audio_set_output_routing(NotaEngine* engine,
    int32_t main_first, int32_t main_width, int32_t cue_first, int32_t cue_width);

/* Bit flags for the routing against the open device:
 * 1 = Main's pair is missing, Main plays on outputs 1/2 instead;
 * 2 = Cue's pair is missing (or would overlap Main): Cue is silent, nothing moves to Main. */
NOTA_API int32_t nota_audio_output_routing_state(const NotaEngine* engine);
```

## Cue

```c
/* A track (audio, instrument, group or return) in Cue: its signal after its devices and
 * before its fader, pan, mute and solo is added to the Cue bus. Any number of tracks. */
NOTA_API NotaResult nota_track_set_cue(NotaEngine* engine, int32_t track_id, int32_t on);
NOTA_API int32_t    nota_track_get_cue(const NotaEngine* engine, int32_t track_id);

/* Cue level, linear gain 0 … 2 (+6 dB), default 1. Never touches Main. */
NOTA_API NotaResult nota_engine_set_cue_volume(NotaEngine* engine, float gain);
NOTA_API float      nota_engine_cue_volume(const NotaEngine* engine);

/* Cue meter, after the Cue level (peak / RMS per side). */
NOTA_API NotaResult nota_engine_cue_meter(const NotaEngine* engine, NotaMeter* out);
```

## Click

```c
/* Where the metronome and count-in clicks go: 0 Main (default, as before), 1 Cue,
 * 2 Main + Cue. A click to Cue bypasses master devices and the master fader. */
NOTA_API NotaResult nota_transport_set_click_destination(NotaEngine* engine, int32_t dest);
NOTA_API int32_t    nota_transport_click_destination(const NotaEngine* engine);

/* Click level, linear gain 0 … 2, default 1 (today's loudness), on every destination. */
NOTA_API NotaResult nota_transport_set_click_volume(NotaEngine* engine, float gain);
```

Settings per output device key on:

```c
/* The uid of the output device the staged config opens: the chosen one when connected, else
 * the system default ("" when there is none). */
NOTA_API int32_t nota_audio_effective_output_uid(const NotaEngine* engine, char* out, int32_t cap);
```

## Tests

```c
/* Pretend the output device has `channels` outputs ("Test N"); 0 clears it. */
NOTA_API void nota_engine_set_test_output_device(NotaEngine* engine, int32_t channels);
```

```c
/* Render one device block into two stereo buffers (Main, Cue) without a device. `cue` may
 * be null (behaves as nota_engine_render_device_block). */
NOTA_API NotaResult nota_engine_render_device_block_cue(NotaEngine* engine, float* main_out,
    float* cue_out, int32_t frames);

/* Run the backends' output placement on given buffers: writes `frames` × `device_channels`
 * interleaved into `device_out` using the current routing; returns routing_state flags. */
NOTA_API int32_t nota_audio_scatter_test(const NotaEngine* engine, const float* main_in,
    const float* cue_in, float* device_out, int32_t device_channels, int32_t frames);
```

Managed mirrors (`NativeMethods.*`, `NotaEngine.*`, `IAudioEngine`): `OutputChannelCount`,
`OutputChannelName`, `SetOutputRouting`, `OutputRoutingState`, `SetTrackCue`/`GetTrackCue`,
`SetCueVolume`/`CueVolume`, `CueMeter`, `SetClickDestination`, `SetClickVolume`,
`RenderDeviceBlockCue`, `ScatterTest`.
