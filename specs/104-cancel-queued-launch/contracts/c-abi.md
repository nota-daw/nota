# Contract: C ABI

Added to `nota_engine.h` next to the Session launch / stop functions.

```c
/* Cancel a queued launch before its quantized start (#480). The track keeps doing what it does
 * now (a playing clip plays on, uninterrupted); nothing is undoable or marks the project dirty.
 * Slot: withdraws the track's queued launch of `scene`. Scene: withdraws every queued launch of a
 * slot in that row and every stop the row's last scene launch queued, and drops that launch's
 * follow action and tempo / signature change. Applied by the audio thread at the start of its next
 * block, before any track's boundary — so a scene is withdrawn on all its tracks or none.
 * Returns NOTA_OK if something was queued to cancel, NOTA_ERR_UNKNOWN if nothing was (the launch
 * already started or was never queued: use the normal stop). */
NOTA_API NotaResult nota_session_cancel_slot_launch(NotaEngine* engine, int32_t track_id, int32_t scene);
NOTA_API NotaResult nota_session_cancel_scene_launch(NotaEngine* engine, int32_t scene);
```

```c
/* Whether a launch waits (something to cancel). Slot: the track's launch of `scene`, also a queued
 * restart of the slot playing (state 3); a group id: any track below it. Scene: a direct launch of
 * one of the row's slots, or the row's scene launch with nothing of it started — a row that queued
 * only stops counts too. */
NOTA_API int32_t nota_session_slot_launch_queued(const NotaEngine* engine, int32_t track_id, int32_t scene);
NOTA_API int32_t nota_session_scene_queued(const NotaEngine* engine, int32_t scene);
```

`nota_session_cancel_slot_launch` takes a group id too: every track below it, in one command.
`nota_session_slot_state` is unchanged: a withdrawn slot reads 1 (filled) again from the block
after the cancel.

Managed mirror (`IAudioEngine`): `bool CancelSlotLaunch(int trackId, int scene)`,
`bool CancelSceneLaunch(int scene)` — true for `NOTA_OK`.
