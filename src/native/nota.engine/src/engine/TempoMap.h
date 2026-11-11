// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Immutable, authoring-thread compiled tempo. Render only searches/interpolates.

#pragma once
#include <algorithm>
#include <cmath>
#include <vector>
#include "nota/nota_engine.h"

namespace nota {
class TempoMap {
  public:
    struct Node { double beat, seconds; };
    std::vector<NotaTempoPoint> points;
    std::vector<Node> nodes;

    static bool valid(const std::vector<NotaTempoPoint>& p) {
        if (p.size() > 4096 || (!p.empty() && p.front().beat != 0.0)) return false;
        double prev = -1.0;
        for (const auto& a : p) {
            if (!std::isfinite(a.beat) || a.beat < 0 || a.beat > 1e8 || a.beat - prev < 1e-6 ||
                !std::isfinite(a.bpm) || a.bpm < 1 || a.bpm > 999 || a.shape < 0 || a.shape > 2) return false;
            for (double h : {a.x1, a.y1, a.x2, a.y2})
                if (!std::isfinite(h) || h < 0 || h > 1) return false;
            if (a.x1 > a.x2) return false;
            if (!std::isfinite(a.incoming_bpm) || (a.incoming_bpm != 0 && (a.incoming_bpm < 1 || a.incoming_bpm > 999))) return false;
            prev = a.beat;
        }
        return true;
    }
    static double bezier(double t, double c1, double c2) {
        const double u = 1 - t;
        return 3*u*u*t*c1 + 3*u*t*t*c2 + t*t*t;
    }
    double bpmAt(double beat) const {
        auto it = std::upper_bound(points.begin(), points.end(), beat,
            [](double b, const auto& p) { return b < p.beat; });
        if (it == points.begin()) return points.front().bpm;
        const auto& a = *--it;
        if (it + 1 == points.end() || a.shape == 0) return a.bpm;
        const auto& b = *(it + 1);
        double x = std::clamp((beat - a.beat) / (b.beat - a.beat), 0.0, 1.0);
        if (a.shape == 2) {
            double lo = 0, hi = 1;
            for (int i = 0; i < 32; ++i) {
                double t = (lo + hi) * 0.5;
                if (bezier(t, a.x1, a.x2) < x) lo = t; else hi = t;
            }
            x = bezier((lo + hi) * 0.5, a.y1, a.y2);
        }
        const double endBpm = b.incoming_bpm > 0 ? b.incoming_bpm : b.bpm;
        return a.bpm + (endBpm - a.bpm) * x;
    }
    explicit TempoMap(std::vector<NotaTempoPoint> p) : points(std::move(p)) {
        nodes.push_back({0, 0});
        for (size_t i = 0; i + 1 < points.size(); ++i) {
            const auto& a = points[i]; const auto& b = points[i + 1];
            // Adaptive Simpson integration is authoring-only. Also bound each leaf's
            // time interpolation error, so inverse conversion needs no root solving in RT.
            compile(a.beat, b.beat, 60.0 / a.bpm,
                60.0 / bpmAt((a.beat + b.beat) * .5),
                60.0 / (a.shape == 0 ? a.bpm : b.incoming_bpm > 0 ? b.incoming_bpm : b.bpm), 0);
        }
    }
    double secondsAt(double beat) const {
        if (beat <= 0) return beat * 60.0 / points.front().bpm;
        if (beat >= nodes.back().beat) return nodes.back().seconds + (beat - nodes.back().beat) * 60.0 / points.back().bpm;
        auto hi = std::upper_bound(nodes.begin(), nodes.end(), beat, [](double b, const Node& n) { return b < n.beat; });
        const auto& a = *(hi - 1); const auto& b = *hi;
        return a.seconds + (b.seconds - a.seconds) * ((beat - a.beat) / (b.beat - a.beat));
    }
    double beatAt(double seconds) const {
        if (seconds <= 0) return seconds * points.front().bpm / 60.0;
        if (seconds >= nodes.back().seconds) return nodes.back().beat + (seconds - nodes.back().seconds) * points.back().bpm / 60.0;
        auto hi = std::upper_bound(nodes.begin(), nodes.end(), seconds, [](double s, const Node& n) { return s < n.seconds; });
        const auto& a = *(hi - 1); const auto& b = *hi;
        return a.beat + (b.beat - a.beat) * ((seconds - a.seconds) / (b.seconds - a.seconds));
    }
    double clockBpm(double beat) const {
        if (beat < 0 || nodes.size() < 2) return points.front().bpm;
        if (beat >= nodes.back().beat) return points.back().bpm;
        auto hi = std::upper_bound(nodes.begin(), nodes.end(), beat, [](double b, const Node& n) { return b < n.beat; });
        const auto& a = *(hi - 1); const auto& b = *hi;
        return 60 * (b.beat - a.beat) / (b.seconds - a.seconds);
    }
  private:
    void compile(double l, double r, double fl, double fm, double fr, int depth) {
        const double m = (l + r) * .5;
        const double f1 = 60.0 / bpmAt((l + m) * .5), f3 = 60.0 / bpmAt((m + r) * .5);
        const double left = (m - l) * (fl + 4*f1 + fm) / 6;
        const double right = (r - m) * (fm + 4*f3 + fr) / 6;
        const double whole = (r - l) * (fl + 4*fm + fr) / 6;
        if (depth < 24 && (std::abs(left + right - whole) > 1e-10 || std::abs(left - right) > 1e-6)) {
            compile(l, m, fl, f1, fm, depth + 1);
            compile(m, r, fm, f3, fr, depth + 1);
        } else nodes.push_back({r, nodes.back().seconds + left + right});
    }
};
} // namespace nota
