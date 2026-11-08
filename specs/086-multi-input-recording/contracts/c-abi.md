# Contract: C ABI (`nota_engine.h`)

All additions are additive (AR-7); every existing entry point keeps its meaning.

## Device inputs

```c
/* Number of input channels of the open capture device (0 = none open), at most 64.
 * Message thread; valid after the device opened (arming / monitoring opens it, so does
 * nota_audio_input_probe). */
NOTA_API int32_t nota_audio_input_channel_count(const NotaEngine* engine);

/* UTF-8 name the device gives input channel `index` (0-based), "" when it gives none; copied
 * into `out` (cap bytes), returns its length. While the device is closed: what it said when
 * last open or probed, until the input device changes. */
NOTA_API int32_t nota_audio_input_channel_name(const NotaEngine* engine, int32_t index, char* out, int32_t cap);

/* Open the selected capture device just to read its channels, then close it unless a track
 * needs it. Once per input device (a failure is remembered too). Returns the channel count
 * (0 on failure). Used by pickers the user opens and the Audio I/O page. */
NOTA_API int32_t nota_audio_input_probe(NotaEngine* engine);
```

## Track audio input

```c
/* An audio track's hardware input: `first` 0-based device channel, `width` 1 (mono) or
 * 2 (stereo pair, `first` even). Kept even when the device lacks it (the track then records
 * and monitors nothing). NOTA_ERR_INVALID_ARG for a bad width / odd stereo first. */
NOTA_API NotaResult nota_track_set_input(NotaEngine* engine, int32_t track_id, int32_t first, int32_t width);
/* Packs first | (width << 8); -1 for a non-audio track. */
NOTA_API int32_t    nota_track_get_input(const NotaEngine* engine, int32_t track_id);
/* 1 when the track's pick exists on the open device. */
NOTA_API int32_t    nota_track_input_available(const NotaEngine* engine, int32_t track_id);
```

`nota_track_set_input_channels` / `nota_track_get_input_channels` stay: 0 ↔ (0,2),
1 ↔ (0,1), 2 ↔ (1,1); any other pick reads back as −1.

```c
/* Inputs switched off on the Audio I/O page (0-based channels). A track on one of them
 * records nothing (reason 3). Message thread; the set is read at take start. */
NOTA_API NotaResult nota_audio_input_set_disabled(NotaEngine* engine, const int32_t* channels, int32_t count);
```

## Track MIDI input

```c
/* An instrument track's MIDI input: source -2 = All inputs, else a hardware source index
 * (nota_midi_source_of_uid); channel 0 = all, 1-16 one channel. */
NOTA_API NotaResult nota_track_set_midi_input(NotaEngine* engine, int32_t track_id, int32_t source, int32_t channel);
/* Packs (source + 2) | (channel << 8). */
NOTA_API int32_t    nota_track_get_midi_input(const NotaEngine* engine, int32_t track_id);
/* Source index of a connected MIDI input by uid; -1 when it is not connected. */
NOTA_API int32_t    nota_midi_source_of_uid(const NotaEngine* engine, const char* uid);
```

The managed side keeps the uid and re-resolves it after a MIDI device change.

## Takes

```c
/* Tracks recording right now (audio and MIDI lanes). Writes up to `cap` ids to `out` and
 * returns the total count. Replaces reading the single audio record track for UI state. */
NOTA_API int32_t nota_engine_recording_tracks(const NotaEngine* engine, int32_t* out, int32_t cap);

/* Armed tracks the last take start could not record, with a reason each:
 * 1 input unavailable, 2 too many lanes, 3 input switched off. Returns the count. */
NOTA_API int32_t nota_engine_record_skipped(const NotaEngine* engine, int32_t* track_ids, int32_t* reasons, int32_t cap);
```

`nota_engine_audio_record_track` (existing) keeps returning the first audio lane's track,
for old callers.

## Tests (no device)

The existing `nota_take_record_test` (one stereo track) stays. Added next to it:

```c
/* Pretend a capture device with `channels` inputs (names "Test N") is open; 0 clears it. */
NOTA_API void    nota_engine_set_test_input_device(NotaEngine* engine, int32_t channels);
/* Smoke-test hook: record `frames` of `channels`-wide interleaved input as one take on every
 * armed track, started at start_beat — through the wide capture ring, the drain and stop,
 * under the current loop — without a device. Returns the number of lanes recorded. */
NOTA_API int32_t nota_take_record_test_n(NotaEngine* engine, const float* interleaved, int32_t channels,
                                         int32_t frames, double start_beat);
/* Feed a MIDI message as if hardware source `source` sent it. */
NOTA_API void    nota_engine_push_midi_for_test(NotaEngine* engine, int32_t source, int32_t status, int32_t channel,
                                                int32_t d1, int32_t d2);
```
