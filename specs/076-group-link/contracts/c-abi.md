# Contract: C ABI (`include/nota/nota_engine.h`)

Placed after `nota_track_get_piano_fold`.

```c
/* Group Link (specs/076, #184): a group track's "link its tracks for editing and recording"
 * flag. Saved with the project, copied with the group, and an undo step when changed; the
 * audio thread never reads it — the app applies the link to arming and clip edits.
 * set: NOTA_ERR_INVALID_ARG for an unknown track or one that is not a group.
 * get: 1 linked, 0 not, -1 unknown track (0 for a track that is not a group). */
NOTA_API NotaResult nota_track_set_edit_link(NotaEngine* engine, int32_t track_id, int32_t on);
NOTA_API int32_t    nota_track_get_edit_link(NotaEngine* engine, int32_t track_id);
```

Behaviour the smoke test asserts:

| Case | Expected |
|------|----------|
| set on a group, get | 1 |
| set on an audio track | `NOTA_ERR_INVALID_ARG`, get = 0 |
| set the same value twice | `NOTA_OK`, no extra undo step |
| set on, `nota_engine_undo` | get = 0; redo → 1 |
| duplicate / copy-paste the group | the copy reads 1 |
| null engine / unknown id | set → error, get → -1 |

Managed mirror (`IAudioEngine`):

```csharp
/// <summary>Group Link (specs/076): links a group's tracks for arming and clip edits. One undo
/// step; false for a track that is not a group.</summary>
bool SetTrackEditLink(int trackId, bool on);
bool GetTrackEditLink(int trackId);
```
