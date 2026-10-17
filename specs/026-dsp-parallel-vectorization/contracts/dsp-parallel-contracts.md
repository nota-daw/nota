# Contracts: Multi-Core Parallel Track Rendering & SIMD Interfaces

## 1. SIMD True-Peak FIR Prototype (`Ceiling.h`)

```cpp
namespace nota {

// Compute 16-tap FIR dot product across 3 phases for 4x oversampled true-peak detection.
// Uses ARM NEON on aarch64, SSE/AVX on x86_64, with scalar fallback.
inline float computeTpFir3Phases(const float* history16, const float coefs[3][16]) noexcept;

} // namespace nota
```

## 2. Audio Thread Pool Interface (`AudioThreadPool.h`)

```cpp
namespace nota {

class AudioThreadPool {
public:
    void init(int workerCount);
    void release();
    void execute(std::span<TrackRenderJob> jobs);
};

} // namespace nota
```
