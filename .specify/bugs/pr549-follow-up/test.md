# Bug Verification: PR #549 follow-up review

- **Slug**: pr549-follow-up (from assessment/fix context)
- **Tested**: 2026-11-09
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified on Windows

## Summary

Both follow-up findings are covered by passing regressions. The final unchanged `scripts/build-win.ps1` run exited zero: native/managed builds, scanner self-test, the complete smoke suite and scan-scale checks passed. Two earlier complete runs failed different render-pool performance guards; all functional checks passed in both. These failures are retained below rather than omitted from the evidence.

## Checks performed

| Check | Command / action | Result | Evidence |
|---|---|---|---|
| Focused suite | `dotnet run --project tests/Nota.SmokeTest -c Release -- --articulations` | pass | 414 passes, zero failures; `articulations.log`. |
| Tempo reproduction | Native self-test 36 via the actual Session renderer | pass | Natural/chased switches at nonzero positions, 120 → 60 and 120 → 240 BPM; each release remains at rendered frame 960. Clearing the map during the hold also preserves delivery. |
| Bounded storage and lifecycle | Native self-test 37 | pass | Queue capacity rejects extra key-ons; every emitted key gets its release; small output buffers retry releases; launch/stop clears flushed pending keys. |
| Duplicate-file reproduction | `ArticulationManagerTests` | pass | Real copied `.notamap` files with identical and differing contents; project-backed and library-only catalogs remain usable and deduplicate each ID. |
| Full build, initial run | `scripts/build-win.ps1` | fail, exit 1 | Export guard reported 0% worker participation, below 20%; all articulation checks passed. `build-win.log`. |
| Isolated render-pool suite | Smoke test `--perf` | pass, exit 0 | Worker participation 89%; parallel bounce CPU 968 ms vs serial 405 ms. `perf.log`. |
| Full build, second run | `scripts/build-win.ps1` | fail, exit 1 | Bounce CPU guard reported 1275 ms parallel vs 400 ms serial; export participation and all articulation checks passed. `build-win-verified.log`. |
| Full build, final run | `scripts/build-win.ps1` | pass, exit 0 | Bounce CPU 1094 ms vs 393 ms; export worker participation 60%; scanner, full smoke and scan-scale passed. `build-win-final.log`. |
| Whitespace | `git diff --check` | pass | No whitespace errors. |

No production code or guard thresholds were changed between the complete runs. The original PR description also records an intermittent 0%-worker export failure. Timing variation remains a test/environment concern; it is not evidence of a clean first run.

## Output excerpts

```text
ARTICULATIONS PASSED
[PASS] ... copied map files with duplicate IDs do not block the manager ...
[PASS] ... a library-only catalog lists each duplicate ID once ...
[PASS] ... (failed check: 0)
PERF PASSED
SCANNER SELFTEST PASSED
SMOKE TEST PASSED
SCAN SCALE PASSED
```

## Residual risks and recommendation

Commit the two fixes independently and push them to PR #549, then resolve review threads `PRRT_kwDOUD4x7861QPvL` and `PRRT_kwDOUD4x7861QPvP`. The prior revision `96733e0e` passed Linux instruction-count CI; the follow-up commits require a new CI run. No local Linux/macOS build or manual sample-library audition is claimed. Local raw logs are ignored alongside these reports.
