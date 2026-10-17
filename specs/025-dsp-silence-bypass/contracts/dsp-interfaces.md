# Contracts: DSP Silence Detection & Sleep Interfaces

## 1. Buffer Silence Check & Activity API (`AudioBufferUtils.h`)

```cpp
namespace nota {

// Fast, early-exiting peak scan to detect whether an interleaved stereo audio buffer
// contains signal exceeding the silence threshold (default 1e-5f = -100 dBFS).
inline bool isBufferSilent(const float* interleaved, int32_t frames, float threshold = 1.0e-5f);

enum class SignalState : uint8_t {
    Active,
    PossiblySilent,
    Silent
};

// Stateful activity detector with dual-threshold hysteresis (attack vs release)
// and sample-accurate hold time to prevent chattering around the noise floor.
struct SilenceDetector {
    double attackThreshold = 1.0e-4;   // RMS ~ -80 dBFS (wake-up threshold)
    double releaseThreshold = 3.0e-5;  // RMS ~ -90.5 dBFS (sleep entry threshold)
    uint64_t holdSamples = 0;          // e.g. 150 ms * sampleRate
    uint64_t silentSamples = 0;
    SignalState state = SignalState::Silent;

    void setSampleRate(double sr, double holdMs = 150.0);
    void reset(SignalState initialState = SignalState::Silent);
    bool isSilent() const;
    bool isActive() const;
    SignalState process(const float* interleaved, int32_t frames);
};

} // namespace nota
```

## 2. Device Interface Contract (`Device.h`)

```cpp
namespace nota {

class Device {
public:
    // Process stereo interleaved buffer in place.
    virtual void process(float* buf, int32_t frames) = 0;

    // Process with caller-provided silence hint. When inputSilent is true, the caller has
    // already verified that `buf` contains silence. Devices avoid re-scanning the buffer.
    virtual void process(float* buf, int32_t frames, bool inputSilent) {
        (void)inputSilent;
        process(buf, frames);
    }

    // Returns whether the device is currently in sleep state and producing zero output.
    virtual bool isIdle() const { return false; }

    // Returns whether the device generates an acoustic/filter tail after input ceases.
    virtual bool hasTail() const { return false; }

    // Explicitly wakes the device from sleep state.
    virtual void wake() {}
};

} // namespace nota
```

## 3. MixGraph Invariant Contract (`Engine_Render.cpp`)

```cpp
// Within Engine::mixGraph():
// 1. Evaluate generator output with Track's SilenceDetector & isBufferSilent().
// 2. If track is fully silent and all devices + PDC are idle: skip the whole track.
// 3. Otherwise, propagate `currentSilent` metadata down the insert chain:
//    - Memoryless devices (isIdle && !hasTail) are bypassed with zero scan when currentSilent == true.
//    - Time-based devices receive `inputSilent = currentSilent` so they skip buffer re-analysis.
//    - If a device produces sound (!d->isIdle()), currentSilent drops to false for downstream nodes.
// 4. Return tracks and master track follow the identical metadata propagation pattern.
```

