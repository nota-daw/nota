# Implementation Plan: Phase 4 (`specs/117-track-cow-arena-simd`)

**Branch**: `perf/track-cow-arena-simd` | **Spec**: [spec.md](spec.md)

## Architecture & Module Breakdown

1. **`src/native/nota.engine/src/engine/TrackContent.h`** + `Track.h`, `Engine.cpp`, `TimelineIndex.h/.cpp`:
   - Implement `nota::CowSlice<T>` Copy-on-Write container.
   - Switch `Track`'s heavy snapshot vectors (`clips`, `takes`, `midiClips`, `sessionSlots`, `automation`, `modulators`, `cvLinks`) to `CowSlice<T>`.
   - Update only genuine mutating call sites across `Engine*.cpp` to call `.write()`; keep read loops cleanly unchanged.
   - Preserve `TimelineIndex` in `cloneTrack()` when `clips`/`midiClips`/`articulationTable` backing pointers stay identical (`TimelineIndex::matchesTrack`), avoiding index rebuilds on automation/session/device edits.
   - Add incremental optimizations in `Engine::publishRaw()` (compare route keys in `recomputeRouting()`, skip telemetry propagation on unchanged shared tracks) and single-pass `Engine::recomputePdc()`.

2. **`src/native/nota.engine/src/dsp/core/ScratchArena.h`** + `RackCore.h/.cpp`, `RackDevice.h/.cpp`, `RackInstrument.h/.cpp`, `DrumRack.cpp`, `Engine.h`, `Engine.cpp`, `Engine_Render.cpp`:
   - Replace fixed `16384`-frame inline arrays in `RackDevice` (`384 KB`) and `RackInstrument` (`128 KB`) with `StereoScratchArena` defaulting to `2048` live frames (`8×` smaller) and growing up to `kMaxBlock` (`16384`) on offline preparation.
   - Extend `Engine::ensureRenderScratch(size_t tracks, int32_t frameCapacity)` so live sessions allocate `2048`-frame `leafBuf` (`32 KB`/track vs `256 KB`/track) while `renderOffline()` prepares larger capacity safely prior to rendering.

3. **`src/native/nota.engine/src/devices/effects/Eq.h` & `.cpp`** + `BiquadCascade.h`:
   - Add per-band cached inputs & `BiquadCoeffs` (`BandCache`) eliminating redundant transcendental math when bands are static.
   - Add vectorized `double2` stereo flat-path runner (`float64x2_t` on ARM64 NEON, `__m128d` on x86_64 SSE2/FMA) with fused SIMD `peakAbs` + compact stereo gain/ring passes.

4. **`src/native/nota.engine/src/dsp/math/SimdDispatch.h`, `SimdDispatch.cpp`, `SimdAvx2.cpp`** + `CMakeLists.txt`:
   - Runtime CPU feature detection & dispatch (AVX2+FMA on x86_64 compiled per-TU, SSE2 fallback, NEON on ARM64).
   - Enable `-fno-math-errno` and `CMAKE_INTERPROCEDURAL_OPTIMIZATION` (ThinLTO on Release when supported and safe with static JUCE/exports) in `CMakeLists.txt`.

5. **Verification**:
   - `tests/Nota.SmokeTest/TrackSnapshotArenaSimdTests.cs` (`Program.cs`), `CHANGELOG.md`, full `./scripts/build.sh`, `-- --perf`, `nota_idle_cpu`, CoW/Eq microbenchmark, and `./scripts/bench-engine.sh -r 3`.
