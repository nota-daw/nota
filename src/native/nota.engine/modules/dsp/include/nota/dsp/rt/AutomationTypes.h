// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AutomationTypes — breakpoint automation lanes and per-block step sampler.

#pragma once

#include "nota/dsp/Export.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace nota::dsp {

enum class AutomationTarget : int32_t {
    Volume = 0,
    Pan = 1,
    DeviceParam = 2,
    PluginParam = 3,
    MidiDeviceParam = 4,
    Send = 5,
    Tempo = 6,
};

inline bool isMixTarget(AutomationTarget t) noexcept {
    return t == AutomationTarget::Volume || t == AutomationTarget::Pan;
}

struct AutomationPoint {
    double beat = 0.0;
    float value = 0.0f;
    float curve = 0.0f;
};

struct NOTA_DSP_API AutomationLane {
    AutomationTarget target = AutomationTarget::Volume;
    int32_t deviceIndex = -1;
    int32_t paramIndex = -1;
    std::string paramId;
    bool suppressRead = false;
    std::vector<AutomationPoint> points;

    float valueAt(double beat) const;

    static constexpr double kCurveSteepness = 8.0;

    static double shape(double t, float curve) noexcept {
        const double k = -static_cast<double>(curve) * kCurveSteepness;
        if (std::fabs(k) < 1e-6) return t;
        return std::expm1(k * t) / std::expm1(k);
    }

    static float subCurve(float curve, double fraction) noexcept {
        return static_cast<float>(static_cast<double>(curve) * std::clamp(fraction, 0.0, 1.0));
    }

    static float pieceCurve(const std::vector<AutomationPoint>& p, double from, double to);
};

struct TrackLane : AutomationLane {
    uint64_t chainUid = 0;
    int32_t slot = -1;
    bool nested() const noexcept { return chainUid != 0u; }
};

class ParamLaneSteps {
  public:
    static constexpr int32_t kMax = 512;

    void fill(const AutomationLane& lane, double beatFrom, double beatTo, double stepBeats) {
        n_ = 0;
        from_ = beatFrom;
        to_ = beatTo;
        if (lane.points.empty() || !(stepBeats > 0.0) || !(beatTo >= beatFrom)) return;
        first_ = static_cast<int64_t>(std::ceil(beatFrom / stepBeats - 1e-9)) - 1;
        const int64_t last = static_cast<int64_t>(std::ceil(beatTo / stepBeats - 1e-9)) - 1;
        const auto& p = lane.points;
        std::size_t seg = 1;
        for (int64_t k = first_; k <= last && n_ < kMax; ++k) {
            const double b = static_cast<double>(k) * stepBeats + kAfter;
            float v = 0.0f;
            if (b <= p.front().beat) {
                v = p.front().value;
            } else if (b >= p.back().beat) {
                v = p.back().value;
            } else {
                while (seg < p.size() && b > p[seg].beat) ++seg;
                const double b0 = p[seg - 1u].beat;
                const double span = p[seg].beat - b0;
                const float v0 = p[seg - 1u].value;
                const float v1 = p[seg].value;
                v = span <= 0.0 ? v1
                                : static_cast<float>(static_cast<double>(v0) +
                                                     static_cast<double>(v1 - v0) *
                                                         AutomationLane::shape((b - b0) / span, p[seg - 1u].curve));
            }
            v_[n_++] = v;
        }
    }

    bool at(int64_t k, float& out) const noexcept {
        const int64_t i = k - first_;
        if (i < 0 || i >= n_) return false;
        out = v_[i];
        return true;
    }

    void clear() noexcept { n_ = 0; }

    void transport(double blockBeat) noexcept {
        if (n_ != 0 && std::fabs(blockBeat - from_) > 1e-6) n_ = 0;
    }

    void rendered(double beatNow, bool playing) noexcept {
        if (n_ != 0 && (!playing || beatNow >= to_ - 1e-6)) n_ = 0;
    }

  private:
    static constexpr double kAfter = 1e-7;
    double from_ = 0.0;
    double to_ = 0.0;
    int64_t first_ = 0;
    int32_t n_ = 0;
    float v_[kMax] = {};
};

} // namespace nota::dsp
