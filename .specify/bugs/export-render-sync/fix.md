# Bug Fix: tempo-synced synth LFOs follow the song position

- **Slug**: export-render-sync
- **Fixed**: 2026-11-18
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Nota Aurora's and Nota Volt's tempo-synced LFOs now take their phase from the song position while the transport plays, as the effects' LFOs already did. Before, the phase carried over from whatever played earlier, so a pulse on the beat while working landed elsewhere in the beat in an export, or after playing from another bar (#676).

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/devices/instruments/WavetableSynth.h` | modified | `syncBeats()` (the division in beats), `beat_` / `playing_` from the transport |
| `src/native/nota.engine/src/devices/instruments/WavetableSynth.cpp` | modified | `setTransport` keeps the start beat and the playing flag; `render()` sets each synced LFO's phase from the start beat while playing |
| `src/native/nota.engine/src/devices/instruments/VoltSynth.h` | modified | the same members; `setTransport` keeps beat and playing |
| `src/native/nota.engine/src/devices/instruments/VoltSynth.cpp` | modified | `syncBeats()`; the same lock in `render()`, skipped while a macro scales that LFO's rate |
| `tests/Nota.SmokeTest/SynthLfoSyncTests.cs` | added test | 6 checks |
| `tests/Nota.SmokeTest/Program.cs` | modified | registered in the full run and as `--synth-lfo-sync` |
| `CHANGELOG.md` | modified | [Unreleased] Fixed entry |

## Diff Highlights

```cpp
if (playing_ && spb_ > 0.0) {
    auto lock = [&](double& ph, float& sh, double cycleBeats) {
        if (cycleBeats <= 0.0) return;
        const double c = beat_ / cycleBeats;
        const double p = c - std::floor(c);
        if (p + 0.5 < ph) sh = frand() * 2.0f - 1.0f; // crossed a cycle start: new S&H step
        ph = p;
    };
    lock(lfoPhase1_, lfoSh1_, syncBeats(Lfo1Sync));
    lock(lfoPhase2_, lfoSh2_, syncBeats(Lfo2Sync));
}
```

The phase is set once per block and then advances at the synced rate (exact at a steady tempo; re-derived every block, so tempo changes, seeks and loop wraps are followed). An idle Aurora skips render; the next block re-derives the phase, so nothing has to catch up.

## Tests Added or Updated

- `SynthLfoSyncTests` — Aurora and Volt, LFO 1 and LFO 2 synced to one beat on Level, held note: the dip lands at the same place in the beat after three histories (none, 1.37 s and 2.91 s played elsewhere), after a play that stopped off the beat, when playing from beat 9.5, and in an export (8192-frame blocks at 44.1 kHz after a 48 kHz device). Also: with the transport stopped a synced LFO still moves.

## Local Verification

- `dotnet tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll --synth-lfo-sync` with the fixed engine → SYNTH LFO SYNC PASSED (6/6; Aurora dips at 0.234 of the beat, Volt at 0.750, every case).
- The same with the engine before the fix → FAILED (4): e.g. Aurora LFO 1 at 0.234 / 0.203 / 0.703 / 0.500 / 0.969 …; playing from beat 9.5 put the pulse half a beat off a play from bar 1.
- Engine: `cmake --build src/native/nota.engine/build --target nota_engine` → builds (only the existing `makeSel` C-linkage warning).
- Full `scripts/build.sh`: see the bug-test report.

## Deviations from Assessment

- The assessment's reproduction table (0.797 / 0.656 / 0.047 …) came from a harness that set the synth's parameters through `DeviceSetParam(t, -1, …)`, which doesn't reach a built-in instrument's parameters, so no LFO was routed and the "dip" was noise. Re-measured with `PluginParamSet` (the numbers above): the mechanism is as assessed (the phase depends on what played before and on where play starts), but play-again and export match the first play when every render runs a whole number of beats, which the old harness did by chance.
- Volt: the lock is skipped while a macro scales that LFO's rate (Volt's macros can move LFO rate; a scaled division has no position on the song grid, and re-deriving it as the macro moves would jump). Such an LFO free-runs as before. Aurora's macros don't reach LFO rate.
- No docs change: the Aurora and Volt pages only say "optional tempo sync".

## Follow-ups

- #667 (plugin delay compensation through groups) was confirmed in passing: a grouped track lands one look-ahead late. Same live and in the export; stays in #667.
- Ask the user of #676 for the project file to confirm their Aurora patches use a synced LFO.

## Review follow-up (PR #680)

- **Events inside a block rewound the LFO.** The engine renders an instrument that doesn't take timed events in slices, up to each note or expression event (`playEvents`), with one `setTransport` per block, so every slice re-locked to the block's start beat. Both synths now count the frames rendered since `setTransport` and lock each slice to `beat + framesSoFar / samplesPerBeat`.
- **The test now fails on silence and on a wrong phase.** Each play and export must sound and dip (quietest / loudest 1/64 beat ≤ 0.25), and the dip must land where it belongs (Aurora 0.25 of the beat, Volt 0.75), not just where the first render put it. A new case puts pitch-bend expression points every 0.37 beat on the held note, so blocks split mid-way. On the c678a13 engine that case fails in the export (Aurora 0.41–0.44, Volt 0.92); with the follow-up all 14 checks pass.
