// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Instrument-wide MIDI controllers (specs/020): the sustain pedal, mod wheel, any CC, the
// pitch wheel and channel pressure, addressed by one id (`ctl`, NOTA_CTL_* in nota_engine.h):
// 0..127 the CC, 128 the pitch wheel, 129 channel pressure. Values are normalised — a CC
// 0..1 (data / 127), the wheel −1..+1, pressure 0..1.
//
// A MIDI clip keeps one breakpoint curve per controller it uses (ControllerCurve), recorded
// with the notes or drawn in the piano roll. Curves are immutable once built and shared by
// pointer, so cloning a track (every recording poll, every undo checkpoint) copies none of
// their points. Switch controllers (CC64–69: sustain, portamento, sostenuto, soft, legato,
// hold 2) hold their value until the next point; the rest are linear between points.
//
// Inside the engine a controller travels on the expression wire (ExprEv / MidiEvent) with
// pitch −1 and dim kCtlDimBase + ctl, and reaches the instrument through
// Instrument::controller.

#pragma once

#include "Automation.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <memory>
#include <vector>

namespace nota {

inline constexpr int32_t kCtlPitchWheel = 128;
inline constexpr int32_t kCtlChannelPressure = 129;
inline constexpr int32_t kCtlCount = 130;
inline constexpr int32_t kCtlSustain = 64;
inline constexpr int32_t kCtlModWheel = 1;
// Controllers ride the expression wire above every per-note / instrument-wide dim.
inline constexpr int32_t kCtlDimBase = 16;

// Data entry / RPN / NRPN (their meaning comes from the messages around them) and the channel
// mode messages: never recorded, drawn or automated.
inline bool ctlIsReserved(int32_t ctl) {
    if (ctl < 0 || ctl >= kCtlCount) return true;
    return ctl == 6 || ctl == 38 || (ctl >= 96 && ctl <= 101) || (ctl >= 120 && ctl <= 127);
}
inline bool ctlIsSwitch(int32_t ctl) { return ctl >= 64 && ctl <= 69; }
inline float ctlMin(int32_t ctl) { return ctl == kCtlPitchWheel ? -1.0f : 0.0f; }
inline float ctlClamp(int32_t ctl, float v) { return std::clamp(v, ctlMin(ctl), 1.0f); }
// Where a controller rests: pedal up, wheel centred, nothing pressed.
inline constexpr float kCtlRest = 0.0f;

struct ControllerCurve {
    int32_t ctl = kCtlSustain;
    std::vector<AutomationPoint> points;   // sorted by beat (clip-local, may be < 0), one per beat

    // Held before the first and after the last point; a switch takes the last point at or
    // before `beat`, the rest interpolate with the automation curve shaping.
    float valueAt(double beat) const {
        const auto& p = points;
        if (p.empty()) return kCtlRest;
        if (beat <= p.front().beat) return p.front().value;
        if (beat >= p.back().beat) return p.back().value;
        auto it = std::upper_bound(p.begin(), p.end(), beat, [](double b, const AutomationPoint& a) { return b < a.beat; });
        const AutomationPoint& b = *it; const AutomationPoint& a = *(it - 1);
        if (ctlIsSwitch(ctl)) return a.value;
        const double span = b.beat - a.beat;
        if (span <= 0.0) return b.value;
        return static_cast<float>(a.value + (b.value - a.value) * AutomationLane::shape((beat - a.beat) / span, a.curve));
    }
    // Whether the curve moves anywhere in [b0, b1) (a linear stretch between differing points).
    bool movesIn(double b0, double b1) const {
        if (ctlIsSwitch(ctl) || points.size() < 2) return false;
        for (size_t i = 1; i < points.size(); ++i)
            if (points[i].beat > b0 && points[i - 1].beat < b1 && points[i].value != points[i - 1].value) return true;
        return false;
    }
};

// Sort by beat and keep the last point given for any one beat.
inline void normalizeCtlPoints(std::vector<AutomationPoint>& p) {
    std::stable_sort(p.begin(), p.end(), [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    std::vector<AutomationPoint> out;
    out.reserve(p.size());
    for (const auto& x : p) {
        if (!std::isfinite(x.beat) || !std::isfinite(x.value)) continue;
        if (!out.empty() && std::fabs(out.back().beat - x.beat) < 1e-9) out.back() = x;
        else out.push_back(x);
    }
    p.swap(out);
}

// A switch keeps only its changes.
inline std::vector<AutomationPoint> dedupeSwitch(const std::vector<AutomationPoint>& p) {
    std::vector<AutomationPoint> out;
    for (const auto& x : p) if (out.empty() || out.back().value != x.value) out.push_back(x);
    return out;
}

// Ramer–Douglas–Peucker over breakpoints (first and last always kept): the fewest points that
// stay within `tol` of a block-rate recording — the shape the gesture had.
inline std::vector<AutomationPoint> thinCtlCurve(const std::vector<AutomationPoint>& p, float tol) {
    if (p.size() <= 2) return p;
    std::vector<char> keep(p.size(), 0);
    keep.front() = keep.back() = 1;
    std::vector<std::pair<size_t, size_t>> stack{ { 0, p.size() - 1 } };
    while (!stack.empty()) {
        auto [a, b] = stack.back(); stack.pop_back();
        if (b <= a + 1) continue;
        float worst = -1.0f; size_t at = a;
        for (size_t i = a + 1; i < b; ++i) {
            const double span = p[b].beat - p[a].beat;
            const double t = span > 0.0 ? (p[i].beat - p[a].beat) / span : 0.0;
            const float d = std::fabs(p[i].value - static_cast<float>(p[a].value + (p[b].value - p[a].value) * t));
            if (d > worst) { worst = d; at = i; }
        }
        if (worst > tol) { keep[at] = 1; stack.push_back({ a, at }); stack.push_back({ at, b }); }
    }
    std::vector<AutomationPoint> out;
    for (size_t i = 0; i < p.size(); ++i) if (keep[i]) out.push_back(p[i]);
    return out;
}

// Recording tolerances: about one 7-bit step for a CC, ~1% of the wheel's travel — the capture
// is block-timed, so a finer tolerance would only keep the block grid's jitter.
inline float ctlThinTolerance(int32_t ctl) { return ctl == kCtlPitchWheel ? 0.012f : 0.008f; }
// A replaced range meets the curve around it in a step: the old value holds up to a hair before
// the range (and the range's last value up to a hair before the old curve takes over again).
inline constexpr double kCtlJunction = 1.0 / 1024.0;   // beats

// The curve cut to [from, to) and shifted by -from, with the value at each edge pinned so the
// piece sounds the same (split / slice / slot hand-off). Before the curve's first point nothing
// is pinned: there the controller keeps what it had.
inline std::vector<AutomationPoint> sliceCtlPoints(const ControllerCurve& c, double from, double to) {
    std::vector<AutomationPoint> out;
    if (!c.points.empty() && from >= c.points.front().beat) out.push_back({ 0.0, c.valueAt(from), 0.0f });
    for (const auto& p : c.points)
        if (p.beat > from && p.beat < to) out.push_back({ p.beat - from, p.value, p.curve });
    if (to > from && !ctlIsSwitch(c.ctl) && c.movesIn(from, to)) out.push_back({ to - from, c.valueAt(to), 0.0f });
    if (out.size() > 1 && ctlIsSwitch(c.ctl)) out = dedupeSwitch(out);
    // A piece that never leaves rest carries nothing.
    for (const auto& p : out) if (p.value != kCtlRest) return out;
    return {};
}

} // namespace nota
