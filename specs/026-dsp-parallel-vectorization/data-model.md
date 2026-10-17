# Data Model: Multi-Core Parallel Track Rendering & SIMD DSP Acceleration

**Feature**: `specs/026-dsp-parallel-vectorization` | **Date**: 2026-10-17

## 1. Concurrency Structures

### `AudioThreadPool`
Pre-allocated fixed thread pool managing real-time audio worker threads.

```cpp
namespace nota {

struct TrackRenderJob {
    Track* track = nullptr;
    float* destinationBus = nullptr;
    int32_t frames = 0;
    double blockStart = 0.0;
    double spb = 0.0;
    bool playing = false;
    float stereoA = 0.0f;
    bool toMix = true;
};

class AudioThreadPool {
public:
    static constexpr int kMaxWorkers = 16;

    AudioThreadPool();
    ~AudioThreadPool();

    void configure(int numThreads);
    void shutdown();

    // Dispatches a batch of jobs across workers; blocks caller until all jobs finish.
    // Real-time safe: zero dynamic allocations, lock-free task stealing or atomic counter.
    void runBatch(TrackRenderJob* jobs, int numJobs);

    int activeWorkers() const { return numWorkers_; }

private:
    std::vector<std::thread> workers_;
    std::atomic<bool> stopping_{false};
    std::atomic<uint32_t> nextJob_{0};
    std::atomic<uint32_t> completedJobs_{0};
    uint32_t totalJobs_{0};
    TrackRenderJob* activeJobs_{nullptr};
    int numWorkers_{0};
};

} // namespace nota
```

---

## 2. SIMD True-Peak State (`Ceiling.h`)

### `TpState` Double-Buffered Array
Eliminates modulo `% 16` during convolution by duplicating circular writes into a 32-float array.

```cpp
struct TpState {
    static constexpr int kTpTaps = 16;
    static constexpr int kTpBufSize = 32;

    alignas(16) std::array<float, kTpBufSize> h{};
    int w = 0; // write cursor in range [0..15]

    inline void push(float x) noexcept {
        h[w] = x;
        h[w + kTpTaps] = x; // contiguous duplicate
        w = (w + 1) & (kTpTaps - 1);
    }

    inline const float* currentWindow() const noexcept {
        return &h[w]; // Contiguous 16-element slice representing history
    }
};
```
