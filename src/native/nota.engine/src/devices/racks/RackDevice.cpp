// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackDevice implementation — processing bodies

#include "RackDevice.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace nota {

void RackDevice::process(float* buf, int32_t frames) {
    const auto done = renderScope();
    RackState* st = live();
    if (!st || frames <= 0 || frames > kMaxBlock) return;
    if (st->chains.empty()) return; // pass-through until chains exist

    const int32_t mode = mode_.load(std::memory_order_relaxed);
    const float gain = rackVolume_.load(std::memory_order_relaxed);
    const float dw = dryWet_.load(std::memory_order_relaxed);
    std::memcpy(input_, buf, sizeof(float) * frames * 2);

    bool anySolo = false;
    for (auto& c : st->chains)
        if (c.ctl->solo.load(std::memory_order_relaxed)) {
            anySolo = true;
            break;
        }

    // Select-mode selector position: manual, or driven by the input level.
    float sel = chainSelect_.load(std::memory_order_relaxed);
    if (mode == 2 && selFollow_.load(std::memory_order_relaxed)) {
        double sum = 0.0;
        for (int32_t i = 0; i < frames * 2; ++i) sum += (double)input_[i] * input_[i];
        const double rms = std::sqrt(sum / std::max(1, frames * 2));
        const double db = rms > 1e-6 ? 20.0 * std::log10(rms) : -60.0;
        sel = (float)std::clamp((db + 48.0) / 48.0, 0.0, 1.0); // −48..0 dB → 0..1
    }
    liveSel_.store(sel, std::memory_order_relaxed);

    if (mode == 1) {
        // Series: one running buffer flows through each chain in order.
        std::memcpy(acc_, input_, sizeof(float) * frames * 2);
        for (auto& c : st->chains) {
            const bool solo = c.ctl->solo.load(std::memory_order_relaxed);
            const bool mute = c.ctl->mute.load(std::memory_order_relaxed);
            if (mute || (anySolo && !solo)) {
                c.ctl->meter.store(0.0f, std::memory_order_relaxed);
                continue;
            }
            for (auto& d : c.devices)
                if (d && !d->bypassed()) d->process(acc_, frames);
            const float g = c.ctl->gain.load(std::memory_order_relaxed);
            float peak = 0.0f;
            for (int32_t i = 0; i < frames; ++i) {
                acc_[i * 2] *= g;
                acc_[i * 2 + 1] *= g;
                const float a = std::max(std::fabs(acc_[i * 2]), std::fabs(acc_[i * 2 + 1]));
                if (a > peak) peak = a;
            }
            c.ctl->meter.store(peak, std::memory_order_relaxed);
        }
    } else {
        // Parallel (0) / Select (2): fan the input to each active chain and sum.
        std::memset(acc_, 0, sizeof(float) * frames * 2);
        for (auto& c : st->chains) {
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
            std::memcpy(scratch_, input_, sizeof(float) * frames * 2);
            for (auto& d : c.devices)
                if (d && !d->bypassed()) d->process(scratch_, frames);
            const float g = c.ctl->gain.load(std::memory_order_relaxed);
            const float pan = c.ctl->pan.load(std::memory_order_relaxed);
            const float lg = g * (pan > 0.0f ? 1.0f - pan : 1.0f);
            const float rg = g * (pan < 0.0f ? 1.0f + pan : 1.0f);
            float peak = 0.0f;
            for (int32_t i = 0; i < frames; ++i) {
                const float l = scratch_[i * 2] * lg, r = scratch_[i * 2 + 1] * rg;
                acc_[i * 2] += l;
                acc_[i * 2 + 1] += r;
                const float a = std::max(std::fabs(l), std::fabs(r));
                if (a > peak) peak = a;
            }
            c.ctl->meter.store(peak, std::memory_order_relaxed);
        }
    }

    // Rack output stage: dry/wet mix against the input, then rack gain.
    const float wet = dw, dry = 1.0f - dw;
    for (int32_t i = 0; i < frames * 2; ++i) buf[i] = (input_[i] * dry + acc_[i] * wet) * gain;
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
    return r;
}

} // namespace nota
