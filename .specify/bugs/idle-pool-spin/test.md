# Bug Verification: Idle engine keeps hardware_concurrency − 1 cores busy (AudioThreadPool never parks)

- **Slug**: idle-pool-spin
- **Tested**: 2026-10-20
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The issue's reproduction now reports 0 % CPU for an idle engine (was ~1240 % on this Mac). The pool
stress test shows no lost or duplicated tickets and no hangs, the multi-core render speed is
unchanged, and the full build with the smoke test passes. Checked on macOS (Apple Silicon) only.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix) | `/tmp/pool223/idle` (issue snippet, linked to `build/libnota_engine.dylib`) | pass | 0 % of one core, twice; pre-fix build ~1240 % |
| Pool correctness | `/tmp/pool223/stress` (3 pools × 20 000 dispatches, park/wake gaps, destroy while parked) | pass | 1 949 904 tickets, 0 bad, no hang |
| Thread sanitizer | `stress` built with `-fsanitize=thread` | pass (no new findings) | 6 warnings on job-slot reads; identical on pre-fix code |
| Render speed / CPU | `/tmp/pool223/bench_{old,new}` 1/8/32 tracks, offline + rt-paced | pass | 32-track wall time same within noise (~21–22 ms per s); CPU lower in every case (rt 32 tracks: 11 894 → 608 ms per s) |
| Regression suite | `scripts/build.sh` (engine + app build + smoke test) | pass | `SMOKE TEST PASSED`, 3782 PASS, 0 FAIL, exit 0 |
| Lint / type-check | — | skipped | no separate lint step for the engine; the build compiled clean |

## Output Excerpts

```
idle engine: 0% of one core
tickets=1949904 bad=0
SMOKE TEST PASSED
exit=0
```

## Residual Risks

- Linux and Windows not run here. The fix uses standard C++20 `std::atomic<uint32_t>::wait/notify`
  (futex on Linux, `WaitOnAddress` on Windows); CI's Linux/Windows builds and smoke tests should
  confirm.
- The app itself was not watched in Activity Monitor; the C ABI reproduction from the issue was used
  instead.
- Waking parked workers adds a futex wake per block on the audio thread (only when a worker is parked
  and the job has more than one ticket) — non-blocking, but a syscall.

## Recommendation

Close the bug once the Linux/Windows CI builds pass — verified end-to-end on macOS.
