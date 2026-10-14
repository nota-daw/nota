# Bug Fix: Engine::start() re-prepares live nodes while the audio thread renders

- **Slug**: engine-start-prepare-race
- **Fixed**: 2026-10-14
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

`Engine::start()` now suspends rendering before the backend starts and resumes it only after
every node is re-prepared at the device rate; `renderOffline()` refuses to run while the
backend is live. No locks or allocation on the audio thread.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/Engine.h` | modified | `std::atomic<bool> renderSuspended_` |
| `src/native/nota.engine/src/Engine.cpp` | modified | `start()`: release-store `true` before `backend_->start()`, `false` after `propagateSampleRate` / `reconfigureAllWarpStreams` / `recomputePdc` (also on the failure path) |
| `src/native/nota.engine/src/Engine_Render.cpp` | modified | `render()`: acquire load; while suspended, zero `numFrames * 2` and return. `renderOffline()`: if `isRunning()`, zero the output and return |
| `CHANGELOG.md` | modified | `[Unreleased]` → Fixed entry |

## Diff Highlights

```cpp
renderSuspended_.store(true, std::memory_order_release);
const bool ok = backend_->start(...);
if (ok) { /* propagateSampleRate, reconfigureAllWarpStreams, recomputePdc */ }
renderSuspended_.store(false, std::memory_order_release);
if (ok) openMidiInput();
```

`openMidiInput()` moved after the clear (it doesn't touch the graph; order otherwise unchanged).

## Tests Added or Updated

None. The smoke test runs without an audio device, so the live race can't be driven in CI.
The offline path is unchanged when the backend is stopped, which the whole suite covers.

## Local Verification

- `scripts/build.sh` (macOS) → exit 0, `SMOKE TEST PASSED`.
- Not exercised on a live device or on Windows.

## Deviations from Assessment

None.

## Follow-ups

- Manual check on Windows with a hosted plug-in: export, freeze, and Preferences → Audio →
  Apply, each with the project playing a plug-in, then confirm audio resumes.
