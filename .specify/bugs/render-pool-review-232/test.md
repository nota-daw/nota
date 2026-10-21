# Bug Verification: render pool follow-ups from the #232 review

- **Slug**: render-pool-review-232
- **Tested**: 2026-10-21
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified (macOS, Apple Silicon; Linux and Windows not run locally)

## Summary

The new checks fail on the pre-fix render code and pass with the fix: chunked exports use
the render threads again (0 % → 86 % of the work, 3.4x faster wall time), and the late-block
bursts at hold expiry are gone. The full smoke test passes. The libc++ wake fix was checked
with a standalone program.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction, pre-fix code | `Engine_Render.cpp` temporarily reverted to `HEAD`, engine rebuilt, `dotnet run --project tests/Nota.SmokeTest -c Release -- --perf` | fail (expected) | export: render threads 0 % of the work; overload: 6 late blocks within 50 ms |
| Reproduction, post-fix | same, fixed code | pass | export: 86 %, 141 ms vs 481 ms on one thread; overload: ≤ 3 late blocks within 50 ms |
| Stability | `--perf` run 3 times | pass | late after warm-up: 15, 2, 16 of 4125; max burst 3, 1, 3 |
| 8-track 512-frame bounce | develop vs branch, 20 interleaved runs each from a neutral host (Python ctypes), plus 15–25-run series from three C++ harness builds | pass | no regression: neutral host 8.7 (develop) vs 8.55 ms (branch) per s of audio, median. The 6.0 ms "fast mode" depends on the host binary, not the engine: byte-identical harness copies keep their mode, buffer offsets and environment size don't change it, and the same engine is fast under one harness build and slow under another, for develop and the branch alike |
| futex wake width | standalone C++ (`/tmp/wake-test`), 8 waiters, one `notify_one` | pass | `atomic<int32_t>` woke 8 of 8, `atomic<int64_t>` 1 of 8 |
| Regression suite | engine build (Release) + `dotnet build Nota.sln -c Release` + smoke test | pass | `SMOKE TEST PASSED` (the build.sh steps, run by hand in a worktree with the dependency sources reused) |
| Bit-identity | smoke: 8 synth tracks on 9 render threads vs one | pass | maxdiff 0 |
| Perf gate (CI, instruction counts) | not run locally | not-run | runs on the PR |

## Output Excerpts

```
pre-fix:  [FAIL] overloaded: at most 6 late blocks within 50 ms (<= 4: no bursts when the pool re-checks the load)
pre-fix:  [FAIL] exporting 16 tracks in 8192-frame chunks: render threads do 0 % of the work (>= 20 %)
post-fix: [PASS] overloaded: at most 3 late blocks within 50 ms (<= 4: no bursts when the pool re-checks the load)
post-fix: [PASS] exporting 16 tracks in 8192-frame chunks: render threads do 86 % of the work (>= 20 %)
post-fix: [PASS] exporting 16 tracks in 8192-frame chunks: parallel 141 ms vs 481 ms on one thread (not slower, <= 1.3x)
SMOKE TEST PASSED
```

## Residual Risks

- The burst check's margin is thin on this machine (3 with the fix, 6 without, limit 4);
  it is a timing budget, not enforced on CI runners.
- The export check drives `RenderOffline` in 8192-frame chunks with a 2 ms gap, not the
  app's `AudioExporter` with a real encoder; the engine path is the same.
- Linux (int32 futex word, unchanged) and Windows were not run locally; CI covers them.
- The leave-the-pool bound can keep the pool engaged after an overload ends (more CPU, no
  late blocks), by design.

## Recommendation

Ready for a PR: verified on macOS, with the CI build matrix and perf gate to confirm Linux
and Windows. Ask Chris to re-run his Linux benchmarks (export at 8192, 40 tracks at 64
frames) on the branch.
