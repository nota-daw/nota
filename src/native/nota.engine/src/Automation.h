// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Parameter automation (M9). A lane binds one target to a breakpoint envelope.
// Structural data — lives in the immutable Graph (on Track, or per clip), copied
// by cloneTrack, so undo/redo comes for free (M6-6). Shared by Track.h (track +
// clip envelopes) and MidiClip.h (MIDI clip envelopes) — kept in its own header
// to avoid an include cycle between them.

#pragma once

#include <cmath>
#include <cstdint>
#include <string>
#include <vector>

namespace nota {

enum class AutomationTarget : int32_t { Volume = 0, Pan = 1, DeviceParam = 2, PluginParam = 3, MidiDeviceParam = 4 };

// `curve` (M9-D) shapes the segment FROM this point TO the next: 0 = linear,
// >0 = ease-out (fast start), <0 = ease-in (slow start), range [-1,1].
struct AutomationPoint { double beat = 0.0; float value = 0.0f; float curve = 0.0f; }; // sorted by beat

struct AutomationLane {
    AutomationTarget target = AutomationTarget::Volume;
    int32_t deviceIndex = -1;   // DeviceParam / PluginParam (PluginParam: <0 = instrument)
    int32_t paramIndex  = -1;   // DeviceParam; PluginParam: resolved from paramId
    std::string paramId;        // PluginParam only: stable JUCE parameter ID (M9-B)
    bool suppressRead = false;  // transient (M9-C): while this target is being written,
                                // applyAutomation skips it so the user's control leads.
                                // Part of the snapshot (no audio-thread race); not persisted.
    std::vector<AutomationPoint> points;

    // Interpolation between surrounding points with per-segment curvature (M9-D);
    // hold before the first / after the last. Caller guarantees points non-empty & sorted.
    float valueAt(double beat) const {
        const auto& p = points;
        if (beat <= p.front().beat) return p.front().value;
        if (beat >= p.back().beat)  return p.back().value;
        for (size_t i = 1; i < p.size(); ++i) {
            if (beat <= p[i].beat) {
                const double b0 = p[i - 1].beat, b1 = p[i].beat;
                const float  v0 = p[i - 1].value, v1 = p[i].value;
                const double span = b1 - b0;
                if (span <= 0.0) return v1;
                const double t = (beat - b0) / span;
                return static_cast<float>(v0 + (v1 - v0) * shape(t, p[i - 1].curve));
            }
        }
        return p.back().value;
    }

    // Power-curve shaping of a 0..1 fraction. curve 0 -> linear; endpoints exact.
    static double shape(double t, float curve) {
        if (curve == 0.0f) return t;
        const double e = std::pow(2.0, -curve * 4.0); // >0: <1 (ease-out), <0: >1 (ease-in)
        return std::pow(t, e);
    }
};

} // namespace nota
