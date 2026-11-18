# Data Model: Next-Take Length for an Empty Session Slot

## NextTakeLength (engine, per `SessionSlot`)

| Field | Type | Values | Notes |
|---|---|---|---|
| `mode` | enum | `Global` (0, default), `Open` (1), `Fixed` (2) | |
| `bars` | int32 | 0–64 | used when `Fixed` |
| `beats` | int32 | 0 – beatsPerBar−1 at set time | used when `Fixed` |

**Validation** (on set): `Fixed` needs bars 0–64, beats 0–31, not 0+0, and beats 0 when bars
is 64 (1 beat .. 64 bars). The engine checks beats against the largest numerator (32), not the
current meter: a meter change reaches the transport only at the next render and a project loads
its slots before that; the inspector offers beats 0 – beatsPerBar−1 of the meter it shows.
`Global`/`Open` ignore and store 0/0. Invalid → `NOTA_ERR_INVALID_ARG`, nothing changes.
(Implementation note: the managed mirror is `NotaSlotTakeLength` + `SlotTakeMode` in
`EngineContracts.cs`, matching the ABI struct, as other Session props structs do.)

**Resolution** (when a fresh take into the slot starts):

```
Global → engine global record length (beats, 0 = open)
Open   → 0 (open-ended)
Fixed  → bars × beatsPerBar(now) + beats
```

**Lifecycle**:

```
Global ──set──▶ Open / Fixed ──set──▶ … ──set Global──▶ Global       (each set = 1 undo step)
   │
   ├─ take starts: resolved once, stored on the take (main take / MIDI lane / audio lane)
   ├─ cancel before start / discard+retry: slot restored as before the take → value unchanged
   ├─ take kept (full or stopped early): slot holds the clip, value kept and hidden
   ├─ clip deleted / moved away / take undone: slot empty again → value shown
   └─ scene deleted: dropped with the row; scene moved/duplicated: carried with the row
```

Clip copy/paste/move and Arrangement→Session drops write the clip into the destination slot
but keep the destination's `nextTake` (it belongs to the slot, not the clip).

## Take length state (engine, transient)

| Where | New field | Meaning |
|---|---|---|
| `Engine` | `double recordSessionLen_` | resolved length of the main Session take, beats; 0 = open |
| `MidiTakeLane` | `double lenBeats` | resolved length of that lane's slot; 0 = open (`growing`) |
| `CaptureLane` | `double lenBeats`, `double startBeat` | an audio lane's resolved length and where it joined |

Not persisted; cleared with the take.

## Persistence (project file)

`TrackDto.SessionTakeLengths : List<SlotTakeLengthDto>?` — absent or empty = every slot Global.

| `SlotTakeLengthDto` | Type |
|---|---|
| `Scene` | int |
| `Mode` | int (1 = Open, 2 = Fixed; Global is never written) |
| `Bars` | int |
| `Beats` | int |

Written for every non-Global slot, filled or empty. Applied after slot contents on load.

## Managed mirror

`IAudioEngine`: `bool TryGetSessionSlotTakeLength(int trackId, int scene, out SlotTakeLength v)`,
`bool SetSessionSlotTakeLength(int trackId, int scene, SlotTakeLength v)`,
`double SessionSlotTakeBeats(int trackId, int scene)`;
`SlotTakeLength` = `record struct (SlotTakeMode Mode, int Bars, int Beats)`.
