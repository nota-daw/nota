# Contract: C ABI additions (`include/nota/nota_engine.h`)

These are plain C types only (AR-7). Every call runs on the message thread unless noted.
Return conventions follow the header: `NotaResult` for setters, `-1` / `0` for "none", and
strings copied into caller buffers (`out`, `cap`) returning the full length.

## Input routes (Story 2)

```c
/* Replace a track's input list. uids[i] with channel mask masks[i] (bit n = channel n+1,
 * 0 not allowed). count = 0 → All inputs. Unconnected uids are kept and receive nothing
 * until they appear. Releases notes owned by removed port/channel pairs (FR-005). */
NOTA_API NotaResult nota_track_set_midi_inputs(NotaEngine* engine, int32_t track_id,
    const char* const* uids, const uint16_t* masks, int32_t count);
NOTA_API int32_t    nota_track_midi_input_count(const NotaEngine* engine, int32_t track_id); /* 0 = All inputs */
NOTA_API int32_t    nota_track_midi_input_at(const NotaEngine* engine, int32_t track_id, int32_t index,
    char* uid_out, int32_t cap, uint16_t* mask_out);
```

`nota_track_set_midi_input` and `nota_track_set_midi_input_uid` keep their behaviour and map
onto the list (FR-001). `nota_track_get_midi_input*` keep reporting the first entry.

## Ports (#1011, outputs list)

```c
/* 1 when the input at this nota_midi_port_* index is connected but over the 16-source limit
 * (not opened, receives nothing). */
NOTA_API int32_t     nota_midi_port_over_limit(const NotaEngine* engine, int32_t index);

/* Every MIDI output (destination) the system lists, paired with an input or not. */
NOTA_API int32_t     nota_midi_output_count(NotaEngine* engine);
NOTA_API const char* nota_midi_output_uid(const NotaEngine* engine, int32_t index);
NOTA_API const char* nota_midi_output_name(const NotaEngine* engine, int32_t index);

/* Native self-test of source-index assignment with n simulated ports: 0 = pass. */
NOTA_API int32_t     nota_engine_midi_index_selftest(int32_t simulated_ports);
```

## External MIDI (Story 1)

```c
/* New instrument track with External MIDI (kind 19), like the other add_*_track calls. */
NOTA_API int32_t    nota_engine_add_external_midi_track(NotaEngine* engine, const char* name);

/* Destination of the track's External MIDI instrument: a resolved output uid ("" = none,
 * send nothing) and channel 1-16, or 0 = Keep channels (MPE member channels). Applied at
 * the next block; notes owned at the previous destination are released there (FR-010). */
NOTA_API NotaResult nota_external_midi_set(NotaEngine* engine, int32_t track_id,
    const char* output_uid, int32_t channel);
NOTA_API int32_t    nota_external_midi_get(const NotaEngine* engine, int32_t track_id,
    char* uid_out, int32_t cap, int32_t* channel_out);
/* 0 no destination, 1 sending, 2 unavailable. Any thread (relaxed atomic). */
NOTA_API int32_t    nota_external_midi_status(const NotaEngine* engine, int32_t track_id);
/* Events dropped because the output ring was full (diagnostic). Any thread. */
NOTA_API uint32_t   nota_external_midi_dropped(const NotaEngine* engine, int32_t track_id);
```

Replacing the instrument uses the existing replace path with kind 19. Persistence goes
through `getState` / `setState` (a small versioned blob: uid and channel).

## Test hooks (smoke test only)

```c
/* Remove a test port added with nota_midi_test_port_add: its input and its "test:<name>"
 * output disappear as if unplugged (portsGeneration bumps). Adding it again reconnects. */
NOTA_API NotaResult nota_midi_test_port_remove(NotaEngine* engine, const char* name);
/* Drain bytes sent to "test:<name>", each message with its due time in ns relative to the
 * engine's first rendered block. Returns messages copied. */
NOTA_API int32_t    nota_midi_test_output_read(NotaEngine* engine, const char* name,
    uint8_t* bytes_out, int32_t bytes_cap, uint64_t* due_ns_out, int32_t* lens_out, int32_t max_msgs);
```

All new calls are covered by the smoke test (constitution quality gate).
