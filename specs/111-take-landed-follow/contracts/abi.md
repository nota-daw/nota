# Contract: C ABI (`nota_engine.h`, Session section, beside the slot take length)

```c
/* A Session slot's landing rule (#481): what happens once a fresh take recorded into the slot
   has landed — been kept as a playable clip. action: 0 = Off, 1 = launch the next scene (wraps),
   2 = launch `scene`. quant_beats: < 0 = the global launch quantum (as a manual scene launch),
   >= 0 = the launch waits for that quantum (0 = at once). Belongs to the slot; kept, hidden,
   while the slot holds a clip. Fires once per landed take; never on load, Undo or Redo. */
typedef struct NotaSlotLandingRule {
    int32_t action;
    int32_t scene;       /* target, action 2 only */
    double  quant_beats; /* -1 or 0..32 */
} NotaSlotLandingRule;

#define NOTA_SLOT_LANDING_OFF        0
#define NOTA_SLOT_LANDING_NEXT_SCENE 1
#define NOTA_SLOT_LANDING_SCENE      2

/* Returns 1 and fills *out for an existing track/scene, 0 otherwise. */
NOTA_API int32_t    nota_session_get_slot_landing_rule(const NotaEngine* engine, int32_t track_id, int32_t scene,
                                                       NotaSlotLandingRule* out);
/* One undo step. NOTA_ERR_INVALID_ARG: unknown track/scene, unknown action, target scene out of
   range, quant_beats not -1 or 0..32. Accepted on filled slots too (project load). */
NOTA_API NotaResult nota_session_set_slot_landing_rule(NotaEngine* engine, int32_t track_id, int32_t scene,
                                                       const NotaSlotLandingRule* in);
```

Threading: message thread only, like every other Session setter. Firing happens inside
`nota_engine_poll` (via `serviceSessionFollow`); nothing new reaches the audio thread.
