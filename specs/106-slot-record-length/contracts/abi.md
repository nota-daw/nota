# Contract: C ABI (`nota_engine.h`, Session section, beside the slot Stop button)

```c
/* A Session slot's next-take length (#503): how long a fresh take recorded into it runs.
   mode: 0 = Global (follow nota_session_set_record_length), 1 = Open (until stopped),
         2 = Fixed (bars + beats of the meter in effect when the take starts).
   The value belongs to the slot: it is kept, hidden, while the slot holds a clip. */
typedef struct NotaSlotTakeLength {
    int32_t mode;
    int32_t bars;  /* 0..64, Fixed only */
    int32_t beats; /* 0..31, Fixed only (the UI offers 0..beats-per-bar-1) */
} NotaSlotTakeLength;

#define NOTA_SLOT_TAKE_GLOBAL 0
#define NOTA_SLOT_TAKE_OPEN   1
#define NOTA_SLOT_TAKE_FIXED  2

/* Returns 1 and fills *out for an existing track/scene, 0 otherwise. */
NOTA_API int32_t    nota_session_get_slot_take_length(const NotaEngine* engine, int32_t track_id, int32_t scene,
                                                      NotaSlotTakeLength* out);
/* One undo step. NOTA_ERR_INVALID_ARG: unknown track/scene, unknown mode, Fixed 0+0, bars > 64,
   beats > 31, or 64 bars + beats. Accepted on filled slots too (project load). */
NOTA_API NotaResult nota_session_set_slot_take_length(NotaEngine* engine, int32_t track_id, int32_t scene,
                                                      const NotaSlotTakeLength* in);
/* The length a fresh take into the slot would get now, in beats; 0 = open-ended. */
NOTA_API double     nota_session_slot_take_beats(const NotaEngine* engine, int32_t track_id, int32_t scene);
```

Unchanged: `nota_session_set_record_length` / `nota_session_get_record_length` (the global
value Global slots follow).

Threading: message thread only, like every other Session setter; nothing reaches the audio
thread except through the existing graph publish and lane-slot table.
