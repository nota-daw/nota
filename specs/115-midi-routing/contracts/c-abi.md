# Contract: C ABI additions (`include/nota/nota_engine.h`)

These are plain C types only (AR-7). Every call runs on the message thread unless noted.
Conventions follow the header: `NotaResult` for setters, `-1` for "none", and strings copied
into caller buffers (`out`, `cap`) returning the full length.

## Input routes (Story 2)

```c
/* all = 1: every input on all_channels (bit n = channel n+1; 0 = all). all = 0: the `count` ports
 * uids[i] (names[i] = the name shown while it is missing), each on masks[i] (mask 0 dropped; a
 * port listed twice takes both). One undo step; held notes from an input no longer listed end. */
NOTA_API NotaResult nota_track_set_midi_inputs(NotaEngine*, int32_t track_id, int32_t all, uint16_t all_channels,
    const char* const* uids, const char* const* names, const uint16_t* masks, int32_t count);
NOTA_API int32_t    nota_track_midi_input_count(const NotaEngine*, int32_t track_id);   /* -1 every input, -2 not an instrument track */
NOTA_API uint16_t   nota_track_midi_input_all_channels(const NotaEngine*, int32_t track_id);
NOTA_API int32_t    nota_track_midi_input_at(const NotaEngine*, int32_t track_id, int32_t index, int32_t field /* 0 uid, 1 name */,
    char* out, int32_t cap, uint16_t* mask_out);
```

`nota_track_set_midi_input` / `nota_track_set_midi_input_uid` keep their behaviour and map onto
the list (FR-001); `nota_track_get_midi_input*` report its first port.

## Ports (#1011, outputs)

```c
NOTA_API int32_t     nota_midi_port_over_limit(const NotaEngine*, int32_t index);  /* 1 = past the 16-input limit, not opened */
NOTA_API int32_t     nota_engine_midi_index_selftest(int32_t simulated_ports);    /* 0 = pass */
NOTA_API int32_t     nota_midi_output_count(NotaEngine*);
NOTA_API const char* nota_midi_output_uid(const NotaEngine*, int32_t index);
NOTA_API const char* nota_midi_output_name(const NotaEngine*, int32_t index);
```

## External MIDI (Story 1)

```c
NOTA_API int32_t    nota_engine_add_external_midi_track(NotaEngine*);
/* A new instance with this destination (one undo step); the old one releases its notes at its
 * output. channel 1-16, or 0 = Keep channels (MPE member channels). Only that exact uid is used. */
NOTA_API NotaResult nota_external_midi_set(NotaEngine*, int32_t track_id, const char* output_uid,
    const char* output_name, const char* role_id, int32_t channel);
NOTA_API int32_t    nota_external_midi_text(const NotaEngine*, int32_t track_id, int32_t field /* 0 uid, 1 name, 2 role */,
    char* out, int32_t cap);
NOTA_API int32_t    nota_external_midi_channel(const NotaEngine*, int32_t track_id);   /* -1 not External MIDI */
NOTA_API int32_t    nota_external_midi_status(const NotaEngine*, int32_t track_id);    /* 0 none, 1 sending, 2 unavailable; any thread */
NOTA_API uint32_t   nota_external_midi_dropped(const NotaEngine*, int32_t track_id);   /* ring overflows; any thread */
```

Replacing the instrument through the existing replace path with kind 19 also works; the state
blob (`getState` / `setState`) carries channel, uid, name and role.

## Output roles (Story 4)

```c
/* In the undo snapshot (Graph::midiRoles). set: the whole table, one undo step. */
NOTA_API NotaResult nota_midi_roles_set(NotaEngine*, const char* const* ids, const char* const* names,
    const char* const* output_uids, const char* const* output_names, int32_t count);
NOTA_API int32_t    nota_midi_role_count(const NotaEngine*);
NOTA_API int32_t    nota_midi_role_text(const NotaEngine*, int32_t index, int32_t field /* 0 id, 1 name, 2 uid, 3 output name */,
    char* out, int32_t cap);
/* The role and every External MIDI track following it move to the output, channels kept, one
 * undo step. NOTA_ERR_INVALID_ARG for an unknown role or while the transport plays. */
NOTA_API NotaResult nota_midi_role_rebind(NotaEngine*, const char* role_id, const char* output_uid, const char* output_name);
```

## Test hooks (smoke test only)

```c
NOTA_API NotaResult nota_midi_test_port_remove(NotaEngine*, const char* name);   /* unplug; _add plugs back in */
NOTA_API int32_t    nota_midi_test_output_read(NotaEngine*, const char* name, uint8_t* bytes_out, int32_t bytes_cap,
    uint64_t* due_ns_out, int32_t* lens_out, int32_t max_msgs);
```

All new calls are covered by the smoke test (constitution quality gate).
