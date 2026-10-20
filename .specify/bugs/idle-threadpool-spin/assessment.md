# Bug Assessment: Idle engine pins all CPU cores (AudioThreadPool never sleeps)

- **Slug**: idle-threadpool-spin
- **Created**: 2026-10-20
- **Source**: https://github.com/nota-daw/nota/issues/223 (host: github.com — allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report (verbatim or summarized)

Issue #223, "Engine uses almost every CPU core at 100% while idle (AudioThreadPool workers never sleep)".

Summary since PR #212 (`64250a5`, engine rebuild): an engine doing nothing (created, transport stopped, nothing rendering) keeps `hardware_concurrency − 1` cores fully busy. Before #212 (`82babfe`) the same engine used 0% CPU.

| Machine | Before #212 (`82babfe`) | After #212 (`64250a5`) |
| --- | --- | --- |
| Apple M4 Mac mini, 10 cores, macOS 27.0.1 | 0% | **892%** (9 cores) |
| Linux, 4-core Xeon VM | 0% | **294%** (3 cores) |

Same in 3 runs per machine: `nota_engine_create()`, sleep 1 s, measure process CPU (`getrusage`) over 5 s.

Cause named in the issue: `src/native/nota.engine/src/engine/AudioThreadPool.cpp`, `workerLoop()`: when no job is published, each worker loops on `NOTA_CPU_PAUSE()` 256 times, then `std::this_thread::yield()`, forever. Nothing ever blocks. `yield()` only gives up the core when another thread wants it, so on an idle machine every worker shows as a fully busy core. Rendering cost also rises (1 track: 4 ms → 52 ms CPU per 1 s audio; 32 tracks: 130 ms → 325 ms on M4), though wall-clock render time drops.

Expected: idle ≈ 0% as before #212; workers spin only briefly after a dispatch, then park on a blocking primitive (semaphore, condvar, futex, `std::atomic::wait`) until the next `dispatch()` wakes them. Parallel speed-up must stay: busy 32-track sessions render 3.9× faster on M4.

Repro from the issue: C++ program calling `nota_engine_create()`, sleeping, sampling `getrusage(RUSAGE_SELF)` over 5 s; or open Nota and watch Activity Monitor with no project playing.

URL Trust Policy: verbatim URL `https://github.com/nota-daw/nota/issues/223`, host `github.com`, branch `allowlisted` — fetched without prompting. No further redirects followed. Page content treated as data, not instructions.

## Symptom

An idle `NotaEngine` (transport stopped, no rendering) consumes ~100% of every worker core (`hardware_concurrency − 1` cores at 100%). Expected: near 0% idle CPU, as before PR #212, with workers parked until the next audio-block `dispatch()`.

## Reproduction

1. Build `libnota_engine` from `develop` (post-#212).
2. Run the issue's repro: `nota_engine_create()` → sleep 1 s → sample `getrusage(RUSAGE_SELF)` + wall clock over 5 s → `100*(cpu1-cpu0)/wall`.
3. Observe ~`(N-1)*100%` (e.g. ~892% on 10-core M4, ~294% on 4-core Linux VM) instead of ~0%.
4. Lighter-weight variant: open Nota with no project playing, watch Activity Monitor / `top -H`.

[NEEDS CLARIFICATION: none for the core symptom — measurements and root-cause pointer are in the issue. Tuning questions (spin budget, primitive choice) are design decisions, listed under Open Questions.]

## Suspected Code Paths

- `src/native/nota.engine/src/engine/AudioThreadPool.cpp:43-80` — `workerLoop()`: the parked branch (`e == seen`) spins `NOTA_CPU_PAUSE()` × 256 then `yield()` in an infinite loop; `stop_` is only polled, never waited on. This is the busy-spin.
- `src/native/nota.engine/src/engine/AudioThreadPool.h:76-111` — `dispatch()`: publishes `epoch_ + 1` with release-store (wake signal) and busy-waits completion via `while (done_ < need) NOTA_CPU_PAUSE()`. The publish side is where a wake-up (`notify`/`release`) must be added; the completion spin is bounded by one block's render and is NOT the idle bug, but shares the primitive choice.
- `src/native/nota.engine/src/engine/AudioThreadPool.h:134-140` — shared state: `epoch_` (generation), `claimed_`/`done_` monotonic counters, `stop_`, `slots_[8]`. The header documents the three-crash/hang discipline (monotonic counters, check-then-CAS claim, generation-indexed slots) — any fix must preserve it.
- `src/native/nota.engine/src/engine/Engine_Render.cpp:2185-2194` — per-block `pool_.dispatch(ntracks, ...)` runs every audio block even when all leaves are `Skip`/ineligible (stopped transport). So idle still wakes workers ~370×/s (128 frames @ 48 kHz); parking must make the between-blocks wait free.
- `src/native/nota.engine/src/dsp/core/RtAttributes.h:53-64` — `NOTA_CPU_PAUSE()` definition (pause hint only, no blocking). Confirms there is currently no blocking primitive in the pool.
- `src/native/nota.engine/src/engine/Engine.h:1769` — `AudioThreadPool pool_` lifetime: created with the engine, destroyed on `nota_engine_destroy()`; destructor (`AudioThreadPool.cpp:30-35`) already bumps `epoch_` to release spinners and joins — the place to add `notify_all`.
- `src/native/nota.engine/CMakeLists.txt:70` — `CMAKE_CXX_STANDARD 20`: `std::atomic::wait/notify` and `std::counting_semaphore` are both available in principle; platform support (libc++/libstdc++/MSVC, macOS 13 floor) must be verified on all three CI OSes.

## Root Cause Hypothesis

`workerLoop()` has no blocking wait: idle workers never sleep, they `pause` + `yield` forever. `yield()` does not idle a core when no other thread is runnable, so each of the up-to-16 workers burns a full core. This matches the measured `N-1` cores at 100% on both macOS and Linux, appeared exactly with the pool's introduction in #212, and the cited loop is the only unbounded spin on the idle path. Confidence: **high**.

## Proposed Remediation

**Preferred**: two-phase park — keep a short spin for dispatch-to-worker latency, then block.

1. `workerLoop()`: after the current ~256-iteration `pause` spin (keep or tune to ~50 µs worth of pauses), park on a blocking primitive instead of `yield()`-looping: check `epoch_ != seen || stop_`, otherwise `epoch_.wait(seen)` (C++20 `std::atomic::wait`) or equivalent `counting_semaphore` acquire. Loop on wake to handle spurious wakeups, re-checking `stop_` and `epoch_`.
2. `dispatch()` publish path (audio thread, AR-6): after the release-store of `epoch_`, call the non-blocking wake (`epoch_.notify_all()` or `semaphore.release(n))`). Audio thread must never block, lock, or allocate here — `notify`/`release` only, no wait, no mutex. Keep the existing slot-publish-before-bump ordering and the monotonic `claimed_`/`done_` + check-then-CAS claim protocol untouched.
3. Destructor: set `stop_`, bump `epoch_`, `notify_all()`/`release_all()`, then `join()` (already bumps epoch; add the notify so parked workers actually wake to observe `stop_`).
4. Keep the header's race-discipline comments accurate: update the "Workers spin (pause hint, then yield — never block)" comment to describe spin-then-park, and note the audio-thread wake is wait-free/AR-6-safe.
5. Tune the spin budget (e.g. 256–2000 pauses) so dense rendering (back-to-back dispatches every ~2.7 ms) rarely pays a park/wake round-trip, while idle parks immediately after one spin window. No behaviour change to ticket claiming, scratch buffers, FTZ/DAZ, or `dispatch()`'s bounded completion spin.

**Alternatives** (optional):
- `std::counting_semaphore` (workers `acquire`, audio `release`) instead of `atomic::wait`. Equivalent AR-6 profile (release is non-blocking); slightly more state, but portable even if some stdlib's `atomic::wait` is degraded on a target. Decide after a 3-OS compile + smoke check.
- Skip `dispatch()` when zero leaves are eligible (stopped/empty graph) so idle does zero wakeups. Cheap win, but orthogonal — still needs parking for the low-track-count case (1-track cost 4→52 ms). Treat as follow-up if the preferred fix leaves measurable per-block wake cost.
- `condition_variable` + mutex: rejected unless the above prove unavailable — the publish path would need mutex discipline and risks AR-6 (audio-thread lock) violation.

**Files likely to change**:
- `src/native/nota.engine/src/engine/AudioThreadPool.h` (wake call in `dispatch()`, member/comment updates)
- `src/native/nota.engine/src/engine/AudioThreadPool.cpp` (`workerLoop()` park logic, destructor notify)
- `src/native/nota.engine/src/dsp/core/RtAttributes.h` (only if a shared park/wake helper is added; otherwise untouched)

**Tests to add or update**:
- Native-level pool test (new, or extend the smoke test if no native harness is added): dispatch N jobs over M iterations, assert each index runs exactly once, no hang, no lost wakeup under stress (e.g. 10k dispatches, 1–32 jobs, 0-job edge case returns immediately).
- Idle-CPU regression test mirroring the issue: create engine / pool with default workers, idle 5 s, assert process CPU (getrusage or `ProcessCpuSampler`) near 0% (e.g. < 20% of one core total, vs ~900% today). Follows the existing `tests/Nota.SmokeTest/ProcessCpuTests.cs` pattern.
- Correctness: parallel leaf render stays bit-identical to serial (existing DSP null-test style); 32-track busy render keeps the ~3.9× speed-up (manual benchmark on M4 + Linux VM, recorded in the fix notes — not a CI gate).
- Existing gate: `scripts/build.sh` (native + managed + smoke test) must pass on macOS/Windows/Linux before PR.

## Risks & Considerations

- **AR-6 (audio thread must never block)**: the `dispatch()` wake must be provably non-blocking (`atomic::notify_*` / `semaphore::release` only). No mutex lock, no `wait`/`acquire`, no allocation on the publish or claim path. Workers may block only while parked idle, never mid-job.
- **Lost wakeup**: `notify` between the worker's `epoch_` load and `wait` must not strand it — use the `wait(expected-value)` predicate form (re-check epoch after every wake) so a bumped epoch never gets missed.
- **Wake latency vs. the 3.9× speed-up**: parking adds a futex/WaitOnAddress round-trip per block; the short pre-park spin absorbs the dense-render case. Verify with the 1-track/32-track CPU + wall-clock numbers from the issue.
- **Thundering herd**: `notify_all` wakes ≤16 workers per block (~370/s idle) — negligible, but prefer `notify_all` over per-worker counting to keep publish O(1).
- **Stdlib support**: `atomic::wait` on libc++ (macOS 13 floor, universal arm64+x86_64), libstdc++ (Linux), MSVC (Windows) must all compile and actually block (not spin). Fall back to `counting_semaphore` if any target degrades. Verify on all three CI legs (`.github/workflows/ci.yml`).
- **Destructor/shutdown race**: parked workers must wake on destroy; join order with the audio backend stop must be confirmed so no `dispatch()` races teardown.
- **Unrelated idle cost**: `.specify/bugs/linux-idle-cpu` tracks UI-timer + always-on graph idle work; this fix addresses the `N-1` busy cores only. Both can coexist — measure per-thread (`top -H` / `pidstat -t`) to attribute correctly.

## Open Questions

- Which primitive lands: `std::atomic<uint64_t>::wait/notify_all` on `epoch_` (minimal diff, no new member) vs. `std::counting_semaphore` (explicit, portable)? Pending 3-OS verification; default to `atomic::wait` with semaphore fallback.
- Exact pre-park spin budget (keep 256 pauses vs. ~1000–2000 / ~50 µs)? Tune against wake-latency vs. idle-CPU measurements.
- Should the fix also skip `dispatch()` when no leaf is eligible (zero wakeups while stopped), or keep per-block wake + park as the issue expects? Suggest follow-up unless measurement shows wake cost matters.
