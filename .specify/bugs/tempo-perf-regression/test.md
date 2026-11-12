<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Verification: Tempo track instruction regressions

- **Slug**: tempo-perf-regression (reused from assessment/fix context)
- **Tested**: 2026-11-12
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified locally

## Summary

The original performance-gate failure no longer reproduces locally. The unchanged
Linux gate passes all 64 scenarios at its existing 1% instruction and 15% wall-time
limits, with exit code 0. The full Windows build and smoke suite also pass.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Original reproduction, post-fix | `bash scripts/perf-gate.sh c613167d6f66b04e40e7e8c4f656797c8091b2bc` | pass | Actual PR head 29749ced plus this patch; all 64 scenarios; five interleaved timing rounds; no accepted-regression override. |
| Focused clock tests | `dotnet run --project tests/Nota.SmokeTest -c Release --nologo -- --tempo` | pass | 44 checks, including nine new clock regressions. |
| Final native + managed build and regression suite | `scripts/build-win.ps1` in Visual Studio x64 developer PowerShell | pass | Scanner self-test, full smoke suite, design checks, plugin scan-scale test; final source including out-of-line helpers. |
| Diff whitespace | `git diff --check` | pass | No whitespace errors. |
| Additional Windows instruction diagnostics | Same `nota_perf.exe` harness before/after, engine instructions counted during measured API calls using DynamoRIO | pass | Every original failing scenario uses fewer instructions than the original local PR build; supplementary to Linux results. |

## Previously Failing Scenarios

These deltas compare the patched PR with CI's exact base, on the local Linux host.

| Scenario | Instructions base | Instructions patched | Change | Gate |
|----------|------------------:|---------------------:|-------:|------|
| edit-warp-split | 142686 | 141335 | -0.95% | pass |
| audio-tails-8 | 4135535 | 4141115 | +0.13% | pass |
| instrument-04 | 6365571 | 6375969 | +0.16% | pass |
| instrument-01 | 6369180 | 6379542 | +0.16% | pass |
| audio-warp-split | 6718517 | 6729083 | +0.16% | pass |
| instrument-03 | 6410057 | 6422324 | +0.19% | pass |
| instrument-17 | 7387219 | 7397625 | +0.14% | pass |

## Output Excerpts

```text
PASS: no scenario does more than 1% more work or takes more than 15% longer than the base.
SMOKE TEST PASSED
SCAN SCALE PASSED
```

The final `silent-32` timing is 9.41 ms base / 9.83 ms patched, within the limit.

## Evidence

- [Complete Linux performance report](https://raw.githubusercontent.com/thoi1/nota/pr-assets/issue-535/perf-gate-fix/report.md).
- Raw TSVs, gate log, and the seven scenarios' Callgrind profiles:
  `build/tempo-linux/results/` (local, ignored).
- Final Windows build log: `build/tempo-perf-final-build.log` (local, ignored).
- The verified source patch SHA-256 is recorded in the full report.

## Residual Risks

- Local Linux uses Ubuntu 26.04.1, GCC 15.2, Valgrind 3.26.0, and 24 logical CPUs;
  GitHub's runner has four cores. Counts and timings therefore differ from the
  earlier CI report. Both local sides use identical toolchain, hardware, harness,
  and pinned dependency versions, with unmodified gate thresholds.
- These verification results were collected before committing/pushing this fix;
  GitHub CI must also pass on the published commit before merging.
- No new audio-thread allocation, lock, I/O, public ABI, or application dependency
  was introduced. Existing mapped playback, warp split/undo, and timing smoke
  coverage pass.

## Recommendation

The fix is ready to commit and push for PR #568's CI rerun. Retain the normal
performance thresholds and require that CI pass before merging.
