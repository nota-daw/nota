<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Verification: Block automation depends on selection order

- **Slug**: tempo-block-automation (from this session's assessment/fix)
- **Tested**: 2026-11-14
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The reviewed duplicate failure and the equivalent block-move failure reproduce
before remediation and pass afterward. Both selection orders retain identical
automation across the regression matrix. Full Windows checks pass on rerun;
initial intermittent export worker-CPU guard failures are recorded below.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Original reproduction | Focused tempo suite on `98d17569` with new tests registered | expected failure | 16 failures: lost points and selection-order differences in eight expanded-span scenarios. |
| Post-fix reproduction and regressions | `dotnet run --project tests/Nota.SmokeTest -c Release -- --tempo` | pass | Exit 0, all 572 checks including 243 new checks. |
| Selection/lane/history matrix | Production C ABI, 27 scenarios in both selection orders | pass | Duplicate, block/range paste, block move, shifted paste; expanding/shrinking/constant spans, mix/device filtering, new lanes, source behavior, outside/gap points and one-step undo/redo. |
| First full Windows check | `scripts/build-win.ps1` in VS x64 developer shell | fail | One existing export worker-CPU guard reported 0%; all other smoke assertions passed. Script stopped before scan-scale. |
| First isolated performance check | `dotnet run --project tests/Nota.SmokeTest -c Release --no-build -- --perf` | fail | Same 0% worker-CPU reading; parallel export 183 ms versus serial 870 ms. |
| Unmodified baseline comparison | Clean native build of `98d17569` in detached worktree; same managed `--perf` harness | pass | 91% export worker participation, parallel 180 ms versus serial 885 ms. |
| Patched DLL comparison | Same copied managed harness, replacing only its native DLL with the exact patched DLL | pass | 96% participation, parallel 183 ms versus serial 874 ms; all performance checks pass. |
| Final complete Windows check | `scripts/build-win.ps1` in VS x64 developer shell | pass | Exit 0: native/managed builds, scanner self-test, full smoke/render-pool guards and scan-scale. Export participation 71%, parallel 184 ms versus serial 867 ms. |
| Whitespace | `git diff --check` and staged whitespace check | pass | No whitespace errors. |
| Documentation/capabilities | nota-docs / nota-features audit | pass | Existing documented behavior restored; no new UI, capability or shortcut. |
| Hosted cross-platform/performance checks | GitHub on the newly published commit | not-run locally | Must pass before merge. |

## Output Excerpts

Before:

```text
[FAIL] block automation: retain every copied point (scenario 0, 60→120, mapped True, reverse True)
[FAIL] block automation: both selection orders produce identical automation (scenario 0, 60→120, mapped True, reverse True)
```

After:

```text
Passed: 572, failed: 0
[PASS] exporting 16 tracks in 8192-frame chunks: render threads do 71 % of the work (>= 20 %)
SMOKE TEST PASSED
SCAN SCALE PASSED
```

## Evidence

Ignored logs under `build/tempo-block-automation-`:
`before.log`, `after.log`, `native.log`, `build-first.log`, `perf.log`,
`base-configure.log`, `base-native.log`, `base-perf.log`, `patched-perf.log`,
`build-final.log`.

Baseline checkout: `build/tempo-block-automation-base`, detached at `98d17569`.
The comparison used the same managed test output in
`build/tempo-block-automation-base-run`, swapping the native DLL between runs.
The baseline DLL's SHA-256 was
`143809CC1A43E90EF8E4B1FD6CDCA4D1467BA377BC74EFD941B1821DC8CA2E36`;
the patched DLL's SHA-256 was
`26BC5F760AC31632DD007D9F5E386390DA9ECCF570454697949AEB5E06D3FF32`.

## Residual Risks

- The existing Windows export CPU-accounting guard intermittently reported zero
  worker CPU despite parallel export remaining roughly five times faster. Its
  cause was not established. Baseline, isolated patched and final full runs passed;
  no source, threshold or test setting was changed to obtain those passes.
- Local verification was on Windows; hosted macOS/Linux/performance checks remain.
- The fix preserves existing beat-relative point offsets and lane filtering.
  No rendering, DSP, callback or public C ABI behavior was changed.

## Recommendation

The automation bug is verified. Publish the follow-up to PR #568, retain the failed
and passing performance evidence, and require hosted checks before merging.
No extension hooks are registered for this workflow.
