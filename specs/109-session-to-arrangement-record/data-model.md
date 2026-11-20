# Data Model: Record a Session Performance into the Arrangement

Nothing new is saved in the project. Everything below lives only while a pass runs. The pass
leaves ordinary Arrangement clips and automation behind (FR-018).

## LaunchEvent (audio thread → message thread)

One applied Session request on one track, pushed from the render loop while a pass runs (R1).

| Field | Type | Meaning |
|---|---|---|
| `trackId` | int32 | the track whose Session player applied the request |
| `slot` | int32 | the slot now playing; −1 = stopped (back to its Arrangement) |
| `beat` | double | timeline beat it took effect (grid beat for a quantized request) |
| `slotPos` | double | the slot's loop position at that beat (0, a Legato carry, or the position reached when the pass started) |
| `gain` | float | launch-velocity gain of the slot |
| `epoch` | uint32 | the take epoch; `poll()` drops events of a discarded attempt |

Ring: `SpscRingBuffer<LaunchEvent, 1024>`. When it is full an event is dropped and a counter is
raised, and the pass reports it in the take result. 1024 events in one ~30 ms poll interval
would need more launches than any set makes.

## PlayedSpan (message thread)

Built from LaunchEvents in `poll()`, one per (track, slot) run.

| Field | Type | Meaning |
|---|---|---|
| `trackId` | int32 | |
| `slot` | int32 | the slot played |
| `start`, `end` | double | timeline beats `[start, end)`; `end` is open while it plays |
| `slotPos` | double | the slot position at `start` |
| `gain` | float | |

Rules:
- An event on a track closes that track's open span at `beat` and, if `slot ≥ 0`, opens a new one.
- A span with `end − start ≤ 0` is dropped. A very short positive span is kept (spec edge case).
- At the end of the pass (Record off or transport stop) every open span closes at the stop beat.
- Loop seam (loop on): every open span closes at the loop end. The playing slots reopen at
  the loop start with their position carried on (FR-012).
- A track that has been deleted drops its spans. A monitored audio track never opens one (R4).

## SessionPass (message thread, inside the TakeAttempt)

| Field | Type | Meaning |
|---|---|---|
| `active` | bool | a pass is running (set with the take; the audio-thread flag mirrors it) |
| `performanceOnly` | bool | started with nothing armed (R7, record start status 3) |
| `spans` | vector<PlayedSpan> | closed and open spans, in time order |
| `tempoBefore` | double | project tempo when the pass started |
| `tempoSteps` | vector<(beat, bpm)> | scene tempos applied during the pass, only when no Tempo lane exists (R6) |
| `dropped` | int32 | LaunchEvents lost to a full ring (expected 0) |

Lifecycle: **idle** → (Record starts a take, or a performance-only pass) **running** →
(Record off or Stop) **writing**: spans → written clips, take notes merged in, the Tempo lane
written, the undo group closed → **idle**.
⌘. during **running** → the take attempt is discarded with the pass → a new attempt starts
**running**.

## Written clip

An ordinary `MidiClip` (one per span) or `AudioClip`s (one per repetition) made from a span
(R2), named after the slot. Nothing links it to the slot once written.
