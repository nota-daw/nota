// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/rt/MidiControllerTypes.h"
#include "nota/dsp/rt/AutomationTypes.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <utility>
#include <vector>

namespace nota::dsp {

float ControllerCurve::valueAt(double beat) const {
    const auto& p = points;
    if (p.empty()) return kCtlRest;
    if (beat <= p.front().beat) return p.front().value;
    if (beat >= p.back().beat) return p.back().value;
    const auto it =
        std::upper_bound(p.begin(), p.end(), beat, [](double b, const AutomationPoint& a) { return b < a.beat; });
    const AutomationPoint& b = *it;
    const AutomationPoint& a = *(it - 1);
    if (ctlIsSwitch(ctl)) return a.value;
    const double span = b.beat - a.beat;
    if (span <= 0.0) return b.value;
    return static_cast<float>(
        static_cast<double>(a.value) +
        static_cast<double>(b.value - a.value) * AutomationLane::shape((beat - a.beat) / span, a.curve));
}

bool ControllerCurve::movesIn(double b0, double b1) const {
    if (ctlIsSwitch(ctl) || points.size() < 2u) return false;
    const auto first = std::upper_bound(
        points.begin() + 1, points.end(), b0, [](double b, const AutomationPoint& q) { return b < q.beat; });
    for (auto it = first; it != points.end(); ++it) {
        const auto& prev = *(it - 1);
        if (prev.beat >= b1) break;
        if (it->value != prev.value) return true;
    }
    return false;
}

void normalizeCtlPoints(std::vector<AutomationPoint>& p) {
    std::stable_sort(
        p.begin(), p.end(), [](const AutomationPoint& a, const AutomationPoint& b) { return a.beat < b.beat; });
    std::vector<AutomationPoint> out;
    out.reserve(p.size());
    for (const auto& x : p) {
        if (!std::isfinite(x.beat) || !std::isfinite(x.value)) continue;
        if (!out.empty() && std::fabs(out.back().beat - x.beat) < 1e-9) out.back() = x;
        else
            out.push_back(x);
    }
    p.swap(out);
}

std::vector<AutomationPoint> thinCtlCurve(const std::vector<AutomationPoint>& p, float tol) {
    if (p.size() <= 2u) return p;
    std::vector<char> keep(p.size(), 0);
    keep.front() = keep.back() = 1;
    std::vector<std::pair<std::size_t, std::size_t>> stack{{0u, p.size() - 1u}};
    while (!stack.empty()) {
        auto [a, b] = stack.back();
        stack.pop_back();
        if (b <= a + 1u) continue;
        float worst = -1.0f;
        std::size_t at = a;
        for (std::size_t i = a + 1u; i < b; ++i) {
            const double span = p[b].beat - p[a].beat;
            const double t = span > 0.0 ? (p[i].beat - p[a].beat) / span : 0.0;
            const float d = std::fabs(
                p[i].value -
                static_cast<float>(static_cast<double>(p[a].value) + static_cast<double>(p[b].value - p[a].value) * t));
            if (d > worst) {
                worst = d;
                at = i;
            }
        }
        if (worst > tol) {
            keep[at] = 1;
            stack.push_back({a, at});
            stack.push_back({at, b});
        }
    }
    std::vector<AutomationPoint> out;
    for (std::size_t i = 0; i < p.size(); ++i) {
        if (keep[i] != 0) out.push_back(p[i]);
    }
    return out;
}

float ctlPieceCurve(const std::vector<AutomationPoint>& p, double from, double to) {
    return AutomationLane::pieceCurve(p, from, to);
}

std::vector<AutomationPoint> sliceCtlPoints(const ControllerCurve& c, double from, double to) {
    std::vector<AutomationPoint> out;
    double lastAt = from;
    if (!c.points.empty() && from >= c.points.front().beat)
        out.push_back({0.0, c.valueAt(from), ctlPieceCurve(c.points, from, to)});
    for (const auto& p : c.points) {
        if (p.beat > from && p.beat < to) {
            out.push_back({p.beat - from, p.value, p.curve});
            lastAt = p.beat;
        }
    }
    if (to > from && !ctlIsSwitch(c.ctl) && c.movesIn(from, to)) {
        if (!out.empty()) out.back().curve = ctlPieceCurve(c.points, lastAt, to);
        out.push_back({to - from, c.valueAt(to), 0.0f});
    }
    return out;
}

} // namespace nota::dsp
