// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// OutputRouting — Main/Cue stereo output bus resolution & scattering.

#pragma once

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <vector>

namespace nota::dsp {

inline constexpr int32_t kMaxOutputChannels = 64;

struct OutputRouting {
    int32_t mainFirst = 0;
    int32_t mainWidth = 2;
    int32_t cueFirst = -1;
    int32_t cueWidth = 2;
};

enum : int32_t {
    kRoutingMainFellBack = 1,
    kRoutingCueUnavailable = 2,
};

struct ResolvedRouting {
    int32_t mainFirst = 0;
    int32_t mainWidth = 0;
    int32_t cueFirst = 0;
    int32_t cueWidth = 0;
    int32_t flags = 0;
};

inline bool validRouting(const OutputRouting& r) noexcept {
    auto ok = [](int32_t first, int32_t width) noexcept {
        return first >= 0 && first < kMaxOutputChannels && (width == 1 || (width == 2 && first % 2 == 0));
    };
    if (!ok(r.mainFirst, r.mainWidth)) return false;
    if (r.cueFirst < 0) return true;
    if (!ok(r.cueFirst, r.cueWidth)) return false;
    return r.cueFirst + r.cueWidth <= r.mainFirst || r.mainFirst + r.mainWidth <= r.cueFirst;
}

inline uint64_t packRouting(const OutputRouting& r) noexcept {
    auto b = [](int32_t v) noexcept { return static_cast<uint64_t>(static_cast<uint16_t>(static_cast<int16_t>(v))); };
    return b(r.mainFirst) | (b(r.mainWidth) << 16u) | (b(r.cueFirst) << 32u) | (b(r.cueWidth) << 48u);
}

inline OutputRouting unpackRouting(uint64_t p) noexcept {
    auto f = [&](int32_t shift) noexcept {
        return static_cast<int32_t>(static_cast<int16_t>(static_cast<uint16_t>(p >> shift)));
    };
    return OutputRouting{f(0), f(16), f(32), f(48)};
}

inline ResolvedRouting resolveRouting(const OutputRouting& r, int32_t deviceChannels) noexcept {
    ResolvedRouting out;
    const int32_t n = std::clamp(deviceChannels, 0, kMaxOutputChannels);
    if (n <= 0) {
        if (r.cueFirst >= 0) out.flags |= kRoutingCueUnavailable;
        return out;
    }
    auto fit = [n](int32_t first, int32_t width) noexcept {
        return first < 0 || first >= n ? 0 : std::min(width, n - first);
    };

    out.mainFirst = r.mainFirst;
    out.mainWidth = fit(r.mainFirst, r.mainWidth);
    if (out.mainWidth == 0) {
        out.mainFirst = 0;
        out.mainWidth = std::min(2, n);
        out.flags |= kRoutingMainFellBack;
    }
    if (r.cueFirst >= 0) {
        const int32_t w = fit(r.cueFirst, r.cueWidth);
        const bool overlaps = w > 0 && r.cueFirst < out.mainFirst + out.mainWidth && out.mainFirst < r.cueFirst + w;
        if (w == 0 || overlaps) {
            out.flags |= kRoutingCueUnavailable;
        } else {
            out.cueFirst = r.cueFirst;
            out.cueWidth = w;
        }
    }
    return out;
}

inline void scatterOutputs(const float* main, const float* cue, float* device, int32_t deviceChannels, int32_t frames,
    const ResolvedRouting& r) noexcept {
    const int32_t ch = deviceChannels;
    if (ch <= 0 || frames <= 0 || !device) return;
    std::memset(device, 0, sizeof(float) * static_cast<std::size_t>(frames) * static_cast<std::size_t>(ch));
    auto place = [&](const float* src, int32_t first, int32_t width) noexcept {
        if (!src || width <= 0 || first < 0 || first + width > ch) return;
        float* d = device + first;
        if (width == 2) {
            for (int32_t i = 0; i < frames; ++i, d += ch) {
                d[0] = src[i * 2];
                d[1] = src[i * 2 + 1];
            }
        } else {
            for (int32_t i = 0; i < frames; ++i, d += ch) { d[0] = 0.5f * (src[i * 2] + src[i * 2 + 1]); }
        }
    };
    place(main, r.mainFirst, r.mainWidth);
    place(cue, r.cueFirst, r.cueWidth);
}

class OutputStage {
  public:
    static constexpr int32_t kChunkFrames = 4096;

    OutputStage()
        : main_(static_cast<std::size_t>(kChunkFrames) * 2u, 0.0f),
          cue_(static_cast<std::size_t>(kChunkFrames) * 2u, 0.0f) {}

    void setRouting(const OutputRouting& r) noexcept { routing_.store(packRouting(r), std::memory_order_relaxed); }
    OutputRouting routing() const noexcept { return unpackRouting(routing_.load(std::memory_order_relaxed)); }

    template <class Render> void process(Render& render, float* device, int32_t deviceChannels, int32_t frames) {
        const ResolvedRouting r = resolveRouting(routing(), deviceChannels);
        for (int32_t done = 0; done < frames;) {
            const int32_t n = std::min(frames - done, kChunkFrames);
            render(main_.data(), cue_.data(), n);
            scatterOutputs(main_.data(), r.cueWidth > 0 ? cue_.data() : nullptr,
                device + static_cast<std::size_t>(done) * static_cast<std::size_t>(deviceChannels), deviceChannels, n,
                r);
            done += n;
        }
    }

  private:
    std::atomic<uint64_t> routing_{packRouting(OutputRouting{})};
    std::vector<float> main_;
    std::vector<float> cue_;
};

} // namespace nota::dsp
