# Research Findings: Phase 4 (`specs/117-track-cow-arena-simd`)

## 1. Track Vector Copying Hotspots (`cloneTrack` & `publishRaw`)
Audit of `src/native/nota.engine/src` identified ~645 sites referencing `Track`'s heavy containers across 15 translation units (`Engine_Tracks.cpp`: 209, `Engine_Session.cpp`: 97, `Engine_Automation.cpp`: 79, `Engine_Modulation.cpp`: 52, `Engine_Takes.cpp`: 48, `Engine_Recording.cpp`: 33, `Engine.cpp`: 25, `Engine_Render.cpp`: 24, `Engine_Rack.cpp`: 18, etc.).
- Every edit clones the full track via `Engine::cloneTrack()` (`Engine.cpp:799-844`), unconditionally copying all 7 vectors by value.
- By introducing `CowSlice<T>` in `TrackContent.h` with `const`-only iteration/indexing (`empty()`, `size()`, `operator[]`, `begin()`, `end()`, `data()`, `read()`) and explicit `.write()` for detaching only when `!mutable_ || data_.use_count() > 1`, read paths compile untouched while the C++ compiler statically enforces every mutation boundary across all 15 files.
- Storing `(clipsStorage_, midiClipsStorage_, artTableStorage_)` tokens inside `TimelineIndex` lets `cloneTrack()` preserve `timelineIndex` for automation/modulator/session/device edits and rebuild only when clip or articulation data actually changes (`O(1)` reuse).

## 2. Incremental Publish & Conditional `recomputeRouting` / `recomputePdc`
- `Engine::publishRaw()` (`Engine.cpp:973-1060`) currently runs `recomputeRouting()` unconditionally on every edit (clobbering `routeSlotCount_` atomics and bumping `routingEpoch_`), applies `applyTelemetryMode()` to every track in the graph, and runs `recomputePdc()` after structural edits even when latencies match existing compensation delays.
- `recomputeRouting()` can compute candidate `(trackId, part)` slots into small stack arrays and skip resetting atomics / bumping `routingEpoch_` unless the route table actually changed.
- `applyTelemetryMode()` in `publishRaw()` can skip tracks whose `shared_ptr<Track>` already existed in `authoring_` when `telemetryMode()` did not change.
- `recomputePdc()` currently computes `trackLatency(*t)` twice per track (inside max loop + delay update loop) and calls `pdc->setDelay` unconditionally; caching track latencies once per track in a local buffer eliminates duplicate virtual traversals.

## 3. Session-Sized `StereoScratchArena`
- `RackDevice` currently embeds `float input_[kMaxBlock * 2]`, `acc_[kMaxBlock * 2]`, `scratch_[kMaxBlock * 2]` (384 KB inline per instance); `RackInstrument` embeds `float scratch_[kMaxBlock * 2]` (128 KB inline per instance).
- Live audio hardware callbacks typically use 64–2048 frames; `StereoScratchArena` sizing to `2048` frames live (`16 KB` per buffer) reduces per-rack footprint by `8x` and improves L2/L3 cache residency.
- Because `RenderOffline` runs outside the live device callback, `Engine::renderOffline()` calls `ensureScratchFrameCapacity(frames)` / `ensureRenderScratch(tracks, frameCapacity)` before entering parallel mix dispatch so both `RenderScratch::leafBuf` and `RackDevice`/`RackInstrument` scratch arenas remain lock/alloc-free inside worker threads and live callbacks (`AR-6`).

## 4. `Eq` Coeff Caching, Pre-pass Folding, `double2` Stereo SIMD, ThinLTO & `SimdDispatch`
- `Eq::process()` (`Eq.cpp:177-206`) recomputes `fast_math::log2f`, `exp2f`, and `BiquadCascade::*` trig (`sin`, `cos`, `pow`, `sqrt`) per enabled band on every single block even when static. Caching per-band parameters & resulting `BiquadCoeffs[kMaxSec]` eliminates all transcendental trig on static blocks.
- In the common flat-Stereo path (`Eq.cpp:259-288`), Left and Right channels apply the exact same sequence of DF2T biquad coefficients independently. Using 128-bit `double2` SIMD (`float64x2_t` on ARM64 NEON, `__m128d` on x86_64 SSE2) computes both channels per sample in parallel with full `double` precision (`0` accuracy loss vs scalar `double`) while keeping filter state vectors in stack registers.
- Replacing the separate `inPk` + `ring_[0]` scalar pre-pass and scalar gain loop with SIMD `peakAbs` + compact stereo loops trims per-frame loop overhead.
- Runtime CPUID dispatch (`SimdDispatch.h/.cpp` + `-mavx2 -mfma`-compiled `SimdAvx2.cpp` on `x86_64`, NEON on `arm64`) enables AVX2/FMA vector kernels on distributed universal binaries without raising the baseline `-msse2` ISA floor.
- `CheckIPOSupported` (ThinLTO on Release) + `-fno-math-errno` on DSP/engine C++ targets removes errno stores around math ops and enables cross-TU inlining of small DSP/index/routing helpers.
