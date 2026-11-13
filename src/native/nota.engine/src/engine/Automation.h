// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Parameter automation (M9). A lane binds one target to a breakpoint envelope.
// Structural data — lives in the immutable Graph (on Track, or per clip), copied
// by cloneTrack, so undo/redo comes for free (M6-6). Shared by Track.h (track +
// clip envelopes) and MidiClip.h (MIDI clip envelopes) — kept in its own header
// to avoid an include cycle between them.

#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <string>
#include <vector>

namespace nota {

// Send stays 5 so a stored lane does not land on another target. It is not automated
// (specs/069): addAutomationLane rejects it and applyAutomation does not play it.
enum class AutomationTarget : int32_t { Volume = 0, Pan = 1, DeviceParam = 2, PluginParam = 3, MidiDeviceParam = 4, Send = 5 };

// The track's own mix values — volume and pan. Not tied to a device, so their lanes
// follow clips to any track (a device lane's index is only meaningful on its own chain).
inline bool isMixTarget(AutomationTarget t) {
    return t == AutomationTarget::Volume || t == AutomationTarget::Pan;
}

// `curve` (M9-D) shapes the segment FROM this point TO the next: 0 = linear,
// >0 = ease-out (fast start), <0 = ease-in (slow start), range [-1,1].
struct AutomationPoint {
    double beat = 0.0;
    float value = 0.0f;
    float curve = 0.0f;
}; // sorted by beat

struct AutomationLane {
    AutomationTarget target = AutomationTarget::Volume;
    int32_t deviceIndex = -1;  // DeviceParam / PluginParam (PluginParam: <0 = instrument); Send: the bus
    int32_t paramIndex = -1;   // DeviceParam; PluginParam: resolved from paramId
    std::string paramId;       // PluginParam only: stable JUCE parameter ID (M9-B)
    bool suppressRead = false; // transient (M9-C): while this target is being written,
                               // applyAutomation skips it so the user's control leads.
                               // Part of the snapshot (no audio-thread race); not persisted.
    std::vector<AutomationPoint> points;

    // Interpolation between surrounding points with per-segment curvature (M9-D);
    // hold before the first / after the last. Caller guarantees points non-empty & sorted.
    float valueAt(double beat) const;

    // Steepness of curve ±1 (#591). Mirrored by Nota.Application.Editing.AutomationCurve.
    static constexpr double kCurveSteepness = 8.0;

    // Exponential shaping of a 0..1 fraction (#591): expm1(k·t)/expm1(k), k = −curve·K.
    // curve 0 -> linear, >0 ease-out, <0 ease-in (mirror images); endpoints exact and the
    // slope finite at both ends, so no curve jumps. The family is shift-invariant: the part
    // of a segment over [t0, t1] is the same shape with curve·(t1 − t0) (see subCurve).
    static double shape(double t, float curve) {
        const double k = -static_cast<double>(curve) * kCurveSteepness;
        if (std::fabs(k) < 1e-6) return t;
        return std::expm1(k * t) / std::expm1(k);
    }

    // The curve of a piece of a segment spanning `fraction` (0..1) of it, exact for any
    // piece: a segment cut short or a tail starting part-way keeps its values.
    static float subCurve(float curve, double fraction) {
        return static_cast<float>(curve * std::clamp(fraction, 0.0, 1.0));
    }

    // The curve a piece of `p` starting at `from` needs when it runs to its next point or is
    // cut at `to`, whichever comes first: the segment's curve scaled to that share of it.
    // 0 before the first / from the last point on. `p` sorted by beat.
    static float pieceCurve(const std::vector<AutomationPoint>& p, double from, double to);
};

// A track's own automation lane (specs/044): it may point at a device grouped into a rack.
// deviceIndex is then the rack's address on the track (>= 0 effect, -1 instrument,
// midiHost(i) MIDI effect) and the lane drives the child at (chainUid, slot); `target` names
// the child's param surface (DeviceParam / MidiDeviceParam generic params, PluginParam the
// plugin-param surface, slot -1 = the chain instrument). chainUid 0 = a track-level lane.
// Clip envelopes stay plain AutomationLanes: every audio clip copies two of them, and a
// bigger lane made clip edits measurably slower.
struct TrackLane : AutomationLane {
    uint64_t chainUid = 0;
    int32_t slot = -1;
    bool nested() const { return chainUid != 0; }
};

} // namespace nota
