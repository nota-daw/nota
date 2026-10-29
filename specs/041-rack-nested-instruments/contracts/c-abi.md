# C ABI additions (`nota_engine.h`)

All take `device_index` like the other `nota_rackdev_*` calls: −1 the track's instrument
rack, ≥ 0 an Audio Effect Rack, `NOTA_RACK_NESTED_BASE − chain` a nested Drum Rack.

```c
/* The Drum Rack held by chain `chain` of the track's Instrument Rack (one level only). */
#define NOTA_RACK_NESTED_BASE (-100000)

/* Drum Rack pads / kit timing, Sampler pads, chain names (twins of nota_rack_*). */
int32_t     nota_rackdev_add_sampler_chain(NotaEngine*, int32_t t, int32_t di, const char* path, int32_t root, int32_t loop);
int32_t     nota_rackdev_set_chain_sampler_sample(NotaEngine*, int32_t t, int32_t di, int32_t chain, const char* path, int32_t root);
int32_t     nota_rackdev_add_plugin_instrument_chain(NotaEngine*, int32_t t, int32_t di, int32_t catalog_index);
const char* nota_rackdev_chain_name(const NotaEngine*, int32_t t, int32_t di, int32_t chain);
void        nota_rackdev_set_chain_name(NotaEngine*, int32_t t, int32_t di, int32_t chain, const char* name);
void        nota_rackdev_set_chain_choke / _tune / _decay (…, int32_t chain, value);
…           nota_rackdev_chain_choke / _tune / _decay (…, int32_t chain);
void        nota_rackdev_set_swing / _set_humanize (…, float v);   float nota_rackdev_swing / _humanize (…);
int32_t     nota_rackdev_chain_sampler_info(const NotaEngine*, int32_t t, int32_t di, int32_t chain, NotaSamplerInfo* out);
int32_t     nota_rackdev_set_chain_sampler_root(NotaEngine*, int32_t t, int32_t di, int32_t chain, int32_t root);
float       nota_rackdev_chain_sampler_play_position(const NotaEngine*, int32_t t, int32_t di, int32_t chain);
/* Chain instrument surface (twins). */
const char* nota_rackdev_chain_instrument_param_id(…, int32_t chain, int32_t param);
float       nota_rackdev_chain_instrument_param_default(…, int32_t chain, int32_t param);
int32_t     nota_rackdev_chain_instrument_scope(…, int32_t chain, float* out, int32_t max_n);
int32_t     nota_rackdev_chain_instrument_voice_count(…, int32_t chain);
int32_t     nota_rackdev_chain_instrument_held_notes(…, int32_t chain, int32_t* out, int32_t max_n);
void        nota_rackdev_chain_instrument_action(…, int32_t chain, int32_t id, int32_t iarg, float farg);

/* Nota Mosaic as a chain instrument. undoable = 1: one undo step (a fresh Mosaic swaps in);
 * 0: live, in place. Return 1 / 0 (not a Mosaic chain). */
int32_t     nota_rackdev_chain_mosaic_set_program(NotaEngine*, int32_t t, int32_t di, int32_t chain, const char* text, int32_t undoable);
int32_t     nota_rackdev_chain_mosaic_program(const NotaEngine*, int32_t t, int32_t di, int32_t chain, char* out, int32_t cap);
int32_t     nota_rackdev_chain_mosaic_status(const NotaEngine*, int32_t t, int32_t di, int32_t chain, NotaMosaicStatus* out);
int64_t     nota_rackdev_chain_mosaic_zone_sample_id(const NotaEngine*, int32_t t, int32_t di, int32_t chain, int32_t zone);
```

Behaviour changes of existing calls:

- `nota_rack_add_chain`, `nota_rack_set_chain_instrument` (+ `rackdev` twins): accept 17
  everywhere instrument chains exist and 4 only on a track's plain Instrument Rack
  (`device_index` −1); otherwise unchanged refusal (−1 / 0).
- All `nota_rackdev_*` calls accept the nested range.

The exact shape of each declaration follows the existing `nota_rack_*` twin; the program
getter follows the `nota_track_mosaic_program` buffer convention used today.
