---
description: "Task list for Phase 4 of nota-daw/nota#1050"
---

# Tasks: Track CoW (`TrackContent`), Session `ScratchArena`, `Eq` SIMD & Runtime Dispatch

## Phase 1: Core Abstractions (`TrackContent.h`, `ScratchArena.h`, `SimdDispatch`)

- [x] T001 Create `src/native/nota.engine/src/engine/TrackContent.h` (`CowSlice<T>`) with lock-free read view, storage token identity, copy/seal semantics, and explicit Copy-on-Write `.write()` detach
- [x] T002 Create `src/native/nota.engine/src/dsp/core/ScratchArena.h` (`StereoScratchArena<N>`) for aligned session-sized stereo scratch buffers (`2048` live frames, `kMaxBlock` offline cap)
- [x] T003 Create `src/native/nota.engine/src/dsp/math/SimdDispatch.h`, `SimdDispatch.cpp`, and `SimdAvx2.cpp` with CPUID feature probing and runtime AVX2+FMA / SSE2 / NEON dispatch, registered in `CMakeLists.txt` with `-mavx2 -mfma` per-TU on x86_64

## Phase 2: Copy-on-Write Track & Incremental Publish Integration (US1)

- [x] T004 Switch `Track`'s heavy snapshot vectors (`clips`, `takes`, `midiClips`, `sessionSlots`, `automation`, `modulators`, `cvLinks`) in `Track.h` to `CowSlice<...>` and update mutating call sites across `Engine*.cpp` to use `.write()`
- [x] T005 Update `TimelineIndex.h/.cpp` with backing storage tokens (`matchesTrack`), preserve `timelineIndex` across `cloneTrack()`, seal snapshots in `publishRaw()`, skip no-op `recomputeRouting()` / `applyTelemetryMode()` work, and optimize `recomputePdc()` in `Engine_Devices.cpp`

## Phase 3: Session-Sized Arenas, Eq Cache/SIMD & Compiler Tuning (US2 + US3)

- [x] T006 Migrate `RackDevice` (`RackDevice.h/.cpp`) and `RackInstrument` (`RackInstrument.h/.cpp`, `DrumRack.cpp`) to `StereoScratchArena`, and update `Engine::ensureRenderScratch()` / `propagateSampleRate()` / `renderOffline()` for session-sized `leafBuf` & capacity negotiation
- [x] T007 Upgrade `Eq.h` & `Eq.cpp` with per-band parameter/coefficient cache, SIMD `peakAbs` pre-pass + compact gain/ring pass, and vectorized `double2` stereo flat-path (`float64x2_t` NEON / `__m128d` SSE2)
- [x] T008 Enable `-fno-math-errno` and verify Release ThinLTO (`CMAKE_INTERPROCEDURAL_OPTIMIZATION`) compatibility in `CMakeLists.txt`

## Phase 4: Verification, Tests, Changelog & Benchmarks

- [x] T009 Add `tests/Nota.SmokeTest/Phase4CowArenaSimdTests.cs` (wired in `Program.cs`) verifying CoW multi-edit isolation + `0` vs `4` thread parity, live-to-offline `ScratchArena` transition (`128` -> `8192` frames), `Eq` static/gliding/MS scalar reference parity (`< -120 dBFS`), and `SimdDispatch` verification
- [x] T010 Update `CHANGELOG.md` `[Unreleased]` with Phase 4 entry
- [x] T011 Run `./scripts/build.sh`, `-- --perf`, `nota_idle_cpu`, Phase 4 CoW/Eq microbenchmark, and `./scripts/bench-engine.sh -r 3`; mark tasks done (`/speckit-converge`)
