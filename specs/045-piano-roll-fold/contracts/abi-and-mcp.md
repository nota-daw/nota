# Contracts: Piano roll Fold

## C ABI (`include/nota/nota_engine.h`)

```c
/* Piano roll Fold (specs/045): a per-track view flag, saved with the project. Control
   thread only; never read by the audio thread. Returns 0 on success, -1 for an unknown track. */
NOTA_API int32_t nota_track_set_piano_fold(nota_engine* e, int32_t track_id, int32_t on);
/* 1 folded, 0 not, -1 unknown track. */
NOTA_API int32_t nota_track_get_piano_fold(nota_engine* e, int32_t track_id);
```

Managed: `IAudioEngine.SetTrackPianoFold(int trackId, bool on)` and `bool GetTrackPianoFold(int trackId)`.

## MCP (`Nota.Mcp`)

| Tool | Arguments | Result |
|---|---|---|
| `set_piano_fold` | `track_id`, `on` | none; an open piano roll picks it up when it next opens a clip on that track |
| `get_piano_fold` | `track_id` | `true` / `false` |

## Project file

Track object: optional `"PianoFold": true`. Absent means off. No format version change.

## UI

- The piano roll toolbar shows a **Fold** switch next to **Scale**, with the tooltip "Fold: show
  only the scale's notes (⌥⇧F)", or on a Drum Rack "Fold: show only the pads (⌥⇧F)".
- Shortcut: ⌥⇧F, or Alt+Shift+F on Windows and Linux.
