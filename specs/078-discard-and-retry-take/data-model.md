# Data Model: Discard and Retry a Take

Engine-side state only; nothing new is persisted in the project file.

## TakeAttempt (engine, message thread)

One Record press or one retry, until Stop or the next retry.

| Field | Meaning |
|-------|---------|
| `active` | an attempt is open (set where `recording_` becomes true for a take) |
| `kind` | Arrangement or Session slot (mutually exclusive, research R4) |
| `baseTracks` | `trackId → shared_ptr<const Track>` as they were before the attempt wrote them: record targets at start; a track whose automation write begins during the attempt is added then (first write wins) |
| `undoBase` | `undoStack_` size when the attempt began (decremented when depth trimming drops the oldest entry) |
| `redoSaved` | the redo stack as it was when the attempt began |
| `countedIn` | the attempt started from stop with a count-in (Arrangement) |
| `startedTransport` / `returnBeat` | existing `recordStartedTransport_` / `recordReturnBeat_` |
| `sessionTrackId`, `sessionScene` | the slot (Session attempts) |
| `retryStartBeat` | Session audio retry: capture before this beat is trimmed; `NaN` otherwise |

**Lifecycle**

```text
idle ──Record/recordSessionSlot──▶ open ──Stop──▶ idle         (take materialised: 1 undo step)
                                    │
                                    └─retry──▶ discard ──▶ open (fresh attempt, same targets)
```

- Stop closes the attempt and drops `baseTracks` / `redoSaved` (no lingering references).
- A count-in cancelled by Stop closes it as today (`cancelSessionCountIn`).

## takeEpoch (engine, atomic)

`uint32_t`, bumped by every discard. The audio thread copies it into each queued
`RecordedNote` / `RecordedCtl` / `RecordedExpr`; `poll()` ignores entries whose epoch is not
current (research R2).

## Session slot state

`nota_session_slot_state` gains **5 = record pending**: the slot is the record target but the
take has not begun capturing (MIDI player still queued, or the playhead before
`retryStartBeat`). 0 empty, 1 filled, 2 queued, 3 playing, 4 recording are unchanged.
