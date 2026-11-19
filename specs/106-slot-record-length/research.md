# Research: Next-Take Length for an Empty Session Slot

All findings below come from the code at `origin/develop` 2426fd71 (2026-11-18).

## R1 — Where the per-slot value lives

**Decision**: A new POD field on `SessionSlot` (`src/native/nota.engine/src/engine/Track.h`),
next to `stopButton`: `NextTakeLength nextTake` (mode + bars + beats).

**Rationale**: Undo/Redo in the engine swaps whole graph snapshots (`Engine::pushUndo`,
`republishWithTrack`), and `SessionSlot` lives in the graph. A field there is undoable,
redoable and restored by a kept take's Undo for free. Scene edits (`rebuildScenes`,
`removeScene`, `moveScene`, `insertSceneRow` with `copyFrom` for Duplicate) already move whole
`SessionSlot`s, so the value rides with its row (FR-015). The discard-and-retry restore
(`Engine_TakeRetry.cpp`, `nt->sessionSlots[sc] = base->sessionSlots[sc]`) and the count-in
cancel (`cancelSessionCountIn`, `nt->sessionSlots[sc] = *was`) put back the slot as it was
before the take, value included (FR-009, FR-010).

**Alternatives considered**:
- A parallel per-track vector (`std::vector<NextTakeLength>` beside `sessionSlots`): survives
  slot overwrites without care, but every scene edit and the retry restore would have to keep
  two vectors in step. Rejected: more places to get wrong than the overwrite sites in R2.
- Managed-only state (app keeps a dictionary, engine gets a length per record call): breaks
  Undo (engine-owned) and the recording paths that don't go through the app (MCP, Remote,
  controller launches, scene record). Rejected.

## R2 — Places that overwrite a whole slot

The value belongs to the slot position, not the clip (spec FR-015, clarification 1). These
sites assign or reset a whole `SessionSlot` and must keep the destination's `nextTake`:

| Site | Today | Change |
|---|---|---|
| `clearSessionSlot` (Engine_Session.cpp ~94) | `= SessionSlot{}` | keep `nextTake` (a deleted clip shows the value again) |
| `copySessionSlot` (~861) | `= copy` (source's whole slot) | keep the destination's `nextTake` |
| `arrangementClipToSession`, `arrangementAudioClipToSession` (~570/584) | `= slotFromMidiClip/AudioClip(...)` | keep `nextTake` |
| `Engine_Render.cpp` ~4291 / ~4437 (capture into a slot) | build a slot | check during implement; keep `nextTake` if they replace a slot |

One helper (`replaceSlotContent(SessionSlot& dst, SessionSlot src)` that carries
`dst.nextTake` over) keeps this in one place. `cancelSessionCountIn` and the retry restore
already assign the *same slot's* earlier state, so they need no change. Note `clearSessionSlot`
also resets `stopButton` today; that pre-existing behaviour is left alone (out of scope).

The clipboard (`SessionView.cs` ~535, `CopySessionSlot` + `ClearSessionSlot` for a move)
copies only filled slots, so an empty slot's value never travels through it. The spec's edge
case was corrected to say so.

## R3 — Resolving the length when a take starts

**Decision**: A new `double Engine::resolveTakeBeats(const SessionSlot&) const` returns
`> 0` for a fixed length and `0` for open-ended:
- Global → `sessionRecordLen_` (unchanged semantics: the toolbar's bars × 4).
- Open → `0`.
- Fixed → `bars × transport_.beatsPerBar() + beats` (Nota's beat is one beat of the meter,
  `Transport::beatsPerBar()` = numerator; 3 bars in 7/8 = 21 beats).

It is called at the three places that today read `sessionRecordLen_` to size a fresh take:
`recordSessionSlot` (Engine_Session.cpp ~374), `addSessionMidiLane` (Engine_Recording.cpp
~768) and `addSessionAudioLane`, and its result is stored with the take (R4). The meter is
read when the record request is made; a Session slot record never launches a scene, so the
meter cannot change between the request and the launch boundary except by the user editing it
mid-wait — accepted as an edge (the take keeps the beats it was sized with).

**Alternatives considered**: storing beats instead of bars+beats — rejected by the spec (a
meter change keeps the bar count).

## R4 — Stopping each take at its own length

Today `serviceSessionFollow` (Engine_Session.cpp ~1188) only checks anything while the global
`sessionRecordLen_ > 0`, compares every MIDI lane and the main target against that one value,
and the audio path writes `sessionRecordLen_` as the kept clip's length (Engine_Recording.cpp
~1568).

**Decision**:
- Main take: new member `recordSessionLen_` (beats, 0 = open) set in `recordSessionSlot`;
  the main stop check compares `sessionRecordTarget` elapsed with it.
- MIDI lanes (specs/086): new `double lenBeats` on `MidiTakeLane`; the per-lane loop stops
  each lane whose `playedBeats >= lenBeats` (and `lenBeats > 0`). The `openMidiLaneCount() > 1`
  gate stays (a single lane is the main take).
- Audio lanes: new `double lenBeats` and `double startBeat` on `CaptureLane`
  (`startBeat` = position the lane joined); a lane whose `uiPosition − startBeat >= lenBeats`
  lands via `stopSessionRecordSlot(trackId)`, which already handles one audio lane leaving.
  Lane 0 keeps the main target's elapsed (it already accounts for retry, `retryStartBeat_`).
- The outer `if (sessionRecordLen_ > 0.0)` gate becomes "any running take or lane has a
  fixed length".
- Audio materialise (~1568): `s.lengthBeats = lane.lenBeats > 0 ? lane.lenBeats : roundedTakeBeats(...)`.
- All of this runs on the message thread (`poll()` → `serviceSessionFollow`); nothing new on
  the audio thread (AR-6). `publishSessionLaneSlots` already passes each lane's slot length
  (the clip was created at `lenBeats`) to the audio thread.

## R5 — Retry and bug #678

The retry restore keeps the slot's value (R1) and the restart goes back through the record
path, so the retried take is sized from the slot again (FR-010). Bug #678 (a retried
fixed-length take ends at once, because the elapsed count keeps the discarded take's time) is
in the same stop check this feature rewrites. **Decision**: do not fix #678 under this issue
(it has its own report, not yet approved); write the retry acceptance check so it is skipped
with a note until #678 lands, and re-enable it then. If #678 is approved first, rebase on it.

## R6 — Undo and "modified"

Setting the value goes through `cloneTrack` + `republishWithTrack`, which pushes one undo
checkpoint — the same as `setSessionSlotStopButton` (clarification 2). Dragging the bars/beats
numbers in the inspector brackets the gesture with the existing undo-group ABI so a drag is one
step (DragNumber `GestureBegin`/`GestureEnd`, as other inspector numbers do). The project's
modified flag follows engine edits the same way it does for the Stop button.

## R7 — ABI shape

**Decision**: a small struct plus get/set, beside the Stop-button pair in `nota_engine.h`:

```c
typedef struct NotaSlotTakeLength { int32_t mode; int32_t bars; int32_t beats; } NotaSlotTakeLength;
NOTA_API int32_t    nota_session_get_slot_take_length(const NotaEngine*, int32_t track_id, int32_t scene, NotaSlotTakeLength* out);
NOTA_API NotaResult nota_session_set_slot_take_length(NotaEngine*, int32_t track_id, int32_t scene, const NotaSlotTakeLength* in);
NOTA_API double     nota_session_slot_take_beats(const NotaEngine*, int32_t track_id, int32_t scene); /* resolved, 0 = open */
```

The resolved-beats query lets the inspector say "Global (4 bars)" and lets the smoke test
check a resolution without recording. The engine accepts the value on any slot (persistence
restores values hidden under clips); the "empty slots only" rule is the UI's and MCP's. See
[contracts/abi.md](contracts/abi.md).

## R8 — UI

- **Inspector** (`BuildEmptyInspector`, SessionView.Inspector.cs ~436): a "Take length" row
  with the existing `Row(label, Chip(value, active, Pick(...)))` pattern — items Global
  (showing its resolved value), Open, 1/2/3/4/8/16 bars. When the slot has a fixed length, a
  second row with two `DragNumber`s (bars 0–64, beats 0–beatsPerBar−1), and a caption line in
  the same style as the Stop-button explanation: limits and when it takes effect. Out-of-range
  typing is clamped by `DragNumber`'s min/max; a total of 0 is refused with the caption saying
  the minimum.
- **Grid** (`SessionView.Grid.cs`, the slot cell ~342): an empty slot whose value is Open or
  fixed draws a small muted label ("3 bars", "Open") in the Studio caption style; Global draws
  nothing new. Follows `DESIGN.md` (no new colours; tertiary text).
- **Strings**: new `session.inspector.take_length*` keys in the EN catalog; RU in the
  translations repo as usual.

## R9 — Persistence

**Decision**: `TrackDto.SessionTakeLengths : List<SlotTakeLengthDto>?` with
`{ Scene, Mode, Bars, Beats }`, written only for non-Global slots, for **every** slot (filled
or empty — the value stays under a clip). Loaded after the slot contents, the same place
`SessionNoStop` is applied (ProjectService.cs ~963). Old projects have no list → all Global
(FR-014). No format-version bump: an absent optional list is the old meaning.

## R10 — MCP

`SessionTools`: `set_session_slot_take_length(trackId, scene, mode, bars, beats)` refusing a
filled slot or an out-of-range value; `SessionSnapshot` gains `PreparedSlots` (non-Global
empty slots with mode/bars/beats/resolved beats). See [contracts/mcp.md](contracts/mcp.md).

## R11 — Tests

A new `tests/Nota.SmokeTest/SlotTakeLengthTests.cs` (pattern of `RetryTakeTests.cs`), run from
`Program.cs`: resolution per mode and meter, the A=3/B=8 scenario (MIDI), global unchanged,
Open under global Fixed, audio slot (test lane), multi-slot lanes, cancel before start, kept +
Undo + Redo, clear slot shows the value again, copy onto a prepared slot keeps it, scene
move/duplicate carry it, save/load round trip, old project → Global, MCP set/refuse. The retry
check waits for #678 (R5).
