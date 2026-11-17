# Contract: C ABI changes (`nota_engine.h`)

All tempo ABI below was added by #568 and is unreleased, so removals are not a
compatibility break.

## Changed

```c
/* Lane targets (nota_track_add_automation_lane): ... 6 = Tempo (BPM 20..300), valid only on
 * nota_engine_master_track_id(); other tracks return -1. The engine derives the tempo map
 * from this lane on every edit (set, live set, section/time edits, undo/redo). */
NOTA_API int32_t nota_track_add_automation_lane(NotaEngine*, int32_t track_id,
                    int32_t target, int32_t device_index, int32_t param_index);

/* Ignored (returns NOTA_ERR_INVALID_ARG, tempo unchanged) while the master Tempo lane has
 * points. */
NOTA_API NotaResult nota_transport_set_bpm(NotaEngine*, double bpm);
```

Pan (1), DeviceParam (2) and PluginParam (3) lanes on the master track id now play back.

## Added

```c
/* 1 when the master Tempo lane has points (tempo comes from automation), else 0. */
NOTA_API int32_t    nota_engine_tempo_automated(const NotaEngine* engine);

/* Master pan stage (after the master devices, before the master fader; project pan law).
 * -1..1 (clamped), 0 = centre at unity gain. A plain set like nota_engine_set_master_volume:
 * the UI wraps a drag in a mix gesture (pan, master track id) for one undo step. */
NOTA_API NotaResult nota_engine_set_master_pan(NotaEngine* engine, float pan);
NOTA_API float      nota_engine_master_pan(const NotaEngine* engine);
/* Device-free self-test of master Pan / device lanes (master Volume refused, lanes drive the
 * pan and the device, device removal drops its lane, undo restores both). 0 = pass. */
NOTA_API int32_t    nota_engine_master_automation_selftest(NotaEngine* engine);

/* Device-free self-test: lane → map derivation (curve exactness, jumps, first point after 0,
 * clamping), re-derivation on section move and undo, unwarped clip reprojection.
 * 0 = pass, else the failed check number. */
NOTA_API int32_t    nota_engine_tempo_lane_selftest(NotaEngine* engine);
```

## Kept

`nota_tempo_beat_to_seconds`, `nota_tempo_seconds_to_beat`, `nota_tempo_bpm_at`.

## Removed

`NotaTempoPoint`, `nota_tempo_point_count`, `nota_tempo_get_point`, `nota_tempo_set_points`,
`NotaTempoPreview`, `nota_tempo_preview_create/destroy/seconds/beat/bpm`.
