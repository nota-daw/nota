// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Immutable, authoring-thread compiled tempo. Render only searches/interpolates.
// Built from the master track's Tempo automation lane (specs/099): each segment follows
// the lane's own curve (AutomationLane::shape), so what is drawn is what plays.

#pragma once
#include <algorithm>
#include <cmath>
#include <memory>
#include <vector>
#include "Automation.h"

namespace nota {
class TempoMap {
  public:
    static constexpr double kMinBpm = 20.0, kMaxBpm = 300.0;
    static constexpr size_t kMaxPoints = 4096;
    // incomingBpm is the tempo arriving at `beat` from the previous segment (a jump when it
    // differs from bpm); curve shapes the segment from this point to the next.
    struct Point { double beat, bpm, incomingBpm; float curve; };
    struct Node { double beat, seconds; };
    std::vector<AutomationPoint> source; // the lane points this map was compiled from
    std::vector<Point> points;
    std::vector<Node> nodes;

    static double clampBpm(double v) { return std::isfinite(v) ? std::clamp(v, kMinBpm, kMaxBpm) : kMinBpm; }

    static bool valid(const std::vector<Point>& p) {
        if (p.empty() || p.size() > kMaxPoints || p.front().beat != 0.0) return false;
        double prev = -1.0;
        for (const auto& a : p) {
            if (!std::isfinite(a.beat) || a.beat > 1e8 || a.beat - prev < 1e-6) return false;
            if (!(a.bpm >= kMinBpm && a.bpm <= kMaxBpm) || !(a.incomingBpm >= kMinBpm && a.incomingBpm <= kMaxBpm)) return false;
            if (!(a.curve >= -1.0f && a.curve <= 1.0f)) return false;
            prev = a.beat;
        }
        return true;
    }

    // Lane points (sorted by beat, as lanes keep them) -> map points. A same-beat pair is a
    // jump (more than two on one beat: the first and the last count, as AutomationLane::valueAt
    // reads them); a first point after 0 holds back to 0. nullptr for an empty or unusable lane.
    static std::shared_ptr<const TempoMap> fromLane(const std::vector<AutomationPoint>& lane) {
        std::vector<Point> p;
        p.reserve(lane.size() + 1);
        for (size_t i = 0; i < lane.size(); ++i) {
            const auto& a = lane[i];
            if (!std::isfinite(a.beat)) return nullptr;
            const double beat = std::max(0.0, a.beat);
            const double v = clampBpm(a.value);
            const float curve = std::isfinite(a.curve) ? std::clamp(a.curve, -1.0f, 1.0f) : 0.0f;
            if (!p.empty() && beat - p.back().beat < 1e-6) { // same beat: the later point is the outgoing side
                p.back().bpm = v;
                p.back().curve = curve;
                continue;
            }
            p.push_back({beat, v, v, curve});
        }
        if (p.empty()) return nullptr;
        if (p.front().beat > 0.0) p.insert(p.begin(), Point{0.0, p.front().incomingBpm, p.front().incomingBpm, 0.0f});
        if (p.size() > kMaxPoints || !valid(p)) return nullptr;
        auto map = std::make_shared<TempoMap>(std::move(p));
        map->source = lane;
        return map;
    }

    double bpmAt(double beat) const {
        auto it = std::upper_bound(points.begin(), points.end(), beat,
            [](double b, const auto& p) { return b < p.beat; });
        if (it == points.begin()) return points.front().bpm;
        const auto& a = *--it;
        if (it + 1 == points.end()) return a.bpm;
        const auto& b = *(it + 1);
        const double x = std::clamp((beat - a.beat) / (b.beat - a.beat), 0.0, 1.0);
        return a.bpm + (b.incomingBpm - a.bpm) * AutomationLane::shape(x, a.curve);
    }
    explicit TempoMap(std::vector<Point> p) : points(std::move(p)) {
        nodes.push_back({0, 0});
        for (size_t i = 0; i + 1 < points.size(); ++i) {
            const auto& a = points[i]; const auto& b = points[i + 1];
            // Adaptive Simpson integration is authoring-only. Also bound each leaf's
            // time interpolation error, so inverse conversion needs no root solving in RT.
            seg_ = i; // the segment compile() evaluates (no search per sample point)
            compile(a.beat, b.beat, 60.0 / a.bpm, 60.0 / segBpm((a.beat + b.beat) * .5), 60.0 / b.incomingBpm, 0);
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
    size_t seg_ = 0;
    // bpmAt() inside segment seg_ — the same formula, without the search.
    double segBpm(double beat) const {
        const auto& a = points[seg_]; const auto& b = points[seg_ + 1];
        const double x = std::clamp((beat - a.beat) / (b.beat - a.beat), 0.0, 1.0);
        return a.bpm + (b.incomingBpm - a.bpm) * AutomationLane::shape(x, a.curve);
    }
    void compile(double l, double r, double fl, double fm, double fr, int depth) {
        const double m = (l + r) * .5;
        const double f1 = 60.0 / segBpm((l + m) * .5), f3 = 60.0 / segBpm((m + r) * .5);
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
