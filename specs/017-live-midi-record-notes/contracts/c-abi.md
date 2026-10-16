# Contract: C ABI for held recorded notes

Added to `src/native/nota.engine/include/nota/nota_engine.h`, next to the audio take
geometry (`nota_engine_audio_record_*`). Pure C, POD only (AR-7).

```c
/* One note held into the in-progress MIDI take (live-recording preview, #56). */
typedef struct NotaRecordHeldNote {
    int32_t pitch;        /* 0..127 */
    int32_t source;       /* 0 live input (keyboard, MIDI, Remote), 1 routed MIDI ("MIDI In") */
    double  start_beat;   /* arrangement: absolute timeline beat, may precede the take's clip
                             and be < 0 (played on the count-in before 1.1);
                             Session: slot-local beat */
    float   velocity;     /* 0..1 */
} NotaRecordHeldNote;

/* Notes currently held into the in-progress MIDI take — keys down that will be written
 * into the take on release — for drawing them growing. Copies up to max_n entries and
 * returns the count (0 when no MIDI take is recording or nothing is held). Optional
 * outputs: *track_id the take's track (0 = none), *session_scene the Session scene being
 * recorded (-1 = arrangement take), *now_beat the capture clock (the held notes' right
 * edge; runs during the count-in too, in the same coordinates as start_beat).
 * Lock-free; safe from any non-audio thread. */
NOTA_API int32_t nota_engine_record_held_notes(const NotaEngine* engine,
                                               NotaRecordHeldNote* out, int32_t max_n,
                                               int32_t* track_id, int32_t* session_scene,
                                               double* now_beat);
```

## Guarantees

- Every returned note is one the engine will store on release if recording continues
  (FR-007); a note dropped by a recording/transport stop disappears from the next read.
- After a note-off is processed, the note is absent from the snapshot and present in the
  `recorded_` queue in the same block — after the next `nota_engine_poll` it is in the clip,
  so a reader that polls first and then reads the snapshot never sees it twice or not at all.
- `start_beat` equals the start the stored note will have (before the clip left move
  is applied; the stored clip-relative start is `start_beat − clip start`).
- No allocation or lock on any thread; a reader racing the writer retries a bounded number
  of times and otherwise returns 0 for that call.
- `out == NULL` or `max_n <= 0` returns the count without copying.

## Behaviour changes behind the same ABI (no new calls)

- Notes started during the count-in and keys held when recording starts are recorded
  (FR-012); the take's clip may move its start earlier during recording (FR-013) —
  readers of `nota_clip_*` see the new start after `nota_engine_poll`.
- Notes with negative clip-relative `start_beat` (via `nota_clip_get_notes`) are legal and
  render from the clip start when they reach into the clip.

## Managed mapping

- `NativeMethods.RecordHeldNotes(IntPtr, [Out] NotaRecordHeldNote[], int, out int, out int, out double)`
  with a `[StructLayout(LayoutKind.Sequential)]` struct (int, int, double, float — natural
  alignment, matches C).
- `IAudioEngine.RecordHeldNotes(NotaRecordHeldNote[] buffer, out int trackId,
  out int sessionScene, out double nowBeat) : int`.
