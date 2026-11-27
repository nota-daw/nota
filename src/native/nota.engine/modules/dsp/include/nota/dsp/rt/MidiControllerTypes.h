// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MidiControllerTypes — instrument-wide MIDI controllers, switch detection, curves.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/AutomationTypes.h"

#include <algorithm>
#include <cstdint>
#include <vector>

namespace nota::dsp {

inline constexpr int32_t kCtlPitchWheel = 128;
inline constexpr int32_t kCtlChannelPressure = 129;
inline constexpr int32_t kCtlCount = 130;
inline constexpr int32_t kCtlSustain = 64;
inline constexpr int32_t kCtlModWheel = 1;
inline constexpr int32_t kCtlDimBase = 16;
inline constexpr float kCtlRest = 0.0f;
inline constexpr double kCtlJunction = 1.0 / 1024.0;

inline bool ctlIsReserved(int32_t ctl) noexcept {
    if (ctl < 0 || ctl >= kCtlCount) return true;
    return ctl == 6 || ctl == 38 || (ctl >= 96 && ctl <= 101) || (ctl >= 120 && ctl <= 127);
}

inline bool ctlIsSwitch(int32_t ctl) noexcept {
    return ctl >= 64 && ctl <= 69;
}
inline float ctlMin(int32_t ctl) noexcept {
    return ctl == kCtlPitchWheel ? -1.0f : 0.0f;
}
inline float ctlClamp(int32_t ctl, float v) noexcept {
    return std::clamp(v, ctlMin(ctl), 1.0f);
}
inline float ctlThinTolerance(int32_t ctl) noexcept {
    return ctl == kCtlPitchWheel ? 0.012f : 0.008f;
}

struct NOTA_DSP_API ControllerCurve {
    int32_t ctl = kCtlSustain;
    std::vector<AutomationPoint> points;

    float valueAt(double beat) const;
    bool movesIn(double b0, double b1) const;
};

NOTA_DSP_API void normalizeCtlPoints(std::vector<AutomationPoint>& p);

inline std::vector<AutomationPoint> dedupeSwitch(const std::vector<AutomationPoint>& p) {
    std::vector<AutomationPoint> out;
    for (const auto& x : p) {
        if (out.empty() || out.back().value != x.value) out.push_back(x);
    }
    return out;
}

NOTA_DSP_API float ctlPieceCurve(const std::vector<AutomationPoint>& p, double from, double to);
NOTA_DSP_API std::vector<AutomationPoint> thinCtlCurve(const std::vector<AutomationPoint>& p, float tol);
NOTA_DSP_API std::vector<AutomationPoint> sliceCtlPoints(const ControllerCurve& c, double from, double to);

} // namespace nota::dsp
