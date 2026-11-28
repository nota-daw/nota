// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "hosting/NotaPlayHead.h"

#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstdlib>

namespace nota::pluginhost::detail {

juce::Optional<juce::AudioPlayHead::PositionInfo> NotaPlayHead::getPosition() const {
    PositionInfo p;
    p.setBpm(info.bpm);
    p.setTimeSignature(TimeSignature{info.tsNum, info.tsDenom});
    p.setPpqPosition(info.ppqPosition);
    const double beatsPerBar = info.tsDenom > 0 ? info.tsNum * (4.0 / info.tsDenom) : static_cast<double>(info.tsNum);
    if (beatsPerBar > 0.0) { p.setPpqPositionOfLastBarStart(std::floor(info.ppqPosition / beatsPerBar) * beatsPerBar); }
    p.setTimeInSamples(info.timeInSamples);
    p.setTimeInSeconds(info.timeInSeconds);
    p.setIsPlaying(info.isPlaying);
    p.setIsLooping(info.isLooping);
    if (info.isLooping) p.setLoopPoints(LoopPoints{info.ppqLoopStart, info.ppqLoopEnd});

    static const bool dbg = std::getenv("NOTA_DEBUG_PLAYHEAD") != nullptr;
    if (dbg) {
        static std::atomic<int> n{0};
        if ((n.fetch_add(1, std::memory_order_relaxed) % 200) == 0) {
            std::fprintf(stderr, "[nota playhead] bpm=%.2f ppq=%.3f playing=%d looping=%d\n", info.bpm,
                info.ppqPosition, static_cast<int>(info.isPlaying), static_cast<int>(info.isLooping));
        }
    }
    return p;
}

} // namespace nota::pluginhost::detail
