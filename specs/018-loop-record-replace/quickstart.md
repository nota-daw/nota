# Quickstart: validate loop record mode

## Automated (PR gate)

```bash
scripts/build.sh
```

The `-- loop record mode --` smoke group in `tests/Nota.SmokeTest/LoopRecordModeTests.cs` drives the engine through the C ABI on a 1-bar loop at 120 BPM:

| Scenario | Expected |
|----------|----------|
| Overdub, a different pitch on each of 3 passes | All 3 pitches in the take (SC-002) |
| Replace, the same phrase on 5 passes with jittered timing | Exactly one phrase's notes (SC-001) |
| Replace, play on pass 1, silent pass 2, stop | Pass-1 notes kept (FR-004) |
| Replace, recording into a clip with existing notes | The existing notes are kept (FR-005) |
| Replace, a note held across the seam, then a fresh note | The head is removed with the old pass; the continuation and the fresh note stay |
| Switch Overdub → Replace mid-pass | Takes effect from the next pass (FR-007) |
| Replace, routed MIDI | Same as live |
| Mode get/set through the ABI, invalid value | Round-trips; 2 → `NOTA_ERR_INVALID_ARG` |

## Manual (in the app)

1. Loop one bar, arm a Volt track, click **Overdub** in the transport so it reads **Replace** (or press ⌘⇧R).
2. Record and play a phrase three times, varying the timing, then stop. The clip holds one phrase.
3. Press ⌘⇧R (it reads **Overdub**), record kick, snare and hats on three passes. All three stay.
4. Quit and relaunch. The mode is as you left it.
