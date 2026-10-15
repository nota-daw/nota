# Contract: C ABI changes (`nota_engine.h`)

## Value gestures (extends specs/009)

```c
#define NOTA_MIX_TRACK_VOLUME      0
#define NOTA_MIX_TRACK_PAN         1
#define NOTA_MIX_MASTER_VOLUME     2
#define NOTA_MIX_DEVICE_PARAM      3   /* audio effect: device_index = chain position      */
#define NOTA_MIX_MIDI_DEVICE_PARAM 4   /* MIDI effect: device_index = MIDI-FX position      */
#define NOTA_MIX_PLUGIN_PARAM      5   /* normalized: device_index -1 = the instrument      */

typedef struct NotaMixTarget {
    int32_t kind;          /* NOTA_MIX_* */
    int32_t track_id;      /* ignored for NOTA_MIX_MASTER_VOLUME; master chain = master id */
    int32_t device_index;  /* parameter kinds only; -1 otherwise */
    int32_t param_index;   /* parameter kinds only; -1 otherwise */
} NotaMixTarget;
```

`nota_mix_gesture_begin` / `nota_mix_gesture_end` keep their signatures; a target whose
device or parameter does not exist is ignored at begin (as a missing track is today).

`nota_engine_begin_automation_write(..., latch = 0)` on a parameter target also opens a
value gesture for it; the matching `nota_engine_end_automation_write` closes it. A latched
write does not.

### Guarantees (smoke-tested)

| # | Guarantee |
|---|---|
| G1 | begin(param) · n sets · end → one step; undo → exact before, redo → exact after |
| G2 | begin · end without a change → no step |
| G3 | an automation-write touch (unlatched) on a parameter is one step; latched is none |
| G4 | parameter, note and section steps undo in reverse order of their edits |
| G5 | a step whose device is gone is skipped; the next step is undone |
| G6 | automation playback of a device parameter adds no step |
| G7 | a recorded touch: the lane edit and the value undo as one step |

## Sections

```c
typedef struct NotaSection {
    double      start_beat;
    double      length_beats;
    const char* name_utf8;     /* NULL = "" */
} NotaSection;

NOTA_API int32_t     nota_engine_section_count(const NotaEngine* engine);
NOTA_API double      nota_engine_section_start(const NotaEngine* engine, int32_t index);
NOTA_API double      nota_engine_section_length(const NotaEngine* engine, int32_t index);
NOTA_API const char* nota_engine_section_name(const NotaEngine* engine, int32_t index);
/* Replace the list (sorted by start; entries with length <= 0 dropped; start clamped >= 0).
 * checkpoint 1 = one undo step (user edit; an identical list makes none), 0 = baseline
 * (project load). Message thread only. */
NOTA_API NotaResult  nota_engine_set_sections(NotaEngine* engine, const NotaSection* sections,
                                              int32_t count, int32_t checkpoint);
```

Out-of-range index → 0 / 0.0 / "". `nota_engine_reset` (project load / new) clears them.
