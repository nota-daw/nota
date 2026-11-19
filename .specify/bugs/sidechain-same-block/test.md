# Bug Verification: Export puts sidechain pumping a chunk late

- **Slug**: sidechain-same-block
- **Tested**: 2026-11-19
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The user's `Bugged.nota`, exported through the app's own `AudioExporter`, now pumps where the app
plays it: the Volt dip sits at 0.156 of the beat in the WAV as live, lag 0 (v0.60.3: 0.406, 125 ms
late). The new test fails on the v0.60.3 engine and passes on the fix; the full `scripts/build.sh`
passes.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix), real export | `/private/tmp/nbugged`: load `Bugged.nota` (`ProjectService.Load/Apply`), solo Volt, `AudioExporter.ExportMaster` from beat 28 (8.1) for 16 beats, 48 kHz float WAV; compare against live 512-frame playback | pass | v0.60.3 engine: dip 0.406, lag 6016 samples (125.3 ms). Fixed: dip 0.156, lag 0 |
| Reproduction, block sweep | same harness, offline blocks 256 / 1024 / 2048 / 4096 / 8192 | pass | all dip 0.156, lag 0 (before: 4096 → 95 ms, 8192 → 125 ms) |
| New tests | `dotnet Nota.SmokeTest.dll --sidechain-sync` | pass | 5/5. With the v0.60.3 engine: 4 FAIL, onset exactly one block (5.3 / 10.7 / 21.3 / 85.3 / 170.7 ms) |
| Touched neighbours | `--lens-sync`, `--sidechain-parts` | pass | Lens key trace lines up with the main trace again after the Lens change |
| Regression suite | `scripts/build.sh` (engine + solution build, full smoke test incl. render pool, scanner, scan paths) | pass | exit 0, SMOKE TEST PASSED (7835 checks) |
| Lint / type-check | compiler (engine and C# builds inside build.sh) | pass | both build; no separate linter run |

## Output Excerpts

```
== v0.60.3 engine
  export block  8192: dip=0,406  lag vs live = 6016 samples (125,3 ms)
  AudioExporter WAV: dip=0,406  lag vs live = 6016 samples (125,3 ms)
== fixed engine
  export block  8192: dip=0,156  lag vs live = 0 samples (0,0 ms)
  AudioExporter WAV: dip=0,156  lag vs live = 0 samples (0,0 ms)
```

```
SIDECHAIN SYNC PASSED
SCANNER SELFTEST PASSED
SMOKE TEST PASSED
SCAN SCALE PASSED
DEFAULT SCAN PATHS PASSED
DEFAULT SCAN PATHS UPGRADE PASSED
exit=0
```

## Residual Risks

- Not checked in the running app's UI (export dialog) or by ear; the harness drives the same
  `ProjectService` + `AudioExporter` code the dialog calls.
- Only the Bugged.nota project was available; the original #676 project (Aurora tracks in a group)
  was not, so its exact routing wasn't replayed.
- A leaf keyed by a group or return, and key cycles, still hear the key a block late (by design).
- Engine performance with sidechains (extra pool dispatch per key level) not benchmarked yet; the
  no-sidechain path is unchanged but for a 16-flag clear. The CI perf gate will run on the PR.
- Live sound change: keyed devices react one buffer earlier than before (tighter pumping).

## Recommendation

Ship as the #676 follow-up — verified end-to-end on the user's project through the real export
path. Before the PR: a before/after engine benchmark (nota-pr, render-path change), and a listen
to the Bugged.nota export in the app.
