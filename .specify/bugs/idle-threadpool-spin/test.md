# Bug Verification: Idle engine pins all CPU cores (AudioThreadPool never sleeps)

- **Slug**: idle-threadpool-spin
- **Tested**: 2026-10-20
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The issue #223 symptom no longer reproduces: the exact engine-level repro (`nota_engine_create()` + `getrusage` over 5 s) reports 0% of one core post-fix (vs 892% on M4 / 294% on Linux pre-fix). Pool stress (2000 dispatches, exact-once), idle-pool (0.0%), and rapid-dispatch checks pass, and the full `Nota.SmokeTest` suite passes with no regressions.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix, issue-exact) | `clang++ engine_idle_repro.cpp -lnota_engine` then `DYLD_LIBRARY_PATH=.../build engine_idle_repro` (create → sleep 1 s → getrusage over 5 s → destroy) | pass | `idle engine: 0% of one core`; threshold 50% |
| New / updated tests (pool stress + idle + rapid) | `/tmp/pool_test` (2000× dispatch 1–32 jobs exact-once, n=0 edge, 3 s idle getrusage, 370×32 rapid) | pass | `ALL POOL TESTS PASSED`; idle `0.0%`, rapid `1.3 ms` |
| Regression suite | `dotnet run --project tests/Nota.SmokeTest -c Release` | pass | `SMOKE TEST PASSED` (full suite) |
| Build / type-check | `cmake --build src/native/nota.engine/build --target nota_engine` (Release, C++20, libc++) | pass | ran in fix phase; link `libnota_engine.dylib` ok, re-used here |

## Output Excerpts

```
idle engine: 0% of one core (wall 5.01s)
PASS: idle engine near 0% CPU
```

```
correctness stress: OK (2000 dispatches)
n=0 edge: OK
workers: 7
idle pool: 0.0% of one core (wall 3.00s)
idle CPU: OK (<20%)
370x32 rapid dispatch: OK (1.3 ms)
ALL POOL TESTS PASSED
```

```
SMOKE TEST PASSED
```

## Residual Risks

- Verified on macOS arm64 only in this session. `std::atomic::wait/notify` portability to Windows (WaitOnAddress) and Linux (futex) still needs the 3-OS CI legs (`.github/workflows/ci.yml`) on the PR.
- Busy-render speed-up (~3.9× on 32-track M4 in the issue) was not re-measured here; rapid-dispatch latency only (370×32 in ~1–4 ms). Dense-render benchmark on M4 + Linux VM remains open.
- No committed regression test was added (fix phase left it as a follow-up); a future regression would rely on CI + manual repro until an idle-CPU smoke assertion lands.
- Shortened pool-idle window (3 s) vs. the issue's 5 s engine window; the engine-level check used the full 5 s.

## Recommendation

Close the bug — verified end-to-end. Open the PR against `develop` with the two-file fix; ensure 3-OS CI passes and record M4/Linux busy-render numbers in the PR description. Add the committed idle-CPU smoke assertion as a follow-up.
