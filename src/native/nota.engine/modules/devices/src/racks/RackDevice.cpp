// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackDevice implementation — processing bodies

#include "nota/devices/racks/RackDevice.h"

#include "common/DeviceMath.h"
#include "nota/dsp/rt/RenderExecutor.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/dsp/rt/ScratchArena.h"
#include "nota/devices/racks/RackCore.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>

namespace nota::devices {

namespace {

inline float computeChainSelector(const float* buf, int32_t frames, float manualSel, bool follow) noexcept {
    if (!follow) return manualSel;
    double sum = 0.0;
    const int32_t total = frames * 2;
    for (int32_t i = 0; i < total; ++i) sum += static_cast<double>(buf[i]) * buf[i];
    const double rms = sum > 0.0 ? std::sqrt(sum / std::max(1, total)) : 0.0;
    const double db = rms > 1e-6 ? 20.0 * std::log10(rms) : -60.0;
    return static_cast<float>(std::clamp((db + 48.0) / 48.0, 0.0, 1.0)); // −48..0 dB → 0..1
}

inline void chainStereoGains(const RackCore::RackChain& c, float& lg, float& rg) noexcept {
    const float g = c.ctl->gain.load(std::memory_order_relaxed);
    const float pan = c.ctl->pan.load(std::memory_order_relaxed);
    lg = g * (pan > 0.0f ? 1.0f - pan : 1.0f);
    rg = g * (pan < 0.0f ? 1.0f + pan : 1.0f);
}

} // namespace

RackDevice::~RackDevice() = default;

void RackDevice::process(float* buf, int32_t frames) {
    const auto done = renderScope();
    RackState* st = live();
    if (!st || frames <= 0 || frames > kMaxBlock || st->chains.empty()) return;
    processActive(*st, buf, frames);
}

void RackDevice::processActive(RackState& st, float* buf, int32_t frames) {
    if (!st.hasChildren && st.chains.size() == 1) {
        processEmptySingleChain(st, buf, frames);
        return;
    }
    if (st.hasChildren && isIdle() &&
        dsp::SilenceGate::isBufferSilent(buf, frames * 2, dsp::SilenceGate::kDefaultReleaseThreshold))
        return;

    const int32_t cap = std::max(1, arena_.capacityFrames());
    for (int32_t off = 0; off < frames;) {
        const int32_t sub = std::min(frames - off, cap);
        processChunk(st, buf + off * 2, sub);
        off += sub;
    }
}

void RackDevice::processEmptySingleChain(RackState& st, float* buf, int32_t frames) {
    RackChain& c = st.chains[0];
    const int32_t mode = mode_.load(std::memory_order_relaxed);
    const float gain = rackVolume_.load(std::memory_order_relaxed);
    const float dw = dryWet_.load(std::memory_order_relaxed);
    const float wet = dw, dry = 1.0f - dw;
    const float sel = computeChainSelector(buf, frames, chainSelect_.load(std::memory_order_relaxed),
        mode == 2 && selFollow_.load(std::memory_order_relaxed));
    liveSel_.store(sel, std::memory_order_relaxed);

    const bool mute = c.ctl->mute.load(std::memory_order_relaxed);
    bool selected = !mute;
    if (selected && mode == 2) {
        const float lo = static_cast<float>(c.ctl->velLo.load(std::memory_order_relaxed)) / 127.0f;
        const float hi = static_cast<float>(c.ctl->velHi.load(std::memory_order_relaxed)) / 127.0f;
        selected = sel >= lo && sel <= hi;
    }
    if (!selected) {
        c.ctl->meter.store(0.0f, std::memory_order_relaxed);
        const float outScale = (mode == 1 ? (dry + wet) : dry) * gain;
        for (int32_t i = 0; i < frames * 2; ++i) buf[i] *= outScale;
        return;
    }

    float lg = 0.0f, rg = 0.0f;
    if (mode == 1) {
        lg = rg = c.ctl->gain.load(std::memory_order_relaxed);
    } else {
        chainStereoGains(c, lg, rg);
    }
    float peak = 0.0f;
    for (int32_t i = 0; i < frames; ++i) {
        const float inL = buf[i * 2], inR = buf[i * 2 + 1];
        const float chL = inL * lg, chR = inR * rg;
        const float a = std::max(std::fabs(chL), std::fabs(chR));
        if (a > peak) peak = a;
        buf[i * 2] = (inL * dry + chL * wet) * gain;
        buf[i * 2 + 1] = (inR * dry + chR * wet) * gain;
    }
    c.ctl->meter.store(peak, std::memory_order_relaxed);
}

void RackDevice::processChunk(RackState& st, float* buf, int32_t frames) {
    float* __restrict const input = arena_.buffer(0);
    float* __restrict const acc = arena_.buffer(1);
    float* __restrict const scratch = arena_.buffer(2);
    const int32_t mode = mode_.load(std::memory_order_relaxed);
    const float gain = rackVolume_.load(std::memory_order_relaxed);
    const float dw = dryWet_.load(std::memory_order_relaxed);
    std::memcpy(input, buf, sizeof(float) * static_cast<size_t>(frames) * 2);

    bool anySolo = false;
    for (auto& c : st.chains)
        if (c.ctl->solo.load(std::memory_order_relaxed)) {
            anySolo = true;
            break;
        }

    // Select-mode selector position: manual, or driven by the input level.
    const float sel = computeChainSelector(input, frames, chainSelect_.load(std::memory_order_relaxed),
        mode == 2 && selFollow_.load(std::memory_order_relaxed));
    liveSel_.store(sel, std::memory_order_relaxed);

    if (mode == 1) {
        // Series: one running buffer flows through each chain in order.
        std::memcpy(acc, input, sizeof(float) * static_cast<size_t>(frames) * 2);
        for (auto& c : st.chains) {
            const bool solo = c.ctl->solo.load(std::memory_order_relaxed);
            const bool mute = c.ctl->mute.load(std::memory_order_relaxed);
            if (mute || (anySolo && !solo)) {
                c.ctl->meter.store(0.0f, std::memory_order_relaxed);
                continue;
            }
            for (auto& d : c.devices)
                if (d && !d->bypassed()) d->process(acc, frames);
            const float g = c.ctl->gain.load(std::memory_order_relaxed);
            float peak = 0.0f;
            for (int32_t i = 0; i < frames; ++i) {
                acc[i * 2] *= g;
                acc[i * 2 + 1] *= g;
                const float a = std::max(std::fabs(acc[i * 2]), std::fabs(acc[i * 2 + 1]));
                if (a > peak) peak = a;
            }
            c.ctl->meter.store(peak, std::memory_order_relaxed);
        }
    } else {
        // Parallel (0) / Select (2): fan the input to each active chain and sum.
        std::memset(acc, 0, sizeof(float) * static_cast<size_t>(frames) * 2);
        auto chainSelected = [&](const RackChain& c) {
            const bool solo = c.ctl->solo.load(std::memory_order_relaxed);
            const bool mute = c.ctl->mute.load(std::memory_order_relaxed);
            if (mute || (anySolo && !solo)) return false;
            if (mode == 2) {
                const float lo = static_cast<float>(c.ctl->velLo.load(std::memory_order_relaxed)) / 127.0f;
                const float hi = static_cast<float>(c.ctl->velHi.load(std::memory_order_relaxed)) / 127.0f;
                return sel >= lo && sel <= hi; // chain-select zone covers the selector
            }
            return true;
        };
        auto mixChainBuffer = [&](RackChain& c, const float* __restrict src, float lg, float rg) {
            float peak = 0.0f;
            for (int32_t i = 0; i < frames; ++i) {
                const float l = src[i * 2] * lg, r = src[i * 2 + 1] * rg;
                acc[i * 2] += l;
                acc[i * 2 + 1] += r;
                const float a = std::max(std::fabs(l), std::fabs(r));
                if (a > peak) peak = a;
            }
            c.ctl->meter.store(peak, std::memory_order_relaxed);
        };
        auto runSequentialChain = [&](RackChain& c, float lg, float rg) {
            std::memcpy(scratch, input, sizeof(float) * static_cast<size_t>(frames) * 2);
            for (auto& d : c.devices)
                if (d && !d->bypassed()) d->process(scratch, frames);
            mixChainBuffer(c, scratch, lg, rg);
        };

        if (st.chains.size() < 3) {
            for (auto& c : st.chains) {
                if (!chainSelected(c)) {
                    c.ctl->meter.store(0.0f, std::memory_order_relaxed);
                    continue;
                }
                float lg = 0.0f, rg = 0.0f;
                chainStereoGains(c, lg, rg);
                runSequentialChain(c, lg, rg);
            }
        } else {
            struct ActiveChain {
                RackChain* chain = nullptr;
                float lg = 0.0f;
                float rg = 0.0f;
            };
            std::array<ActiveChain, dsp::ChainScratchBank::kMaxSlots> work;
            int32_t nActive = 0;
            int32_t nChainDevs = 0;
            bool allParallel = true;
            bool overflow = false;

            for (auto& c : st.chains) {
                if (!chainSelected(c)) {
                    c.ctl->meter.store(0.0f, std::memory_order_relaxed);
                    continue;
                }
                float lg = 0.0f, rg = 0.0f;
                chainStereoGains(c, lg, rg);
                if (nActive < dsp::ChainScratchBank::kMaxSlots) {
                    work[static_cast<size_t>(nActive++)] = {&c, lg, rg};
                    for (const auto& d : c.devices)
                        if (d && !d->bypassed()) ++nChainDevs;
                    if (!chainSupportsParallel(c)) allParallel = false;
                } else {
                    overflow = true;
                }
            }

            const bool fanOut = !overflow && (nActive >= 5 || (nActive >= 3 && nChainDevs >= 3)) && allParallel &&
                                parallelArena_.slotCount() >= nActive && parallelArena_.capacityFrames() >= frames;
            if (fanOut) {
                dsp::RenderExecutor::parallelFor(nActive, true, [&](int32_t k) {
                    RackChain& c = *work[static_cast<size_t>(k)].chain;
                    float* bufSlot = parallelArena_.buffer(k);
                    std::memcpy(bufSlot, input, sizeof(float) * static_cast<size_t>(frames) * 2);
                    for (auto& d : c.devices)
                        if (d && !d->bypassed()) d->process(bufSlot, frames);
                });
                for (int32_t k = 0; k < nActive; ++k) {
                    const ActiveChain& w = work[static_cast<size_t>(k)];
                    mixChainBuffer(*w.chain, parallelArena_.buffer(k), w.lg, w.rg);
                }
            } else if (!overflow) {
                for (int32_t k = 0; k < nActive; ++k) {
                    const ActiveChain& w = work[static_cast<size_t>(k)];
                    runSequentialChain(*w.chain, w.lg, w.rg);
                }
            } else {
                for (auto& c : st.chains) {
                    if (!chainSelected(c)) continue;
                    float lg = 0.0f, rg = 0.0f;
                    chainStereoGains(c, lg, rg);
                    runSequentialChain(c, lg, rg);
                }
            }
        }
    }

    // Rack output stage: dry/wet mix against the input, then rack gain.
    const float wet = dw, dry = 1.0f - dw;
    for (int32_t i = 0; i < frames * 2; ++i) buf[i] = (input[i] * dry + acc[i] * wet) * gain;
}

int32_t RackDevice::latencySamples() const {
    if (!pdc_.load(std::memory_order_relaxed)) return 0;
    if (mode_.load(std::memory_order_relaxed) == 1) {
        RackState* st = live();
        if (!st) return 0;
        int32_t lat = 0;
        for (auto& c : st->chains)
            for (auto& d : c.devices)
                if (d) lat += d->latencySamples();
        return lat;
    }
    return maxChainLatency();
}

std::shared_ptr<Device> RackDevice::clone() const {
    auto r = std::make_shared<RackDevice>();
    r->setSampleRate(sampleRate_, maxBlock_);
    auto blob = serialize();
    r->setState(blob.data(), static_cast<int32_t>(blob.size()));
    r->ensureBlockCapacity(arena_.capacityFrames());
    return r;
}

} // namespace nota::devices
