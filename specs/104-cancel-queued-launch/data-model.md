# Data model: Cancel a queued Session launch

Runtime state only — nothing is saved in the project.

## Per-track queued request (`SessionPlayer`)

| Field | Writer | Meaning |
|-------|--------|---------|
| `request` *(replaces `pending`)* | message thread (launch / stop), audio thread (follow / repeat, apply, withdraw) | one 64-bit word: slot (`kNone`, `kStop`, `kStopNow` or a slot index), scene-launch generation (24 bits; 0 = not a scene launch), serial (8 bits, new for every request) |
| `appliedGen` *(new)* | audio thread in `maybeApply`, scene requests only | generation of the last scene request that applied |
| `applySeq` *(new)* | audio thread in `maybeApply` | seqlock around consuming a request, read by `viewGen` |
| `repeatSlot` | message thread; cleared by a cancel of that slot | Repeat-mode retrigger |

Transitions added: a cancel sends the exact words it saw; at the drain, a request still equal to
one → `kNone` (unless its scene launch has started). `kStopNow` is never a target.

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

## Cancel batches (command queue)

One `CancelSessionLaunch` per target (i0 / i1 = the request word, d0 = track id), pushed with
`pushBatch` so the audio thread drains the whole cancel in one go. No audio-thread table.
