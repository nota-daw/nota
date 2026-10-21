# Bug Verification: Review findings on the engine rebuild (#212)

- **Slug**: pr212-review
- **Tested**: 2026-10-20
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Summary

The use-after-free reproduces on pre-fix `develop` (crash every run) and is gone after the fix,
including under ASan. Pool, idle CPU, render speed and the full build + smoke test all pass on macOS.
The result is partial because three items could not be exercised here: the ARM64 ordering fix (no
reliable repro), the rack-with-plugin path (no plugin installed) and the CI workflow changes (run on
GitHub only).

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| UAF reproduction (pre-fix) | `/tmp/pool223/grow_old` vs original `develop` dylib | fail as expected | SIGSEGV, exit 139, 3/3 |
| UAF reproduction (post-fix) | `/tmp/pool223/grow_rel` | pass | exit 0, 5/5 |
| UAF under ASan | `grow_asan` against `build-asan` (RelWithDebInfo + `-fsanitize=address`) | pass | 2/2, no ASan reports |
| Pool ordering / correctness | `/tmp/pool223/stress` | pass | 1 949 904 tickets, 0 bad — exercises the pool but can't prove the ARM64 race is gone |
| Idle CPU | `/tmp/pool223/idle` | pass | 0 % |
| Render speed | `bench_new 32`, `32 rt`, `8` | pass | 22.3 ms wall/s (unchanged), CPU lower |
| Rack with hosted plugin stays serial | — | not-run | no VST3/AU in the test environment; code-reviewed |
| Pinned deps from scratch | fresh configure with empty `CPM_SOURCE_CACHE` | pass | JUCE `2cdfca8`, flac `1507800`, miniaudio `9634bed` checked out |
| Workflow syntax | YAML parse of `ci.yml`, `release.yml` | pass | the cache/env behaviour on runners can only be confirmed by a CI run |
| Render-pool guards | `dotnet run --project tests/Nota.SmokeTest -c Release -- --perf` | pass | 10 checks; fail against the reintroduced #212 spin (idle/stop/real-time), against waking every thread each block (render threads at 19 % of a core while one thread keeps up), and against in-place scratch growth (crash) |
| Regression suite | `scripts/build.sh` (engine + app + smoke test) | pass | `SMOKE TEST PASSED`, 3789 PASS, 0 FAIL |

## Output Excerpts

```
grow_old: exit 139 (x3)
grow_rel: rendered 18899 blocks, tracks now 0 / exit=0
grow_asan: rendered 11726 blocks, tracks now 0 (no AddressSanitizer report)
SMOKE TEST PASSED
```

## Residual Risks

- Linux/Windows untested locally; the CI run on the PR will cover the build and the new cache steps
  (including the container job that sets `CPM_SOURCE_CACHE` from `$GITHUB_WORKSPACE`).
- Priority inversion between the audio callback and normal-priority pool workers is not addressed
  (follow-up).

## Recommendation

Merge once the PR's CI passes on all three platforms. Follow up separately on the pool-worker priority.
