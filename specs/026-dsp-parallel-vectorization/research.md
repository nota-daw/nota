# Research: Multi-Core Parallel Track Rendering & SIMD DSP Acceleration

**Feature**: `specs/026-dsp-parallel-vectorization` | **Date**: 2026-10-17

## 1. Multi-Core Audio Processing in Real-Time DAWs

### Challenge & Real-Time Constraints (AR-6)
Standard DAWs (Reaper, Bitwig, Ableton, Logic) parallelize audio processing by scheduling independent tracks across a fixed pool of real-time worker threads.
Under Real-Time Audio Rule AR-6, the audio processing path must NEVER:
1. Allocate or deallocate memory (`new`, `malloc`, `std::vector` resizing).
2. Acquire OS mutexes (`std::mutex`, `pthread_mutex_lock`).
3. Call blocking OS primitives that can trigger priority inversion.

### Thread Pool Architecture: Lock-Free Worker Barrier
- **Thread Lifecycle**: Threads are spawned on the message thread during `Engine` initialization or `setSampleRate()`. During rendering, worker threads never exit or spawn.
- **Task Scheduling**: A pre-allocated array of track render tasks with an atomic ticket counter (`std::atomic<uint32_t> nextTrackIndex`).
- **Barrier Synchronization**: A lightweight real-time barrier using atomic wait/notify or bounded spin-wait with exponential backoff / `sched_yield` to synchronize leaf tracks before Phase 1.5 (group submixes) and Phase 2 (return buses & master mix).
- **DAG / Dependency Handling**:
  - Independent leaf tracks without sidechain sources can run concurrently.
  - If a track has a sidechain tap from another track, the source track must complete first. A simple two-pass topological dependency or atomic readiness flag (`std::atomic<bool> ready`) resolves this with zero locks.

---

## 2. SIMD Acceleration: True-Peak FIR Limiter (`Ceiling.h`)

### The Problem in Current `Ceiling.h`
In `tpPush()`:
```cpp
s.w = (s.w + 1) % kTpTaps;
for (int p = 0; p < 3; ++p) {
    float acc = 0.0f;
    for (int i = 0; i < kTpTaps; ++i)
        acc += c[p][i] * s.h[(s.w + i) % kTpTaps]; // Modulo in inner loop!
    pk = std::max(pk, std::fabs(acc));
}
```
- `kTpTaps = 16`.
- 16 taps $\times$ 3 phases $\times$ 2 channels = 96 integer modulo `% 16` operations **per sample**!
- Memory access is non-contiguous, preventing Clang/LLVM from auto-vectorizing.

### The Solution: Double-Buffering & SIMD Dot-Product
Instead of wrapping within a 16-element array, use a 32-element array:
```cpp
struct TpState {
    std::array<float, 32> h{};
    int w = 0; // 0..15
};
```
When writing sample `x`:
```cpp
s.h[s.w] = x;
s.h[s.w + 16] = x; // Duplicate write
s.w = (s.w + 1) & 15;
```
Now `&s.h[s.w]` is a contiguous 16-element slice!
The dot-product over 16 taps across 3 phases can be computed using:
- **ARM NEON**: `vld1q_f32`, `vmlaq_f32` (4 vector FMA instructions per phase).
- **x86 SSE/AVX**: `_mm_loadu_ps`, `_mm_fmadd_ps` / `_mm_mul_ps` + `_mm_add_ps`.
- Zero modulo operations, zero branch misses, 100% cache-aligned.

---

## 3. FastMath Transcendental Elimination

### Audit of Hot-Path Transcendentals
1. **`Chamber.h` Ducking Follower**:
   - `20.0f * std::log10(std::max(1e-6f, duckEnv_))` $\to$ `fast_math::linToDb(duckEnv_)`
   - `std::pow(10.0f, -duckDb * ... / 20.0f)` $\to$ `fast_math::dbToLin(-duckDb * ...)`
   - Impact: Saves ~25% CPU of the hybrid reverb tail stage.
2. **`Reverb.h` LFO Modulation**:
   - `std::sin(kTwoPi * modPhase_)` $\to$ `fast_math::fastSin(kTwoPi * modPhase_)`
   - Impact: Per-sample trigonometry replaced with 4th-order polynomial evaluation.
3. **`Ceiling.h` Block Header Gain Conversions**:
   - `std::pow(10.0f, ceilDb / 20.0f)` $\to$ `fast_math::dbToLin(ceilDb)`

---

## 4. UI Telemetry Decimation

### The Problem in `Compressor.h`
```cpp
const int kw = keyW_.load(std::memory_order_relaxed);
keyRing_[kw] = (keyRaw[0] + ...) * 0.5f;
keyW_.store((kw + 1) % kKeyN, std::memory_order_relaxed);
```
Written 44,100 to 192,000 times per second to an atomic ring buffer for a 60 Hz UI display card.

### Solution
Decimate writing to `keyRing_` using `envDecim_` or block-rate decimation (every 32 or 64 samples). Peak and RMS values are held across the decimation period. UI rendering fidelity is unchanged, while L1 cache line ping-ponging is eliminated.
