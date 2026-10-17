# Data Model & Mathematical Primitives: Polyphonic Synth FastMath & SIMD Buffer Inspection

**Feature**: `027-dsp-synth-vector-optimizations`
**Date**: 2026-10-17

## Overview

This feature optimizes internal DSP execution and buffer inspection within Nota's native C++20 engine (`src/native/nota.engine`). There are no database entities or serialization format changes. This document formalizes the low-level data structures, state machines, and mathematical interfaces.

---

## 1. SIMD Vector Primitives (`Simd.h`)

### `simd::isSilent`
Vectorized test determining whether all float samples in a contiguous buffer fall within a silence threshold $|x| \le \text{threshold}$.

```cpp
namespace nota {
namespace simd {

// Returns true if all samples have std::fabs(src[i]) <= threshold.
// Evaluates 16 samples per iteration; returns false immediately upon detecting active signal.
inline bool isSilent(const float* src, int32_t count, float threshold = 1.0e-5f) noexcept;

} // namespace simd
} // namespace nota
```

- **Invariants**:
  - Null or `count <= 0` returns `true`.
  - Chunk size: 16 floats (64 bytes = 1 L1 cache line).
  - Clean scalar cleanup tail for `count % 16 != 0`.

### `simd::peakAndSumSquares`
Fuses absolute maximum peak detection and vector dot product sum-of-squares into a single pass across memory.

```cpp
namespace nota {
namespace simd {

// Evaluates maximum absolute amplitude and sum of squared samples in a single pass.
inline void peakAndSumSquares(const float* src, int32_t count,
                              float& outPeak, double& outSumSquares) noexcept;

} // namespace simd
} // namespace nota
```

- **Mathematical Definition**:
  $$\text{outPeak} = \max_{i=0}^{N-1} |x_i|$$
  $$\text{outSumSquares} = \sum_{i=0}^{N-1} x_i^2$$
- **Precision**: Accumulates intermediate products in SIMD 32-bit float vectors and sums reductions into 64-bit `double` to prevent precision loss.

### `simd::clip`
Vectorized clamp of buffer values between `minVal` and `maxVal`.

```cpp
namespace nota {
namespace simd {

inline void clip(float* dst, const float* src, float minVal, float maxVal, int32_t count) noexcept;

} // namespace simd
} // namespace nota
```

---

## 2. SilenceDetector State Machine (`AudioBufferUtils.h`)

The `SilenceDetector` maintains hysteresis around silence entry/exit to prevent chattering.

```
       [ Signal Peak >= attackThreshold OR RMS >= attackThreshold ]
                          ┌─────────────────┐
       ┌─────────────────►│     Active      │◄────────────────┐
       │                  └────────┬────────┘                 │
       │                           │                          │
       │ [Peak < attack]           │ [RMS <= releaseThreshold │
       │                           │  AND Peak <= attack]     │
       │                           ▼                          │
       │                  ┌─────────────────┐                 │
       │                  │ PossiblySilent  │                 │
       │                  │ (Hold Counter)  │                 │
       │                  └────────┬────────┘                 │
       │                           │                          │
       │                           │ [silentSamples >= hold]  │
       │                           ▼                          │
       │                  ┌─────────────────┐                 │
       └──────────────────┤     Silent      ├─────────────────┘
                          └─────────────────┘
```

### Enhanced State & Properties
```cpp
struct SilenceDetector {
    double attackThreshold = 1.0e-4;   // RMS ~ -80 dBFS
    double releaseThreshold = 3.0e-5;  // RMS ~ -90.5 dBFS
    uint64_t holdSamples = 6615;       // ~150 ms at 44.1 kHz
    uint64_t silentSamples = 6615;
    SignalState state = SignalState::Silent;
    float lastMaxPeak = 0.0f;          // Cached peak from latest process() call

    // Fast query avoiding redundant buffer passes
    bool isBufferSilent(float threshold = 1.0e-5f) const noexcept {
        return lastMaxPeak <= threshold;
    }

    SignalState process(const float* interleaved, int32_t frames);
};
```

---

## 3. FastMath Function Signatures & Precision Profiles (`FastMath.h`)

| Function | Domain | Max Relative Error | Target Replacements |
|:---|:---|:---|:---|
| `fast_math::fastSin(float phase01)` | Any $\phi \in \mathbb{R}$ (folds $[0, 1)$) | $< 0.0007$ ($> 65\text{ dB}$ SNR) | `OperatorSynth` FM sine, `WavetableSynth` LFO/Fold, `PendulumSynth` additive harmonics, `VoltSynth` osc/LFO, `FluxSynth` drift/sub, `BassSynth` morph/sub |
| `fast_math::fastTanh(float v)` | $[-3.0, 3.0]$ (saturates $\pm 1$) | Rational knee | `WavetableSynth` Tube drive, `FluxSynth` drive, `BassSynth` drive |
