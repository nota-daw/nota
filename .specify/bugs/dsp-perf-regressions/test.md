# Bug Verification: Prism, EQ-8 and Auto Filter render up to 2× slower since the engine rebuild

- **Slug**: dsp-perf-regressions
- **Tested**: 2026-10-23
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Summary

The wall-time slowdown no longer reproduces on Apple Silicon: Prism and EQ-8 drop from
1.8× / 2.1× of 0.56.1 to 1.05× / 1.2×, Auto Filter from 1.4× to 1.15×. The output matches
develop to below −99 dB, and bit for bit for every device that only shares the touched
headers. No regressions were found. Partial only because reproduction step 3 — the Linux /
Valgrind instruction-count gate — could not run locally and is left to the Performance
workflow on the PR.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Fix binary matches the tree | `cmake --build /tmp/nperf/engine-fix --target nota_engine` | pass | "no work to do" — built from the current working tree |
| Reproduction (post-fix), wall time | `nota_perf run <id> 1`, best of 5, main / develop / fix interleaved, all 16 scenarios that failed the gate | pass | the three real regressions fixed; the other 13 within noise of develop |
| Reproduction, instruction-count gate | `scripts/perf-gate.sh origin/develop` (Linux + Valgrind) | not-run | no Valgrind on macOS, Docker daemon not running; the Performance workflow runs it on the PR |
| Sound unchanged (null test) | C ABI harness: Synth + device, 3 s, defaults + 5 seeded random sets with a mid-render change, develop vs fix; EQ-8, Compressor, Auto Filter, Prism, Dynamic EQ-8, EQ-3 (36 renders) | pass | worst diff peak −106.9 dBFS, RMS −99.0 dB below signal (Auto Filter); Dynamic EQ-8 and EQ-3 bit-identical; no non-finite samples |
| Regression suite | `scripts/build.sh` (engine, scanner self-test, managed build, smoke test) | pass (1 flaky timing budget) | 3914 PASS / 1 FAIL `RenderPoolTests` burst budget; see below |
| Smoke re-run | `dotnet run --project tests/Nota.SmokeTest -c Release` ×2 | fail, then pass | the same budget: 9 then 4 (limit 4) |
| Lint / format | `clang-format` | skipped | not installed; CI does not check formatting |

## Output Excerpts

```
id                  main   develop       fix
effect-00 EQ-8      4.82     10.19      5.85
effect-21 Prism    13.70     24.37     14.36
effect-07 AutoFlt   4.25      5.93      4.87
effect-01 Comp      5.27      5.14      5.06
effect-02 Reverb   33.15     32.74     32.60
```

```
[FAIL] overloaded: at most 7 late blocks within 50 ms (<= 4: no bursts when the pool re-checks the load)
```

That check is a `TimingBudget` (not enforced under `CI`). On develop's own CI runs before
this change the same number was 5, 6, 9, 24, 26, 27, 28 and 35, so it exceeds 4 without the
fix too; its scenario renders Synth + Reverb + Delay, none of which touch the changed code.

## Residual Risks

- x86 / Valgrind instruction counts not measured locally; the gate on the PR decides
  `effect-00`, `effect-07`, `effect-21`, `effect-01`.
- Auto Filter (~15 %) and EQ-8 (~20 %) remain a little above 0.56.1 on Apple Silicon.
- The other 16 rows of the release-PR gate (idle-sleep peak tracking) are out of scope and
  still need `perf-regression-accepted` on develop → main.
- Windows / Linux builds not run locally (`build-win.ps1`, `build-linux.sh`); CI covers them.

## Recommendation

Hold for the CI Performance gate on the PR: if `effect-00`, `effect-07` and `effect-21`
pass there (or show only the residual Auto Filter / EQ-8 overhead), close the bug. The
burst-budget smoke failure is pre-existing noise and does not block this change.
