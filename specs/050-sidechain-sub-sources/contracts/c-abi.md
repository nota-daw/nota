# Contract: C ABI additions (`nota_engine.h`)

All message thread. Existing calls keep their meaning: `nota_device_set_sidechain_source` and
`nota_track_set_instrument_sidechain_source` set a **whole-track** key (part cleared);
`nota_device_sidechain_source` / `nota_track_instrument_sidechain_source` still return the source
track id for any key, part or not.

```c
/* ---- Sidechain sub-sources (specs/050) -----------------------------------
 * A key may name one part of the source track's instrument: a Drum Rack pad, an Instrument
 * Rack chain (first level) or a Nota Rhythm voice. Parts are addressed by their CURRENT
 * index; the engine keeps the key on the same part through reorder / insert / delete of
 * others. part -1 = whole track. Getters report -2 when the keyed part no longer exists
 * (the key is silent until an undo brings the part back). */

/* Number of parts of the track's instrument (0 = none / not multi-part). */
NOTA_API int32_t nota_track_part_count(const NotaEngine* engine, int32_t track_id);
/* Display name of part `index` (chain name, else its instrument's; Rhythm "1 Kick").
 * Returns the full UTF-8 length; copies up to cap-1 bytes + NUL when out is non-null. */
NOTA_API int32_t nota_track_part_name(const NotaEngine* engine, int32_t track_id, int32_t index,
                                      char* out, int32_t cap);

/* Device key. 0 = set, -1 bad address / part, -2 no free route (nothing changed). */
NOTA_API int32_t nota_device_set_sidechain_key(NotaEngine* engine, int32_t track_id, int32_t device_index,
                                               int32_t source_track_id, int32_t part);
/* 0 = read, -1 bad address. *out_source -1 = none; *out_part -1 whole, -2 missing. */
NOTA_API int32_t nota_device_sidechain_key(const NotaEngine* engine, int32_t track_id, int32_t device_index,
                                           int32_t* out_source, int32_t* out_part);

/* Instrument key (React, Nota Flux) — always the Post tap. Same results as above. */
NOTA_API int32_t nota_track_set_instrument_sidechain_key(NotaEngine* engine, int32_t track_id,
                                                         int32_t source_track_id, int32_t part);
NOTA_API int32_t nota_track_instrument_sidechain_key(const NotaEngine* engine, int32_t track_id,
                                                     int32_t* out_source, int32_t* out_part);

/* Keyed rack children (save / load, specs/044): the part of a chain child's key. */
NOTA_API int32_t nota_rack_chain_sidechain_part(const NotaEngine* engine, int32_t track_id, int32_t device_index,
                                                int32_t chain, int32_t slot, int32_t* out_part);
NOTA_API int32_t nota_rack_chain_set_sidechain_part(NotaEngine* engine, int32_t track_id, int32_t device_index,
                                                    int32_t chain, int32_t slot, int32_t part);

/* Route pool: capacity (16), free slots now, and whether (source, part) already has a slot
 * (a further consumer of it needs no new one). */
NOTA_API int32_t nota_sidechain_route_capacity(const NotaEngine* engine);
NOTA_API int32_t nota_sidechain_routes_free(const NotaEngine* engine);
NOTA_API int32_t nota_sidechain_route_in_use(const NotaEngine* engine, int32_t source_track_id, int32_t part);
```

Behaviour that the smoke test pins:

- `set_sidechain_key(.., part)` then `sidechain_key` round-trips the index; after moving the keyed
  pad to index 0 the getter returns 0.
- Removing the keyed chain → getter `-2`; `nota_engine_undo` → the original index again.
- Filling the pool: the 17th distinct source returns `-2`, `routes_free` = 0, an in-use source still
  returns 0.
- `nota_device_set_sidechain_source(t, d, s)` after a part key → `sidechain_key` part = -1.
