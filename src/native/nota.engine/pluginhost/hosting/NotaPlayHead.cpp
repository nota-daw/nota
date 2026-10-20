// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// NotaPlayHead implementation.

#include "NotaPlayHead.h"

#include <atomic>
#include <cmath>
#include <cstdio>

namespace nota {
namespace hosting {

juce::Optional<juce::AudioPlayHead::PositionInfo> NotaPlayHead::getPosition() const {
    PositionInfo p;
    p.setBpm(info.bpm);
    p.setTimeSignature(TimeSignature{info.tsNum, info.tsDenom});
    p.setPpqPosition(info.ppqPosition);
    // Start of the current bar (quarter notes): many tempo-synced plugins
    // beat-align their playback to this rather than to the raw ppq.
    const double beatsPerBar = info.tsDenom > 0 ? info.tsNum * (4.0 / info.tsDenom) : static_cast<double>(info.tsNum);
    if (beatsPerBar > 0.0) p.setPpqPositionOfLastBarStart(std::floor(info.ppqPosition / beatsPerBar) * beatsPerBar);
    p.setTimeInSamples(info.timeInSamples);
    p.setTimeInSeconds(info.timeInSeconds);
    p.setIsPlaying(info.isPlaying);
    p.setIsLooping(info.isLooping);
    if (info.isLooping) p.setLoopPoints(LoopPoints{info.ppqLoopStart, info.ppqLoopEnd});

    // Diagnostic (NOTA_DEBUG_PLAYHEAD=1): proves the plugin is actually querying
    // our playhead and shows the values it sees. If nothing ever prints while a
    // plugin runs, that plugin ignores host transport (its own problem, not ours).
    static const bool dbg = std::getenv("NOTA_DEBUG_PLAYHEAD") != nullptr;
    if (dbg) {
        static std::atomic<int> n{0};
        if ((n.fetch_add(1, std::memory_order_relaxed) % 200) == 0)
            std::fprintf(stderr, "[nota playhead] bpm=%.2f ppq=%.3f playing=%d looping=%d\n", info.bpm,
                info.ppqPosition, (int)info.isPlaying, (int)info.isLooping);
    }
    return p;
}

} // namespace hosting
} // namespace nota
