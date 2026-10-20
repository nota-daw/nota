# Bug Verification: Audio Unit plug-ins get 31 channels per block — JUCE assertion and dropouts

- **Slug**: au-process-assert
- **Tested**: 2026-10-20
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Summary

The engine-level reproduction no longer triggers the assertion. On a Debug engine (assertions on), the AU instrument plus AU
effect render from `--hostcheck` produces 0 × `juce_AudioUnitPluginFormatImpl.h:1346`, against 38 before the fix. Audio still
flows. The full build and smoke test pass on the Release engine. The reporter's own scenario has not been re-run: the
reporter's project in the GUI app, under a debugger. Neither has the VST3 layout-widening guard (Reaktor 6). Hence `partial`.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix), Debug engine | `cmake … -B build-debug -DCMAKE_BUILD_TYPE=Debug` + `Nota.SmokeTest --hostcheck build-debug/nota-scanworker` (bin copy with the Debug dylib, `NOTA_DATA_DIR` isolated) | pass | 0 × `:1346` (38 before the fix, see fix.md). AUSampler + AUVectorPanner audible. The 13 `repo root located` FAILs come from running the binary copy outside the repo (design checks look for `Nota.sln`) and are unrelated |
| Hosted AU check, Release engine | `tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest --hostcheck build/nota-scanworker` | pass | `HOSTCHECK PASSED`, instrument rms 0.2157, through effect 0.0096, restored project audible, 0 JUCE assertions |
| Regression suite | `scripts/build.sh` (Release engine + managed build + full smoke test) | pass | `SMOKE TEST PASSED`, 3688 PASS / 0 FAIL, exit 0 |
| In-app reproduction (reporter's project, press play) | GUI app on the Debug engine | not-run | Needs the GUI and the reporter's AU project; to be done by the user |
| VST3 regression guard (Reaktor 6 Play/Patch/Build) | `plugin-add-crash-windows` harness | not-run | Harness `/tmp/widenharness` no longer exists. The VST3 width is algebraically unchanged: `max(max(31, have), in, out)` |
| Lint / type-check | — | skipped | No separate C++ lint step in the project. Both engine configs compile cleanly |

## Output Excerpts

```
# Debug engine (build-debug), post-fix
  [PASS] hosted instrument produces audio (rms=0,2157)
  [PASS] audio flows through the effect (rms=0,0096)
  [PASS] restored plugin project is audible (rms=0,0038)
   5 JUCE Assertion failure in juce_LeakedObjectDetector.h:116
   2 JUCE Assertion failure in juce_MessageListener.cpp:50
   (no juce_AudioUnitPluginFormatImpl.h:1346)

# scripts/build.sh
SMOKE TEST PASSED
exit=0
```

## Residual Risks

- The `--hostcheck` AUs are Apple's (AUSampler, AUVectorPanner), both stereo. A mono-output AU (now handed 1 channel) and the
  reporter's Massive AU were not exercised.
- The reporter's dropouts had a second cause that this fix does not cover: the engine in `build/` had been reconfigured as Debug
  (-O0) and the app ran under netcoredbg. The VSCodium setup now keeps `build/` Release (CMake Tools → `build-debug/`). On a
  -O0 engine, heavy projects can still drop out regardless of this fix.
- The VST3 path is unchanged by construction but was not exercised with a layout-changing plug-in.

## Recommendation

Hold for one manual check, then close. Open the AU project in the app with **Run Nota** (Release engine): no dropouts. Then open
it once with **Nota + Debug engine (LLDB)**: no `:1346`. If Reaktor 6 is handy, switch its Play/Patch/Build modes once to confirm
the VST3 guard. Before merging, move the change to its own `fix/…` branch from `develop`, with a CHANGELOG `[Unreleased]` → Fixed
entry (fix.md § Follow-ups).
