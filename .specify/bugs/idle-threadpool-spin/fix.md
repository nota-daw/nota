# Bug Fix: Idle engine pins all CPU cores (AudioThreadPool never sleeps)

- **Slug**: idle-threadpool-spin
- **Fixed**: 2026-10-20
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Replaced the `AudioThreadPool` idle `pause` + `yield` busy-spin with spin-then-park on C++20 `std::atomic::wait`. Workers spin 256 pauses for low-latency wake on back-to-back audio blocks, then block in the kernel until the next `dispatch()` `notify_all()`. The audio thread only adds a non-blocking `notify_all()` after its release-store publish (AR-6 safe). Idle pool CPU drops from ~(N-1)*100% to ~0%; dispatch correctness and wake latency verified locally.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/AudioThreadPool.cpp` | modified | `workerLoop()`: 256-pause spin then `epoch_.wait(seen)`; destructor adds `epoch_.notify_all()` before `join()`; header comment updated |
| `src/native/nota.engine/src/engine/AudioThreadPool.h` | modified | `dispatch()` adds `epoch_.notify_all()` after publish store; class comment updated from "spin (pause, then yield — never block)" to "spin briefly, then park on `atomic::wait`" |

## Diff Highlights

```cpp
// workerLoop(): parked branch (was: pause x256 then yield() forever)
if (e == seen) {
    for (int i = 0; i < 256; ++i) {
        NOTA_CPU_PAUSE();
        e = epoch_.load(std::memory_order_acquire);
        if (e != seen) break;
    }
    if (e == seen) {
        if (stop_.load(std::memory_order_relaxed)) return;
        epoch_.wait(seen, std::memory_order_acquire);
        continue;
    }
}
```

```cpp
// dispatch() publish (audio thread, AR-6: store + notify only, never waits)
epoch_.store(g, std::memory_order_release);
epoch_.notify_all();
```

```cpp
// ~AudioThreadPool(): wake parked workers so they observe stop_
epoch_.fetch_add(1, std::memory_order_release);
epoch_.notify_all();
```

The monotonic `claimed_`/`done_` + check-then-CAS claim protocol, slot publish-before-bump ordering, FTZ/DAZ, and scratch handling are untouched.

## Tests Added or Updated

No committed tests added in this change (see Follow-ups). Temporary verification (not committed):

- `/tmp pool_test` (standalone, `AudioThreadPool` direct): 2000 dispatches × 1–32 jobs exact-once check; `n=0` edge; 370×32 rapid-dispatch block simulation; idle-CPU `getrusage` over 3 s.
- Existing gate: `tests/Nota.SmokeTest` via `dotnet run` (unchanged).

## Local Verification

- Commands run:
  - `cmake -G Ninja -S src/native/nota.engine -B src/native/nota.engine/build -DCMAKE_BUILD_TYPE=Release` → ok (libc++ `atomic::wait/notify` compiles on macOS).
  - `cmake --build src/native/nota.engine/build --target nota_engine -j 8` → link `libnota_engine.dylib` ok.
  - Standalone pool test → `correctness stress: OK (2000 dispatches)`, `n=0 edge: OK`, `idle pool: 0.0% of one core`, `370x32 rapid dispatch: OK (4.3 ms)`, `ALL POOL TESTS PASSED`.
  - `dotnet run --project tests/Nota.SmokeTest -c Release` → `SMOKE TEST PASSED`.
- Manual checks: none beyond the above (no M4 892% / Xeon 294% re-measurement on this host; idle window shortened to 3 s vs. the issue's 5 s).

## Deviations from Assessment

- Spin budget kept at the existing 256 pauses (assessment range was 256–2000). Rationale: rapid-dispatch test shows 370×32 dispatches complete in 4.3 ms with parking, so a longer spin is unnecessary; idle parks after one window either way.
- No committed regression test added (assessment proposed a native pool test + idle-CPU smoke assertion). The standalone test above proves the behavior but was not committed as a harness — left as a follow-up for the `bug-test` phase to avoid inventing a new native test layout in this fix.

## Follow-ups

- Add a committed idle-CPU regression test mirroring issue #223 (engine/pool idle + `getrusage`/`ProcessCpuSampler` assert < 20% of one core), following `tests/Nota.SmokeTest/ProcessCpuTests.cs`.
- Re-measure on the issue's hardware (M4 10-core, Linux 4-core VM): idle % over 5 s + 1-track/32-track CPU-per-second-audio to confirm 0% idle and retained ~3.9× busy speed-up.
- Consider follow-up: skip `dispatch()` when zero leaves are eligible (zero wakeups while stopped) if per-block wake cost ever shows in profiles.
- Full 3-OS CI (`.github/workflows/ci.yml`) must pass on PR — `atomic::wait` verified here only on macOS arm64; Windows (WaitOnAddress) and Linux (futex) need the CI legs.
