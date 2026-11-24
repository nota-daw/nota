# Data model: clip Follow bypass

## SessionSlot (engine, immutable in a snapshot)

| Field | Type | Default | Meaning |
|---|---|---|---|
| `followBypass` | bool | false | the clip's Follow actions are held; A/B, chances, Jump, followBeats untouched |

## SessionPlayer (engine, audio-thread-only additions)

| Field | Type | Reset | Meaning |
|---|---|---|---|
| `followHeld` | bool | on launch, on None outcome | the playing slot was bypassed in an earlier block |
| `followBase` | double (beats) | 0 on launch, on None outcome | subtracted from `playedBeats` in the follow check |

State transitions per block (playing slot `s`, `due` its follow time, `p = playedBeats`):

```
s.followBypass            → followHeld = true; no follow
!bypass && followHeld     → followBase = floor(p / due) * due; followHeld = false; then normal check
normal check              → fire when p - followBase + block/2 >= due
launch / None outcome     → p = 0, followBase = 0, followHeld = false
```

## NotaSessionClipProps (C ABI) / NotaSessionClipProps (C#)

Appends `int32_t follow_bypass` / `int FollowBypass` (0/1). Size unchanged (64 bytes; the field sits at offset 60).

## SessionSlotDto (project file, v35)

Appends `bool FollowBypass` (default false). v34 → v35 migration: no-op.
