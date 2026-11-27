// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ClipOps — time-range delete/split/duplicate, split fades, insert/duplicate time, and consolidate.

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
#include <limits>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

using namespace nota::detail::clip_helpers;

// --- time-range clip ops (arrangement time-selection) -----------------------
namespace {
bool rangeTouchesTrack(const Track& t, double s, double e, double spb, double devSR, const TempoMap* map = nullptr) {
    constexpr double eps = 1e-6;
    if (t.type() == TrackType::Instrument) {
        for (const auto& c : t.midiClips) {
            double cs = c.startBeat, ce = cs + c.lengthBeats;
            if (ce > s + eps && cs < e - eps) return true;
        }
    } else {
        for (const auto& c : t.clips) {
            double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR, map);
            if (ce > s + eps && cs < e - eps) return true;
        }
    }
    return false;
}
} // namespace

bool ClipOps::deleteClipsInRange(const std::vector<int32_t>& trackIds, double start, double end, bool withAutomation) {
    if (end <= start + 1e-9 || trackIds.empty()) return false;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    auto listed = [&](int32_t id) { return std::find(trackIds.begin(), trackIds.end(), id) != trackIds.end(); };
    bool any = false;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        if (t->type() != TrackType::Return && listed(t->id()) &&
            rangeTouchesTrack(*t, start, end, spb, devSR, rt_.tempoMap())) {
            auto nt = rt_.cloneTrack(*t);
            if (withAutomation) { // the spans copyRange captured: each clip's slice of the range
                auto clear = [&](double cs, double ce) {
                    if (ce > start + 1e-6 && cs < end - 1e-6)
                        removeAutomationInRange(*nt, std::max(cs, start), std::min(ce, end));
                };
                if (nt->type() == TrackType::Instrument)
                    for (const auto& c : nt->midiClips) clear(c.startBeat, c.startBeat + c.lengthBeats);
                else
                    for (const auto& c : nt->clips)
                        clear(c.startBeat, c.startBeat + audioDisplayLenBeats(c, spb, devSR, rt_.tempoMap()));
            }
            if (nt->type() == TrackType::Instrument) midiOverwriteRange(nt->midiClips.write(), start, end);
            else
                audioOverwriteRange(nt->clips.write(), start, end, spb, devSR, rt_.tempoMap());
            g->tracks.push_back(nt);
            any = true;
        } else
            g->tracks.push_back(t);
    }
    if (!any) return false;
    rt_.pushUndo();
    rt_.publishRaw(std::move(g));
    return true;
}

// Cut (not delete) every listed track's clips at both `start` and `end`, so the covered
// slice becomes standalone clip(s). Any clip crossing a boundary is replaced by its
// consecutive slices; clips clear of both boundaries are untouched. One undo step.
bool ClipOps::splitClipsAtRange(const std::vector<int32_t>& trackIds, double start, double end) {
    if (end <= start + 1e-9 || trackIds.empty()) return false;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    constexpr double eps = 1e-6;
    auto listed = [&](int32_t id) { return std::find(trackIds.begin(), trackIds.end(), id) != trackIds.end(); };
    bool any = false;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        if (t->type() == TrackType::Return || !listed(t->id()) ||
            !rangeTouchesTrack(*t, start, end, spb, devSR, rt_.tempoMap())) {
            g->tracks.push_back(t);
            continue;
        }
        auto nt = rt_.cloneTrack(*t);
        bool split = false;
        if (nt->type() == TrackType::Instrument) {
            std::vector<MidiClip> out;
            out.reserve(nt->midiClips.size() + 2);
            for (const auto& c : nt->midiClips) {
                const double cs = c.startBeat, ce = cs + c.lengthBeats;
                int nc = 0;
                double cuts[2];
                if (start > cs + eps && start < ce - eps) cuts[nc++] = start;
                if (end > cs + eps && end < ce - eps) cuts[nc++] = end;
                if (nc == 0) {
                    out.push_back(c);
                    continue;
                }
                double a = cs;
                for (int i = 0; i < nc; ++i) {
                    out.push_back(midiSlice(c, a, cuts[i]));
                    a = cuts[i];
                }
                out.push_back(midiSlice(c, a, ce));
                split = true;
            }
            if (split) nt->midiClips.replace(std::move(out));
        } else {
            std::vector<AudioClip> out;
            out.reserve(nt->clips.size() + 2);
            for (const auto& c : nt->clips) {
                const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR, rt_.tempoMap());
                int nc = 0;
                double cuts[2];
                if (start > cs + eps && start < ce - eps) cuts[nc++] = start;
                if (end > cs + eps && end < ce - eps) cuts[nc++] = end;
                if (nc == 0) {
                    out.push_back(c);
                    continue;
                }
                const double fadeBeats = splitFades_ ? splitFadeBeats() : 0.0;
                double a = cs;
                auto pushPiece = [&](double from, double to) {
                    AudioClip piece = audioSlice(c, from, to, spb, devSR, rt_.tempoMap());
                    if (fadeBeats > 0.0) raiseSplitEdge(piece, from > cs + eps, to < ce - eps, fadeBeats, to - from);
                    out.push_back(std::move(piece));
                };
                for (int i = 0; i < nc; ++i) {
                    pushPiece(a, cuts[i]);
                    a = cuts[i];
                }
                pushPiece(a, ce);
                split = true;
            }
            if (split) nt->clips.replace(std::move(out));
        }
        if (split) {
            g->tracks.push_back(nt);
            any = true;
        } else
            g->tracks.push_back(t);
    }
    if (!any) return false;
    rt_.pushUndo();
    rt_.publishRaw(std::move(g));
    return true;
}

double ClipOps::splitFadeBeats() const {
    double bpm = rt_.authoringBpm_;
    if (!(bpm > 0.0)) bpm = 120.0;
    return kSplitFadeSeconds * bpm / 60.0;
}

bool ClipOps::fadeTouchingSplits(const std::vector<std::pair<int32_t, int32_t>>& sel) {
    if (sel.empty() || !rt_.authoring_) return false;
    const double fade = splitFadeBeats();
    if (!(fade > 0.0)) return false;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    constexpr double join = 1e-4;
    auto picked = [&](int32_t trackId, int32_t clipIndex) {
        for (const auto& s : sel)
            if (s.first == trackId && s.second == clipIndex) return true;
        return false;
    };
    auto trackPicked = [&](int32_t trackId) {
        for (const auto& s : sel)
            if (s.first == trackId) return true;
        return false;
    };
    bool any = false;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        if (t->type() != TrackType::Audio || !trackPicked(t->id())) {
            g->tracks.push_back(t);
            continue;
        }
        auto nt = rt_.cloneTrack(*t);
        const int n = static_cast<int>(nt->clips.size());
        std::vector<int> order(static_cast<size_t>(n));
        for (int i = 0; i < n; ++i) order[static_cast<size_t>(i)] = i;
        std::sort(order.begin(), order.end(), [&](int a, int b) {
            const double ds = nt->clips[static_cast<size_t>(a)].startBeat - nt->clips[static_cast<size_t>(b)].startBeat;
            return ds < 0.0 || (ds == 0.0 && a < b);
        });
        bool changed = false;
        auto& ac = nt->clips.write();
        for (int i = 0; i + 1 < n; ++i) {
            const int li = order[static_cast<size_t>(i)], ri = order[static_cast<size_t>(i + 1)];
            if (!picked(t->id(), li) && !picked(t->id(), ri)) continue;
            AudioClip& left = ac[static_cast<size_t>(li)];
            AudioClip& right = ac[static_cast<size_t>(ri)];
            const double llen = audioDisplayLenBeats(left, spb, devSR, rt_.tempoMap());
            const double rlen = audioDisplayLenBeats(right, spb, devSR, rt_.tempoMap());
            if (std::abs((left.startBeat + llen) - right.startBeat) > join) continue;
            if (raiseSplitEdge(left, false, true, fade, llen)) changed = true;
            if (raiseSplitEdge(right, true, false, fade, rlen)) changed = true;
        }
        if (changed) {
            g->tracks.push_back(nt);
            any = true;
        } else
            g->tracks.push_back(t);
    }
    if (!any) return false;
    rt_.pushUndo();
    rt_.publishRaw(std::move(g));
    return true;
}

double ClipOps::duplicateRange(const std::vector<int32_t>& trackIds, double start, double end) {
    if (end <= start + 1e-9 || trackIds.empty()) return -1.0;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    const double len = end - start;
    constexpr double eps = 1e-6;
    auto listed = [&](int32_t id) { return std::find(trackIds.begin(), trackIds.end(), id) != trackIds.end(); };
    bool any = false;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        if (t->type() == TrackType::Return || !listed(t->id()) ||
            !rangeTouchesTrack(*t, start, end, spb, devSR, rt_.tempoMap())) {
            g->tracks.push_back(t);
            continue;
        }
        auto nt = rt_.cloneTrack(*t);
        if (nt->type() == TrackType::Instrument) {
            std::vector<MidiClip> slices;
            for (auto& c : nt->midiClips) {
                double cs = c.startBeat, ce = cs + c.lengthBeats;
                if (ce > start + eps && cs < end - eps) {
                    auto s = midiSlice(c, start, end);
                    s.startBeat += len;
                    slices.push_back(std::move(s));
                }
            }
            auto& mc = nt->midiClips.write();
            midiOverwriteRange(mc, end, end + len); // the copy overwrites what it lands on (2.8)
            for (auto& s : slices) mc.push_back(std::move(s));
        } else {
            std::vector<AudioClip> slices;
            bool changedSpan = false;
            for (auto& c : nt->clips) {
                double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR, rt_.tempoMap());
                if (ce > start + eps && cs < end - eps) {
                    auto s = audioSlice(c, start, end, spb, devSR, rt_.tempoMap());
                    const double sourceLen = audioDisplayLenBeats(s, spb, devSR, rt_.tempoMap());
                    s.startBeat += len;
                    changedSpan |= std::abs(audioDisplayLenBeats(s, spb, devSR, rt_.tempoMap()) - sourceLen) > eps;
                    slices.push_back(std::move(s));
                }
            }
            // Natural-rate copies can grow or shrink in beats after placement. Carve
            // their actual landing spans before inserting any copy. When lengths stay
            // the same, retain the selected range's existing silence/gap semantics.
            auto& ac = nt->clips.write();
            if (changedSpan) {
                for (const auto& s : slices)
                    audioOverwriteRange(ac, s.startBeat,
                        s.startBeat + audioDisplayLenBeats(s, spb, devSR, rt_.tempoMap()), spb, devSR, rt_.tempoMap());
            } else
                audioOverwriteRange(ac, end, end + len, spb, devSR, rt_.tempoMap());
            for (auto& s : slices) ac.push_back(std::move(s));
        }
        g->tracks.push_back(nt);
        any = true;
    }
    if (!any) return -1.0;
    rt_.pushUndo();
    rt_.publishRaw(std::move(g));
    return len;
}

// --- insert time (Insert Silence ⌘⇧I / Duplicate Time ⌘⇧D) ----------------------
namespace {
// The lane's curve over [a,b] re-based so `a` is 0 (laneSliceExact: the value at each edge and
// every point strictly inside, a curved segment cut at either end keeping its shape).
// Opens a `len`-beat gap at `at`: points at or after `at` move right. With `fill` (a
// laneSlice `len` long) the gap plays that curve; without, it holds the lane's value at `at`.
// False when the lane didn't change.
bool laneInsertGap(AutomationLane& lane, double at, double len, const std::vector<AutomationPoint>* fill) {
    if (lane.points.empty()) return false;
    constexpr double eps = 1e-9;
    const float v = lane.valueAt(at);
    auto& p = lane.points;
    const auto mid = std::find_if(p.begin(), p.end(), [&](const AutomationPoint& q) { return q.beat >= at - eps; });
    const bool before = mid != p.begin(), after = mid != p.end();
    if (!fill && !after) return false;
    std::vector<AutomationPoint> out;
    out.reserve(p.size() + (fill ? fill->size() + 1 : 2));
    out.insert(out.end(), p.begin(), mid);
    if (before) endSegmentAt(out, p, at);
    // After the gap the lane resumes the segment that ran through `at`, its curve scaled to
    // what is left of it, so the tail keeps its shape (#591).
    const float tail = AutomationLane::pieceCurve(p, at, std::numeric_limits<double>::infinity());
    if (fill) {
        out.push_back({at, v, 0.0f}); // a step from the left side into the copy
        for (auto q : *fill) {
            q.beat += at;
            out.push_back(q);
        }
        if (!fill->empty()) out.back().curve = tail;
    } else if (before) {
        out.push_back({at, v, 0.0f});
        out.push_back({at + len, v, tail});
    }
    for (auto it = mid; it != p.end(); ++it) {
        auto q = *it;
        q.beat += len;
        out.push_back(q);
    }
    p.swap(out);
    return true;
}
} // namespace

bool ClipOps::insertTime(double at, double len, bool duplicate) {
    at = std::max(0.0, at);
    if (len <= 1e-9) return false;
    if (duplicate && at - len < -1e-9) return false;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    if (!insertTimeInto(*g, at, len, duplicate)) return false;
    rt_.pushUndo();
    rt_.publishRaw(std::move(g));
    return true;
}

bool ClipOps::insertTimeInto(Graph& g, double at, double len, bool duplicate) {
    const double src = at - len; // Duplicate Time copies [src, at) into the gap
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    constexpr double eps = 1e-6;

    auto shiftLane = [&](AutomationLane& lane) {
        if (!duplicate) return laneInsertGap(lane, at, len, nullptr);
        if (lane.points.empty() || lane.points.back().beat < src - eps) return laneInsertGap(lane, at, len, nullptr);
        const auto fill = laneSlice(lane, src, at);
        return laneInsertGap(lane, at, len, &fill);
    };
    // A clone of `t` with the gap opened (and filled), or null when nothing in it moved.
    auto shiftTrack = [&](const Track& t) -> std::shared_ptr<Track> {
        auto nt = rt_.cloneTrack(t);
        bool changed = false;
        if (nt->type() == TrackType::Instrument) {
            std::vector<MidiClip> out;
            out.reserve(nt->midiClips.size() + 2);
            std::vector<MidiClip> copies;
            for (const auto& c : nt->midiClips) {
                const double cs = c.startBeat, ce = cs + c.lengthBeats;
                if (duplicate && ce > src + eps && cs < at - eps) {
                    auto s = midiSlice(c, src, at);
                    s.startBeat += len;
                    copies.push_back(std::move(s));
                }
                if (ce <= at + eps) {
                    out.push_back(c);
                    continue;
                }
                if (cs < at - eps) { // crosses the insert point: cut it there
                    out.push_back(midiSlice(c, cs, at));
                    auto r = midiSlice(c, at, ce);
                    r.startBeat += len;
                    out.push_back(std::move(r));
                } else {
                    auto m = c;
                    m.startBeat += len;
                    out.push_back(std::move(m));
                }
                changed = true;
            }
            changed |= !copies.empty();
            for (auto& c : copies) out.push_back(std::move(c));
            if (changed) nt->midiClips.replace(std::move(out));
        } else if (nt->type() == TrackType::Audio) {
            std::vector<AudioClip> out;
            out.reserve(nt->clips.size() + 2);
            std::vector<AudioClip> copies;
            for (const auto& c : nt->clips) {
                const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR, rt_.tempoMap());
                if (duplicate && ce > src + eps && cs < at - eps) {
                    auto s = audioSlice(c, src, at, spb, devSR, rt_.tempoMap());
                    s.startBeat += len;
                    copies.push_back(std::move(s));
                }
                if (ce <= at + eps) {
                    out.push_back(c);
                    continue;
                }
                if (cs < at - eps) {
                    out.push_back(audioSlice(c, cs, at, spb, devSR, rt_.tempoMap()));
                    auto r = audioSlice(c, at, ce, spb, devSR, rt_.tempoMap());
                    r.startBeat += len;
                    out.push_back(std::move(r));
                } else {
                    auto m = c;
                    m.startBeat += len;
                    out.push_back(std::move(m));
                }
                changed = true;
            }
            changed |= !copies.empty();
            for (auto& c : copies) out.push_back(std::move(c));
            if (changed) nt->clips.replace(std::move(out));
        }
        // The master Tempo lane (specs/099) is the ruler itself, not content on it: time and
        // section edits leave it where it is, as they left the tempo map before it was a lane.
        bool hasShiftLanes = false;
        for (const auto& lane : nt->automation)
            if (lane.target != AutomationTarget::Tempo && !lane.points.empty()) {
                hasShiftLanes = true;
                break;
            }
        if (hasShiftLanes) {
            for (auto& lane : nt->automation.write())
                if (lane.target != AutomationTarget::Tempo) changed |= shiftLane(lane);
        }
        return changed ? nt : nullptr;
    };

    bool any = false;
    any |= shiftLane(g.masterVolume);
    if (g.masterTrack)
        if (auto nm = shiftTrack(*g.masterTrack)) {
            g.masterTrack = nm;
            any = true;
        }
    g.tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        if (auto nt = shiftTrack(*t)) {
            g.tracks.push_back(nt);
            any = true;
        } else
            g.tracks.push_back(t);
    }
    return any;
}

// --- consolidate (⌘J): one clip per track over a range ------------------------
namespace {
// One source clip's audible slice [a,b) of a consolidate range (timeline beats), with the
// clip's start and its envelope (null/empty = unity) for stitching.
struct EnvSpan {
    double a, b, clipStart;
    const AutomationLane* lane;
};

// Stitch per-clip envelopes into one lane in consolidated-clip-local beats: each span keeps
// its clip's curve, clips without one and the gaps between clips hold unity, and a boundary
// between two clips is a step (two points on the same beat). Empty when no span carries an
// envelope, so the result keeps the no-envelope fast path.
AutomationLane stitchEnvelopes(const std::vector<EnvSpan>& spans, double start, double end) {
    AutomationLane out;
    if (std::none_of(spans.begin(), spans.end(), [](const EnvSpan& s) { return s.lane && !s.lane->points.empty(); }))
        return out;
    auto add = [&](double beat, float v, float curve = 0.0f) { out.points.push_back({beat - start, v, curve}); };
    double cursor = start;
    for (const auto& s : spans) {
        const double a = std::max(s.a, cursor), b = s.b;
        if (b <= a) continue;
        if (a > cursor) {
            add(cursor, 1.0f);
            add(a, 1.0f);
        }
        const bool has = s.lane && !s.lane->points.empty();
        // Each piece keeps its curve scaled to the part of its segment inside the span (#591).
        const double la = a - s.clipStart, lb = b - s.clipStart;
        add(a, has ? s.lane->valueAt(la) : 1.0f, has ? AutomationLane::pieceCurve(s.lane->points, la, lb) : 0.0f);
        if (has)
            for (const auto& p : s.lane->points) {
                const double x = s.clipStart + p.beat;
                if (x > a && x < b) add(x, p.value, AutomationLane::pieceCurve(s.lane->points, p.beat, lb));
            }
        add(b, has ? s.lane->valueAt(b - s.clipStart) : 1.0f);
        cursor = b;
    }
    if (cursor < end) {
        add(cursor, 1.0f);
        add(end, 1.0f);
    }
    return out;
}

// The covered MIDI clips merged into one clip over [s,e): exactly the notes that play —
// a note must start inside its clip's window and the range, and its tail stops at its clip's
// end (or the range end), as playback cuts it. Velocity envelopes are baked into the notes.
MidiClip consolidateMidiClips(const std::vector<MidiClip>& clips, double s, double e) {
    constexpr double eps = 1e-6;
    std::vector<const MidiClip*> src;
    for (const auto& c : clips)
        if (c.startBeat + c.lengthBeats > s + eps && c.startBeat < e - eps) src.push_back(&c);
    std::sort(src.begin(), src.end(), [](const MidiClip* x, const MidiClip* y) { return x->startBeat < y->startBeat; });

    MidiClip out;
    out.startBeat = s;
    out.lengthBeats = e - s;
    for (const MidiClip* c : src)
        if (!c->name.empty()) {
            out.name = c->name;
            break;
        }
    std::vector<EnvSpan> spans;
    for (const MidiClip* c : src) {
        if (!c->active) continue; // a deactivated clip is silent
        const double cs = c->startBeat, ce = std::min(cs + c->lengthBeats, e);
        spans.push_back({std::max(cs, s), ce, cs, &c->volumeEnvelope});
        const bool hasVel = !c->velocityEnvelope.points.empty();
        for (const Note& n : c->notes) {
            // A note starting before its clip (#56) sounds from the clip start, as in playback.
            if (n.startBeat >= c->lengthBeats || n.startBeat + n.lengthBeats <= 0.0) continue;
            const double on = cs + std::max(0.0, n.startBeat);
            if (on < s - eps || on >= e - eps) continue;
            Note m = n;
            m.startBeat = std::max(0.0, on - s);
            m.lengthBeats = std::min(cs + n.startBeat + n.lengthBeats, ce) - on;
            if (m.lengthBeats <= 0.0) continue;
            if (hasVel) m.velocity = n.velocity * std::clamp(c->velocityEnvelope.valueAt(n.startBeat), 0.0f, 1.0f);
            out.notes.push_back(m);
        }
    }
    out.volumeEnvelope = stitchEnvelopes(spans, s, e);
    // Controllers (specs/020): each clip's piece in turn; where a clip ends holding a pedal down
    // or the wheel bent, it goes back to rest there, as playback releases it.
    std::vector<int32_t> ctls;
    for (const MidiClip* c : src)
        for (const auto& cv : c->controllers)
            if (cv && std::find(ctls.begin(), ctls.end(), cv->ctl) == ctls.end()) ctls.push_back(cv->ctl);
    for (int32_t ctl : ctls) {
        std::vector<AutomationPoint> pts;
        for (const MidiClip* c : src) {
            if (!c->active) continue;
            const ControllerCurve* cv = c->controller(ctl);
            if (!cv) continue;
            const double cs = c->startBeat, a = std::max(cs, s), b = std::min(cs + c->lengthBeats, e);
            if (b <= a) continue;
            for (auto p : sliceCtlPoints(*cv, a - cs, b - cs)) {
                p.beat += a - s;
                pts.push_back(p);
            }
            if ((ctlIsSwitch(ctl) || ctl == kCtlPitchWheel) && cv->valueAt(b - cs) != kCtlRest && !cv->points.empty() &&
                b - cs >= cv->points.front().beat)
                pts.push_back({b - s, kCtlRest, 0.0f});
        }
        // A point the next clip pins on the same beat wins (setController keeps the last).
        std::stable_sort(
            pts.begin(), pts.end(), [](const AutomationPoint& x, const AutomationPoint& y) { return x.beat < y.beat; });
        if (!pts.empty()) out.setController(ctl, std::move(pts));
    }
    return out;
}
} // namespace

// Bounce what the covered clips play over [s,e) — the same renderer the audio thread uses,
// so gain, varispeed, warp, edge fades and clip envelopes are all baked — into a new
// device-rate stereo sample, placed as one clip at `s`. If any audible source was warped the
// result is warped too (neutral markers, that clip's mode) so it keeps following tempo; an
// all-unwarped range stays an unwarped, sample-exact copy.
AudioClip ClipOps::consolidateAudioClips(const std::vector<AudioClip>& clips, double s, double e) {
    constexpr double eps = 1e-6;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    std::vector<AudioClip> src;
    for (const auto& c : clips) {
        const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR, rt_.tempoMap());
        if (ce <= s + eps || cs >= e - eps) continue;
        src.push_back(c);
        // Consolidation must capture deferred clips too. Build only this private copy;
        // the live graph and its background cache job keep their existing state.
        if (rt_.tempoMap()) configureTempoWarp(src.back(), rt_.authoring_->tempoMap, devSR, true);
        else if (warpNeedsBuild(src.back(), false))
            buildWarpCache(src.back(), spb, devSR);
    }
    std::sort(src.begin(), src.end(), [](const AudioClip& x, const AudioClip& y) { return x.startBeat < y.startBeat; });

    Transport clock;
    clock.setSampleRate(devSR);
    clock.setBpm(rt_.authoringBpm_);
    clock.attachTempoMap(rt_.tempoMap());
    clock.seekBeats(s);
    clock.play();
    const double origin = clock.samplesAtBeat(s);
    const int64_t frames = std::max<int64_t>(1, std::llround(clock.samplesAtBeat(e) - origin));
    auto sb = std::make_shared<SampleBuffer>();
    sb->channels = 2;
    sb->frames = frames;
    sb->sourceSampleRate = devSR;
    sb->samples.assign(static_cast<size_t>(frames) * 2, 0.0f);
    constexpr int64_t kBlock = 4096;
    for (int64_t off = 0; off < frames; off += kBlock) {
        const int32_t n = static_cast<int32_t>(std::min(kBlock, frames - off));
        rt_.renderAudioClipsAt(src, sb->samples.data() + off * 2, n, clock);
        clock.advanceBy(n);
    }

    AudioClip out;
    out.sample = std::move(sb);
    out.startBeat = s;
    out.lengthFrames = frames;
    for (const auto& c : src)
        if (!c.name.empty()) {
            out.name = c.name;
            break;
        }
    const auto warped =
        std::find_if(src.begin(), src.end(), [](const AudioClip& c) { return c.active && c.warpEnabled; });
    if (warped != src.end()) {
        out.warpEnabled = true;
        out.warpMode = warped->warpMode;
        if (rt_.tempoMap()) {
            out.warpBeats = e - s;
            out.warpMarkers.push_back({0, 0});
            for (const auto& node : rt_.tempoMap()->nodes)
                if (node.beat > s && node.beat < e)
                    out.warpMarkers.push_back({clock.samplesAtBeat(node.beat) - origin, node.beat - s});
            out.warpMarkers.push_back({static_cast<double>(frames), e - s});
        } else
            seedNeutralWarp(out, spb, devSR);
        configureClipWarp(out, spb, devSR);
    }
    return out;
}

bool ClipOps::consolidateRange(const std::vector<int32_t>& trackIds, double start, double end) {
    start = std::max(0.0, start);
    if (end <= start + 1e-9 || trackIds.empty()) return false;
    const double spb = rt_.transport_.samplesPerBeat(), devSR = rt_.transport_.sampleRate();
    if (spb <= 0.0 || devSR <= 0.0) return false;
    auto listed = [&](int32_t id) { return std::find(trackIds.begin(), trackIds.end(), id) != trackIds.end(); };
    std::vector<std::pair<int32_t, int32_t>> placed;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        const bool clipTrack = t->type() == TrackType::Instrument || t->type() == TrackType::Audio;
        if (!clipTrack || !listed(t->id()) || !rangeTouchesTrack(*t, start, end, spb, devSR, rt_.tempoMap())) {
            g->tracks.push_back(t);
            continue;
        }
        auto nt = rt_.cloneTrack(*t);
        if (nt->type() == TrackType::Instrument) {
            MidiClip merged = consolidateMidiClips(nt->midiClips.read(), start, end);
            auto& mc = nt->midiClips.write();
            midiOverwriteRange(mc, start, end);
            mc.push_back(std::move(merged));
            placed.emplace_back(nt->id(), static_cast<int32_t>(mc.size()) - 1);
        } else {
            AudioClip bounced = consolidateAudioClips(nt->clips.read(), start, end);
            auto& ac = nt->clips.write();
            audioOverwriteRange(ac, start, end, spb, devSR, rt_.tempoMap());
            ac.push_back(std::move(bounced));
            placed.emplace_back(nt->id(), static_cast<int32_t>(ac.size()) - 1);
        }
        g->tracks.push_back(nt);
    }
    if (placed.empty()) return false;
    rt_.pushUndo();
    rt_.publishRaw(std::move(g));
    lastPlaced_ = std::move(placed);
    return true;
}

} // namespace nota::detail
