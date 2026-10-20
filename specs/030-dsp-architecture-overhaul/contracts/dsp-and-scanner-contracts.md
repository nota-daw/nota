# Contracts: C ABI, Plugin Scanner Bridge & Real-Time DSP Interfaces

**Feature**: `030-dsp-architecture-overhaul` | **Date**: 2026-10-19

## 1. Public C ABI Preservation Contract (`include/nota/nota_engine.h`)

- **Invariant**: 100% of function signatures, struct layouts, typedefs, and enum constants in `include/nota/nota_engine.h` MUST remain bit-for-bit and name-for-name identical.
- **Export Control**:
  - macOS: `nota_exports.txt` (`_nota_*`)
  - Linux: `nota_exports.map` (`nota_*; local: *;`)
  - Windows: `NOTA_API` (`__declspec(dllexport)`)
- **Scanner & Hosting C ABI Symbols**:
  - `nota_pluginhost_scan`, `nota_pluginhost_plugin_count`, `nota_pluginhost_plugin_desc`, `nota_pluginhost_plugin_id`, `nota_pluginhost_index_of_id`, `nota_pluginhost_add_scan_path`, `nota_pluginhost_remove_scan_path`, `nota_pluginhost_scan_path_count`, `nota_pluginhost_scan_path` are implemented in `pluginhost/scanner/abi_pluginscanner.cpp` (`nota_pluginscanner` target) and whole-archive linked into `nota_engine`.
  - `nota_pluginhost_open_test_window` and `nota_pluginhost_pump` are implemented in `pluginhost/hosting/PluginHost.cpp` (`nota_pluginhost` target) and whole-archive linked into `nota_engine`.

---

## 2. Plugin Scanner & Hosting Bridge Contracts (`pluginhost/bridge/`)

### `PluginCatalogBridge.h` (JUCE-free C++ interface)
```cpp
#pragma once
#include <cstdint>
#include <string>
#include <vector>

namespace nota {

struct PluginCatalogEntry {
    std::string name;
    std::string formatName;
    std::string manufacturerName;
    std::string category;
    std::string fileOrIdentifier;
    std::string identifierString;
    int32_t uniqueId = 0;
    int32_t deprecatedUid = 0;
    bool isInstrument = false;
};

// Thread-safe catalog queries (safe from UI thread, engine thread, or background scan thread)
int32_t pluginCatalogCount();
bool pluginCatalogGetEntry(int32_t index, PluginCatalogEntry& outEntry);
std::string pluginCatalogGetXmlForIndex(int32_t index);
int32_t pluginCatalogIndexOfId(const char* identifier);

} // namespace nota
```

### `PluginHostBridge.h` (JUCE-free C++ interface for `Engine`)
```cpp
#pragma once
#include <cstdint>
#include <memory>

namespace nota {

class Instrument;
class Device;

std::shared_ptr<Instrument> createPluginInstrument(int32_t catalogIndex, double sampleRate, int32_t maxBlock);
std::shared_ptr<Device> createPluginEffect(int32_t catalogIndex, double sampleRate, int32_t maxBlock);

} // namespace nota
```

---

## 3. Real-Time `Device` and `Instrument` Contracts (`src/devices/`)

### `Device.h`
```cpp
class Device {
public:
    virtual ~Device() = default;

    // Message-thread preparation (the ONLY place buffers may be allocated).
    virtual void setSampleRate(double sr, int32_t maxBlock) = 0;

    // Real-time audio processing (zero allocations, zero locks).
    virtual void process(float* buf, int32_t frames) NOTA_RT_SAFE = 0;

    // Real-time audio processing with upstream silence hint.
    virtual void process(float* buf, int32_t frames, bool inputSilent) NOTA_RT_SAFE;

    // Auto-sleep & tail lifecycle
    virtual bool isIdle() const noexcept { return false; }
    virtual bool hasTail() const noexcept { return false; }
    virtual void wake() noexcept {}
    // ... existing parameter, state, scope, sidechain, and transport methods preserved ...
};
```

### `Instrument.h`
```cpp
class Instrument {
public:
    virtual ~Instrument() = default;

    virtual void setSampleRate(double sr) = 0;
    virtual void noteOn(int32_t pitch, float velocity) NOTA_RT_SAFE = 0;
    virtual void noteOff(int32_t pitch) NOTA_RT_SAFE = 0;
    virtual void allNotesOff() NOTA_RT_SAFE = 0;
    virtual void render(float* out, int32_t frames) NOTA_RT_SAFE = 0;

    // Fast idle query: true when no voices or release tails are active.
    virtual bool isIdle() const noexcept { return activeVoiceCount() == 0; }
    // ... existing MPE, controller, parameter, state, and scope methods preserved ...
};
```

---

## 4. `FastMath`, `Simd` & `SimdAudio` Contracts (`src/dsp/math/`)

```cpp
namespace nota::fast_math {
    float exp2f(float x) noexcept;       // [-60, 60], rel error < 1.5e-5
    float log2f(float x) noexcept;       // x > 0, rel error < 1.2e-4, clamped at <= 1e-15f
    float dbToLin(float db) noexcept;
    float linToDb(float lin) noexcept;
    float tanhRatio(float v) noexcept;   // tanh(v)/v
    float tanh(float v) noexcept;        // rational Padé saturation
    float tanhPrecise(float x) noexcept; // [7/6] Padé, rel error < 0.005%
    float sin(float phase01) noexcept;   // normalized phase [0, 1), max err < 0.0007
    float cos(float phase01) noexcept;   // normalized phase [0, 1)
    float tanPrewarp(float x) noexcept;  // Padé tan for bilinear transform
}

namespace nota::simd {
    void add(float* dst, const float* src, int32_t count) noexcept;
    void addScaled(float* dst, const float* src, float scale, int32_t count) noexcept;
    void scale(float* dst, const float* src, float scale, int32_t count) noexcept;
    void mul(float* dst, const float* a, const float* b, int32_t count) noexcept;
    void clip(float* dst, const float* src, float minVal, float maxVal, int32_t count) noexcept;
    float dot16(const float* a, const float* b) noexcept;
    float maxAbs(const float* src, int32_t count) noexcept;
    float squashNonFinite(float x) noexcept;
}

namespace nota::simd_audio {
    bool isSilent(const float* src, int32_t count, float threshold = 1.0e-5f) noexcept;
    void peakAndSumSquares(const float* src, int32_t count, float& outPeak, double& outSumSquares) noexcept;
    void deinterleaveStereo(float* dstL, float* dstR, const float* srcInterleaved, int32_t frames) noexcept;
    void interleaveStereo(float* dstInterleaved, const float* srcL, const float* srcR, int32_t frames) noexcept;
    void interleaveStereoAdd(float* dstInterleaved, const float* srcL, const float* srcR, int32_t frames) noexcept;
    void scaleStereo(float* dst, const float* src, float gl, float gr, int32_t frames) noexcept;
    void addStereoScaled(float* dst, const float* src, float gl, float gr, int32_t frames) noexcept;
}
```
