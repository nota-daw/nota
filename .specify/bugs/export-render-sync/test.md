# Bug Verification: tempo-synced synth LFOs follow the song position

- **Slug**: export-render-sync
- **Tested**: 2026-11-18
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

With the fix, a synced LFO in Nota Aurora and Nota Volt pulses at the same place in the beat after any play history, from any start point and in an export at another block size and sample rate; without it, the same checks fail. The full build and smoke test pass apart from one hosted-plug-in timing check, which passes on its own (load flake, unrelated to the synths).

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (pre-fix) | `--synth-lfo-sync` against the 0.60.2-equivalent engine (main checkout's `libnota_engine.dylib`, identical synth sources) | fail (expected) | 4/6 fail: the dip moves with history and start beat (e.g. 0.234 → 0.703 from beat 9.5) |
| Reproduction (post-fix) | `dotnet tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll --synth-lfo-sync` | pass | 6/6: Aurora at 0.234, Volt at 0.750 of the beat in every case |
| Harness, both synths | `/private/tmp/n676harness` (play, play again, export after 0 / 1.37 / 2.90 s of history) | pass | fixed: one place per synth (±1/64 beat); before: 0.234 / 0.500 / 0.438 (Aurora), 0.734 / 0.000 / 0.938 (Volt) |
| Aurora preset determinism | full run, `-- aurora null --` | pass | factory presets render bit-identically from a fresh engine (residual −240 dBFS) |
| Regression suite | `scripts/build.sh` (engine, scanner self-test, .NET build, full smoke test, scan checks) | pass with one unrelated flake | 6638 PASS, 1 FAIL: "four indivisible VST3 jobs: 4/100 device deadlines missed (<= 1)" |
| Flake re-run | `Nota.SmokeTest.dll --hosted-render-pool`, twice | pass | 0/100 deadlines missed both times |
| Build warnings | `cmake --build … --target nota_engine` | pass | only the existing `makeSel` C-linkage warning |

## Output Excerpts

```
-- synth lfo sync --
  [PASS] Aurora LFO 1 synced to the beat: … (0,234)
  [PASS] Aurora LFO 2 synced to the beat: … (0,234)
  [PASS] Volt LFO 1 synced to the beat: … (0,750)
  [PASS] Volt LFO 2 synced to the beat: … (0,750)
  [PASS] Aurora: a synced LFO still moves with the transport stopped (…)
  [PASS] Volt: a synced LFO still moves with the transport stopped (…)
SYNTH LFO SYNC PASSED
```

## Residual Risks

- The user's project isn't available: that their Aurora patches use a synced LFO is likely (the group holds chords, as Shimmer Arp-style patches do) but not confirmed. The engine-level causes that would hit every track (note timing, block size, sample rate, tempo maps, groups) were ruled out in the assessment.
- Not checked in the running app's Export dialog by hand; the test drives the same `RenderOffline` path the exporter uses.
- Volt: a synced LFO whose rate a macro moves still free-runs (by design, see fix.md).
- Projects that relied on the old random phase pulse at a different (now fixed) place.

## Recommendation

Ship in 0.60.3 and close #676 with the PR; ask the reporter for the project file and to confirm on 0.60.3.
