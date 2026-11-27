// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Internal MixContext definition and inline graph helpers shared by
// RenderGraphExecutor.cpp and RenderGraphWaves.cpp.

#pragma once

#include "render/RenderGraphExecutor.h"

#include "core/EngineRuntime.h"
#include "model/EngineTypes.h"
#include "model/Track.h"
#include "nota/devices/racks/RackCore.h"
#include "nota/dsp/Device.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/rt/RenderExecutor.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "render/RenderRoutingPlan.h"

#include <cmath>
#include <cstddef>
#include <cstdint>

namespace nota::detail {
namespace render_detail {

enum class ChainRest : uint8_t { Settled, Opaque, Busy };

inline bool hasLiveDevice(const Track& t) noexcept {
    if (t.pdc && !t.pdc->isIdle()) return true;
    for (const auto& d : t.devices)
        if (d && !d->bypassed()) return true;
    return false;
}

inline ChainRest chainRest(const Track& t) noexcept {
    if (t.pdc && !t.pdc->isIdle()) return ChainRest::Busy;
    ChainRest r = ChainRest::Settled;
    for (const auto& d : t.devices) {
        if (!d || d->bypassed() || d->isIdle()) continue;
        if (d->builtinKind() >= 0) return ChainRest::Busy;
        r = ChainRest::Opaque;
    }
    return r;
}

inline bool trackRequiresActiveProcessing(const Track& t, bool anyOpenEditor = dsp::anyOpenPluginEditor(),
    bool anyNeverSleep = dsp::anyNeverSleepDevice()) noexcept {
    if (!anyOpenEditor && !anyNeverSleep) return false;
    if (t.instrument && !t.instrument->bypassed() && t.instrument->requiresActiveProcessing()) return true;
    for (const auto& d : t.devices)
        if (d && !d->bypassed() && d->requiresActiveProcessing()) return true;
    return false;
}

inline bool trackHasOpenEditor(const Track& t, bool anyOpenEditor = dsp::anyOpenPluginEditor()) noexcept {
    if (!anyOpenEditor) return false;
    if (t.instrument && !t.instrument->bypassed()) {
        if (t.instrument->editorOpen()) return true;
        if (const auto* r = t.instrument->asRack(); r && r->chainsHaveOpenEditor(true)) return true;
    }
    for (const auto& d : t.devices) {
        if (!d || d->bypassed()) continue;
        if (d->editorOpen()) return true;
        if (const auto* r = d->asRack(); r && r->chainsHaveOpenEditor(false)) return true;
    }
    return false;
}

inline bool midiEffectsIdle(const Track& t) noexcept {
    for (const auto& d : t.midiEffects)
        if (d && !d->bypassed() && !d->isIdle()) return false;
    return true;
}

inline bool canSkipInactiveBus(Track& t, bool anyOpenEditor, bool anyNeverSleep) noexcept {
    return t.tailQuiet() < 0 && chainRest(t) == ChainRest::Settled &&
           !trackRequiresActiveProcessing(t, anyOpenEditor, anyNeverSleep);
}

inline void settleInactiveBus(Track& t, const float* buf, int32_t frames, double sr, float tailFloor,
    double tailQuietSec, double tailMaxQuietSec, bool anyOpenEditor, bool anyNeverSleep) noexcept {
    int64_t& quiet = t.tailQuiet();
    if (simd_audio::peakAbs(buf, frames * 2) > tailFloor ||
        trackRequiresActiveProcessing(t, anyOpenEditor, anyNeverSleep)) {
        quiet = 0;
        return;
    }
    if (quiet < 0) quiet = 0;
    quiet += frames;
    const ChainRest rest = chainRest(t);
    if (rest == ChainRest::Settled || (rest == ChainRest::Opaque && quiet >= static_cast<int64_t>(tailQuietSec * sr)) ||
        quiet >= static_cast<int64_t>(tailMaxQuietSec * sr))
        quiet = -1;
}

inline bool canProcessTrackParallel(const Track& t) {
    if (t.instrument && !t.instrument->supportsParallelRender()) return false;
    for (const auto& d : t.devices)
        if (d && !d->bypassed() && !d->supportsParallelRender()) return false;
    return true;
}

struct BusAccumResult {
    float peakL = 0.0f;
    float peakR = 0.0f;
    double sqL = 0.0;
    double sqR = 0.0;
    double sLR = 0.0;
    int64_t recDropped = 0;
};

// Shared meter, internal recording queue, and stereo correlation pass over an already-panned buffer.
template <typename Ring>
inline BusAccumResult scanPannedMetrics(
    const float* m, int32_t frames, bool tapRec, Ring& internalQueue, bool collectStereoProduct) noexcept {
    BusAccumResult res;
    for (int32_t i = 0; i < frames; ++i) {
        const float l = m[i * 2];
        const float r = m[i * 2 + 1];
        if (tapRec && !internalQueue.push(InputFrame{l, r})) ++res.recDropped;
        const float al = std::fabs(l);
        const float ar = std::fabs(r);
        if (al > res.peakL) res.peakL = al;
        if (ar > res.peakR) res.peakR = ar;
        res.sqL += static_cast<double>(l) * static_cast<double>(l);
        res.sqR += static_cast<double>(r) * static_cast<double>(r);
        if (collectStereoProduct) res.sLR += static_cast<double>(l) * static_cast<double>(r);
    }
    return res;
}

} // namespace render_detail

struct RenderGraphExecutor::MixContext {
    EngineRuntime& eng;
    Graph* g = nullptr;
    const RenderRoutingPlan* plan = nullptr;
    float* out = nullptr;
    int32_t frames = 0;
    double blockStart = 0.0;
    bool playing = false;
    double spb = 0.0;
    double sr = 44100.0;
    bool cueOn = false;
    bool anySolo = false;
    bool arrangementActive = true;
    bool offline = true;
    bool parallelOk = false;
    bool anyOpenEditor = false;
    bool anyNeverSleep = false;
    float stereoA = 0.0f;
    float panLawStep = 0.0f;
    int32_t recSrc = 0;
    int32_t capTrackId = -1;
    int32_t numReturns = 0;
    int32_t numGroups = 0;
    int32_t maxGroupDepth = 0;
    int32_t nRoutes = 0;
    int32_t feedIds[16] = {};
    int32_t numFeeds = 0;
    TransportInfo ti{};
    dsp::RenderExecutorHost execHost{};

    int32_t cachedRouteSlot(size_t li, int32_t tid) const noexcept {
        return li < eng.rs_->trackRouteSlot.size() ? eng.rs_->trackRouteSlot[li] : eng.routeSlotForTrack(tid);
    }

    int groupSlotOf(int32_t id) const noexcept {
        if (id < 0) return -1;
        if (plan) return plan->groupSlotOfId(id);
        for (int i = 0; i < numGroups; ++i)
            if (eng.rs_->groupWork[static_cast<size_t>(i)].track &&
                eng.rs_->groupWork[static_cast<size_t>(i)].track->id() == id)
                return i;
        return -1;
    }

    bool feedsMonitor(int32_t id) const noexcept {
        for (int i = 0; i < numFeeds; ++i)
            if (feedIds[i] == id) return true;
        return false;
    }

    void markFresh(int32_t id) const noexcept {
        const int32_t s = eng.routeSlotForTrack(id);
        if (s >= 0) eng.routeFresh_[s] = true;
    }
};

} // namespace nota::detail
