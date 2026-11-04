# Quickstart: verify Discard and Retry

## Automated (the PR gate)

```bash
scripts/build.sh
```

The smoke test `RetryTakeTests` (tests/Nota.SmokeTest) drives the engine offline and checks:

1. Arrangement MIDI from stop with count-in: notes, retry mid-take → clip gone (or back to its
   pre-take notes), playhead at the take start, count-in running; play again, stop → only the
   second take's notes; one `Undo` → the pre-first-take state; `CanRedo` false.
2. Arrangement audio (internal resample source, no device): retry → no clip and no take lane;
   stop after retry → one clip starting at the original start beat.
3. Loop recording with ≥2 passes → retry → no new take lanes; earlier takes intact.
4. Punch-in while playing from beat 4, take begun at beat 8 → retry → playhead at 4, recording,
   no count-in.
5. Automation written during the take → retry → lane back to pre-take points.
6. A note released after the retry → not in the new take (epoch guard).
7. Session MIDI slot with another slot playing on another track → retry → the other track's
   slot still playing, target slot state 5 until the quantum, then 4; stop → slot plays.
8. Session audio slot retry → take trimmed to start on the quantum boundary.
9. `RetryTake` with nothing recording → `None`, project unchanged.
10. Palette/catalog: `transport.retryTake` present with `⌘.`.

## Manual (the app)

1. Arm an audio track, set count-in to 1 bar, press ⌘R, play a few bars, press ⌘. — the take
   disappears, the count-in clicks again, recording continues; ⌘. a few more times, then Return
   → one take; ⌘Z once → empty track; ⌘⇧Z → nothing to redo.
2. Session view: launch a drum clip, record into an empty slot on an armed track, press ⌘.
   → drums never stop; the slot shows pending until the next bar, then records.
