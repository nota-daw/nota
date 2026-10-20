# Phase 1 Data Model & Module Architecture: Professional C++20 DSP Overhaul

**Feature**: `030-dsp-architecture-overhaul` | **Date**: 2026-10-19

## 1. Core DSP Entities (`src/dsp/core/` & `src/dsp/math/`)

### 1.1 `StereoSpan` / `ConstStereoSpan` (`src/dsp/core/AudioSpan.h`)
Lightweight, zero-overhead non-owning view over interleaved stereo float buffers (`L0, R0, L1, R1, ...`).
```cpp
namespace nota::dsp {

class StereoSpan {
public:
    constexpr StereoSpan() noexcept = default;
    constexpr StereoSpan(float* interleaved, int32_t frames) noexcept
        : data_(interleaved), frames_(frames > 0 ? frames : 0) {}

    [[nodiscard]] constexpr float* data() const noexcept { return data_; }
    [[nodiscard]] constexpr int32_t frames() const noexcept { return frames_; }
    [[nodiscard]] constexpr int32_t samples() const noexcept { return frames_ * 2; }
    [[nodiscard]] constexpr std::span<float> span() const noexcept {
        return { data_, static_cast<size_t>(frames_ * 2) };
    }
    [[nodiscard]] constexpr StereoSpan subframes(int32_t offset, int32_t count) const noexcept {
        return { data_ + offset * 2, count };
    }
private:
    float* data_ = nullptr;
    int32_t frames_ = 0;
};

} // namespace nota::dsp
```

### 1.2 `SilenceDetector` (`src/dsp/core/SilenceDetector.h` + `SilenceDetector.cpp`)
Stateful audio activity detector with Schmitt-trigger hysteresis and sample-accurate hold counter.
- **Fields**:
  - `double attackThreshold = 1.0e-4` ($-80\text{ dBFS}$ wake threshold)
  - `double releaseThreshold = 3.0e-5` ($-90.5\text{ dBFS}$ sleep threshold)
  - `uint64_t holdSamples = 6615` (~150 ms at 44.1 kHz)
  - `uint64_t silentSamples = 6615`
  - `SignalState state = SignalState::Silent` (`Active`, `PossiblySilent`, `Silent`)
  - `float lastMaxPeak = 0.0f` (cached peak from single-pass `simd_audio::peakAndSumSquares`)
- **Methods** (implemented in `SilenceDetector.cpp`):
  - `void setSampleRate(double sr, double holdMs = 150.0) noexcept`
  - `void reset(SignalState initialState = SignalState::Silent) noexcept`
  - `SignalState process(const float* interleaved, int32_t frames) NOTA_RT_SAFE`
  - `bool isPeakSilent(float threshold = 1.0e-5f) const noexcept`

---

## 2. Shared DSP Primitives (`src/dsp/primitives/` & `src/dsp/poly/`)

Each primitive is split into `.h` (class definition + hot inline tick) and `.cpp` (setup, coefficient calculation, block processing, reset):

| Module | Files | Responsibility | Used By |
| :--- | :--- | :--- | :--- |
| **TPT SVF Filter** | `SvfFilter.h`, `SvfFilter.cpp` | Cytomic zero-delay feedback SVF (LP, BP, HP, Notch, Allpass, Bell, Shelf) & 4th-order Linkwitz-Riley crossover (`Lr4Splitter`) | `AutoFilter`, `Prism`, `Synth`, `VoltSynth`, `BassSynth`, `FluxSynth`, `WavetableSynth` |
| **Biquad Cascade** | `BiquadCascade.h`, `BiquadCascade.cpp` | Direct Form II Transposed biquad section & multi-section cascade with 64-sample chunked block processing and channel routing (`Stereo`, `L`, `R`, `Mid`, `Side`) | `Eq`, `DynamicEq`, `Eq3`, `Amp`, `Keys`, `Chamber` |
| **Ladder Filter** | `LadderFilter.h`, `LadderFilter.cpp` | 4-pole transistor ladder filter with `fast_math::tanhRatio` OTA stage linearization and internal 2x oversampling | `Monolith`, `Consort`, `Pentad` |
| **Delay Line** | `DelayLine.h`, `DelayLine.cpp` | Power-of-two circular buffer with bitmask wrap (`& mask_`), linear, 4-point Hermite cubic, and first-order allpass interpolation | `Delay`, `Chamber`, `Reverb`, `Chorus`, `Flanger`, `Vintage`, `Consort` |
| **Hadamard** | `Hadamard.h` | Branchless unrolled 4-point and 8-point Fast Walsh-Hadamard Transforms | `Chamber`, `PhysicalSynth`, `Reverb` |
| **Oscillator & LFO** | `Oscillator.h`, `Oscillator.cpp` | PolyBLEP / PolyBLAMP band-limited oscillators and tempo-synced multi-waveform `LfoGenerator` | All synths & modulation effects |
| **Envelope** | `Envelope.h`, `Envelope.cpp` | `AdsrEnvelope`, `ArEnvelope`, and `EnvelopeFollower` (using `fast_math::linToDb` / `dbToLin`) | All synths, `Compressor`, `DynamicEq`, `AutoFilter`, `AutoGain`, `Prism` |
| **Oversampler** | `Oversampler.h`, `Oversampler.cpp` | HIIR 2x/4x/8x polyphase half-band IIR oversampler with SIMD stereo deinterleave/interleave | `Vintage`, `Amp`, `Forge`, `Crush`, `Pentad`, `Consort` |
| **Convolution** | `Convolution.h`, `Convolution.cpp` | `ConvKernel` zero-latency partitioned FFT convolution (64-tap direct FIR + uniform/two-stage Signalsmith RealFFT partitions) | `Chamber` |
| **True-Peak Meter** | `TruePeakMeter.h`, `TruePeakMeter.cpp` | 4x oversampled 16-tap FIR True-Peak detector with 32-float double-buffered history and `simd::dot16` | `Ceiling`, `AutoGain` |
| **PDC Delay** | `CompensationDelay.h`, `CompensationDelay.cpp` | Lock-free per-track latency compensation delay line with `isIdle()` state | `Track`, `Engine` |
| **Polyphony** | `SynthVoice.h`, `SynthVoice.cpp`, `PolyVoiceManager.h`, `PolyVoiceManager.cpp` | Base voice state and sample-accurate polyphonic voice manager (voice stealing, sustain pedal, MPE) | Polyphonic instruments |

---

## 3. Multi-Core Audio Thread Pool (`src/engine/AudioThreadPool.h` + `.cpp`)

Inspired by `zrythm::dsp::graph::ForkJoinExecutor` and `0001` patch:

```cpp
namespace nota {

using AudioTaskFn = void (*)(void* ctx, int workerId) noexcept;

struct NOTA_CACHE_ALIGNED AudioTask {
    AudioTaskFn fn = nullptr;
    void* ctx = nullptr;
};

class AudioThreadPool {
public:
    static constexpr int kMaxWorkers = 16;

    AudioThreadPool();
    ~AudioThreadPool();

    void configure(int numThreads, int32_t maxBlockFrames = 16384);
    void shutdown();

    int workerCount() const noexcept { return numWorkers_; }
    float* workerScratchBuffer(int workerId) noexcept;

    // Dispatches tasks across workers + calling audio thread (workerId = 0).
    void runTasks(const AudioTask* tasks, int numTasks) NOTA_RT_SAFE;

private:
    void workOnTasks(int workerId) NOTA_RT_SAFE;
    void workerLoop(int workerId);

    std::vector<std::thread> workers_;
    std::vector<std::vector<float>> workerScratch_; // [kMaxWorkers + 1][maxBlockFrames * 2]
    NOTA_CACHE_ALIGNED std::atomic<bool> stopping_{false};
    NOTA_CACHE_ALIGNED std::atomic<uint32_t> generation_{0};
    NOTA_CACHE_ALIGNED std::atomic<uint32_t> nextTaskIndex_{0};
    NOTA_CACHE_ALIGNED std::atomic<uint32_t> completedTasks_{0};
    NOTA_CACHE_ALIGNED std::atomic<uint32_t> totalTasks_{0};
    const AudioTask* activeTasks_{nullptr};
    int numWorkers_{0};
};

} // namespace nota
```
- **Key improvements over raw patch `0001`**:
  1. `AudioTaskFn` receives `int workerId` (`0` for the main audio thread, `1..numWorkers_` for pool threads), allowing each worker to use `threadPool_.workerScratchBuffer(workerId)` instead of allocating 128 KB on every `Track`!
  2. Every worker thread initializes `ScopedNoDenormals` at the start of `workerLoop(workerId)`, guaranteeing FTZ/DAZ on all cores.
  3. All hot atomic counters are cache-line aligned (`NOTA_CACHE_ALIGNED` = `alignas(64)`).

---

## 4. Separated Plugin Scanner Architecture (`pluginhost/scanner/`)

```text
+-------------------------------------------------------------------+
|                     include/nota/nota_engine.h                    |
|   nota_pluginhost_scan, _plugin_count, _plugin_desc, _plugin_id,  |
|   _index_of_id, _add_scan_path, _remove_scan_path, _scan_path*    |
+---------------------------------+---------------------------------+
                                  |
         +------------------------+------------------------+
         |                                                 |
         v                                                 v
+----------------------------------+     +----------------------------------+
|      nota_pluginscanner          |     |         nota_pluginhost          |
|      (pluginhost/scanner/)       |     |       (pluginhost/hosting/)      |
|                                  |     |                                  |
| - PluginCatalog (.h/.cpp)        |<--->| - PluginHost (.h/.cpp)           |
|   (std::shared_mutex, XML/paths) |     | - PluginInstrument (.h/.cpp)     |
| - OutOfProcessScanner (.h/.cpp)  |     | - PluginEffect (.h/.cpp)         |
| - abi_pluginscanner.cpp          |     | - PluginEditorWindow (.h/.cpp)   |
+----------------+-----------------+     | - NotaPlayHead (.h/.cpp)         |
                 |                       +----------------------------------+
                 | spawns / IPC
                 v
+----------------------------------+
|         nota-scanworker          |
|   (pluginhost/scanner/worker/)   |
| - PluginScannerWorker (.h/.cpp)  |
| - main.cpp                       |
+----------------------------------+
```
