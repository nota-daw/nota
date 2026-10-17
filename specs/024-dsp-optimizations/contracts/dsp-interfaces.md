# Contracts: Internal DSP & FastMath Interfaces

## 1. FastMath API Contract (`FastMath.h`)

```cpp
namespace nota {
namespace fast_math {

// Range: x in [-60.0f, 60.0f]
// Precision: relative error < 1.5e-5 (~0.03 cent)
inline float exp2f(float x);

// Range: x > 0.0f
// Precision: relative error < 1.2e-4 (< 0.001 dB)
inline float log2f(float x);

// Decibels to linear gain (10^(db/20))
inline float dbToLin(float db);

// Linear amplitude to decibels (20 * log10(lin))
inline float linToDb(float lin);

// Rational tanh saturation: accurate up to |x| <= 3, flat +/-1 beyond
inline float fastTanh(float x);

// tanh(x) / x: useful for feedback loop saturation
inline float tanhRatio(float x);

// Normalized phase in [0, 1): sin(2*pi*phase)
inline float fastSin(float phase01);

// Normalized phase in [0, 1): cos(2*pi*phase)
inline float fastCos(float phase01);

// Bilinear transform pre-warp: tan(x) for x in [0, ~1.3]
inline float tanPrewarp(float x);

} // namespace fast_math
} // namespace nota
```

## 2. Invariant Hoisting Contract (`Delay.h`)

- **Method**: `Delay::process(float* buf, int32_t frames)`
- **Precondition**: `drive` parameter retrieved and clamped.
- **Contract**: `1.0f / std::tanh(drive)` evaluated prior to frame iteration if `tape` is true. `fastSin` used for `wowPhase_` modulation.
- **Postcondition**: Output matches expected saturation profile within 0.1% THD tolerance.

## 3. Biquad Block-Processing Contract (`Eq.h`)

- **Method**: `Eq::process(float* buf, int32_t frames)`
- **Contract**:
  - Processing sliced into chunks of at most `kCtrl = 64` samples so smooth parameter updates occur on time.
  - For each chunk, active bands loop through sample buffers directly; channel selection logic (`ChLeft`, `ChRight`, `ChMid`, `ChSide`, `ChStereo`) occurs outside the sample processing loop.
  - Zero heap allocation throughout the process call.
