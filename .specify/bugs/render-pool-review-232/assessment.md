# Bug Assessment: render pool follow-ups from the #232 review

- **Slug**: render-pool-review-232
- **Created**: 2026-10-21
- **Source**: https://github.com/nota-daw/nota/pull/232 (host: github.com, allowlisted; review text pasted by the user)
- **Verdict**: valid
- **Severity**: high (exports and freezes regressed to one thread in develop; the rest is medium)

## Report (summarized)

Chris benchmarked develop (79e42a5) against #232 (62f905a) on Linux (4 cores) and a Mac mini M4.

1. **Blocking** — `AudioExporter` and `TrackFreezer` render 8192-frame chunks;
   `offline = !deviceBlock_ && pool_->backToBack()` is false for them, so they render on one
   thread (render threads used 0 ms of CPU). Mac, ms per second of audio: 16 synth tracks
   17.1 (develop) → 68.5 (PR) → 20.8 (fix); 64 audio tracks 62 → 276 → 78. Proposed fix:
   `offline = !deviceBlock_`.
2. **Should fix** — on macOS one `notify_one` woke 8 of 8 waiters (libc++ routes 32-bit atomic
   waits through a contention table); Linux woke 1 of 8. `dispatch()`'s k × `notify_one`
   wakes the whole pool on the Mac.
3. **Should fix** — late-block bursts every 2 s under overload: when `kRtHoldSeconds` runs out
   the engine drops to one thread to re-measure. Linux 40 tracks @ 64 frames: bursts of 24,
   12, 8, 16 late blocks at 0/2/4/6 s; Mac 256 tracks @ 128: 2 late blocks at every 2 s mark.
4. **Should fix** — the tests miss both: `RenderPoolTests` exports in 512-frame blocks, and
   on a 4-core runner the overload check compares 1125 late blocks with 1125 (`<=`), so it
   cannot fail.

## Symptom

Exports and freezes render on the audio-calling thread alone (up to 4.5x slower than
develop before #232). On macOS every dispatch that wakes any worker wakes all of them.
Under sustained live overload, a burst of late blocks (audible dropouts) every 2 s.

## Reproduction

1. Session with 16 instrument tracks; File → Export (or freeze a track).
2. Watch the render threads' CPU (`nota_engine_render_threads_cpu_seconds`): ~0.
3. Live: 256 tracks at a 64-frame buffer (more than one thread can render in time); count
   late blocks per 250 ms window: a burst at every 2 s mark.

## Suspected Code Paths

- `src/native/nota.engine/src/engine/Engine_Render.cpp:2200` — `offline` requires
  `backToBack()` (gap < 200 µs since the previous job). Between two 8192-frame chunks the
  serial rest of `processBlock`, the master chain and the encoder (FLAC) take longer, so
  the chunks fall into the device branch: `helpers = 0` until the single-thread cost passes
  the "buffer period" — 170 ms for 8192 frames, never reached.
- `src/native/nota.engine/src/engine/AudioThreadPool.h` — `std::atomic<uint32_t> wake_`.
  libc++ (`__atomic/contention_t.h`): `__cxx_contention_t` is `int32_t` on Linux and
  `int64_t` elsewhere; a non-native width waits on a shared table entry, whose notify wakes
  every waiter.
- `Engine_Render.cpp:2202–2245` + `Engine.h:1777–1795` — `rtParallel_` hold: when
  `rtParallelLeftNs_` runs out, `rtParallel_ = false` and `leafWorkNs_ = 0.85 × period`; the
  1/16 smoothing then needs several (mild overload: dozens of) late blocks before it passes
  the period again.
- `tests/Nota.SmokeTest/RenderPoolTests.cs` — `Block = 512` for bounces; check 3b asserts
  `prl.Late <= ser.Late` at a fixed 256 tracks.

## Root Cause Hypothesis

(1) `backToBack()` was meant to tell an overloaded device callback from a bounce, but
`renderOffline` already refuses to run while the device is running, so `!deviceBlock_` alone
is exact; the extra gap test only misclassifies chunked offline renders. (2) Wrong futex
word width for libc++ on Apple. (3) The re-measure step drops to one thread blindly, with an
optimistic seed and slow smoothing. Confidence: high for all three (confirmed in code and
the SDK's libc++ headers).

## Proposed Remediation

**Preferred**:

1. `offline = !deviceBlock_` (Chris's one-liner); drop the "back-to-back" wording from the
   comments. `backToBack()` stays for the linger decision inside `dispatch()`.
2. Futex word type: `int32_t` on Linux, `int64_t` elsewhere (the width libc++ waits on
   natively; MSVC's `WaitOnAddress` takes either).
3. Hold expiry without blind drops: while the pool runs, track the parallel leaves' summed
   task time (smoothed). That sum over-reads the single-thread cost (cold cores, scattered
   state), so it is a safe upper bound. When the hold runs out, leave the pool only if that
   bound fits within `kRtSizeLoad` of the buffer; otherwise renew the hold. After leaving,
   any block in the first stretch alone that misses its period rejoins at once (no
   smoothing) and doubles the hold (cap 32 s), so a wrong guess costs one late block and
   happens rarely. Helper count sizes from the larger of the two estimates, which also gives
   live playback back some headroom.
4. Tests: bounce in 8192-frame chunks with an encoder-like gap between them, and require
   the render threads to do a real share of the work; size the overload case per machine
   (one thread ~1.6x over the period, within the pool's capacity), run it past several hold
   expiries, and require late blocks to stay rare after warm-up — a check that fails on the
   current code.

**Alternatives**:
- Keep the blind re-measure but seed `leafWorkNs_` from the last measurement instead of
  0.85 × period: fewer late blocks per burst, still one burst every 2 s.
- Never leave the pool once joined until load drops below a fixed fraction: simplest, but
  burns CPU after a transient overload.

**Files likely to change**:
- `src/native/nota.engine/src/engine/Engine_Render.cpp`
- `src/native/nota.engine/src/engine/Engine.h`
- `src/native/nota.engine/src/engine/AudioThreadPool.h`, `AudioThreadPool.cpp`
- `tests/Nota.SmokeTest/RenderPoolTests.cs`

**Tests to add or update**:
- Chunked bounce (8192 frames, gap between chunks): render threads' CPU share > 25 %.
- Calibrated overload over ~6 s: few late blocks with the pool, none in bursts at the hold
  marks; one thread alone mostly late.

## Risks & Considerations

- Audio-thread code (AR-6): the change adds arithmetic only — no allocation, locks or
  blocking.
- The over-reading bound may keep the pool engaged somewhat longer after an overload ends
  (more CPU, no late blocks) — the deliberate trade.
- Timing checks go through `TimingBudget` (not enforced on shared CI runners); the render
  threads' CPU share is a functional check enforced everywhere.

## Open Questions

- Real-time priority / QoS for the render threads (review "minor") — out of scope here.
