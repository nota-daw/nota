# Data Model: Loop record mode — Overdub / Replace

## LoopRecordMode (app preference)

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `LoopRecordReplace` | bool | `false` (Overdub) | In `Settings` (app preferences, like `CountInBars`). Not saved in the project. |

Engine mirror: `std::atomic<bool> loopRecordReplace_`. It is written on the message thread and latched by the audio thread at take start and at each wrap (`passReplace_`).

## Pass (audio thread, per take)

| Field | Type | Notes |
|-------|------|-------|
| `recordPass_` | int32 | 0 at take start. +1 on each loop wrap while an Arrangement MIDI take records. |
| `passReplace_` | bool | The mode latched for the current pass. |
| `passTriggered_` | bool | A fresh note-on already fired the replace in this pass. |
| `replaceUpTo_` | atomic int32 | Notes with `pass <` this value are to be removed. Monotonic within a take, published with release semantics. |

## RecordedNote (audio → message queue)

Gains `int32_t pass`: the pass in which the note (or its post-seam continuation) started.

## Take bookkeeping (message thread)

`takeNotes_`: a list of `{pitch, absStartBeat, pass}` for every note this take appended to the record clip. Cleared at take start. `appliedReplaceUpTo_` holds the last `replaceUpTo_` that was applied.

### State transitions (one Arrangement MIDI take, loop on)

```
take start: pass=0, latch mode, replaceUpTo=0
wrap:        pass+=1, latch mode, triggered=false
fresh note-on (record track / routed) && latched Replace && !triggered && pass>0:
             replaceUpTo=pass (release), triggered=true
Poll:        read replaceUpTo (acquire) → drain queue → append → if replaceUpTo>applied:
             remove takeNotes with pass<replaceUpTo from the clip, applied=replaceUpTo
```

Validation: Replace never touches notes that are absent from `takeNotes_` (FR-005). A pass without a fresh note-on never sets `replaceUpTo_` (FR-004).
