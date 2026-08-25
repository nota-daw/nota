// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Clip warping (time-stretch + pitch) built on Signalsmith Stretch (MIT, vendored).
// Offline model: when a clip is warped, we render its source into a device-rate
// buffer stretched to the target musical length; the audio thread then plays that
// cached buffer straight. Recomputed on tempo/pitch/length/mode change.

#pragma once

#include "SampleBuffer.h"
#include <cstdint>
#include <memory>
#include <vector>

namespace nota {

// Warp modes. RePitch changes pitch with speed (resample); the
// rest are pitch-preserving stretches tuned for different material.
enum class WarpMode : int32_t {
    Beats = 0,       // percussive: short window, transient-preserving
    Tones = 1,       // monophonic pitched: tonal window
    Texture = 2,     // atmospheric/polyphonic: long window (granular feel)
    Complex = 3,     // full mixes: default high-quality preset
    ComplexPro = 4,  // full mixes + formant preservation
    RePitch = 5,     // vinyl/tape: speed and pitch move together (resample)
};

// A warp marker ties a source position (frames) to a musical position (beats).
// Consecutive markers bound a segment stretched independently to its beat span.
struct WarpMarker { double srcFrame; double beat; };

// Offline stretch cache: the clip's played window rendered ONCE to
// a device-rate interleaved-stereo buffer on the authoring thread, so the audio
// thread just copies from it (no realtime stretcher, no per-clip-start priming
// spike). Immutable after build and shared across graph snapshots via shared_ptr;
// rebuilt whenever anything the stretch depends on changes — tempo (spb), pitch,
// mode, markers, the play window, or the device sample rate. Clip gain is applied
// at read time (NOT baked), so gain changes need no rebuild.
struct WarpCache {
    std::vector<float> samples;   // interleaved stereo, frames*2 (device rate)
    int64_t frames = 0;           // played-window length in device frames
    double  spb = 0.0;            // device samples-per-beat this was built for
    double  devSR = 0.0;          // device sample rate this was built for
};

} // namespace nota
