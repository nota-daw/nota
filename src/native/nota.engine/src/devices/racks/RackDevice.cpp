// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackDevice implementation — processing bodies

#include "RackDevice.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace nota {

// Prepared on the message thread; process and invalidation never allocate.
// Match the engine's existing compensation limit (65535 samples).
class RackCompensation {
  public:
    static constexpr int kCapacity = 65536;
    RackCompensation() : ring_(kCapacity * 2, 0.0f) {}
    void invalidate() { valid_ = 0; }
    void process(float* buf, int frames, int delay) {
        delay = std::clamp(delay, 0, kCapacity - 1);
        if (delay != delay_) { delay_ = delay; valid_ = 0; }
        if (delay == 0) return;
        for (int i = 0; i < frames; ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];
            int read = pos_ - delay;
            if (read < 0) read += kCapacity;
            buf[i * 2] = valid_ >= delay ? ring_[read * 2] : 0.0f;
            buf[i * 2 + 1] = valid_ >= delay ? ring_[read * 2 + 1] : 0.0f;
            ring_[pos_ * 2] = l;
            ring_[pos_ * 2 + 1] = r;
            pos_ = (pos_ + 1) % kCapacity;
            if (valid_ < kCapacity) ++valid_;
        }
    }
  private:
    std::vector<float> ring_;
    int pos_ = 0, valid_ = 0, delay_ = 0;
};

RackDevice::RackDevice() = default;

void RackDevice::prepareRackState(RackState& state) {
    if (!state.chains.empty() && !dryCompensation_) dryCompensation_ = std::make_shared<RackCompensation>();
    for (auto& chain : state.chains)
        if (!chain.compensation) chain.compensation = std::make_shared<RackCompensation>();
}

void RackDevice::inheritRackRuntime(const RackCore& source) {
    if (const auto* rack = dynamic_cast<const RackDevice*>(&source)) dryCompensation_ = rack->dryCompensation_;
}

static int chainLatency(const RackCore::RackChain& chain) {
    int latency = 0;
    for (const auto& device : chain.devices)
        if (device && !device->bypassed()) latency += std::max(0, device->latencySamples());
    return latency;
}

void RackDevice::process(float* buf, int32_t frames) {
    const auto done = renderScope();
    RackState* st = live();
    if (!st || frames <= 0 || frames > kMaxBlock) return;
    if (st->chains.empty()) return; // pass-through until chains exist

    const int32_t mode = mode_.load(std::memory_order_relaxed);
    const float gain = rackVolume_.load(std::memory_order_relaxed);
    const float dw = dryWet_.load(std::memory_order_relaxed);
    const bool pdc = pdc_.load(std::memory_order_relaxed) != 0;
    bool anySolo = false;
    for (auto& c : st->chains)
        if (c.ctl->solo.load(std::memory_order_relaxed)) { anySolo = true; break; }
    int wetLatency = 0;
    if (pdc)
        for (const auto& c : st->chains) {
            if (mode == 1 && (c.ctl->mute.load(std::memory_order_relaxed) ||
                              (anySolo && !c.ctl->solo.load(std::memory_order_relaxed)))) continue;
            const int latency = chainLatency(c);
            wetLatency = mode == 1 ? wetLatency + latency : std::max(wetLatency, latency);
        }
    std::memcpy(input_, buf, sizeof(float) * frames * 2);

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
            if (c.compensation) c.compensation->invalidate();
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
                if (c.compensation) c.compensation->invalidate();
                c.ctl->meter.store(0.0f, std::memory_order_relaxed);
                continue;
            }
            std::memcpy(scratch_, input_, sizeof(float) * frames * 2);
            for (auto& d : c.devices)
                if (d && !d->bypassed()) d->process(scratch_, frames);
            if (c.compensation) c.compensation->process(scratch_, frames, pdc ? wetLatency - chainLatency(c) : 0);
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
    dryCompensation_->process(input_, frames, pdc ? wetLatency : 0);
    const float wet = dw, dry = 1.0f - dw;
    for (int32_t i = 0; i < frames * 2; ++i) buf[i] = (input_[i] * dry + acc_[i] * wet) * gain;
}

int32_t RackDevice::latencySamples() const {
    if (!pdc_.load(std::memory_order_relaxed)) return 0;
    if (mode_.load(std::memory_order_relaxed) == 1) {
        RackState* st = live();
        if (!st) return 0;
        int32_t lat = 0;
        bool anySolo = false;
        for (const auto& c : st->chains) anySolo |= c.ctl->solo.load(std::memory_order_relaxed);
        for (auto& c : st->chains) {
            if (c.ctl->mute.load(std::memory_order_relaxed) ||
                (anySolo && !c.ctl->solo.load(std::memory_order_relaxed))) continue;
            for (auto& d : c.devices)
                if (d && !d->bypassed()) lat += d->latencySamples();
        }
        return lat;
    }
    RackState* st = live();
    int32_t latency = 0;
    if (st) for (const auto& chain : st->chains) latency = std::max(latency, chainLatency(chain));
    return latency;
}

std::shared_ptr<Device> RackDevice::clone() const {
    auto r = std::make_shared<RackDevice>();
    r->setSampleRate(sampleRate_, maxBlock_);
    auto blob = serialize();
    r->setState(blob.data(), static_cast<int32_t>(blob.size()));
    return r;
}

} // namespace nota
