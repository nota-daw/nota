// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RenderGraphExecutor — DAG wave scheduler and device-chain runner extracted
// from Engine_Render.cpp. Schedules leaves (sidechain levels), Group submixes
// (depth + intra-depth sidechain waves), and Return buses (sidechain waves)
// with deterministic index-order reduction (0 vs N threads bit-identical), and
// runs contiguous planar effect stretches through PlanarStereoBuffer.

#pragma once

#include "engine/EngineTypes.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

namespace dsp {
class PlanarStereoBuffer;
}

class Engine;
struct Graph;
class Track;
struct TransportInfo;

namespace render_detail {

constexpr double kTwoPi = 6.283185307179586;

struct PanGains {
    float ll, lr, rl, rr;
};

inline PanGains computePanGains(float pan, float vol, PanLaw law) noexcept {
    const double theta = (pan + 1.0) * (kTwoPi / 8.0);
    const double c = std::cos(theta);
    const double s = std::sin(theta);
    if (law == PanLaw::Balance) return {static_cast<float>(c) * vol, 0.0f, 0.0f, static_cast<float>(s) * vol};
    const double b = 0.5 * std::fabs(pan);
    if (pan <= 0.0f)
        return {static_cast<float>(c - b) * vol, static_cast<float>(b) * vol, 0.0f, static_cast<float>(s) * vol};
    return {static_cast<float>(c) * vol, 0.0f, static_cast<float>(b) * vol, static_cast<float>(s - b) * vol};
}

struct PanStage {
    PanGains to, from;
    bool cross, fade;
    float pos, step;

    PanStage(float pan, float vol, PanLaw toLaw, PanLaw fromLaw, float fadePos, float fadeStep) noexcept
        : to(computePanGains(pan, vol, toLaw)), from(to), fade(fadePos < 1.0f), pos(fadePos), step(fadeStep) {
        if (fade) from = computePanGains(pan, vol, fromLaw);
        cross = fade || to.lr != 0.0f || to.rl != 0.0f;
    }

    void apply(int32_t i, float inL, float inR, float& l, float& r) const noexcept {
        if (!cross) {
            l = inL * to.ll;
            r = inR * to.rr;
            return;
        }
        PanGains k = to;
        if (fade) {
            const float x = std::min(1.0f, pos + step * static_cast<float>(i));
            k = {from.ll + (to.ll - from.ll) * x, from.lr + (to.lr - from.lr) * x, from.rl + (to.rl - from.rl) * x,
                from.rr + (to.rr - from.rr) * x};
        }
        l = k.ll * inL + k.lr * inR;
        r = k.rl * inL + k.rr * inR;
    }
};

inline float stereoBlockStep(int32_t frames, double sr) noexcept {
    return static_cast<float>(1.0 - std::exp(-frames / (0.3 * (sr > 0.0 ? sr : 44100.0))));
}

inline void stereoFold(std::atomic<float>& eL, std::atomic<float>& eR, std::atomic<float>& eLR, double sqL, double sqR,
    double sLR, int32_t frames, float a) noexcept {
    const double n = frames > 0 ? frames : 1;
    auto fold = [a](std::atomic<float>& e, double mean) {
        const float cur = e.load(std::memory_order_relaxed);
        e.store(cur + a * (static_cast<float>(mean) - cur), std::memory_order_relaxed);
    };
    fold(eL, sqL / n);
    fold(eR, sqR / n);
    fold(eLR, sLR / n);
}

} // namespace render_detail

class RenderGraphExecutor {
  public:
    static void mixGraph(Engine& eng, Graph* g, float* out, int32_t frames, double blockStart, double elapsedStart,
        bool playing, double spb);

    static void processDeviceChain(Engine& eng, Graph* g, Track& t, float* buf, int32_t wid, int32_t frames,
        double blockStart, double spb, bool playing, const TransportInfo& ti, bool frozenActive,
        dsp::PlanarStereoBuffer* overridePlanar = nullptr);

  private:
    struct MixContext;

    static uint32_t collectChainSidechainNeeds(
        const Engine& eng, const Track& t, bool includeInstrument, bool includeDevices);
    static void runLeafDeviceList(Engine& eng, Graph* g, Track& t, float* buf, int32_t wid, int32_t frames,
        double blockStart, double spb, bool playing, const TransportInfo& ti,
        dsp::PlanarStereoBuffer* overridePlanar = nullptr);
    static bool instrumentHasBlockInput(const MixContext& ctx, const Track& t, size_t trackPos);
    static bool instrumentLeafCanSkip(const MixContext& ctx, const Track& t, size_t trackPos, bool capturing);
    static void initGroupsAndBuses(MixContext& ctx);
    static void classifyLeaves(MixContext& ctx, int32_t* slotLeaf, int32_t& outMaxLevel);
    static void writeLeafTaps(MixContext& ctx, size_t leafIndex, const float* pre, const float* post);
    static void runLeafWaves(MixContext& ctx, const int32_t* slotLeaf, int32_t maxLevel);
    static void reduceLeavesAndMeterInputs(MixContext& ctx, const int32_t* slotLeaf);
    template <typename BusWorkT>
    static int32_t assignBusWaves(const MixContext& ctx, BusWorkT* items, int32_t count);
    static void runGroupWaves(MixContext& ctx);
    static void runReturnWaves(MixContext& ctx);
    static void finalizeMixBlock(MixContext& ctx);
};

} // namespace nota
