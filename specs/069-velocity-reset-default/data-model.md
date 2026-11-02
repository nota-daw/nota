# Data Model: Reset a note's velocity to the default

No data is added, saved or migrated. A note's velocity already travels in the project file as a float in 0..1; this feature only changes its value.

## Default velocity

| Field | Type | Rule |
|---|---|---|
| `VelocityReset.Default` | float | 0.8. Not a setting. Also the initial "last touched" velocity and the playable keys' velocity. |

**Equality**: two velocities are the same when they differ by less than half a MIDI step (0.5 / 127). A note at the default is never touched by a reset.

## Reset targets

Input: the lane's note list, the selection (indices), the stem hit (index), each note's lock state.

- Stem in the selection → every selected, unlocked note.
- Stem outside the selection → that note only, and it becomes the selection.
- Locked notes (a reference clip) and ghost notes → never targets.

Output: the targets whose velocity is not already the default. Empty = nothing changes, no undo step, menu item disabled.

## State

```
idle ──press on a stem──▶ burst open (seeded undo step, notes changed live)
burst open ──second press of a double-click on the same stem──▶ reset applied inside the same burst
burst open ──idle 600 ms / another edit / undo / redo──▶ closed (Remember: last touched = this note)
```

One undo step per burst. A reset after a long pause is its own burst (its own step).
