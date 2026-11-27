// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ClipOps — timeline remap and arrangement section move/duplicate/delete with content.

#include "clips/ClipHelpers.h"
#include "clips/ClipOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "core/TempoMap.h"
#include "core/Transport.h"
#include "model/Graph.h"
#include "model/MidiClip.h"
#include "model/Track.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <map>
#include <memory>
#include <set>
#include <utility>
#include <vector>

namespace nota::detail {

using namespace nota::detail::clip_helpers;

// --- section edits with content (specs/073) ----------------------------------------
// Moving, duplicating or deleting a section carries everything in its span. A move is a
// remap of a window [lo,hi) tiled by regions that each shift by their own offset (the moved
// section one way, the material it passes the other), so clips, lanes, takes and sections
// all follow the same map and cannot drift apart. Clips are cut where a region ends, like
// Insert silence; each lane plays every region's own curve, stepping at a join.
namespace {

constexpr double kSecEps = 1e-6;

// A partial take's first played frame moves by beats; its frame-zero origin must
// be solved in seconds so later comp swipes still address the same source audio.
void shiftTake(AudioTake& take, double offset) {
    if (take.tempoMap && take.sample && take.sample->sourceSampleRate > 0.0) {
        const double start = take.beatAtFrame(take.sourceOffsetFrames) + offset;
        take.originBeat = take.tempoMap->beatAt(
            take.tempoMap->secondsAt(start) - take.sourceOffsetFrames / take.sample->sourceSampleRate);
    } else
        take.originBeat += offset;
}

const TimeRegion* regionAt(const std::vector<TimeRegion>& rs, double t) {
    for (const auto& r : rs)
        if (t >= r.start - kSecEps && t < r.end - kSecEps) return &r;
    return nullptr;
}

// Cut every clip crossing a region edge and shift each piece by its region's offset (dropped
// regions lose theirs); what lies outside [lo,hi) stays. True when a clip was touched.
template <class Clip, class LenFn, class SliceFn>
bool remapClips(
    std::vector<Clip>& clips, double lo, double hi, const std::vector<TimeRegion>& rs, LenFn len, SliceFn slice) {
    std::vector<Clip> out;
    out.reserve(clips.size() + 4);
    bool changed = false;
    for (const auto& c : clips) {
        const double cs = c.startBeat, ce = cs + len(c);
        if (ce <= lo + kSecEps || cs >= hi - kSecEps) {
            out.push_back(c);
            continue;
        }
        changed = true;
        if (cs < lo - kSecEps) out.push_back(slice(c, cs, lo));
        for (const auto& r : rs) {
            const double a = std::max(cs, r.start), b = std::min(ce, r.end);
            if (r.drop || b <= a + kSecEps) continue;
            Clip piece = a <= cs + kSecEps && b >= ce - kSecEps ? c : slice(c, a, b);
            piece.startBeat += r.offset;
            out.push_back(std::move(piece));
        }
        if (ce > hi + kSecEps) out.push_back(slice(c, hi, ce));
    }
    clips.swap(out);
    return changed;
}

// Two points on one beat make a step, but AutomationLane::valueAt returns the first (outgoing)
// value exactly on that beat — so a section starting there would begin with its neighbour's
// setting for a block. The outgoing point moves a hair earlier (a millionth of a beat, under a
// sample at any tempo), so the incoming value holds from the join itself; where there is no
// room for it (beat 0, or right after another point) it goes, and an equal pair is merged.
void settleJoins(std::vector<AutomationPoint>& pts) {
    constexpr double eps = 1e-9, nudge = 1e-6;
    std::vector<AutomationPoint> out;
    out.reserve(pts.size());
    for (size_t i = 0; i < pts.size(); ++i) {
        const bool join = i + 1 < pts.size() && std::abs(pts[i + 1].beat - pts[i].beat) < eps;
        if (!join) {
            out.push_back(pts[i]);
            continue;
        }
        if (pts[i + 1].value == pts[i].value) continue; // same value: the incoming point alone
        AutomationPoint q = pts[i];
        q.beat -= nudge;
        if (q.beat < 0.0 || (!out.empty() && out.back().beat > q.beat - eps)) continue; // no room: drop it
        out.push_back(q);
    }
    pts.swap(out);
}

// Rebuild the lane over [lo,hi): points before and after keep their place, each kept region's
// curve (laneSlice: exact values at both edges) is laid at its new start, in new order. Every
// cut and join holds the value the original had there (FR-005). False when the lane is empty
// or holds one value through the window.
bool laneRemap(AutomationLane& lane, double lo, double hi, const std::vector<TimeRegion>& rs) {
    constexpr double eps = 1e-9;
    auto& p = lane.points;
    if (p.empty() || p.back().beat < lo - eps) return false;
    if (std::isfinite(hi) && p.front().beat > hi + eps) return false;

    std::vector<AutomationPoint> out;
    out.reserve(p.size() + 2 * rs.size() + 2);
    for (const auto& q : p)
        if (q.beat < lo - eps) out.push_back(q);
    endSegmentAt(out, p, lo);
    out.push_back({lo, lane.valueAt(lo), 0.0f}); // the left side ends (or holds) as it did

    std::vector<const TimeRegion*> order;
    for (const auto& r : rs)
        if (!r.drop) order.push_back(&r);
    std::sort(order.begin(), order.end(),
        [](const TimeRegion* a, const TimeRegion* b) { return a->start + a->offset < b->start + b->offset; });
    for (const TimeRegion* r : order) {
        const double at = r->start + r->offset;
        if (std::isfinite(r->end)) {
            for (auto q : laneSlice(lane, r->start, r->end)) {
                q.beat += at;
                out.push_back(q);
            }
        } else { // to the end of the song: the curve from its start, then every later point
            appendLaneFrom(out, lane, r->start, std::numeric_limits<double>::infinity(), r->offset);
            for (const auto& q : p)
                if (q.beat > r->start + eps) out.push_back({q.beat + r->offset, q.value, q.curve});
        }
    }

    if (std::isfinite(hi)) {
        // The right side starts (or holds) on hi as it did, playing the rest of the segment
        // that ran through it.
        const auto right =
            std::find_if(p.begin(), p.end(), [&](const AutomationPoint& q) { return q.beat > hi + eps; });
        appendLaneFrom(out, lane, hi, std::numeric_limits<double>::infinity(), 0.0);
        out.insert(out.end(), right, p.end());
    }
    settleJoins(out);
    p.swap(out);
    return true;
}

// The lane with a copy of [a,b) inserted at b (Duplicate with content): the original up to b,
// the copy, then the rest from b on, later by the section's length — every piece playing as
// it did, cuts laid as in laneRemap. False when the lane is empty or holds one value from a on.
bool laneDuplicate(AutomationLane& lane, double a, double b) {
    constexpr double eps = 1e-9;
    auto& p = lane.points;
    if (p.empty() || p.back().beat < a - eps) return false;
    const double len = b - a;
    std::vector<AutomationPoint> out;
    out.reserve(p.size() * 2 + 4);
    for (const auto& q : p)
        if (q.beat < b - eps) out.push_back(q);
    endSegmentAt(out, p, b);
    out.push_back({b, lane.valueAt(b), 0.0f}); // the left side ends as it did
    for (auto q : laneSliceExact(lane, a, b)) {
        q.beat += b;
        out.push_back(q);
    }
    appendLaneFrom(out, lane, b, std::numeric_limits<double>::infinity(), len);
    for (const auto& q : p)
        if (q.beat > b + eps) out.push_back({q.beat + len, q.value, q.curve});
    settleJoins(out);
    p.swap(out);
    return true;
}

} // namespace

void ClipOps::remapTimeline(double lo, double hi, const std::vector<TimeRegion>& rs, std::vector<Section> sections) {
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    auto remapTrack = [&](const Track& t) -> std::shared_ptr<Track> {
        auto nt = rt_.cloneTrack(t);
        bool changed = false;
        if (nt->type() == TrackType::Instrument && !nt->midiClips.empty())
            changed |= remapClips(
                nt->midiClips.write(), lo, hi, rs, [](const MidiClip& c) { return c.lengthBeats; },
                [](const MidiClip& c, double a, double b) { return midiSlice(c, a, b); });
        else if (nt->type() == TrackType::Audio && !nt->clips.empty())
            changed |= remapClips(
                nt->clips.write(), lo, hi, rs,
                [&](const AudioClip& c) { return audioDisplayLenBeats(c, spb, devSR, rt_.tempoMap()); },
                [&](const AudioClip& c, double a, double b) {
                    return audioSlice(c, a, b, spb, devSR, rt_.tempoMap());
                });
        {
            bool hasLanes = false;
            for (const auto& lane : nt->automation)
                if (lane.target != AutomationTarget::Tempo && !lane.points.empty()) {
                    hasLanes = true;
                    break;
                }
            if (hasLanes) {
                for (auto& lane : nt->automation.write()) // not the master Tempo lane: the ruler stays (specs/099)
                    if (lane.target != AutomationTarget::Tempo) changed |= laneRemap(lane, lo, hi, rs);
            }
        }
        // A take travels with the region its first played frame is in, so the comp sections
        // still where it put them stay on it (specs/067).
        for (size_t ki = 0; ki < nt->takes.size(); ++ki) {
            const auto& k = nt->takes[ki];
            if (const TimeRegion* r = regionAt(rs, k.beatAtFrame(k.sourceOffsetFrames));
                r && !r->drop && r->offset != 0.0) {
                shiftTake(nt->takes.write()[ki], r->offset);
                changed = true;
            }
        }
        return changed ? nt : nullptr;
    };

    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    laneRemap(g->masterVolume, lo, hi, rs);
    if (g->masterTrack)
        if (auto nm = remapTrack(*g->masterTrack)) g->masterTrack = nm;
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        auto nt = remapTrack(*t);
        g->tracks.push_back(nt ? nt : t);
    }
    std::stable_sort(
        sections.begin(), sections.end(), [](const Section& a, const Section& b) { return a.startBeat < b.startBeat; });
    g->sections = std::move(sections);
    rt_.pushUndo();
    rt_.publishRaw(std::move(g));
}

bool ClipOps::sectionOverlaps(int32_t index) const {
    const auto& ss = rt_.sections();
    if (index < 0 || index >= static_cast<int32_t>(ss.size())) return false;
    const Section& s = ss[static_cast<size_t>(index)];
    for (int32_t i = 0; i < static_cast<int32_t>(ss.size()); ++i) {
        if (i == index) continue;
        const Section& si = ss[static_cast<size_t>(i)];
        const double a = std::max(s.startBeat, si.startBeat);
        const double b = std::min(s.startBeat + s.lengthBeats, si.startBeat + si.lengthBeats);
        if (b - a > kSecEps) return true;
    }
    return false;
}

namespace {
// True when `beat` lies strictly inside a section's span — a cut there would split it.
bool insideSection(const std::vector<Section>& ss, double beat) {
    for (const auto& s : ss)
        if (beat > s.startBeat + kSecEps && beat < s.startBeat + s.lengthBeats - kSecEps) return true;
    return false;
}
} // namespace

bool ClipOps::moveSection(int32_t index, int32_t toBoundary) {
    if (!rt_.authoring_) return false;
    const auto& ss = rt_.authoring_->sections;
    const auto n = static_cast<int32_t>(ss.size());
    if (index < 0 || index >= n || toBoundary < 0 || toBoundary > n) return false;
    if (toBoundary == index || toBoundary == index + 1) return false; // already there
    if (sectionOverlaps(index)) return false;
    double end = 0.0; // "after the last section": the end of the song's structure
    for (const auto& s : ss) end = std::max(end, s.startBeat + s.lengthBeats);
    const double a = ss[static_cast<size_t>(index)].startBeat, b = a + ss[static_cast<size_t>(index)].lengthBeats,
                 len = b - a;
    const double d = toBoundary < n ? ss[static_cast<size_t>(toBoundary)].startBeat : end;
    if (insideSection(ss, d)) return false;

    std::vector<TimeRegion> rs;
    double lo, hi;
    if (d <= a) { // earlier: [d,a) moves right by the section's length, the section lands on d
        lo = d;
        hi = b;
        rs.push_back({d, a, len, false});
        rs.push_back({a, b, d - a, false});
    } else { // later: [b,d) moves left, the section lands so it ends on d
        lo = a;
        hi = d;
        rs.push_back({a, b, d - b, false});
        rs.push_back({b, d, -len, false});
    }
    std::vector<Section> next = ss;
    for (auto& s : next)
        if (const TimeRegion* r = regionAt(rs, s.startBeat)) s.startBeat += r->offset;
    remapTimeline(lo, hi, rs, std::move(next));
    return true;
}

bool ClipOps::deleteSectionWithContent(int32_t index) {
    if (!rt_.authoring_) return false;
    const auto& ss = rt_.authoring_->sections;
    if (index < 0 || index >= static_cast<int32_t>(ss.size()) || sectionOverlaps(index)) return false;
    const double a = ss[static_cast<size_t>(index)].startBeat, b = a + ss[static_cast<size_t>(index)].lengthBeats,
                 len = b - a;
    constexpr double inf = std::numeric_limits<double>::infinity();
    const std::vector<TimeRegion> rs{{a, b, 0.0, true}, {b, inf, -len, false}};
    std::vector<Section> next;
    next.reserve(ss.size());
    for (int32_t i = 0; i < static_cast<int32_t>(ss.size()); ++i) {
        if (i == index) continue;
        Section s = ss[static_cast<size_t>(i)];
        if (s.startBeat >= b - kSecEps) s.startBeat -= len;
        next.push_back(std::move(s));
    }
    // Take lanes that start in the deleted span — picked before the remap shifts later ones in.
    std::set<int32_t> inSpan;
    for (const auto& t : rt_.authoring_->tracks)
        for (const auto& k : t->takes)
            if (const double ks = k.beatAtFrame(k.sourceOffsetFrames); ks >= a - kSecEps && ks < b - kSecEps)
                inSpan.insert(k.id);
    remapTimeline(a, inf, rs, std::move(next));
    if (inSpan.empty()) return true;
    // Such a take goes with the section, unless a comp clip still plays from it elsewhere
    // (specs/067).
    auto g = std::make_shared<Graph>(*rt_.authoring_);
    bool dropped = false;
    for (auto& t : g->tracks) {
        auto gone = [&](const AudioTake& k) {
            return inSpan.count(k.id) &&
                   std::none_of(t->clips.begin(), t->clips.end(), [&](const AudioClip& c) { return c.takeId == k.id; });
        };
        if (std::none_of(t->takes.begin(), t->takes.end(), gone)) continue;
        auto nt = rt_.cloneTrack(*t);
        std::erase_if(nt->takes.write(), gone);
        t = nt;
        dropped = true;
    }
    if (dropped) rt_.publishRaw(std::move(g)); // part of the same undo step
    return true;
}

bool ClipOps::duplicateSectionWithContent(int32_t index) {
    if (!rt_.authoring_) return false;
    const auto& ss = rt_.authoring_->sections;
    if (index < 0 || index >= static_cast<int32_t>(ss.size()) || sectionOverlaps(index)) return false;
    const double a = ss[static_cast<size_t>(index)].startBeat, b = a + ss[static_cast<size_t>(index)].lengthBeats,
                 len = b - a;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    insertTimeInto(*g, b, len, true); // nothing to copy is fine: the section still doubles
    // Automation: Duplicate time's gap closes a curved segment cut at b with a straight line, so
    // the lanes are rebuilt from the originals the way a move lays them (laneDuplicate).
    auto redoLanes = [&](std::shared_ptr<Track>& t, const Track& orig) {
        if (std::none_of(
                orig.automation.begin(), orig.automation.end(), [](const TrackLane& l) { return !l.points.empty(); }))
            return;
        auto nt = rt_.cloneTrack(*t);
        auto& autos = nt->automation.write();
        for (size_t j = 0; j < autos.size() && j < orig.automation.size(); ++j) {
            autos[j].points = orig.automation[j].points;
            if (autos[j].target != AutomationTarget::Tempo) laneDuplicate(autos[j], a, b); // specs/099
        }
        t = nt;
    };
    for (size_t i = 0; i < g->tracks.size() && i < rt_.authoring_->tracks.size(); ++i)
        redoLanes(g->tracks[i], *rt_.authoring_->tracks[i]);
    if (g->masterTrack && rt_.authoring_->masterTrack) redoLanes(g->masterTrack, *rt_.authoring_->masterTrack);
    g->masterVolume = rt_.authoring_->masterVolume;
    laneDuplicate(g->masterVolume, a, b);
    // Take lanes: later ones move with the song; one starting in the section gets a copy (a new
    // id, number and recording group) under the copied section, and the copied comp clips point at it so the
    // copy can be comped on its own (specs/067).
    auto takeStart = [](const AudioTake& k) { return k.beatAtFrame(k.sourceOffsetFrames); };
    for (auto& t : g->tracks) {
        if (t->takes.empty()) continue;
        auto nt = rt_.cloneTrack(*t);
        std::map<int32_t, int32_t> copied; // old take id -> its copy's
        std::map<int32_t, int32_t> groups; // old recording group -> its copy's: Flatten reads a
                                           // group's origin from its first take
        int32_t number = 0;
        for (const auto& k : nt->takes) number = std::max(number, k.number);
        std::vector<AudioTake> copies;
        {
            auto& takes = nt->takes.write();
            for (auto& k : takes) {
                const double ks = takeStart(k);
                if (ks >= b - kSecEps) shiftTake(k, len);
                else if (ks >= a - kSecEps) {
                    AudioTake c = k;
                    c.id = ++rt_.nextTakeId_;
                    c.number = ++number;
                    shiftTake(c, len);
                    auto [git, fresh] = groups.try_emplace(k.group, 0);
                    if (fresh) git->second = ++rt_.nextTakeGroup_;
                    c.group = git->second;
                    copied[k.id] = c.id;
                    copies.push_back(std::move(c));
                }
            }
            for (auto& c : copies) takes.push_back(std::move(c));
        }
        if (!copied.empty()) {
            for (auto& c : nt->clips.write()) // the copies insertTimeInto laid in [b, b+len)
                if (c.takeId != 0 && c.startBeat >= b - kSecEps && c.startBeat < b + len - kSecEps)
                    if (auto it = copied.find(c.takeId); it != copied.end()) c.takeId = it->second;
        }
        t = nt;
    }
    std::vector<Section> next;
    next.reserve(ss.size() + 1);
    for (const auto& s0 : ss) {
        Section s = s0;
        if (s.startBeat >= b - kSecEps) s.startBeat += len;
        next.push_back(std::move(s));
    }
    Section copy = ss[static_cast<size_t>(index)];
    copy.startBeat = b;
    next.push_back(std::move(copy));
    std::stable_sort(
        next.begin(), next.end(), [](const Section& x, const Section& y) { return x.startBeat < y.startBeat; });
    g->sections = std::move(next);
    rt_.pushUndo();
    rt_.publishRaw(std::move(g));
    return true;
}

} // namespace nota::detail
