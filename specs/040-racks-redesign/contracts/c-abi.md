# Contract: C ABI additions (`include/nota/nota_engine.h`)

All calls are message-thread, pure C (AR-7), `NotaEngine*` first. Structural edits
fork the rack for undo (`rackForEdit` pattern, #236) and publish a new snapshot (AR-5);
value setters are relaxed atomics (AR-4/AR-6). Every new call is covered by the smoke test.

## Generic MIDI effect state (any MIDI effect)

```c
/* Opaque state blob of a MIDI effect (MIDI Effect Rack: its whole structure).
 * out == NULL returns the size; otherwise writes up to cap bytes and returns the count. */
int32_t    nota_midi_effect_get_state(const NotaEngine*, int32_t track_id, int32_t index, uint8_t* out, int32_t cap);
/* Restore a state blob; checkpoints undo when checkpoint != 0. */
NotaResult nota_midi_effect_set_state(NotaEngine*, int32_t track_id, int32_t index, const uint8_t* data, int32_t size, int32_t checkpoint);
```

`nota_track_add_midi_effect(engine, track, 6)` adds a MIDI Effect Rack with one empty chain.

## MIDI Effect Rack family — `nota_midirack_*(engine, track_id, midi_index, …)`

Mirrors `nota_rackdev_*`; `midi_index` is the MIDI effect's index on the track. Calls on
an index that isn't a MIDI rack return `<0` / `NOTA_ERR_*` / 0.

Structure:

```c
int32_t    nota_midirack_chain_count(…);
int32_t    nota_midirack_add_chain(…);                                   /* returns index, <0 at the 16-chain limit */
NotaResult nota_midirack_remove_chain(…, int32_t chain);
NotaResult nota_midirack_duplicate_chain(…, int32_t chain);              /* inserts after; copies devices + controls + its mappings */
NotaResult nota_midirack_move_chain(…, int32_t from, int32_t to);
int32_t    nota_midirack_chain_device_count(…, int32_t chain);
int32_t    nota_midirack_add_chain_device(…, int32_t chain, int32_t midi_kind);   /* kinds 0–5; 6 refused */
NotaResult nota_midirack_remove_chain_device(…, int32_t chain, int32_t dev);
NotaResult nota_midirack_move_chain_device(…, int32_t chain, int32_t from, int32_t to);
int32_t    nota_midirack_chain_device_kind(…, int32_t chain, int32_t dev);
const char* nota_midirack_chain_device_name(…, int32_t chain, int32_t dev);
int32_t    nota_midirack_chain_device_param_count(…, int32_t chain, int32_t dev);
const char* nota_midirack_chain_device_param_name(…, int32_t chain, int32_t dev, int32_t p);
float      nota_midirack_chain_device_param_min/max/get(…, int32_t chain, int32_t dev, int32_t p);
void       nota_midirack_chain_device_param_set(…, int32_t chain, int32_t dev, int32_t p, float v);
void       nota_midirack_set_chain_device_bypassed(…, int32_t chain, int32_t dev, int32_t b);
int32_t    nota_midirack_chain_device_bypassed(…, int32_t chain, int32_t dev);
```

Chain controls:

```c
const char* nota_midirack_chain_name(…, int32_t chain);   void nota_midirack_set_chain_name(…, int32_t chain, const char*);
void  nota_midirack_set_chain_mute/solo(…, int32_t chain, int32_t on);   int32_t nota_midirack_chain_mute/solo(…);
void  nota_midirack_set_chain_zone(…, int32_t chain, int32_t key_lo, int32_t key_hi, int32_t vel_lo, int32_t vel_hi);
void  nota_midirack_chain_zone(…, int32_t chain, int32_t* key_lo, int32_t* key_hi, int32_t* vel_lo, int32_t* vel_hi);
void  nota_midirack_set_chain_select(…, int32_t chain, int32_t lo, int32_t hi);
void  nota_midirack_chain_select_range(…, int32_t chain, int32_t* lo, int32_t* hi);
void  nota_midirack_set_chain_transpose(…, int32_t chain, int32_t semitones);    /* −48…+48 */
int32_t nota_midirack_chain_transpose(…, int32_t chain);
void  nota_midirack_set_chain_vel_scale(…, int32_t chain, float scale);         /* 0–2 */
float nota_midirack_chain_vel_scale(…, int32_t chain);
float nota_midirack_chain_activity(…, int32_t chain);                          /* 0–1, decays */
```

Rack:

```c
void  nota_midirack_set_mode(…, int32_t mode);   int32_t nota_midirack_mode(…);   /* 0 Parallel, 2 Select */
void  nota_midirack_set_selector(…, float v01);  float nota_midirack_selector(…);
void  nota_midirack_set_merge(…, int32_t on);    int32_t nota_midirack_merge(…);
void  nota_midirack_set_thru(…, int32_t on);     int32_t nota_midirack_thru(…);
```

Macros and mappings (same semantics as `nota_rackdev_*`):

```c
float nota_midirack_macro_get(…, int32_t m);  void nota_midirack_macro_set(…, int32_t m, float v);
const char* nota_midirack_macro_name(…, int32_t m);  void nota_midirack_set_macro_name(…, int32_t m, const char*);
int32_t nota_midirack_add_macro_mapping(…, int32_t macro, int32_t chain, int32_t device, int32_t param, float lo, float hi);
int32_t nota_midirack_mapping_count(…);
int32_t nota_midirack_mapping_info(…, int32_t i, int32_t* macro, int32_t* chain, int32_t* device, int32_t* param, float* lo, float* hi, int32_t* curve);
NotaResult nota_midirack_remove_mapping(…, int32_t i);
NotaResult nota_midirack_set_mapping_range(…, int32_t i, float lo, float hi);
NotaResult nota_midirack_set_mapping_curve(…, int32_t i, int32_t curve);
```

Automation of macros goes through the existing plugin-param / MIDI-effect param surface
(macros appear as the rack's params 0–7), so automation lanes, MIDI Learn and
`begin/end_automation_write` work unchanged.

## Instrument Rack additions — `nota_rack_*(engine, track_id, …)`

Chain MIDI effects (issue #329), addressed like chain audio devices:

```c
int32_t    nota_rack_chain_midi_count(…, int32_t chain);
int32_t    nota_rack_add_chain_midi(…, int32_t chain, int32_t midi_kind);       /* kinds 0–5 */
NotaResult nota_rack_remove_chain_midi(…, int32_t chain, int32_t idx);
NotaResult nota_rack_move_chain_midi(…, int32_t chain, int32_t from, int32_t to);
int32_t    nota_rack_chain_midi_kind(…, int32_t chain, int32_t idx);
const char* nota_rack_chain_midi_name(…, int32_t chain, int32_t idx);
int32_t    nota_rack_chain_midi_param_count(…, int32_t chain, int32_t idx);
const char* nota_rack_chain_midi_param_name(…, int32_t chain, int32_t idx, int32_t p);
float      nota_rack_chain_midi_param_min/max/get(…, int32_t chain, int32_t idx, int32_t p);
void       nota_rack_chain_midi_param_set(…, int32_t chain, int32_t idx, int32_t p, float v);
void       nota_rack_set_chain_midi_bypassed(…, int32_t chain, int32_t idx, int32_t b);
int32_t    nota_rack_chain_midi_bypassed(…, int32_t chain, int32_t idx);
/* Editor telemetry/commands so the full MIDI-effect cards work inside a chain: */
int32_t    nota_rack_chain_midi_last_in/last_out(…, int32_t chain, int32_t idx);
int32_t    nota_rack_chain_midi_scope(…, int32_t chain, int32_t idx, float* out, int32_t max_n);
void       nota_rack_chain_midi_command(…, int32_t chain, int32_t idx, int32_t cmd);
```

Macro mappings to a chain MIDI effect use `device = NOTA_RACK_MIDI_DEVICE_BASE − idx`
(`#define NOTA_RACK_MIDI_DEVICE_BASE (-1000)`).

```c
void    nota_rack_set_mono(…, int32_t on);   int32_t nota_rack_mono(…);
/* glide: existing nota_rack_set_glide / nota_rack_glide — now audible in mono mode */
```

## Shared rack-control mapping target

`device = −3, param = 0` in `*_add_macro_mapping` targets the rack selector (Audio and
MIDI racks). Range is in selector units 0–1.

## Mapping uniqueness

`nota_rack_add_macro_mapping`, `nota_rackdev_add_macro_mapping` and
`nota_midirack_add_macro_mapping` move an existing mapping of the same (chain, device,
param) to the new macro instead of adding a second one, and return its index.
