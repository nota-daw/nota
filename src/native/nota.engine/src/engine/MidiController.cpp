// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "MidiController.h"

#include <utility>

namespace nota {

float ControllerCurve::valueAt(double beat) const {
    const auto& p = points;
    if (p.empty()) return kCtlRest;
    if (beat <= p.front().beat) return p.front().value;
    if (beat >= p.back().beat) return p.back().value;
    auto it = std::upper_bound(p.begin(), p.end(), beat, [](double b, const AutomationPoint& a) { return b < a.beat; });
    const AutomationPoint& b = *it;
    const AutomationPoint& a = *(it - 1);
    if (ctlIsSwitch(ctl)) return a.value;
    const double span = b.beat - a.beat;
    if (span <= 0.0) return b.value;
    return static_cast<float>(a.value + (b.value - a.value) * AutomationLane::shape((beat - a.beat) / span, a.curve));
}

bool ControllerCurve::movesIn(double b0, double b1) const {
    if (ctlIsSwitch(ctl) || points.size() < 2) return false;
    for (size_t i = 1; i < points.size(); ++i)
        if (points[i].beat > b0 && points[i - 1].beat < b1 && points[i].value != points[i - 1].value) return true;
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
    if (p.size() <= 2) return p;
    std::vector<char> keep(p.size(), 0);
    keep.front() = keep.back() = 1;
    std::vector<std::pair<size_t, size_t>> stack{{0, p.size() - 1}};
    while (!stack.empty()) {
        auto [a, b] = stack.back();
        stack.pop_back();
        if (b <= a + 1) continue;
        float worst = -1.0f;
        size_t at = a;
        for (size_t i = a + 1; i < b; ++i) {
            const double span = p[b].beat - p[a].beat;
            const double t = span > 0.0 ? (p[i].beat - p[a].beat) / span : 0.0;
            const float d = std::fabs(p[i].value - static_cast<float>(p[a].value + (p[b].value - p[a].value) * t));
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
    for (size_t i = 0; i < p.size(); ++i)
        if (keep[i]) out.push_back(p[i]);
    return out;
}

std::vector<AutomationPoint> sliceCtlPoints(const ControllerCurve& c, double from, double to) {
    std::vector<AutomationPoint> out;
    if (!c.points.empty() && from >= c.points.front().beat) out.push_back({0.0, c.valueAt(from), 0.0f});
    for (const auto& p : c.points)
        if (p.beat > from && p.beat < to) out.push_back({p.beat - from, p.value, p.curve});
    if (to > from && !ctlIsSwitch(c.ctl) && c.movesIn(from, to)) out.push_back({to - from, c.valueAt(to), 0.0f});
    if (out.size() > 1 && ctlIsSwitch(c.ctl)) out = dedupeSwitch(out);
    // A piece that never leaves rest carries nothing.
    for (const auto& p : out)
        if (p.value != kCtlRest) return out;
    return {};
}

} // namespace nota
