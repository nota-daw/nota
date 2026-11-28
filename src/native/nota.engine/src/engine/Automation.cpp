// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "Automation.h"

#include <algorithm>

namespace nota {

float AutomationLane::valueAt(double beat) const {
    const auto& p = points;
    if (beat <= p.front().beat) return p.front().value;
    if (beat >= p.back().beat) return p.back().value;
    // Mirror the original linear search: first i >= 1 with p[i].beat >= beat (O(log P)).
    const auto it = std::lower_bound(p.begin() + 1, p.end(), beat,
        [](const AutomationPoint& q, double b) { return q.beat < b; });
    if (it == p.end()) return p.back().value;
    const AutomationPoint& prev = *(it - 1);
    const AutomationPoint& cur = *it;
    const double span = cur.beat - prev.beat;
    if (span <= 0.0) return cur.value;
    const double t = (beat - prev.beat) / span;
    return static_cast<float>(prev.value + (cur.value - prev.value) * shape(t, prev.curve));
}

float AutomationLane::pieceCurve(const std::vector<AutomationPoint>& p, double from, double to) {
    constexpr double eps = 1e-9; // a cut a hair before a point belongs to that point's segment
    const auto next = std::upper_bound(p.begin(), p.end(), from + eps,
                                       [](double b, const AutomationPoint& q) { return b < q.beat; });
    if (next == p.begin() || next == p.end()) return 0.0f;
    const AutomationPoint& a = *(next - 1);
    const double span = next->beat - a.beat;
    if (span <= 0.0 || a.curve == 0.0f) return 0.0f;
    return subCurve(a.curve, (std::min(to, next->beat) - std::max(from, a.beat)) / span);
}

} // namespace nota
