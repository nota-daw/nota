// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "MidiClip.h"

namespace nota {

float NoteExpr::valueAt(int32_t d, double beat) const {
    const auto& p = dim[d];
    if (p.empty()) return kExprNeutral[d];
    if (beat <= p.front().beat) return p.front().value;
    if (beat >= p.back().beat) return p.back().value;
    auto it = std::upper_bound(p.begin(), p.end(), beat, [](double b, const ExprPoint& e) { return b < e.beat; });
    const ExprPoint& b = *it;
    const ExprPoint& a = *(it - 1);
    const double t = b.beat > a.beat ? (beat - a.beat) / (b.beat - a.beat) : 1.0;
    return static_cast<float>(a.value + (b.value - a.value) * t);
}

std::vector<ExprPoint> thinExprCurve(const std::vector<ExprPoint>& p, float tol) {
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
            const float span = p[b].beat - p[a].beat;
            const float t = span > 0.0f ? (p[i].beat - p[a].beat) / span : 0.0f;
            const float d = std::fabs(p[i].value - (p[a].value + (p[b].value - p[a].value) * t));
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
    std::vector<ExprPoint> out;
    for (size_t i = 0; i < p.size(); ++i)
        if (keep[i]) out.push_back(p[i]);
    return out;
}

void MidiClip::setController(int32_t ctl, std::vector<AutomationPoint> pts) {
    normalizeCtlPoints(pts);
    auto it = std::find_if(controllers.begin(), controllers.end(), [&](const auto& c) { return c && c->ctl == ctl; });
    if (pts.empty()) {
        if (it != controllers.end()) controllers.erase(it);
        return;
    }
    auto c = std::make_shared<ControllerCurve>();
    c->ctl = ctl;
    c->points = std::move(pts);
    if (it != controllers.end()) {
        *it = std::move(c);
        return;
    }
    auto at = std::find_if(controllers.begin(), controllers.end(), [&](const auto& x) { return x && x->ctl > ctl; });
    controllers.insert(at, std::move(c));
}

void MidiClip::sliceControllersFrom(const MidiClip& src, double from, double len) {
    controllers.clear();
    for (const auto& c : src.controllers) {
        if (!c) continue;
        std::vector<AutomationPoint> p;
        if (from <= 0.0) {
            for (const auto& x : c->points)
                if (x.beat < len) p.push_back(x);
        } else
            p = sliceCtlPoints(*c, from, from + len);
        if (!p.empty()) setController(c->ctl, std::move(p));
    }
}

void MidiClip::shiftControllers(double delta) {
    if (delta == 0.0) return;
    for (auto& c : controllers) {
        if (!c) continue;
        auto n = std::make_shared<ControllerCurve>(*c);
        for (auto& p : n->points) p.beat += delta;
        c = std::move(n);
    }
}

} // namespace nota
