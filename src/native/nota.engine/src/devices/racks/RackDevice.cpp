// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackDevice implementation — processing bodies

#include "RackDevice.h"

#include "dsp/core/RenderExecutor.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>

namespace nota {

void RackDevice::process(float* buf, int32_t frames) {
    const auto done = renderScope();
    RackState* st = live();
    if (!st || frames <= 0 || frames > kMaxBlock) return;
    if (st->chains.empty()) return; // pass-through until chains exist
    if (isIdle() && dsp::SilenceGate::isBufferSilent(buf, frames * 2, dsp::SilenceGate::kDefaultReleaseThreshold))
        return;

    const int32_t cap = std::max(1, arena_.capacityFrames());
    for (int32_t off = 0; off < frames;) {
        const int32_t sub = std::min(frames - off, cap);
        processChunk(*st, buf + off * 2, sub);
        off += sub;
    }
}

void RackDevice::processChunk(RackState& st, float* buf, int32_t frames) {
    float* const input = arena_.buffer(0);
    float* const acc = arena_.buffer(1);
    float* const scratch = arena_.buffer(2);
    const int32_t mode = mode_.load(std::memory_order_relaxed);
    const float gain = rackVolume_.load(std::memory_order_relaxed);
    const float dw = dryWet_.load(std::memory_order_relaxed);
    std::memcpy(input, buf, sizeof(float) * frames * 2);

    bool anySolo = false;
    for (auto& c : st.chains)
        if (c.ctl->solo.load(std::memory_order_relaxed)) {
            anySolo = true;
            break;
        }

    // Select-mode selector position: manual, or driven by the input level.
    float sel = chainSelect_.load(std::memory_order_relaxed);
    if (mode == 2 && selFollow_.load(std::memory_order_relaxed)) {
        double sum = 0.0;
        for (int32_t i = 0; i < frames * 2; ++i) sum += (double)input[i] * input[i];
        const double rms = std::sqrt(sum / std::max(1, frames * 2));
        const double db = rms > 1e-6 ? 20.0 * std::log10(rms) : -60.0;
        sel = (float)std::clamp((db + 48.0) / 48.0, 0.0, 1.0); // −48..0 dB → 0..1
    }
    liveSel_.store(sel, std::memory_order_relaxed);

    if (mode == 1) {
        // Series: one running buffer flows through each chain in order.
        std::memcpy(acc, input, sizeof(float) * frames * 2);
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
        std::memset(acc, 0, sizeof(float) * frames * 2);
        struct ActiveChain {
            RackChain* chain = nullptr;
            float lg = 0.0f;
            float rg = 0.0f;
        };
        std::array<ActiveChain, dsp::ChainScratchBank::kMaxSlots> work{};
        int32_t nActive = 0;
        int32_t nChainDevs = 0;
        bool allParallel = true;
        bool overflow = false;

        for (auto& c : st.chains) {
            const bool solo = c.ctl->solo.load(std::memory_order_relaxed);
            const bool mute = c.ctl->mute.load(std::memory_order_relaxed);
            bool active = !(mute || (anySolo && !solo));
            if (active && mode == 2) {
                const float lo = c.ctl->velLo.load(std::memory_order_relaxed) / 127.0f;
                const float hi = c.ctl->velHi.load(std::memory_order_relaxed) / 127.0f;
                active = sel >= lo && sel <= hi; // chain-select zone covers the selector
            }
            if (!active) {
                c.ctl->meter.store(0.0f, std::memory_order_relaxed);
                continue;
            }
            const float g = c.ctl->gain.load(std::memory_order_relaxed);
            const float pan = c.ctl->pan.load(std::memory_order_relaxed);
            const float lg = g * (pan > 0.0f ? 1.0f - pan : 1.0f);
            const float rg = g * (pan < 0.0f ? 1.0f + pan : 1.0f);
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
                std::memcpy(bufSlot, input, sizeof(float) * frames * 2);
                for (auto& d : c.devices)
                    if (d && !d->bypassed()) d->process(bufSlot, frames);
            });
            for (int32_t k = 0; k < nActive; ++k) {
                const ActiveChain& w = work[static_cast<size_t>(k)];
                const float* bufSlot = parallelArena_.buffer(k);
                float peak = 0.0f;
                for (int32_t i = 0; i < frames; ++i) {
                    const float l = bufSlot[i * 2] * w.lg, r = bufSlot[i * 2 + 1] * w.rg;
                    acc[i * 2] += l;
                    acc[i * 2 + 1] += r;
                    const float a = std::max(std::fabs(l), std::fabs(r));
                    if (a > peak) peak = a;
                }
                w.chain->ctl->meter.store(peak, std::memory_order_relaxed);
            }
        } else {
            for (auto& c : st.chains) {
                const bool solo = c.ctl->solo.load(std::memory_order_relaxed);
                const bool mute = c.ctl->mute.load(std::memory_order_relaxed);
                bool active = !(mute || (anySolo && !solo));
                if (active && mode == 2) {
                    const float lo = c.ctl->velLo.load(std::memory_order_relaxed) / 127.0f;
                    const float hi = c.ctl->velHi.load(std::memory_order_relaxed) / 127.0f;
                    active = sel >= lo && sel <= hi;
                }
                if (!active) continue;
                std::memcpy(scratch, input, sizeof(float) * frames * 2);
                for (auto& d : c.devices)
                    if (d && !d->bypassed()) d->process(scratch, frames);
                const float g = c.ctl->gain.load(std::memory_order_relaxed);
                const float pan = c.ctl->pan.load(std::memory_order_relaxed);
                const float lg = g * (pan > 0.0f ? 1.0f - pan : 1.0f);
                const float rg = g * (pan < 0.0f ? 1.0f + pan : 1.0f);
                float peak = 0.0f;
                for (int32_t i = 0; i < frames; ++i) {
                    const float l = scratch[i * 2] * lg, r = scratch[i * 2 + 1] * rg;
                    acc[i * 2] += l;
                    acc[i * 2 + 1] += r;
                    const float a = std::max(std::fabs(l), std::fabs(r));
                    if (a > peak) peak = a;
                }
                c.ctl->meter.store(peak, std::memory_order_relaxed);
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

} // namespace nota
