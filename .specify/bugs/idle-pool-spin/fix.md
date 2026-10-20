# Bug Fix: Idle engine keeps hardware_concurrency − 1 cores busy (AudioThreadPool never parks)

- **Slug**: idle-pool-spin
- **Fixed**: 2026-10-20
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

AudioThreadPool workers now spin for a short, fixed number of pause hints after each job and then
park on a C++20 `std::atomic<uint32_t>::wait()` futex word; `dispatch()` wakes them with a
non-blocking `notify_all()` only when a worker is parked and the job has more than one ticket. An
idle engine drops from ~(hw − 1) × 100 % CPU to 0 %.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/AudioThreadPool.h` | modified | `sleepers_` + `wake_` members, `park()` decl; `dispatch()` publishes `epoch_` seq_cst and wakes parked workers when `n > 1`; header comment documents the park protocol |
| `src/native/nota.engine/src/engine/AudioThreadPool.cpp` | modified | `park()`; `workerLoop()` spins 64 × `NOTA_CPU_PAUSE()` then parks (no more `yield()` loop); destructor always bumps + notifies `wake_` |

## Diff Highlights

```cpp
// dispatch()
epoch_.store(g, std::memory_order_seq_cst); // publish (seq_cst: pairs with park())
if (n > 1 && sleepers_.load(std::memory_order_seq_cst) > 0) {
    wake_.fetch_add(1, std::memory_order_release);
    wake_.notify_all(); // non-blocking futex wake
}

// park()
const uint32_t w = wake_.load(std::memory_order_acquire);
sleepers_.fetch_add(1, std::memory_order_seq_cst);
if (epoch_.load(std::memory_order_seq_cst) == seen && !stop_.load(std::memory_order_seq_cst))
    wake_.wait(w, std::memory_order_acquire);
sleepers_.fetch_sub(1, std::memory_order_relaxed);
```

## Tests Added or Updated

No in-repo test target exists for the engine's internals (the engine is covered end-to-end by
`tests/Nota.SmokeTest`). Throwaway harnesses in `/tmp/pool223/` (not committed):

- `idle.cpp` — the issue's reproduction (create engine, measure process CPU over 5 s).
- `stress.cpp` — 3 pools × 20 000 dispatches of 1–64 tickets mixing back-to-back dispatches, 100 µs
  gaps (forces park/wake) and 5 ms gaps; checks every index runs exactly once; destroys while
  parked. Also built with `-fsanitize=thread`.
- `bench.cpp` — 1/8/32 audio tracks (WAV clip + Reverb), 10 s rendered in 512-frame blocks via
  `nota_engine_render_offline`, back-to-back (offline) or paced like a 512 @ 48 kHz callback (rt).

## Local Verification

- `idle` → **0 %** of one core (before: ~1240 % on this 14-thread Mac, 13 workers).
- `stress` → 1 949 904 tickets, 0 missed/duplicated, no hang. TSan reports 6 warnings on the
  non-atomic job-slot reads in the claim loop — identical on the pre-fix code, so pre-existing and
  not from the park path.
- `bench` (CPU ms / wall ms per second of audio, M-series Mac, before → after):

  | Case | Before | After |
  |---|---|---|
  | offline, 1 track | 71.5 / 5.9 | 4.4–5.9 / 4.4–5.9 |
  | offline, 8 tracks | 182 / 14.0 | 63 / 9.5 |
  | offline, 32 tracks | 270–280 / 20.9–22.4 | 206–209 / 21.9–22.2 |
  | rt-paced, 1 track | 12 438 / 1000 | 32 / 1000 |
  | rt-paced, 32 tracks | 11 894 / 1000 | 608 / 1000 |

  Parallel render speed is unchanged (32-track wall time within run-to-run noise).
- `scripts/build.sh` (incl. smoke test) → passed on the first version of the fix (3782 PASS,
  0 FAIL); re-run on the final version — see the test report.

## Deviations from Assessment

- The assessment proposed a ~50 µs time-bounded spin window. Measured 0 / 20 / 100 µs windows:
  wall-clock render time was the same for all (within noise) while CPU grew with the window
  (32 tracks rt: 639 / 695 / 749 ms), because the engine dispatches once per block. The fix uses a
  fixed 64-pause spin with no clock reads instead.
- Added the `n > 1` gate on waking: a single-ticket job is taken by the caller itself, so waking
  workers for it only costs syscalls.

## Follow-ups

- TSan noise on the job-slot fields (pre-existing): consider relaxed atomics for `JobSlot` fields so
  the pool can be checked under TSan.
- Consider a small in-repo native test target for `AudioThreadPool` (the stress harness above).
