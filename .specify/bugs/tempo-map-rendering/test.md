<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Verification: Mapped rendering and split performance

- **Slug**: tempo-map-rendering
- **Tested**: 2026-11-12 (local checks before publication)
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial — local checks pass; hosted performance confirmation pending

## Summary

The three reported audio bugs reproduce before the changes and pass their automated
reproductions afterward. Full Windows regression checks and all 64 Linux work/timing
scenarios pass. The preceding source passed locally but failed hosted split work, so
this pre-publication report deliberately awaits hosted confirmation.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Original symptoms before fixes | Initial `--tempo` rendering regressions on 7b65ef7e | pass (reproduced) | Reverse, release, consolidation duration/ending assertions fail; `build/tempo-rendering-before.log`. |
| Focused engine regressions | `dotnet run --project tests/Nota.SmokeTest -c Release -- --tempo` | pass | 101 total checks; 38 added for reversal modes/builders/direction changes/trim, mapped ADSR, nonzero/deferred consolidation and unchanged live transport. |
| Full Windows build | `scripts/build-win.ps1` in VS x64 environment | pass | Scanner, native/managed build, complete smoke, render CPU guards and scan-scale; repeated after merging develop 39c7857c. |
| Tempo UI | `dotnet run --project tests/Nota.TempoUiTest -c Release` | pass | All 40 checks on the merged tree, including waveform pixels. |
| Linux native/performance | Unchanged `scripts/perf-gate.sh 39c7857c3c11ab44a5dd77e500acce26e9661312` | pass | 64 scenarios, 1% work / 15% wall-time limits, five interleaved rounds; no acceptance override. |
| Before/fixed split work | Same Linux harness, Valgrind, pinned sources and compiler | pass | Before 7b65ef7e: 140992 instructions; fixed: 138367; develop: 144284. Fixed is 1.86% below the preceding PR and 4.10% below develop in this environment. |
| Hosted failure before fix | Actions run 43237488237, job 131248636073 | reproduced | CI measured 139432 base / 142433 PR (+2.15%); local and hosted counts differ and must not be conflated. |
| Native snapshot identity | Git subtree comparison | pass | Windows and Linux both test `17a8133cb04e169554232b4b1bf90e655fa8a95d`; the develop merge changes no native/performance sources. |
| Conflicts and whitespace | `git ls-files -u`, marker scan, `git diff --cached --check` | pass | Empty results after retaining both branches' changelog changes. |
| Docs/features/shortcuts | Audit the fixes | pass | Restores documented behavior; no new user capability, workflow, label or binding. |
| Hosted confirmation after fix | New PR CI on the published source | pending | Required because the preceding local pass did not predict CI. Final hosted status is recorded with the PR evidence. |

## Output Excerpts

```text
SMOKE TEST PASSED
SCAN SCALE PASSED
[PASS] tempo rendering: reversed mode 5, build 0 sounds at beat 6 (second 4)
[PASS] tempo rendering: trimmed reversed window preserves beat 6, mode 3, build 2
PASS: no scenario does more than 1% more work or takes more than 15% longer than the base.
edit-warp-split: 144284 base / 138367 fixed (-4.10%)
```

## Residual Risks

- Hosted source confirmation is still pending when this report is committed. This
  snapshot does not claim a green hosted result. See PR #568 checks and the final
  hosted-verification evidence under `pr-assets/issue-535/tempo-map-rendering/`.
- Recording and playback tests use offline/device-free rendering, not physical audio hardware.

## Recommendation

The three fixes hold locally with unchanged performance limits. Require the hosted
performance job and platform checks to pass before merging. No verification hooks
are registered (`.specify/extensions.yml`: `hooks: {}`).
