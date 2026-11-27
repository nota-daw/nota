// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// IWavetableInstrument & WavetableAsset — shared mipmapped wavetable model and synth capability.

#pragma once

#include "nota/dsp/Export.h"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::dsp {

struct NOTA_DSP_API WavetableAsset {
    static constexpr int32_t kSize = 2048;
    static constexpr int32_t kLevels = 10;
    static constexpr int32_t kMaxFrames = 256;
    static constexpr int32_t kRecordingFrames = 16;
    static constexpr int32_t kMinLen = 256;
    static constexpr double kMaxSeconds = 60.0;

    enum Origin : int32_t { WavetableFile = 0, SingleCycle = 1, Recording = 2 };
    enum Result : int32_t { Ok = 0, ErrDecode = -1, ErrFormat = -2, ErrSilent = -3, ErrEmpty = -4, ErrArgs = -5 };

    int32_t frames = 0;
    int32_t origin = WavetableFile;
    bool truncatedLength = false;
    bool truncatedFrames = false;
    std::string name;
    uint8_t hash[16] = {};
    uint32_t serial = 0;
    std::vector<float> level[kLevels];

    static constexpr int32_t len(int32_t k) noexcept {
        const int32_t l = k == 0 ? kSize : (2 * kSize) >> k;
        return l > kSize ? kSize : (l < kMinLen ? kMinLen : l);
    }

    static constexpr int32_t harmonics(int32_t k) noexcept { return (kSize / 2) >> k; }

    const float* frame(int32_t k, int32_t f) const noexcept {
        return level[k].data() + static_cast<std::size_t>(f) * static_cast<std::size_t>(len(k));
    }
    const float* base() const noexcept { return level[0].data(); }

    static int32_t buildFromFile(const std::string& path, std::shared_ptr<const WavetableAsset>& out);
    static int32_t buildFromMono(const float* mono, int64_t n, int32_t declaredFrame, std::string name,
        bool truncatedLength, std::shared_ptr<const WavetableAsset>& out);
    static int32_t buildFromFrames(const float* frames, int32_t count, std::string name, int32_t origin,
        std::shared_ptr<const WavetableAsset>& out);
};

struct WavetableSet {
    std::shared_ptr<const WavetableAsset> slot[2];
    std::string missing[2];
};

namespace capabilities {

class NOTA_DSP_API IWavetableInstrument {
  public:
    virtual ~IWavetableInstrument();

    virtual void setWavetable(int32_t osc, std::shared_ptr<const WavetableAsset> t) = 0;
    virtual void clearWavetable(int32_t osc) = 0;
    virtual void setWavetableSource(int32_t osc, bool custom) = 0;
    virtual void setWavetableMissing(int32_t osc, const std::string& name) = 0;
    virtual std::shared_ptr<const WavetableAsset> wavetable(int32_t osc) const = 0;
    virtual bool usesCustomWavetable(int32_t osc) const = 0;
    virtual std::string missingWavetableName(int32_t osc) const = 0;
    virtual std::vector<uint8_t> serializeState(bool includeTables) const = 0;
};

} // namespace capabilities
} // namespace nota::dsp
