// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement clip geometry, carve, slice, and split-fade helpers shared across clips/*.

#pragma once

#include "clips/ClipTypes.h"
#include "core/EnginePrelude.h"
#include "core/TempoMap.h"
#include "model/MidiClip.h"
#include "model/Track.h"
#include "nota/dsp/rt/AutomationTypes.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <vector>

namespace nota::detail::clip_helpers {

// Audio isn't warped: convert between musical beats and *source* sample frames
// at the current tempo/sample-rate.
inline double audioBeatsToSrcFrames(const AudioClip& c, double beats, double spb, double devSR,
    const TempoMap* map = nullptr, double fromBeat = std::numeric_limits<double>::quiet_NaN()) {
    if (!c.sample || spb <= 0 || devSR <= 0 || c.sample->sourceSampleRate <= 0) return 0.0;
    if (map && !c.warpEnabled) {
        const double start = std::isnan(fromBeat) ? c.startBeat : fromBeat;
        return (map->secondsAt(start + beats) - map->secondsAt(start)) * c.sample->sourceSampleRate * c.pitchRatio();
    }
    return beats * spb * c.sample->sourceSampleRate / devSR * c.pitchRatio();
}
inline double audioLenBeats(const AudioClip& c, double spb, double devSR, const TempoMap* map = nullptr,
    double fromBeat = std::numeric_limits<double>::quiet_NaN()) {
    if (!c.sample || spb <= 0 || devSR <= 0 || c.sample->sourceSampleRate <= 0) return 0.0;
    const double devFrames =
        static_cast<double>(c.effectiveLength()) * devSR / c.sample->sourceSampleRate / c.pitchRatio();
    const double start = std::isnan(fromBeat) ? c.startBeat : fromBeat;
    return map ? map->beatAt(map->secondsAt(start) + devFrames / devSR) - start : devFrames / spb;
}
// Timeline span (beats) of an audio clip as shown/played: warp uses the play window,
// unwarped uses the source region.
inline double audioDisplayLenBeats(const AudioClip& c, double spb, double devSR, const TempoMap* map = nullptr,
    double fromBeat = std::numeric_limits<double>::quiet_NaN()) {
    if (c.warpEnabled && c.warpMarkers.size() >= 2 && c.warpBeats > 0.0) return c.warpPlayLen();
    return audioLenBeats(c, spb, devSR, map, fromBeat);
}
// Source frame just past a REVERSED clip's audible head (its region end) after the left
// edge moved by `deltaBeats`. Trims measure a reversed clip backwards from here.
inline double audioReverseHead(
    const AudioClip& c, double deltaBeats, double spb, double devSR, const TempoMap* map = nullptr) {
    double head = c.sourceOffsetFrames + static_cast<double>(c.effectiveLength()) -
                  audioBeatsToSrcFrames(c, deltaBeats, spb, devSR, map);
    if (c.sample) head = std::min(head, static_cast<double>(c.sample->frames));
    return std::max(0.0, head);
}

// Push `start` right until [start, start+len) overlaps no existing clip on the track.
inline double placeMidiNonOverlap(const Track& t, double start, double len) {
    bool moved = true;
    while (moved) {
        moved = false;
        for (const auto& c : t.midiClips) {
            const double cs = c.startBeat, ce = cs + c.lengthBeats;
            if (start < ce && start + len > cs) {
                start = ce;
                moved = true;
            }
        }
    }
    return start;
}
inline double placeAudioNonOverlap(
    const Track& t, const AudioClip& placed, double start, double spb, double devSR, const TempoMap* map = nullptr) {
    bool moved = true;
    while (moved) {
        moved = false;
        for (const auto& c : t.clips) {
            const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR, map);
            const double len = audioDisplayLenBeats(placed, spb, devSR, map, start);
            if (start < ce && start + len > cs) {
                start = ce;
                moved = true;
            }
        }
    }
    return start;
}

// --- overwrite / comp: carve existing clips out of a timeline range ---------
// Replace-on-drop: a newly placed clip (recorded / dragged) replaces the region it covers.
// The parts of an existing clip outside the range survive as trimmed remainders.

// Append the lane from `t` up to (not including) its next point, shifted by `shift`: one point
// carrying the rest of the segment through `t`, its curve scaled to end at the next point or at
// `limit`, whichever comes first — exact, as the segment shape is shift-invariant (#591).
inline void appendLaneFrom(
    std::vector<AutomationPoint>& out, const AutomationLane& lane, double t, double limit, double shift) {
    out.push_back({t + shift, lane.valueAt(t), AutomationLane::pieceCurve(lane.points, t, limit)});
}

// The last point of `out` (an original point of `src` at its own beat) now ends its segment at
// `end`: shorten its curve so the stretch before `end` keeps its values (#591).
inline void endSegmentAt(std::vector<AutomationPoint>& out, const std::vector<AutomationPoint>& src, double end) {
    if (!out.empty()) out.back().curve = AutomationLane::pieceCurve(src, out.back().beat, end);
}

// The lane over [a,b] re-based so `a` is 0, playing as it did: the value at each edge plus
// every point strictly inside. Empty when the lane is.
inline std::vector<AutomationPoint> laneSliceExact(const AutomationLane& lane, double a, double b) {
    constexpr double eps = 1e-9;
    std::vector<AutomationPoint> out;
    if (lane.points.empty()) return out;
    appendLaneFrom(out, lane, a, b, -a);
    for (const auto& q : lane.points)
        if (q.beat > a + eps && q.beat < b - eps) {
            if (!out.empty() && q.beat - a <= out.back().beat + eps) continue;
            out.push_back({q.beat - a, q.value, AutomationLane::pieceCurve(lane.points, q.beat, b)});
        }
    out.push_back({b - a, lane.valueAt(b), 0.0f});
    return out;
}

inline MidiClip midiCarveLeft(const MidiClip& src, double endLocal) { // keep [start, start+endLocal)
    MidiClip r;
    r.name = src.name;
    r.startBeat = src.startBeat;
    r.lengthBeats = endLocal;
    for (const auto& n : src.notes)
        if (n.startBeat < endLocal) r.notes.push_back(n);
    r.sliceControllersFrom(src, 0.0, endLocal);
    return r;
}
inline MidiClip midiCarveRight(const MidiClip& src, double startLocal) { // keep [start+startLocal, end)
    MidiClip r;
    r.name = src.name;
    r.startBeat = src.startBeat + startLocal;
    r.lengthBeats = src.lengthBeats - startLocal;
    for (const auto& n : src.notes)
        if (n.startBeat >= startLocal) {
            Note m = n;
            m.startBeat -= startLocal;
            r.notes.push_back(m);
        }
    r.sliceControllersFrom(src, startLocal, r.lengthBeats);
    return r;
}
// Both carves work in PLAYED time, so a reversed clip mirrors: its audible head is the
// region's end, so keeping the first beats keeps the region's tail and vice versa.
inline AudioClip audioCarveLeft(
    const AudioClip& src, double endLocal, double spb, double devSR, const TempoMap* map = nullptr) {
    AudioClip left = src;
    left.adsr.release = 0.0; // the cut is a new, hard end: the tail's release stays with the right piece
    if (src.warpEnabled && src.warpMarkers.size() >= 2 && src.warpBeats > 0.0) {
        if (src.reversed) {
            left.warpPlayStart = std::max(0.0, src.warpPlayEndEff() - endLocal);
            left.warpPlayEnd = src.warpPlayEndEff();
        } else {
            left.warpPlayEnd = src.warpPlayStart + endLocal;
        }
        buildWarpCache(left, spb, devSR);
    } else {
        const int64_t keep = static_cast<int64_t>(audioBeatsToSrcFrames(src, endLocal, spb, devSR, map));
        if (src.reversed)
            left.sourceOffsetFrames = src.sourceOffsetFrames + static_cast<double>(src.effectiveLength() - keep);
        left.lengthFrames = keep;
    }
    return left;
}
inline AudioClip audioCarveRight(
    const AudioClip& src, double startLocal, double spb, double devSR, const TempoMap* map = nullptr) {
    AudioClip right = src;
    right.startBeat = src.startBeat + startLocal;
    right.adsr.attack = 0.0;
    right.adsr.decay = 0.0; // the head's attack/decay stay with the left piece
    if (src.warpEnabled && src.warpMarkers.size() >= 2 && src.warpBeats > 0.0) {
        if (src.reversed) {
            right.warpPlayStart = src.warpPlayStart;
            right.warpPlayEnd = std::max(src.warpPlayStart, src.warpPlayEndEff() - startLocal);
        } else {
            right.warpPlayStart = src.warpPlayStart + startLocal;
            right.warpPlayEnd = src.warpPlayEndEff();
        }
        buildWarpCache(right, spb, devSR);
    } else {
        const int64_t cut = static_cast<int64_t>(audioBeatsToSrcFrames(src, startLocal, spb, devSR, map));
        if (!src.reversed) // reversed drops the region's tail instead, so the offset stays
            right.sourceOffsetFrames = src.sourceOffsetFrames + static_cast<double>(cut);
        right.lengthFrames = src.effectiveLength() - cut;
    }
    return right;
}
// What a carve leaves out, for a piece cut from `src` over clip-local [a,b]: the clip's
// deactivation and its own part of every envelope (#467). The carves stay lean (a split is a
// hot edit, perf-gate edit-warp-split), so the callers that cut a clip into pieces that stay
// — a split, an overwrite's remainders — add this, and only when the clip has something to
// carry: an active clip without envelopes costs a few compares.
inline void carryMidiPiece(MidiClip& piece, const MidiClip& src, double a, double b) {
    piece.active = src.active;
    if (!src.velocityEnvelope.points.empty()) {
        piece.velocityEnvelope = src.velocityEnvelope;
        piece.velocityEnvelope.points = laneSliceExact(src.velocityEnvelope, a, b);
    }
    if (!src.volumeEnvelope.points.empty()) {
        piece.volumeEnvelope = src.volumeEnvelope;
        piece.volumeEnvelope.points = laneSliceExact(src.volumeEnvelope, a, b);
    }
}
// An audio piece already holds the whole clip's envelopes (a carve copies the clip); re-base
// them onto the piece, so the right-hand one plays the envelope from where it begins.
inline void carryAudioPiece(AudioClip& piece, const AudioClip& src, double a, double b) {
    if (!src.volumeEnvelope.points.empty()) piece.volumeEnvelope.points = laneSliceExact(src.volumeEnvelope, a, b);
    if (!src.panEnvelope.points.empty()) piece.panEnvelope.points = laneSliceExact(src.panEnvelope, a, b);
}
inline bool midiCarriesState(const MidiClip& c) {
    return !c.active || !c.velocityEnvelope.points.empty() || !c.volumeEnvelope.points.empty();
}
inline bool audioCarriesState(const AudioClip& c) {
    return !c.volumeEnvelope.points.empty() || !c.panEnvelope.points.empty();
}

inline void midiOverwriteRange(std::vector<MidiClip>& clips, double ns, double ne) {
    std::vector<MidiClip> out;
    out.reserve(clips.size() + 2);
    constexpr double eps = 1e-6;
    for (const auto& c : clips) {
        const double cs = c.startBeat, ce = cs + c.lengthBeats;
        if (ce <= ns + eps || cs >= ne - eps) {
            out.push_back(c);
            continue;
        } // no overlap
        const bool carry = midiCarriesState(c);
        if (ns > cs + eps) { // left remainder
            out.push_back(midiCarveLeft(c, ns - cs));
            if (carry) carryMidiPiece(out.back(), c, 0.0, ns - cs);
        }
        if (ne < ce - eps) { // right remainder
            out.push_back(midiCarveRight(c, ne - cs));
            if (carry) carryMidiPiece(out.back(), c, ne - cs, c.lengthBeats);
        }
    }
    clips.swap(out);
}
inline void audioOverwriteRange(
    std::vector<AudioClip>& clips, double ns, double ne, double spb, double devSR, const TempoMap* map = nullptr) {
    std::vector<AudioClip> out;
    out.reserve(clips.size() + 2);
    constexpr double eps = 1e-6;
    for (const auto& c : clips) {
        const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR, map);
        if (ce <= ns + eps || cs >= ne - eps) {
            out.push_back(c);
            continue;
        }
        const bool carry = audioCarriesState(c);
        if (ns > cs + eps) {
            out.push_back(audioCarveLeft(c, ns - cs, spb, devSR, map));
            if (carry) carryAudioPiece(out.back(), c, 0.0, ns - cs);
        }
        if (ne < ce - eps) {
            out.push_back(audioCarveRight(c, ne - cs, spb, devSR, map));
            if (carry) carryAudioPiece(out.back(), c, ne - cs, ce - cs);
        }
    }
    clips.swap(out);
}
// A clip clipped to the timeline range [ns,ne] (its intersection), timeline positions
// preserved — used to lift the covered slice for a range duplicate.
// The carves keep a MIDI piece's notes and controllers only, and leave an audio piece's
// envelopes where the whole clip had them. A slice is a piece that moves on its own (range
// duplicate, insert time, section edits — specs/073), so it also keeps the clip's
// deactivation and its own share of the envelopes, as a split's and an overwrite's pieces do
// (carryMidiPiece / carryAudioPiece, #467). (The carves themselves stay lean: a split is a
// hot edit, perf-gate edit-warp-split.)
inline MidiClip midiSlice(const MidiClip& c, double ns, double ne) {
    const double cs = c.startBeat, ce = cs + c.lengthBeats;
    const double a = std::max(cs, ns), b = std::min(ce, ne);
    MidiClip r = midiCarveLeft(midiCarveRight(c, a - cs), b - a);
    r.active = c.active;
    if (!c.velocityEnvelope.points.empty()) {
        r.velocityEnvelope = c.velocityEnvelope;
        r.velocityEnvelope.points = laneSliceExact(c.velocityEnvelope, a - cs, b - cs);
    }
    if (!c.volumeEnvelope.points.empty()) {
        r.volumeEnvelope = c.volumeEnvelope;
        r.volumeEnvelope.points = laneSliceExact(c.volumeEnvelope, a - cs, b - cs);
    }
    return r;
}
inline AudioClip audioSlice(
    const AudioClip& c, double ns, double ne, double spb, double devSR, const TempoMap* map = nullptr) {
    const double cs = c.startBeat, ce = cs + audioDisplayLenBeats(c, spb, devSR, map);
    const double a = std::max(cs, ns), b = std::min(ce, ne);
    // Carve only the edges that are cut: a carve resets that edge's ADSR stages (attack and
    // decay at a new head, release at a new tail), and a piece that keeps the clip's own start
    // or end keeps the user's fade there.
    constexpr double eps = 1e-6;
    AudioClip r = a > cs + eps ? audioCarveRight(c, a - cs, spb, devSR, map) : c;
    if (b < ce - eps) r = audioCarveLeft(r, b - a, spb, devSR, map);
    if (!c.volumeEnvelope.points.empty()) r.volumeEnvelope.points = laneSliceExact(c.volumeEnvelope, a - cs, b - cs);
    if (!c.panEnvelope.points.empty()) r.panEnvelope.points = laneSliceExact(c.panEnvelope, a - cs, b - cs);
    return r;
}

// A split fade (issue #286) is a short ADSR edge at a cut. Clips on one track cannot
// overlap, so it is a fade-out on the left piece and a fade-in on the right — the same
// handles the user can drag — not a hidden declick and not an overlapping crossfade.
// Only raises an edge that is shorter than `fadeBeats`; a longer fade stays as the user
// left it. Each edge is capped at half the piece and at the room the other stages leave.
inline constexpr double kSplitFadeSeconds = 0.004;

inline bool raiseSplitEdge(AudioClip& clip, bool fadeIn, bool fadeOut, double fadeBeats, double lenBeats) {
    if (!(fadeBeats > 0.0) || !(lenBeats > 1e-9)) return false;
    bool changed = false;
    if (fadeIn && clip.adsr.attack + 1e-9 < fadeBeats) {
        const double cap = std::min(lenBeats * 0.5, std::max(0.0, lenBeats - clip.adsr.decay - clip.adsr.release));
        const double v = std::min(fadeBeats, cap);
        if (v > clip.adsr.attack + 1e-12) {
            clip.adsr.attack = v;
            changed = true;
        }
    }
    if (fadeOut && clip.adsr.release + 1e-9 < fadeBeats) {
        const double cap = std::min(lenBeats * 0.5, std::max(0.0, lenBeats - clip.adsr.attack - clip.adsr.decay));
        const double v = std::min(fadeBeats, cap);
        if (v > clip.adsr.release + 1e-12) {
            clip.adsr.release = v;
            changed = true;
        }
    }
    return changed;
}

inline std::vector<AutomationPoint> laneSlice(const AutomationLane& lane, double a, double b) {
    return laneSliceExact(lane, a, b);
}

} // namespace nota::detail::clip_helpers
