# Contract: C ABI additions (`include/nota/nota_engine.h`)

Pure C (AR-7): `extern "C"`, opaque handles, UTF-8, plain structs, integer results. Threads
are stated per function. "Message thread" means the managed UI / engine-owner thread,
like every other structural call.

## Types

```c
typedef struct NotaAuroraTable NotaAuroraTable;   /* opaque, immutable once built */

typedef struct NotaAuroraTableInfo {
    int32_t  frames;            /* 1..256; 0 = no table in that slot */
    int32_t  origin;            /* 0 wavetable file, 1 single cycle, 2 recording */
    int32_t  truncated_length;  /* 1 = only the first 60 s were read */
    int32_t  truncated_frames;  /* 1 = frames beyond 256 were dropped */
    int32_t  use_custom;        /* track queries only: the source flag */
    int32_t  missing;           /* track queries only: 1 = restore failed, name holds it */
    uint32_t serial;            /* changes whenever the slot's table changes; 0 = none */
    uint8_t  hash[16];          /* content hash of the base frames */
    char     name[256];         /* UTF-8, NUL-terminated */
} NotaAuroraTableInfo;

enum {
    NOTA_AURORA_OK = 0,
    NOTA_AURORA_ERR_DECODE = -1,   /* unreadable or broken file */
    NOTA_AURORA_ERR_FORMAT = -2,   /* not WAV / FLAC / MP3 */
    NOTA_AURORA_ERR_SILENT = -3,   /* peak below -60 dBFS */
    NOTA_AURORA_ERR_EMPTY  = -4,
    NOTA_AURORA_ERR_ARGS   = -5,
};
```

## Building (any thread; touches no engine state)

```c
/* Decode a file and build a conditioned, band-limited table (spec FR-008/FR-009).
 * On success *out receives a new handle (caller frees). Returns NOTA_AURORA_OK or an error. */
NOTA_API int32_t nota_aurora_table_build(const char* path_utf8, NotaAuroraTable** out);

/* Build from already-conditioned frames (project / preset reload): no DC removal, no
 * normalisation, no windowing — only mips are rebuilt, so a reload is bit-identical. */
NOTA_API int32_t nota_aurora_table_from_frames(const float* frames, int32_t frame_count,
                                               const char* name_utf8, int32_t origin,
                                               NotaAuroraTable** out);

NOTA_API int32_t nota_aurora_table_info(const NotaAuroraTable* t, NotaAuroraTableInfo* out);
/* The base frames (2048 × frames floats) into out; returns floats written, or the count when
 * out is null. Added in implementation: the app writes a fresh import to the bundle from
 * these, without decoding the file again (research R13). */
NOTA_API int64_t nota_aurora_table_frames(const NotaAuroraTable* t, float* out, int64_t cap);
NOTA_API void    nota_aurora_table_free(NotaAuroraTable* t);
```

## Applying to a track (message thread)

```c
/* Put t into oscillator osc (0/1) of an Aurora track and make it the active source.
 * checkpoint 1 = one undo step (clone + republish; sounding notes stop), 0 = no undo
 * (project load). t stays owned by the caller (the engine takes its own reference).
 * Returns 1 on success, 0 when the track is not an Aurora or osc is out of range. */
NOTA_API int32_t nota_track_aurora_set_table(NotaEngine* e, int32_t track_id, int32_t osc,
                                             const NotaAuroraTable* t, int32_t checkpoint);
/* Remove the imported table from osc (one undo step); the oscillator plays its built-in. */
NOTA_API int32_t nota_track_aurora_clear_table(NotaEngine* e, int32_t track_id, int32_t osc);
/* In place, lock-free: 1 = play the imported table, 0 = the built-in. No undo. */
NOTA_API int32_t nota_track_aurora_set_source(NotaEngine* e, int32_t track_id, int32_t osc, int32_t use_custom);
/* Mark osc as missing (name shown as "Missing: <name>"); "" clears. No undo. */
NOTA_API int32_t nota_track_aurora_set_missing(NotaEngine* e, int32_t track_id, int32_t osc, const char* name_utf8);
```

## Queries (message thread)

```c
NOTA_API int32_t nota_track_aurora_table_info(const NotaEngine* e, int32_t track_id, int32_t osc,
                                              NotaAuroraTableInfo* out);
/* Copy the base frames (2048 × frames floats) into out; returns floats written, 0 if none. */
NOTA_API int64_t nota_track_aurora_table_read(const NotaEngine* e, int32_t track_id, int32_t osc,
                                              float* out, int64_t cap);
/* Drawing preview: rep_frames (≤ 16) evenly chosen frames then grid_frames (≤ 64) evenly chosen
 * frames, each `points` (≤ 256) values back to back. Each frame is a min / max outline of
 * its base level: points / 2 slices, each giving its min and max in the order they occur, so
 * a bright frame keeps its envelope (research R13). Returns floats written. */
NOTA_API int32_t nota_track_aurora_table_preview(const NotaEngine* e, int32_t track_id, int32_t osc,
                                                 int32_t rep_frames, int32_t grid_frames, int32_t points,
                                                 float* out, int32_t cap);
/* State blob with (1) or without (0) the table section — 0 is what a project save of a
 * top-level track stores beside its samples/ files. Same size/copy semantics as
 * nota_track_get_plugin_state. */
NOTA_API int32_t nota_track_aurora_state(const NotaEngine* e, int32_t track_id, int32_t include_tables,
                                         uint8_t* out, int32_t cap);
```

Live follow uses the existing `nota_track_instrument_scope` (`nota_engine.h:1081-1083`), with
the layout given in [../data-model.md](../data-model.md) § Scope telemetry.

## Rack chains (message thread)

> **Deferred** (research R13): not in this release. An Aurora inside a rack keeps its
> tables through the state blob, but its card cannot import or remove one yet.

These mirror `nota_rack_set_chain_sampler_sample` (`nota_engine.h:1801`). They carry the same
semantics, but address an Aurora in Instrument Rack chain `chain`, or on a Drum Rack pad:

```c
NOTA_API int32_t nota_rack_chain_aurora_set_table(NotaEngine* e, int32_t track_id, int32_t chain, int32_t osc,
                                                  const NotaAuroraTable* t);   /* one undo step */
NOTA_API int32_t nota_rack_chain_aurora_clear_table(NotaEngine* e, int32_t track_id, int32_t chain, int32_t osc);
NOTA_API int32_t nota_rack_chain_aurora_set_source(NotaEngine* e, int32_t track_id, int32_t chain, int32_t osc, int32_t use_custom);
NOTA_API int32_t nota_rack_chain_aurora_table_info(const NotaEngine* e, int32_t track_id, int32_t chain, int32_t osc,
                                                   NotaAuroraTableInfo* out);
NOTA_API int32_t nota_rack_chain_aurora_table_preview(const NotaEngine* e, int32_t track_id, int32_t chain, int32_t osc,
                                                      int32_t rep_frames, int32_t grid_frames, int32_t points,
                                                      float* out, int32_t cap);
```

## Invariants

- No function here runs or blocks on the audio thread. The `set_*` calls publish a new
  snapshot (republish or `SnapshotHistory::publish`), and frees happen on the message thread.
- The `nota_aurora_table_*` builders are re-entrant; two builds may run at once.
- Smoke coverage: every function is called by `AuroraTableTests.cs` (tasks T017, T025–T030, T047).
