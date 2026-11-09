# Contract: C ABI additions (`nota_engine.h`)

Pure C, no exceptions (AR-7). All functions are on the message thread. The audio thread never
calls them; it reads the published snapshot. `nota_*` symbols are exported by wildcard on every
platform, so no export list changes.

## Already written (not yet built)

```c
typedef struct NotaNoteData {
    /* ...existing fields... */
    int32_t articulation_id;   /* 0 = none; appended LAST so older field offsets are unchanged */
} NotaNoteData;

NOTA_API NotaResult nota_track_set_midi_offset_ms(NotaEngine*, int32_t track_id, double ms);
NOTA_API double     nota_track_get_midi_offset_ms(NotaEngine*, int32_t track_id);
```

C# `NotaNote` mirrors the struct with `ArticulationId` as its last field; the two layouts must
stay identical (`StructLayout.Sequential`). Because the struct grew, **the managed code and the
native library must be built together**; a stale DLL with a new managed build would read garbage.

## To add

```c
typedef struct NotaArticulationEntry {
    int32_t id;            /* > 0, unique, ascending */
    int32_t trigger_mode;  /* 0 latching, 1 momentary */
    double  offset_ms;     /* added to the track offset */
    double  lead_ms;       /* >= 0 */
    int32_t action_start;  /* index into the action array */
    int32_t action_count;
} NotaArticulationEntry;

typedef struct NotaArticulationAction {
    int32_t kind;          /* 0 keyswitch, 1 control change, 2 program change, 3 bank MSB, 4 bank LSB */
    int32_t channel;       /* 0 = the note's own, 1..16 explicit */
    int32_t data1;         /* pitch / cc / program, 0..127 */
    int32_t data2;         /* velocity / value, 0..127 */
} NotaArticulationAction;

/* Gives the track its articulation map: the compiled table plus the map file as opaque JSON
 * (saved with the project and restored by undo). One undo step. default_id 0 = none.
 * Returns NOTA_ERR_INVALID_ARG for unsorted/duplicate ids, a range that leaves the action array,
 * out-of-range values, or counts < 0. Nothing is changed on error. */
NOTA_API NotaResult nota_track_set_articulation_map(NotaEngine*, int32_t track_id,
    int32_t default_id,
    const NotaArticulationEntry* entries, int32_t entry_count,
    const NotaArticulationAction* actions, int32_t action_count,
    const char* map_json, int32_t map_json_len);

/* Removes the map (notes keep their ids). One undo step. */
NOTA_API NotaResult nota_track_clear_articulation_map(NotaEngine*, int32_t track_id);

/* Copies the stored map JSON out (out = NULL -> just the length). 0 = no map. */
NOTA_API int32_t nota_track_get_articulation_map_json(const NotaEngine*, int32_t track_id,
    char* out, int32_t max_len);

/* Self-test with a recording test instrument: returns 1 on pass. Used by the smoke test. */
NOTA_API int32_t nota_engine_articulation_selftest(NotaEngine*);
```

## Rules

- The engine validates every field it is given; the managed side's validation is not trusted.
- `entries` ids ascend strictly; each `[action_start, action_start+action_count)` lies within
  `actions`.
- Changing offsets or lead times on a map applies through `nota_track_set_articulation_map`; there
  is no per-articulation setter.
- `nota_track_set_midi_offset_ms` clamps to ±2000 ms and treats NaN as 0.
- Managed: `IAudioEngine.SetTrackArticulationMap`, `ClearTrackArticulationMap`,
  `GetTrackArticulationMapJson`, plus `NotaEngine.ArticulationSelfTest()`.

## Instrument interface (C++, not part of the C ABI)

```cpp
class Instrument {
    int32_t eventChannel = 0;                       // like eventOffset; 0 = channel 1 / note's own
    virtual void programChange(int32_t /*program*/) {}   // default: ignored
};
```

`PluginInstrument` sends on `eventChannel + 1` (it hardcodes 1 today) and maps `programChange` to
a real MIDI program change. Bank select uses `controller(0, v)` and `controller(32, v)`.
