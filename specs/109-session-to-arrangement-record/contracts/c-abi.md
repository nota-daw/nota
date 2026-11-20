# Contract: C ABI changes (`include/nota/nota_engine.h`)

One new test hook (below); no other new functions. Recording still starts with `nota_engine_set_recording(engine, 1)`. These are
the behaviour changes:

## `nota_engine_set_recording(engine, 1)`

- Arrangement takes now also write the Session performance (spec FR-001…FR-013). When
  recording stops, the Arrangement holds the written clips.
- With **nothing armed** and a Session clip playing or queued, a **performance-only pass**
  starts: Session clips and automation are written, no input is recorded, nothing is armed.

## `nota_engine_record_start_status(engine)`

The doc comment gains one value:

```c
/* Why the last set_recording(1) did (not) start: 0 ok, 1 no armed track, 2 audio input failed,
 * 3 ok — a performance-only pass (nothing armed; records the Session performance). */
```

Managed mirror: `RecordStartStatus` handling in `TransportViewModel` (status 3 = started, show
the performance message) and the MCP `start_recording` result text.

## `nota_engine_retry_take(engine)`

Unchanged contract: an Arrangement retry (result 1) now also drops what the pass wrote, and
restores the Arrangement clips the pass replaced and the Tempo lane.

## Test hooks

`nota_engine_session_pass_selftest(engine)` — device-free check of the span rules and the MIDI
unroll; 0 = pass, else the failed check (managed `NotaEngine.SessionPassSelfTest()`). No other
hooks are needed. The smoke test drives a pass with the existing calls: arm or not,
`set_recording`, `launch_slot` / `launch_scene` / `stop_slot`, offline block rendering, then
reads Arrangement clips (`midi_clip_*`, `audio_clip_*`), automation points and the undo depth.
