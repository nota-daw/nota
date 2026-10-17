# Feature Specification: DSP Core Performance & Optimization

**Feature Branch**: `024-dsp-optimizations`

**Created**: 2026-10-17

**Status**: Implemented

**Input**: User request: "Optimize real-time DSP performance across native audio engine components." — optimize real-time DSP performance across native engine audio processing components, focusing on FastMath approximations, loop reorganization (block-based processing), invariant hoisting, and algorithmic streamlining without compromising audio fidelity or real-time safety.

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply (AR-6: no allocations, no locks, no blocking I/O)
- [ ] Managed app (C#, `src/managed/*`)
- [ ] UI (Avalonia)
- [ ] Plugin hosting (`pluginhost/`)
- [ ] Build / packaging / CI
- [ ] Docs only

**Current state**:
In `src/native/nota.engine/src/`:
1. `Delay.h` recalculates loop-invariant `1.0f / std::tanh(drive)` on every sample, and calls standard `std::sin()` per sample for wow/flutter LFO.
2. `Compressor.h` calls heavy `std::log10()` and `std::exp()` (`dbToLin`) per sample per channel in the audio inner loop.
3. `Eq.h` processes biquad filter cascades per-sample in an outer loop over frames with an inner loop over 8 bands containing an inner `switch(channel)` executed up to 2048 times per block.
4. `Chamber.h` executes an 8-point Fast Walsh-Hadamard Transform (`hadamard8`) using three nested loops with dynamic stride inside every diffusion stage of every sample (over 170,000 times/second per instance).
5. Fast math functions (`exp2f`, `tanPrewarp`, `tanhRatio`) exist in `Pentad.h` but are isolated and not shared with other native effects.

---

## User Scenarios & Testing

### User Story 1 - Lower CPU Load on Projects with Multiple Audio Effects (Priority: P1)

A producer runs a demanding project with multiple tracks containing EQ, compressors, delays, and reverbs. With DSP optimizations applied, the DAW runs with significantly lower CPU usage and higher headroom before audio dropouts occur at low buffer sizes (e.g. 64–128 samples).

**Why this priority**: Directly impacts real-time performance, dropout resistance, and the number of simultaneous tracks/effects a user can run smoothly.

**Independent Test**: Render audio tracks loaded with `Eq`, `Compressor`, `Delay`, and `Chamber` under high voice/track counts and measure block rendering time / CPU percentage.

**Acceptance Scenarios**:

1. **Given** tracks with active `Delay` devices, **When** audio is rendered, **Then** tape saturation and LFO processing use streamlined math without audible deviation or clicks.
2. **Given** tracks with active `Compressor` devices, **When** dynamic gain reduction is active, **Then** envelope tracking and makeup gain run using fast approximations without audible distortion or timing skew.
3. **Given** tracks with active `Eq` instances, **When** filtering 8 bands, **Then** processing runs block-by-block with channel routing evaluated once per block, reducing per-sample branch overhead.
4. **Given** a session with `Chamber` reverb, **When** audio is diffused, **Then** `hadamard8` executes an unrolled/streamlined calculation delivering identical diffusion with less CPU overhead.

---

### User Story 2 - Perceptual Audio Transparency & Bit-Accurate Stability (Priority: P1)

A mixing engineer relies on Nota's sonic signature. The mathematical approximations must be sonically indistinguishable from the previous standard library math (relative error below audible thresholds, < 0.05 dB deviation), without introducing DC offsets, blowups, or denormal penalties.

**Why this priority**: Performance optimizations that degrade sound quality or cause instability in feedback loops are unacceptable in professional audio.

**Independent Test**: Run signal regression tests comparing outputs of original vs optimized processing on standard test vectors (impulses, sine sweeps, noise bursts).

**Acceptance Scenarios**:

1. **Given** full-scale audio or near-silent tails, **When** rendered through optimized DSP devices, **Then** signals decay smoothly to zero (FTZ/DAZ active) without infinite values, NaNs, or numerical blowup.
2. **Given** frequency response curves across 20 Hz to 20 kHz, **When** processed through `Eq` and `Compressor`, **Then** the frequency response matches original behavior within 0.05 dB.

---

### User Story 3 - Clean Regression Testing & Zero Real-Time Audio Violations (Priority: P2)

All changes preserve Nota's architectural invariants: strictly zero heap allocations on the audio thread (AR-6), no locking, no blocking I/O, no C ABI changes.

**Why this priority**: Adherence to the project constitution and passing the full smoke test suite.

**Independent Test**: Run `scripts/build.sh` (or `tests/Nota.SmokeTest`) ensuring all end-to-end audio tests pass green without warnings or memory issues.

**Acceptance Scenarios**:

1. **Given** the smoke test suite `tests/Nota.SmokeTest`, **When** executed, **Then** all tests pass green.
2. **Given** static inspection and runtime checks, **Then** no new heap allocations (`malloc`, `new`, `std::vector::resize`) occur inside audio rendering callbacks.

---

---

### User Story 4 - High-Load Native DSP Hotspots Optimization (Priority: P1)

A user runs complex sessions with multiple instances of `Chamber`, `Vintage`, `AutoFilter`, `AutoPan`, `BassSynth`, `DynamicEq`, and `Ceiling`. The DSP inner loops of these devices are optimized by eliminating per-sample transcendentals (`std::sin`, `std::cos`, `std::tanh`, `std::log10`), hoisting invariant divisions outside sample loops, and removing per-sample branches.

**Why this priority**: These devices are primary workhorses in Nota projects; eliminating hot-loop bottlenecks directly unlocks significantly more concurrent tracks and plugins before audio dropouts.

**Independent Test**: Smoke tests in `Nota.SmokeTest` executing each device with full parameter modulation and audio rendering to verify numerical bounds, absence of NaNs, and stability.

**Acceptance Scenarios**:

1. **Given** `Chamber` FDN reverb tank, **When** running under overdrive or freeze, **Then** `loopLimit` uses `fast_math::fastTanh`, diffusion stages are statically unrolled, and invariant branches (`cubic`, `vintage`, `shim`) are hoisted.
2. **Given** `Vintage` saturation and tape/vinyl mode, **When** audio is processed, **Then** `Curve::eval()` uses `fast_math::fastTanh` and precomputed constants, and wow/flutter LFOs use `fast_math::fastSin`.
3. **Given** `AutoFilter` with drive or analog mode, **When** filtering audio, **Then** saturation uses `fast_math::fastTanh` and LFO uses `fast_math::fastSin`.
4. **Given** `AutoPan`, **When** modulation shape is adjusted, **Then** `1.0 / std::tanh(kk)` is precomputed once per block outside the sample loop and LFO uses `fast_math::fastSin`.
5. **Given** `BassSynth` with `preDrive > 1.0`, **When** rendering multiple voices, **Then** `1.0 / std::tanh(preDrive)` is precomputed outside the audio sample loop.
6. **Given** `DynamicEq`, **When** dynamic bands are active, **Then** per-sample envelope level detection uses `fast_math::linToDb` without calling `std::log10`.
7. **Given** `Ceiling` limiter, **When** soft clipping is engaged, **Then** ceiling reciprocal is precomputed and `softClip` uses `fast_math::fastTanh`.

---

### Edge Cases

- **Extreme Parameter Ranges**: `drive = 0` or very high drive in `Delay` saturation: invariant hoisting must guard against division by zero (e.g. `std::tanh(0) == 0`).
- **Very Low Signal Levels (< -90 dB)**: Fast log/exp approximations must handle subnormal and near-zero values gracefully without returning `-infinity` or generating subnormal CPU penalties.
- **Dynamic Parameter Automation**: Block-based processing in `Eq.h` must seamlessly support mid-block coefficient updates during parameter movement (`kCtrl` sample intervals).
- **Extreme High Sample Rates (96 kHz / 192 kHz)**: Precomputed tables and coefficients must scale correctly with sample rate.

---

## Requirements

### Functional Requirements

- **FR-001**: System MUST provide a centralized `FastMath.h` utility header in `src/native/nota.engine/src/` exposing fast, SIMD-friendly approximations for `exp2f`, `log2f`, `tanPrewarp`, `tanhRatio`, `dbToLin`, and fast polynomial sine/cosine.
- **FR-002**: In `Delay.h`, the saturation stage MUST precompute loop-invariant scaling `1.0f / std::tanh(drive)` outside the sample loop when `tape` mode is enabled, with a safe fallback when `drive <= 0.0f`.
- **FR-003**: In `Delay.h`, the wow/flutter LFO MUST replace per-sample `<cmath>` `std::sin()` with a fast polynomial sine approximation or table-less phase oscillator.
- **FR-004**: In `Compressor.h`, the envelope detector and gain-reduction calculation MUST replace per-sample `std::log10()` and `std::exp()` with fast logarithmic/exponential approximations.
- **FR-005**: In `Eq.h`, `Eq::process()` MUST be refactored into a block-oriented architecture where channel routing (`ChLeft`, `ChRight`, `ChMid`, `ChSide`, `ChStereo`) is resolved outside the inner sample loop, allowing tight, uninterrupted biquad processing.
- **FR-006**: In `Chamber.h`, the 8-point Fast Walsh-Hadamard Transform `hadamard8` MUST be unrolled to eliminate dynamic nested loop indexing in the inner audio path.
- **FR-007**: All DSP modifications MUST strictly follow Real-Time Audio Rule AR-6 (no heap allocations, no locks, no system calls, no exception throwing).
- **FR-008**: In `Chamber.h`, `loopLimit` and `softClip` MUST use `fast_math::fastTanh`; `process()` inner loop MUST hoist invariant branches (`p.cubic`, `p.vintage`, `shim` feedback injection) and statically unroll the 4-stage diffusion loop.
- **FR-009**: In `Vintage.h`, `Curve::eval()` and `saturate()` MUST use `fast_math::fastTanh` and precalculated `tanh(0.6f)`; wow/flutter LFO MUST use `fast_math::fastSin`.
- **FR-010**: In `AutoFilter.h`, drive saturation and analog mode MUST use `fast_math::fastTanh`; LFO MUST use `fast_math::fastSin`.
- **FR-011**: In `AutoPan.h`, `1.0 / std::tanh(kk)` MUST be precomputed outside the sample loop; sine LFO MUST use `fast_math::fastSin`.
- **FR-012**: In `BassSynth.h`, `1.0 / std::tanh(preDrive)` MUST be hoisted outside the audio loop.
- **FR-013**: In `DynamicEq.h`, `20.0 * std::log10(detEnv + 1e-9)` MUST be replaced with `fast_math::linToDb` in the per-sample envelope detector; `exp2f` MUST be used for frequency/Q modulation.
- **FR-014**: In `Ceiling.h`, `1.0f / std::max(1e-6f, ceil)` MUST be hoisted and `fast_math::fastTanh` MUST be used in `softClip`.

---

## Success Criteria

### Measurable Outcomes

- **SC-001**: Overall CPU utilization of `Delay.h` tape/LFO loop reduced by at least 25% in micro-benchmarks.
- **SC-002**: Overall CPU utilization of `Compressor.h` inner detection loop reduced by at least 20%.
- **SC-003**: Overall CPU utilization of `Eq.h` 8-band processing reduced by at least 15% due to branch elimination and improved cache residency.
- **SC-004**: The complete test suite (`scripts/build.sh` driving `Nota.SmokeTest`) passes with 100% success rate and zero audio dropouts.
- **SC-005**: Zero memory allocations or deallocations during steady-state audio rendering.

---

## Assumptions

- Standard IEEE-754 32-bit `float` representation is used on all target platforms (x86_64, aarch64).
- Fast approximations with maximum relative error < 0.1% (< 0.05 dB) are considered sonically transparent for real-time creative DSP (LFOs, saturation curves, envelope tracking).
- The existing C ABI boundary (`nota_engine.h`) remains completely untouched; this is a pure native internal DSP optimization.
