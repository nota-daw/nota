// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Realtime streaming warp (roadmap item 3). Replaces the offline stretched cache:
// each warped clip owns a live Signalsmith stretcher run on the audio thread, so
// tempo changes become a live ratio (no rebuild) and no per-clip cache buffer is
// held. Configured (allocating + warmed up) on the message thread; render() is
// allocation-free. PIMPL keeps the heavy Signalsmith template out of headers.

#pragma once

#include "Warp.h"   // WarpMode, WarpMarker

#include <memory>
#include <vector>

namespace nota {

struct SampleBuffer;

class ClipWarpStream {
public:
    ClipWarpStream();
    ~ClipWarpStream();
    ClipWarpStream(const ClipWarpStream&) = delete;
    ClipWarpStream& operator=(const ClipWarpStream&) = delete;

    // --- message thread ---
    // (Re)configure for a clip's DSP identity: builds + warms up the stretcher so
    // the audio thread never allocates. Only mode/pitch/channels/rates matter here
    // (tempo/markers are read live in render()). srcSR is the sample's own rate — the
    // stretcher is a same-rate processor, so a src≠device rate would pitch the output
    // by devSR/srcSR; we cancel that in the transpose so warp stays at natural pitch.
    void configure(int channels, WarpMode mode, float pitchSemitones, double srcSR, double devSR);
    bool ready() const;

    // --- audio thread ---
    // Produce `frames` device-rate output for the timeline region beginning at
    // `outStartSamples` (device frames from the clip's warp origin, i.e. 0 ==
    // marker[0].beat). Maps output→beat→source through `markers`, feeds Signalsmith
    // at the live ratio, and ADDS interleaved stereo × gain into `out`. Re-seeks on
    // any discontinuity (loop wrap / transport seek / (re)start).
    void render(float* out, int frames, double outStartSamples,
                const std::vector<WarpMarker>& markers, double warpBeats,
                const SampleBuffer& src, double spb, double devSR, float gain);

private:
    struct Impl;
    std::unique_ptr<Impl> d_;
};

} // namespace nota
