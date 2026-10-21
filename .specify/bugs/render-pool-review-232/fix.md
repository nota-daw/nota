# Bug Fix: render pool follow-ups from the #232 review

- **Slug**: render-pool-review-232
- **Fixed**: 2026-10-21
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Exports and freezes render on every render thread again (`offline = !deviceBlock_`); the
pool's futex word has the width libc++ waits on natively, so `notify_one` wakes one worker
on macOS; under live overload the pool no longer drops to one thread blindly every 2 s.
Tests now bounce in the exporter's 8192-frame chunks and size the overload per machine,
with checks that fail on the old behaviour. Separately, the nota-pr skill no longer
hardcodes the maintainer's git identity.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | `offline = !deviceBlock_`; hold expiry leaves the pool only when the summed parallel task time fits the buffer; probe window after leaving rejoins on the first late block and doubles the hold |
| `src/native/nota.engine/src/engine/Engine.h` | modified | `parWorkNs_`, `rtProbeLeftNs_`, `rtHoldNs_`, `kRtMaxHoldSeconds`, `kRtProbeSeconds`; comment rewritten |
| `src/native/nota.engine/src/engine/AudioThreadPool.h` | modified | `WakeWord` = `int32_t` on Linux, `int64_t` elsewhere; a job that wants every thread (offline) wakes them with one `notify_all` |
| `src/native/nota.engine/src/engine/AudioThreadPool.cpp` | modified | `park()` uses `WakeWord` |
| `tests/Nota.SmokeTest/RenderPoolTests.cs` | updated tests | 3b calibrated overload over 6 s with burst check; new 4b chunked export; `Measure` gains `gapMs` and `lateAt` |
| `scripts/bench/engine-bench.cpp`, `scripts/bench-engine.sh`, `scripts/bench-chart.py` | modified | `export` mode: 8192-frame chunks with 2 ms between them; three export scenarios; export charts (for the PR benchmark) |
| `.claude/skills/nota-pr/SKILL.md`, `.agents/skills/nota-pr/SKILL.md` | modified | commit under the contributor's own identity (scope expansion requested by the user) |

## Diff Highlights

```cpp
const bool offline = !deviceBlock_;
...
if (rtParallel_ && (rtParallelLeftNs_ -= periodNs) <= 0) {
    if (parWorkNs_ <= periodNs) { rtParallel_ = false; leafWorkNs_ = parWorkNs_; rtProbeLeftNs_ = kRtProbeSeconds * 1e9; }
    else rtParallelLeftNs_ = rtHoldNs_; // still over: stay, no blind drop
}
```

## Tests Added or Updated

- `RenderPoolTests` 3b — overload sized to ~1.3 buffer periods on one thread; with the pool:
  ≤ 1 % late blocks after warm-up and ≤ 4 late blocks within any 50 ms (timing budgets).
- `RenderPoolTests` 4b — 16 tracks bounced in 8192-frame chunks with 2 ms between them:
  render threads do ≥ 20 % of the work (functional, enforced on CI too); wall time not
  slower than one thread (budget).

## Local Verification

- Standalone libc++ check on this Mac (Apple clang, macOS 26): one `notify_one` woke 8 of 8
  waiters on `std::atomic<int32_t>`, 1 of 8 on `std::atomic<int64_t>`.
- Build and smoke results: see test.md.

## Deviations from Assessment

- The leave threshold is the buffer period, not `kRtSizeLoad` of it: the summed parallel
  time over-reads, so a bound under one period already means real headroom, and the probe
  catches the rare case where it under-reads.
- Added while benchmarking: an offline job (`helpers < 0`, every thread wanted) wakes the
  pool with one `notify_all` instead of k `notify_one` syscalls on the caller's path. It
  was first suspected for a slower 8-track 512-frame bounce; more runs showed that case is
  bimodal (6.0 or 8.5–9.3 ms per second of audio) on develop and on this branch alike, so
  the change is kept for its cost argument, not for a measured gain.
- Scope expansion: the bench harness gains an `export` mode for the PR's evidence.
- Scope expansion: the nota-pr skill identity (requested by the user in the same turn).

## Follow-ups

- Real-time priority / QoS for the render threads; `PluginCatalog::addType` O(N²) sync;
  `renderThreads()` vs `setRenderThreads()` race; stale "message thread only" comments
  (review "minor" items, not in this fix).
