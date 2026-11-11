# Bug Fix: Monitoring an instrument track through an audio track crackles

- **Slug**: 563-monitor-crackle
- **Fixed**: 2026-11-11
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

A monitoring audio track now reads its source track's post-fader tap from a per-slot ring at a
fixed lag, instead of copying "the previous segment, `frames` long" out of the double-buffered
route bus. Segments of varying length (a rate-converting device, a loop seam) no longer read
stale frames or drop any, so the monitored signal is continuous.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Engine.h` | modified | `trackTapRing_` (one stereo ring per route slot, 2 × `kMaxBlock` frames), owner per slot, write position, `monitorLag_` + its one-second window; `advanceMonitorLag`, `publishTrackTapRings` |
| `src/native/nota.engine/src/engine/Engine.cpp` | modified | rings allocated in the constructor (no audio-thread allocation) |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | `renderMonitorInput` reads the ring `monitorLag_` frames behind; `advanceMonitorLag` before Pass 1; `publishTrackTapRings` appends each whole-track slot's post tap before the route-bus swap; a slot that changes source starts from silence |
| `tests/Nota.SmokeTest/TrackMonitorTests.cs` | added test | fixed / varying / long-then-short block patterns |
| `tests/Nota.SmokeTest/Program.cs` | modified | `--track-monitor` alone, and in the full run |
| `CHANGELOG.md` | modified | `[Unreleased] → Fixed` entry |

## Diff Highlights (optional)

```cpp
// renderMonitorInput: the source's post-fader tap, monitorLag_ frames behind.
const int64_t from = trackTapRingPos_ - monitorLag_;
for (int32_t i = 0; i < frames; ++i) {
    const size_t k = static_cast<size_t>((from + i) & mask) * 2;
    dst[i * 2] = ring[k];
    dst[i * 2 + 1] = ring[k + 1];
}
```

The lag grows at once to the longest segment and shrinks to the longest segment of the last
second, so an export's 8192-frame chunks or a larger buffer don't keep their latency for good.

## Tests Added or Updated

- `TrackMonitorTests` (`--track-monitor`) — an audio track soloed while monitoring a sine clip on
  another track:
  - fixed 512-frame blocks: smooth (worst second difference) and exactly one block behind;
  - blocks of 470/471/470/333/512/128/1024/471: smooth and at a fixed lag (the longest block);
  - 4096-frame chunks for 0.5 s, then 256-frame blocks: the lag is back to 256 and smooth.

## Local Verification

- `cmake --build src/native/nota.engine/build` + `dotnet build tests/Nota.SmokeTest -c Release` → OK.
- `Nota.SmokeTest.dll --track-monitor`, engine **without** the fix (dylib copied into `bin/`):
  varying blocks FAIL — worst jump 0.389, correlation with the delayed source 0.445.
- Same, **with** the fix: all 5 PASS — worst jump 0.000442 (the sine's own), correlation 1.000.
- `scripts/build.sh` (full build + smoke run): see `test.md`.

## Deviations from Assessment

- Ring capacity is per slot for whole-track slots only (part slots never feed a monitor); the
  owner check is by track id.
- The armed-not-monitoring input meter (#352) still reads `routeBus_`: a meter, not heard.

## Follow-ups

- Sidechain keys and that input meter share the per-segment route-bus contract; a key read
  across varying segments sees the same stale frames (a detector smooths it). Worth its own issue.
