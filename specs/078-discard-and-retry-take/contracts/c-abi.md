# Contract: C ABI (`nota_engine.h`)

```c
/* Discard the take in progress and record it again (#185, specs/078). Drops everything the
 * take wrote — audio, loop passes, MIDI notes/controllers/expression, a clip it created,
 * automation written while recording — with no undo or redo step, then:
 *  - Arrangement: returns to where the transport was started for the take (its start beat
 *    when the take started the transport, else the Play position) and records again, with
 *    the count-in again when the take had one;
 *  - Session slot: leaves other clips and the transport running and records into the same
 *    slot again from the next launch quantum (immediately when quantize is off).
 * Returns 0 nothing was recording (no change), 1 Arrangement take retried,
 * 2 Session slot take retried, -1 discarded but recording could not restart
 * (see nota_engine_record_start_status). Message thread. */
NOTA_API int32_t nota_engine_retry_take(NotaEngine* engine);

/* Slot state: 0 empty, 1 filled, 2 queued, 3 playing, 4 recording,
 * 5 record pending (the slot will record from the next quantum). */
NOTA_API int32_t nota_session_slot_state(const NotaEngine* engine, int32_t track_id, int32_t scene);
```

Managed mirror: `IAudioEngine.RetryTake()` → `RetryTakeResult { None, Arrangement, Session, Failed }`.
