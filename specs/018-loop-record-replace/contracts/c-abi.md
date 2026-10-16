# Contract: C ABI — loop record mode

Added to `src/native/nota.engine/include/nota/nota_engine.h`, next to the count-in calls:

```c
/* Loop record mode for MIDI recorded into the Arrangement with the loop on:
 * 0 = overdub (every pass adds to the take), 1 = replace (the first new note of a
 * pass removes what earlier passes of this take recorded). App preference; takes
 * effect from the next pass. Session slot recording is unaffected. */
NOTA_API NotaResult nota_transport_set_loop_record_mode(NotaEngine* engine, int32_t mode);
NOTA_API int32_t    nota_transport_loop_record_mode(const NotaEngine* engine);
```

- `mode` outside {0, 1} → `NOTA_ERR_INVALID_ARG`. A null engine → `NOTA_ERR_INVALID_ARG` (setter) or 0 (getter).
- Thread: any non-audio thread. Lock-free (an atomic store).

Managed mirror (`IAudioEngine` / `NotaEngine`):

```csharp
void SetLoopRecordMode(bool replace);
bool LoopRecordReplace { get; }
```

MCP (`TransportTools`):

- `get_transport` → `TransportState` gains `bool LoopRecordReplace`.
- `set_loop_record_mode(bool replace)`: sets the mode. The UI toggle and the persisted preference follow it.
