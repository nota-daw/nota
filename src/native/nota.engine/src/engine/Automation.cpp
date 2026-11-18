// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "Automation.h"

#include <algorithm>

namespace nota {

float AutomationLane::valueAt(double beat) const {
    const auto& p = points;
    if (beat <= p.front().beat) return p.front().value;
    if (beat >= p.back().beat) return p.back().value;
    for (size_t i = 1; i < p.size(); ++i) {
        if (beat <= p[i].beat) {
            const double b0 = p[i - 1].beat, b1 = p[i].beat;
            const float v0 = p[i - 1].value, v1 = p[i].value;
            const double span = b1 - b0;
            if (span <= 0.0) return v1;
            const double t = (beat - b0) / span;
            return static_cast<float>(v0 + (v1 - v0) * shape(t, p[i - 1].curve));
        }
    }
    return p.back().value;
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
