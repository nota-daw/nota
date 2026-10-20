# Bug Assessment: Idle engine keeps hardware_concurrency − 1 cores busy (AudioThreadPool never parks)

- **Slug**: idle-pool-spin
- **Created**: 2026-10-20
- **Source**: https://github.com/nota-daw/nota/issues/223 (host: github.com — allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report (summarized)

Issue #223, "Engine uses almost every CPU core at 100% while idle (AudioThreadPool workers never
sleep)". Since #212 (`64250a5`), an engine that was created and does nothing (transport stopped)
burns `hardware_concurrency − 1` cores: 892 % on a 10-core M4 Mac mini, 294 % on a 4-core Linux VM;
0 % before #212 (`82babfe`). Measured with `nota_engine_create()`, 1 s sleep, then `getrusage` over
5 s. While rendering, total CPU work per second of audio also rises (1 track: 4 → 52 ms; 32 tracks:
130 → 325 ms on M4), although wall-clock render time drops (32 tracks render 3.9× faster). Expected:
idle ≈ 0 % CPU; workers spin briefly after a dispatch, then park on a blocking primitive until the
next `dispatch()` wakes them; keep the parallel speed-up.

## Symptom

With the engine idle, every AudioThreadPool worker shows as a fully busy core (fans, heat, battery
drain, competition with the audio callback thread). Expected: close to 0 % CPU when nothing renders.

## Reproduction

1. Build the issue's snippet against `libnota_engine` from `develop`.
2. Run it: it calls `nota_engine_create()`, sleeps 1 s, measures process CPU over 5 s.
3. Observe ≈ (hw − 1) × 100 % instead of ≈ 0 %. Alternatively open Nota and watch Activity Monitor.

## Suspected Code Paths

- `src/native/nota.engine/src/engine/AudioThreadPool.cpp:workerLoop()` — when `epoch_ == seen` the
  worker does 256 × `NOTA_CPU_PAUSE()` then `std::this_thread::yield()`, forever; nothing blocks.
  `yield()` only gives the core away when another thread is runnable.
- `src/native/nota.engine/src/engine/AudioThreadPool.h:dispatch()` — publishes by storing `epoch_`
  only; there is no wake-up path for a parked worker (the destructor comment "wake sleepers" refers
  to sleepers that do not exist).
- `src/native/nota.engine/src/engine/Engine_Render.cpp:2186` — the only `dispatch()` call, once per
  audio block, so the gap between jobs is a whole block period (~10 ms at 512 @ 48 kHz) — all of it
  spent spinning, which also explains the extra CPU work while rendering.

## Root Cause Hypothesis

Confidence: high. The pool introduced in #212 is spin-only by design (header comment: "Workers spin
(pause hint, then yield — never block on a mutex)"). Spinning across the whole inter-block gap and
across idle time costs one core per worker.

## Proposed Remediation

**Preferred**: two-phase wait in `workerLoop()`. After finishing (or seeing no) work, spin with
`NOTA_CPU_PAUSE()` for a short, time-bounded window (~50 µs, checked against `steady_clock` every few
dozen pauses), then park on a C++20 `std::atomic<uint32_t>::wait()` futex word. Parking protocol
(Dekker-style, all `seq_cst`): the worker reads the wake word, increments `sleepers_`, re-checks
`epoch_`/`stop_`, and only then `wait()`s on the wake word; `dispatch()` stores `epoch_` and, only if
`sleepers_ > 0`, bumps the wake word and calls `notify_all()`. Either the worker sees the new epoch
or the dispatcher sees the sleeper, so no wake-up is lost, and a hot render (workers still in their
spin window) costs no syscall. The audio thread never blocks: `notify_all` is a non-blocking futex /
`__ulock_wake` / `WakeByAddressAll`. A 32-bit wake word keeps the wait on the native futex size on
every platform (libstdc++ proxies 64-bit waits). The destructor always bumps and notifies.

**Alternatives**:
- `std::counting_semaphore` released W times per dispatch — simpler, but `release()` on libstdc++/MSVC
  can take internal paths that are heavier than a single wake, and releasing per worker every block
  costs more than one `notify_all` gated on sleepers.
- Keep spinning but `sleep_for` after N yields — cuts idle CPU but adds ms-scale wake latency that
  breaks the render speed-up; rejected.

**Files likely to change**:
- `src/native/nota.engine/src/engine/AudioThreadPool.h`
- `src/native/nota.engine/src/engine/AudioThreadPool.cpp`

**Tests to add or update**:
- Idle-CPU check: create the engine, measure process CPU over a few seconds, expect ≈ 0 % (the
  issue's snippet as a throwaway harness).
- Correctness: many dispatches with gaps longer than the spin window (forcing park/wake each time)
  plus back-to-back dispatches — every index runs exactly once, no hang; destroy while parked.
- `scripts/build.sh` smoke test must still pass (end-to-end render).

## Risks & Considerations

- Wake latency (~5–50 µs) is now paid once per block when the gap exceeds the spin window; the
  caller starts claiming tickets immediately, so the block still completes, only with less help for
  the first microseconds. Large sessions should keep most of the speed-up.
- Lost-wakeup races would hang the audio thread's final `done_` wait only if a ticket were never
  claimed — but the caller claims every unclaimed ticket itself, so a missed wake degrades to
  serial rendering, never a hang.
- AR-6: `notify_all` is a syscall on the audio thread, but non-blocking and lock-free; it is skipped
  whenever no worker is parked.

## Open Questions

- None blocking. Spin window length (~50 µs) is a tunable; can be revisited with the issue's
  render benchmark.
