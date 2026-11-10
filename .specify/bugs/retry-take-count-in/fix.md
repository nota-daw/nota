# Bug Fix: Discard and retry ignores count-in

- **Slug**: retry-take-count-in
- **Fixed**: 2026-11-10
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The audio thread now shuts the capture gate itself when it starts a count-in. Before, a
retried take queued a Stop ahead of the count-in, and the Stop's `endCountIn()` reopened the
gate that `startCountIn()` had already shut on the message thread. With the gate open, the
hardware input recorded the count-in into the take.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | `TransportPlay` count-in branch: `captureGate_.store(false)` |
| `src/native/nota.engine/src/engine/Engine.h` | added | `inputBlockForTest()`: one block through `onInputBlock` (the real input-thread path) |
| `src/native/nota.engine/include/nota/nota_engine.h`, `src/abi/nota_engine_track.cpp` | added | `nota_engine_input_block_test` |
| `src/managed/Nota.Infrastructure/Interop/NativeMethods.cs`, `NotaEngine.cs` | added | `InputBlockTest` wrapper |
| `tests/Nota.SmokeTest/RetryTakeTests.cs` | added test | `AudioFromStopWithCountIn` + `LiveBeats` helper |
| `CHANGELOG.md` | modified | `[Unreleased]` → Fixed entry |

## Diff Highlights

```cpp
case CommandType::TransportPlay:
    if (c.i0 > 0 && !transport_.isPlaying()) { // count-in first (i0 = bars)
        ...
        captureGate_.store(false, std::memory_order_release);
```

## Tests Added or Updated

- `RetryTakeTests.AudioFromStopWithCountIn`: an audio track on a 2-channel test input, a
  1-bar count-in from beat 8, retried two beats into the take. Input blocks go through the
  real `onInputBlock` gate before every render block. The kept clip must start at beat 8 and
  be about 2 beats long.

## Local Verification

- `Nota.SmokeTest --retry-take` with the engine change reverted: the new check **fails**
  (`at 8,00, 6,01 beats, want 2`), which reproduces the bug.
- `Nota.SmokeTest --retry-take` with the fix: `RETRY TAKE PASSED` (`at 8,00, 2,02 beats`).
- `scripts/build.sh` (full build + smoke test): see test.md.

## Deviations from Assessment

None.

## Follow-ups

- None.
