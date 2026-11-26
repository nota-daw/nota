# Feature Specification: Copy-on-Write Track Content, Session-Sized Scratch Arenas & Eq SIMD / Dispatch

**Feature Branch**: `perf/track-cow-arena-simd`  
**Created**: 2026-11-24  
**Status**: Approved (`nota-daw/nota#1050` — Phase 4)  
**Parent Issue**: [`nota-daw/nota#1050`](https://github.com/nota-daw/nota/issues/1050)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Zero-Hitch Editing on Large Projects (`TrackContent` CoW & Incremental Publish) (Priority: P1)

When editing clips, dragging automation points, recording MIDI takes, or tweaking modulators on a track containing thousands of notes/clips inside a 100+ track session, the UI/message thread must clone only the specific data section being modified rather than copying every vector (`clips`, `takes`, `midiClips`, `sessionSlots`, `automation`, `modulators`, `cvLinks`) on the track and re-scanning untouched project tracks on publish.

**Why this priority**: Currently `Engine::cloneTrack()` deep-copies all seven vectors on every structural or live edit (`setClipNotesLive`, `setAutomationPointsLive`, automation write sampling, device edits), while `Engine::publishRaw()` unconditionally clears/recomputes sidechain route slots, re-applies telemetry to every track, and rebuilds `TimelineIndex` even when clips did not change.

**Independent Test**: Construct a 64-track project where one track holds 400 MIDI clips (4,000 notes) and 400 audio clips; perform 500 live automation-point drags and clip-note edits, verify audio output matches `0` vs `4` worker threads bit-identically and that unmodified `CowSlice` sections retain identical backing storage pointers while edit latency shrinks dramatically.

**Acceptance Scenarios**:
1. **Given** a published `Track` snapshot with populated `clips`, `takes`, `midiClips`, `sessionSlots`, `automation`, `modulators`, and `cvLinks`, **When** `Engine::cloneTrack()` creates a new authoring copy and only `automation.write()` is modified, **Then** the other six `CowSlice` containers share the exact same underlying immutable storage without allocation or copying, and `TimelineIndex` is preserved in $O(1)$ without rebuilding.
2. **Given** an edit to a clip/note/automation lane that does not change sidechain keys or monitor sources, **When** `Engine::publishRaw()` publishes the new `Graph`, **Then** `recomputeRouting()` preserves unchanged route slots without bumping `routingEpoch_`, `applyTelemetryMode` skips untouched tracks, and `recomputePdc()` computes each track's latency once and skips unchanged delay lines.
3. **Given** live automation recording or undo/redo restoring an older `Graph`, **When** restoring and re-editing a track, **Then** sealed snapshots never mutate backing vectors in place (any `.write()` on shared or sealed storage detaches cleanly first).

---

### User Story 2 — Cache-Resident Per-Session Scratch Buffers (`ScratchArena`) (Priority: P1)

During live playback at low buffer sizes (`64–2048` frames) across dozens of tracks and `RackDevice` / `RackInstrument` chains, scratch audio buffers must occupy memory proportional to the negotiated session frame budget rather than embedding 128 KB–384 KB fixed `kMaxBlock` (`16384`-frame) arrays inside every rack instance and 256 KB per track slot in `RenderScratch::leafBuf`.

**Why this priority**: Every `RackDevice` currently embeds `3 × 16384 × 2 × 4 = 384 KB` inline (`128 KB` per `RackInstrument`), and `RenderScratch::leafBuf` allocates `256 KB` per track slot even when running a 128-frame live device callback — thrashing Apple M2 / x86_64 L2/L3 caches.

**Independent Test**: Run a 32-track nested `RackDevice` / `RackInstrument` session at `128`-frame hardware/live blocks, then render offline `8192`-frame export chunks on the same session; verify bit-identical `0` vs `4` threads, zero live-callback allocations (`AR-6`), and smaller memory footprint during live playback with seamless capacity growth before large offline chunks execute.

**Acceptance Scenarios**:
1. **Given** a `RackDevice`, `RackInstrument`, or `DrumRack` initialized for live playback (`maxBlock <= 2048`), **When** prepared at the sample rate, **Then** `StereoScratchArena` allocates `2048`-frame aligned buffers (`8×` smaller than `16384`-frame fixed buffers) and never allocates inside the RT audio callback.
2. **Given** `RenderScratch` in `Engine::ensureRenderScratch()`, **When** live blocks run with `<= 2048` frames, **Then** `leafBuf` allocates `2048 * 4` floats per track slot (`32 KB` vs `256 KB`), and grows safely up to `kMaxBlock` (`16384`) on `renderOffline()` preparation prior to rendering large chunks (e.g., `8192` frames).

---

### User Story 3 — Fast `Nota EQ-8` Filtering, Coefficient Cache & Cross-Platform SIMD / Dispatch (Priority: P1)

Tracks carrying `Nota EQ-8` (built-in EQ on nearly every audio/instrument/group track) must skip redundant per-block parameter coefficient trigonometry when parameters are stationary, run the common flat-stereo biquad cascade through a vectorized `double2` L/R SIMD path (`float64x2_t` on ARM64 NEON, `__m128d` on x86_64 SSE2/AVX) with fused peak/ring capture, and compile with `-fno-math-errno` / ThinLTO plus a runtime `SimdDispatch` table for x86_64 AVX2+FMA kernels.

**Why this priority**: Currently `Eq::process()` recomputes `fast_math::log2f`, `exp2f`, `std::sin`, `std::cos`, `std::pow`, and `std::sqrt` for every active band on every 64-sample block (even when static!), runs a separate pre-pass loop, and processes stereo channels separately rather than in a 128-bit `double2` lane. Moreover, baseline x86_64 universal builds never execute AVX2/FMA kernels in `Simd.h` without runtime dispatch.

**Independent Test**: Compare static/gliding/MS/LR `Nota EQ-8` audio output against scalar double reference (`diff < -120 dBFS` — `< -240 dBFS` on double2 SIMD), verify bit-identical `0` vs `4` worker threads across all Eq smoke tests, verify x86_64/ARM64 capability probing via `SimdDispatch` self-check, and measure multi-band `Eq` CPU reduction.

**Acceptance Scenarios**:
1. **Given** static parameters on an enabled `Eq` band (`freq`, `gain`, `q`, `scale`, `type`, `slope`, `sr`), **When** subsequent blocks process, **Then** `Eq` reuses cached `BiquadCoeffs` without calling `log2f`, `exp2f`, or `buildBand()` trigonometry.
2. **Given** the common flat-stereo path (`!moving` and all active bands `ChStereo`), **When** running filter stages, **Then** `(L, R)` run simultaneously in 128-bit `double` SIMD (`float64x2_t` / `__m128d`), keeping all filter state in registers and matching scalar `double` within `< -120 dBFS`, bit-identically across `0` and `N` worker threads.
3. **Given** desktop x86_64 and ARM64 builds, **When** `nota_engine` starts, **Then** `nota::simd::cpuCaps()` / `SimdDispatch` selects the best supported vector backend (`NEON` on ARM64, `AVX2+FMA` or `SSE2` on x86_64 via dedicated `-mavx2 -mfma` TU) safely on older and newer CPUs alike.

---

### Edge Cases & Invariants

- **Strict `AR-6` (Zero RT Allocations / Locks)**: `CowSlice::read()`, `StereoScratchArena` reads, `Eq::process()`, and `SimdDispatch` kernels never allocate or lock on the audio thread. Any offline buffer expansion in `RenderOffline` executes before entering parallel worker tasks or hardware callbacks.
- **Edit-Isolation Invariant**: Calling `CowSlice::seal()` at publish time guarantees `cloneTrack()` copies only `shared_ptr<const std::vector<T>>` with `mutable_ == nullptr`; any future mutation must clone if shared or unsealed.
- **TimelineIndex CoW Invalidation**: `TimelineIndex` stores backing storage tokens for `clips`, `midiClips`, and `articulationTable`; `Track::invalidateTimelineIndex()` / token check ensures stale indices are never served after any clip/note edit, `settleTakeJoins`, `settleArticulations`, or `configureTempoWarp`.
- **Universal macOS Compatibility**: `-mavx2 -mfma` must apply ONLY to `SimdAvx2.cpp` on x86_64 (not globally, so non-AVX2 Macs never hit `SIGILL`), and `arm64` slice compiles cleanly with NEON without x86 flags.
- **Hard Readability Gate (`#1050`)**: All new mechanisms live in concise, domain-named modules (`TrackContent.h`, `ScratchArena.h`, `SimdDispatch.h/.cpp`, `SimdAvx2.cpp`) with SPDX AGPL-3.0 headers, without bloating `Engine.h`/`Engine_Render.cpp`.

## Success Criteria *(mandatory)*

- **SC-001 (Zero regressions)**: `./scripts/build.sh` (`nota-scanner-selftest`, full `Nota.SmokeTest`, `--scan-scale`, `--default-scan-paths`, `--default-scan-paths-upgrade`) and standalone `-- --perf` pass cleanly with `0` failures and `nota_idle_cpu` stays `0.0%` (`PASS`).
- **SC-002 (`0` vs `N` thread bit-identity)**: `TrackSnapshotArenaSimdTests.cs` passes bit-identical comparison between single-threaded and multi-threaded renders across CoW edits, live-to-offline scratch scaling, static/gliding/MS EQ configurations, and vector kernels.
- **SC-003 (Measurable speedups)**:
  - Microbenchmarks demonstrate dramatic speedups on: (a) repeated `cloneTrack` / live automation & clip edits on note-heavy tracks, (b) multi-instance `Eq` 8-band stereo processing, and (c) multi-rack live buffer footprint / cache locality.
  - `./scripts/bench-engine.sh -r 3` confirms overall improvements / zero regressions against `develop` baseline.
