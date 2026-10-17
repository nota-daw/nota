# Phase 1 Data Model: DSP Core Performance & Optimization

## 1. Entities & Structures

### `FastMath` (Header-only static math utility)
Lives in `src/native/nota.engine/src/FastMath.h`.
- **Properties**: Static inline functions with zero mutable state. Thread-safe, reentrant, suitable for real-time audio thread execution.
- **Functions**:
  - `exp2f(float x) -> float`: Fast $2^x$ approximation.
  - `log2f(float x) -> float`: Fast $\log_2(x)$ approximation.
  - `dbToLin(float db) -> float`: Fast decibels to linear amplitude ($10^{db/20}$).
  - `linToDb(float lin) -> float`: Fast linear amplitude to decibels ($20 \log_{10}(lin)$).
  - `fastTanh(float x) -> float`: Fast rational tanh saturation ($x \cdot \text{tanhRatio}(x)$).
  - `tanhRatio(float x) -> float`: Ratio $\tanh(x)/x$ for non-linear feedback and drive stages.
  - `fastSin(float phase01) -> float`: Fast sine on $[0, 1)$ normalized phase.
  - `fastCos(float phase01) -> float`: Fast cosine on $[0, 1)$ normalized phase.
  - `tanPrewarp(float x) -> float`: Padé approximation for bilinear transform pre-warping $\tan(x)$.

### `Delay::BlockInvariants` (Precomputed per-block values)
Evaluated before the audio frame loop in `Delay::process()`:
- `drive`: Input saturation amount $\in [0, 3]$.
- `invTanhDrive`: Precomputed scalar multiplier $1.0f / \tanh(\text{drive})$ (or $1.0f$ when $\text{drive} \le 10^{-4}$).
- `wowInc`: Phase increment per frame.

### `Eq::BlockState` (Biquad processing state per band)
Maintains filter memory across blocks:
- `Coeffs`: Direct Form II Transposed filter coefficients $\{b_0, b_1, b_2, a_1, a_2\}$.
- `State`: Delay states $\{z_1, z_2\}$ per section per channel.
- Processing channels:
  - `ChLeft`: processes Left channel in place.
  - `ChRight`: processes Right channel in place.
  - `ChMid`: encodes to Mid, filters Mid, decodes to stereo.
  - `ChSide`: encodes to Side, filters Side, decodes to stereo.
  - `ChStereo`: dual-channel symmetric filter running Left and Right simultaneously.

---

## 2. Audio Thread Memory Invariant (AR-6)

All data structures used during audio processing MUST:
1. Be allocated in the device constructor / `setSampleRate()` during initialization.
2. Never allocate on the stack beyond small fixed-size scratch buffers ($\le 1024$ floats, well within standard thread stack limits).
3. Incur zero heap allocations (`malloc`, `free`, `new`, `delete`, vector resizing) in any `process()` or `render()` method.
