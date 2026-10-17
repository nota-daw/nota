# C ABI: device fold

```c
/* Device fold (specs/022): a view flag the Devices panel draws as a narrow strip. Saved with
 * the project, never read by the audio thread. Kept on the device, so it follows moves and
 * undo/redo. device_index -1 = the track's instrument. Message thread. */
NOTA_API void    nota_track_set_device_folded(NotaEngine* engine, int32_t track_id, int32_t device_index, int32_t folded);
NOTA_API int32_t nota_track_device_folded(const NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API void    nota_midi_effect_set_folded(NotaEngine* engine, int32_t track_id, int32_t index, int32_t folded);
NOTA_API int32_t nota_midi_effect_folded(const NotaEngine* engine, int32_t track_id, int32_t index);
```

Out-of-range / null engine: setters are no-ops, getters return 0.
