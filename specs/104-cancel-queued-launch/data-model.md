# Data model: Cancel a queued Session launch

Runtime state only — nothing is saved in the project.

## Per-track queued request (`SessionPlayer`)

| Field | Writer | Meaning |
|-------|--------|---------|
| `pending` | message thread (launch / stop), audio thread (follow / repeat, apply, cancel) | `kNone`, `kStop`, `kStopNow` or a slot index |
| `pendingGen` *(new)* | whoever writes `pending` | scene-launch generation owning the request; 0 = not a scene launch |
| `appliedGen` *(new)* | audio thread in `maybeApply` | generation of the request that last applied |
| `repeatSlot` | message thread; cleared by a cancel of that slot | Repeat-mode retrigger |

Transitions added: `slot → kNone` (slot cancel, when `pending == slot`);
`slot | kStop → kNone` (scene cancel, when `pending == scene` or `pendingGen == gen`).
`kStopNow` is never withdrawn.

## Scene launch record (`Engine`, message thread)

| Field | Meaning |
|-------|---------|
| `sceneLaunchGen_` | counter, one per `launchScene` |
| `waitSceneGen_` / `waitScene_` | the launch waiting for its boundary (0 = none) |
| `prevFollowScene_`, `prevFollowStart_` | the scene follow running before the waiting launch |
| `sceneFollowScene_`, `sceneFollowStart_`, `sceneTempoPending_` … | as today, acted on only once the launch is confirmed |

States: **waiting** → **confirmed** (a track applied a request of this generation) or
**withdrawn** (no track holds one any more). Withdrawn restores the previous follow unless it was
already due, and drops the tempo change.

## Cancel table (`Engine`, audio thread)

`cancelSlots_[8]` (track id, scene), `cancelSlotCount_`, `cancelScene_` (scene, gen) — filled by
`drainCommands`, read in the per-track Session step, cleared after the track loop. Overflow beyond
8 slot cancels in one block drops the extra ones (a performer cannot click that fast; the queue
already drops on overflow).
