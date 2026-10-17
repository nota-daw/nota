# Research & Technical Decisions: Polyphonic Synth FastMath & SIMD Buffer Inspection

**Feature**: `027-dsp-synth-vector-optimizations`
**Date**: 2026-10-17

## Overview

This research document analyzes the mathematical approximations, vectorization primitives, and memory access patterns needed to optimize polyphonic synthesizer voice generation, buffer silence detection, and mixer summing in Nota's C++20 real-time audio engine.

---

## 1. FastMath Approximations in Polyphonic Synthesis

### Context & Problem
In polyphonic synthesis (`OperatorSynth`, `WavetableSynth`, `PendulumSynth`, `VoltSynth`, `FluxSynth`, `BassSynth`), inner voice loops execute at audio rate ($F_s = 44.1\text{ kHz}$ to $192\text{ kHz}$) multiplied by voice polyphony (up to 8 voices) and operator/harmonic counts (up to 4 operators in FM, up to 4 harmonics in additive).
Evaluating standard library `std::sin()` and `std::tanh()` in these innermost loops consumes excessive CPU cycles and stalls superscalar pipeline execution with heavy microcoded transcendental instructions.

### Findings & Decisions

#### Decision 1: Phase Normalization & `fast_math::fastSin`
- **Choice**: Use `fast_math::fastSin(static_cast<float>(ph))` across all native synth oscillators, LFOs, and vibrato generators.
- **Rationale**:
  - `fast_math::fastSin` accepts phase in $[0, 1)$ with period 1.0. Internally, it performs branchless fractional folding: `x = phase01 - std::floor(phase01) - 0.5f`.
  - In FM synthesis (`OperatorSynth`), phase modulation $ph + pmod$ can be outside $[0, 1)$ or even negative. Because `std::floor` handles arbitrary signed real numbers consistently, `fastSin` handles large FM excursion and feedback gracefully without phase discontinuities or NaN corruption.
  - Maximum absolute approximation error is $< 0.0007$, corresponding to $> 65\text{ dB}$ harmonic purity, which is indistinguishable in musical synthesis and far exceeds analog oscillator tolerance.
- **Alternatives Considered**:
  - Direct 2048-point wavetable lookups with linear interpolation: Rejected for FM synthesis because modulated phase jumps create cache misses and table wrapping branches, whereas polynomial `fastSin` computes purely in registers.
  - Standard `std::sin(kTwoPi * ph)`: Rejected due to 15x higher cycle latency on x86/ARM cores.

#### Decision 2: Invariant Hoisting & Rational `fast_math::fastTanh`
- **Choice**: In `WavetableSynth::driveSample` (Tube mode), hoist the normalization factor `invNorm = 1.0f / fast_math::fastTanh(d * 0.7f + 1.0f)` out of the per-sample loop, and use `fast_math::fastTanh(x * d) * invNorm` for saturation.
- **Rationale**:
  - In `WavetableSynth.h`, drive parameter $d = 1.0 + \text{amt} \times 8.0$ is block-invariant. The previous code evaluated `std::tanh(d * 0.7f + 1.0f)` for *every sample of every voice*.
  - Hoisting the divisor evaluation to a block-rate precomputed inverse float transforms an expensive per-sample division and transcendental function into a single multiplication by constant.
  - In `FluxSynth` and `BassSynth`, pre/out-drive saturation similarly benefits from `fast_math::fastTanh`.

---

## 2. SIMD Audio Buffer Silence Inspection & Energy Tracking

### Context & Problem
Every audio track and return channel in every block evaluates `isBufferSilent()` and `SilenceDetector::process()`.
1. `isBufferSilent()` previously looped over $frames \times 2$ scalar floats with `std::fabs(x) > threshold`.
2. `SilenceDetector::process()` then performed a second pass over the same memory buffer to compute both $sumSquares = \sum x^2$ and $maxPeak = \max |x|$.
3. `Engine_Render.cpp` frequently called `isBufferSilent()` immediately after `SilenceDetector::process()`, performing a third redundant scan of the same buffer.

### Findings & Decisions

#### Decision 3: 16-Sample Vectorized Silence Detection with Early Breakout
- **Choice**: Implement `simd::isSilent(const float* src, int32_t count, float threshold) noexcept` in `Simd.h`.
- **Implementation**:
  - **ARM NEON**:
    - Load 16 floats (4x `float32x4_t` registers: $s_0, s_1, s_2, s_3$).
    - Absolute values via `vabsq_f32`.
    - Compare greater than threshold via `vcgtq_f32(vabs, vThresh)`.
    - Combine masks using bitwise OR (`vorrq_u32(vorrq_u32(m0, m1), vorrq_u32(m2, m3))`).
    - Use `vmaxvq_u32(combinedMask) != 0` to detect if *any* of the 16 samples exceeded the threshold.
    - If non-zero, immediately return `false` (early break).
  - **x86 SSE/AVX**:
    - Absolute value via bitwise AND with `0x7fffffff` mask.
    - Compare greater than threshold via `_mm_cmpgt_ps`.
    - Combine masks via `_mm_or_ps` and test with `_mm_movemask_ps(combined) != 0`.
    - If non-zero, immediately return `false`.
  - **Tail**: Standard scalar loop for remaining samples ($< 16$).

#### Decision 4: Single-Pass Peak and Energy Accumulation (`simd::peakAndSumSquares`)
- **Choice**: Implement `simd::peakAndSumSquares(const float* src, int32_t count, float& outPeak, double& outSumSquares) noexcept`.
- **Rationale**:
  - Fusing absolute maximum search and sum-of-squares calculation into a single pass cuts memory bandwidth demand by 50% for audio monitoring and metering.
  - Both operations vectorize naturally with SIMD:
    - Absolute maximum using `vmaxq_f32` / `_mm_max_ps`.
    - Sum-of-squares using FMA vector accumulation `vmlaq_f32(acc, s, s)` / `_mm_fmadd_ps(s, s, acc)`.
  - The accumulator is reduced across lanes at the end of the block and added to the double-precision energy sum, preventing single-precision accumulation rounding errors over large buffers.

#### Decision 5: Eliminating Redundant Buffer Scans in `Engine_Render.cpp`
- **Choice**: In `Engine_Render.cpp`, leverage the already-computed `SilenceDetector` state:
  - `SilenceDetector::process()` already computes `maxPeak`.
  - If `maxPeak <= threshold`, the buffer is guaranteed to be silent!
  - We can store `lastMaxPeak` or have `SilenceDetector` expose `isBufferSilent(threshold)` directly, eliminating the redundant second scan of `out` and `scratch`.

---

## 3. UI Telemetry Atomic Decimation

### Context & Problem
In `OperatorSynth.h`, the spectrum analysis ring buffer (`ring_`) written at lines 292-293:
```cpp
ring_[ringW_ & (kRing - 1)] = mono;
ringW_.fetch_add(1, std::memory_order_relaxed);
```
performs an atomic read-modify-write operation (`fetch_add`) for *every single audio sample* (44,100 to 192,000 times/sec). While relaxed atomics do not place full memory fences, executing atomic fetch-adds on every sample invalidates CPU cache lines across cores and hampers compiler loop vectorization.

### Decision 6: Batching Ring Buffer Updates
- **Choice**: Update `ringW_` only once per audio block (or every 32/64 samples) by writing samples sequentially into `ring_` using a local integer index, and publishing the updated head index to `ringW_` via a single relaxed atomic store at the end of the block.
- **Rationale**:
  - The UI message thread reads the spectrum analysis ring buffer asynchronously at 60 Hz (~once every 735 audio samples at 44.1 kHz).
  - Updating the atomic write pointer once per block (e.g. 64–512 samples) provides the exact same visual representation with zero thread contention on the audio thread.
