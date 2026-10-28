# Contract: C ABI — plug-in editor preview

Added to `nota_engine.h` next to `nota_plugin_get_state` / `nota_plugin_set_state`.
`device_index < 0` addresses the track's instrument, `>= 0` an audio effect, as the
existing plug-in calls do. All three run on the UI (message) thread.

```c
/* Editor preview (specs/046): a small JPEG of a hosted plug-in's own window, captured
 * when the window is hidden; the Devices panel draws it behind the plug-in card. Kept on
 * the device, so it follows moves, copies and undo. get writes up to `capacity` bytes
 * into `out` (out=NULL queries the size) and returns the full size, 0 when there is none.
 * set replaces it with a copy of `data` (size 0 clears) — project load. serial changes
 * whenever the preview does (0 = none), so the UI can poll it cheaply. UI thread. */
NOTA_API int32_t    nota_plugin_preview_get(const NotaEngine* engine, int32_t track_id, int32_t device_index,
                                            uint8_t* out, int32_t capacity);
NOTA_API NotaResult nota_plugin_preview_set(NotaEngine* engine, int32_t track_id, int32_t device_index,
                                            const uint8_t* data, int32_t size);
NOTA_API int32_t    nota_plugin_preview_serial(const NotaEngine* engine, int32_t track_id, int32_t device_index);
```

| Call | Unknown track / index | Notes |
|---|---|---|
| `get` | returns 0 | `out` may be NULL; partial copy when `capacity` < size |
| `set` | `NOTA_ERR_INVALID_ARG` | `size` < 0 or > 4 MB, or `data` NULL with size > 0 → `NOTA_ERR_INVALID_ARG` |
| `serial` | returns 0 | 0 means "no preview" |

The calls work on any device (built-ins included), which keeps the smoke test free of
real plug-ins. Only hosted plug-ins ever *capture* one.

Managed (`IAudioEngine`):

```csharp
byte[] GetPluginPreview(int trackId, int deviceIndex);              // empty = none
bool   SetPluginPreview(int trackId, int deviceIndex, byte[] jpeg);     // empty clears; false = refused
int    PluginPreviewSerial(int trackId, int deviceIndex);           // 0 = none
```
