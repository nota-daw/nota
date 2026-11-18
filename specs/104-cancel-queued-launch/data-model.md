# Data model: Cancel a queued Session launch

Runtime state only — nothing is saved in the project.

## Per-track queued request (`SessionPlayer`)

| Field | Writer | Meaning |
|-------|--------|---------|
| `request` *(replaces `pending`)* | message thread (launch / stop / cancel mark), audio thread (follow / repeat, apply, withdraw) | one 64-bit word: slot (`kNone`, `kStop`, `kStopNow` or a slot index), scene-launch generation (24 bits; 0 = not a scene launch), cancel ticket (8 bits; 0 = none) |
| `appliedGen` *(new)* | audio thread in `maybeApply`, scene requests only | generation of the last scene request that applied |
| `applySeq` *(new)* | audio thread in `maybeApply` | seqlock around consuming a request, read by `viewGen` |
| `repeatSlot` | message thread; cleared by a cancel of that slot | Repeat-mode retrigger |

Transitions added: a request seen by a cancel gets its ticket; at the drain, a request still
carrying the ticket → `kNone` (or the ticket is cleared, the scene launch having started).
`kStopNow` is never marked.

## Scene launch record (`Engine`, message thread)

| Field | Meaning |
|-------|---------|
| `sceneLaunchGen_` | counter, one per `launchScene` |
| `waitSceneGen_` / `waitScene_` | the launch waiting for its boundary (0 = none) |
| `prevFollowScene_`, `prevFollowStart_` | the scene follow running before the waiting launch |
| `sceneFollowScene_`, `sceneFollowStart_`, `sceneTempoPending_` … | as today, acted on only once the launch is confirmed |

States: **waiting** → **confirmed** (a track applied a request of this generation) or
**withdrawn** (no track holds one any more). Withdrawn drops the tempo change and restores the
previous follow — even if its time came meanwhile — unless a scene follow made the launch
(`waitFromFollow_`), which ends follow.

## Cancel tickets (`Engine`, message thread)

`cancelTicket_` cycles 1…255; each cancel marks its targets with a fresh ticket and pushes one
`CancelSessionLaunch` command carrying it. No audio-thread table.
