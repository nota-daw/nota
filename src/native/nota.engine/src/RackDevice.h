// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackDevice — a parallel-chain Audio Effect Rack. A container Device whose
// parallel chains are effects-only (no instrument): the incoming stereo buffer is
// fanned to every chain, each chain runs its own insert devices, and the results
// are summed (per-chain gain/pan/mute/solo) back in place. All the chain/macro/
// snapshot/persistence machinery is shared with the Instrument Rack via RackCore.
//
// An empty rack (no chains) passes audio through untouched, so dropping the device
// in is transparent until the user builds chains.

#pragma once

#include <memory>
#include <algorithm>

#include "RackCore.h"

#include <cstring>

namespace nota {

class RackDevice final : public Device, public RackCore {
public:
    int32_t builtinKind() const override { return 5; }   // Audio Effect Rack (project compat)
    const char* displayName() const override { return "Nota Audio Effect Rack"; }

    void setSampleRate(double sr, int32_t maxBlock) override { setRates(sr, maxBlock); }

    // Forward the DAW transport to every chain child (nested plugins sync too).
    void setTransportInfo(const TransportInfo& ti) override { forwardTransport(ti); }

    // ---- in-place processing: Parallel / Series / Select + dry-wet + gain ----
    void process(float* buf, int32_t frames) override {
        RackState* st = live();
        if (!st || frames <= 0 || frames > kMaxBlock) return;
        if (st->chains.empty()) return;   // pass-through until chains exist

        const int32_t mode = mode_.load(std::memory_order_relaxed);
        const float   gain = rackVolume_.load(std::memory_order_relaxed);
        const float   dw   = dryWet_.load(std::memory_order_relaxed);
        std::memcpy(input_, buf, sizeof(float) * frames * 2);

        bool anySolo = false;
        for (auto& c : st->chains)
            if (c.ctl->solo.load(std::memory_order_relaxed)) { anySolo = true; break; }

        // Select-mode selector position: manual, or driven by the input level.
        float sel = chainSelect_.load(std::memory_order_relaxed);
        if (mode == 2 && selFollow_.load(std::memory_order_relaxed)) {
            double sum = 0.0;
            for (int32_t i = 0; i < frames * 2; ++i) sum += (double)input_[i] * input_[i];
            const double rms = std::sqrt(sum / std::max(1, frames * 2));
            const double db  = rms > 1e-6 ? 20.0 * std::log10(rms) : -60.0;
            sel = (float)std::clamp((db + 48.0) / 48.0, 0.0, 1.0);   // −48..0 dB → 0..1
        }
        liveSel_.store(sel, std::memory_order_relaxed);

        if (mode == 1) {
            // Series: one running buffer flows through each chain in order.
            std::memcpy(acc_, input_, sizeof(float) * frames * 2);
            for (auto& c : st->chains) {
                const bool solo = c.ctl->solo.load(std::memory_order_relaxed);
                const bool mute = c.ctl->mute.load(std::memory_order_relaxed);
                if (mute || (anySolo && !solo)) { c.ctl->meter.store(0.0f, std::memory_order_relaxed); continue; }
                for (auto& d : c.devices) if (d && !d->bypassed()) d->process(acc_, frames);
                const float g = c.ctl->gain.load(std::memory_order_relaxed);
                float peak = 0.0f;
                for (int32_t i = 0; i < frames; ++i) {
                    acc_[i * 2] *= g; acc_[i * 2 + 1] *= g;
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
                    active = sel >= lo && sel <= hi;   // chain-select zone covers the selector
                }
                if (!active) { c.ctl->meter.store(0.0f, std::memory_order_relaxed); continue; }
                std::memcpy(scratch_, input_, sizeof(float) * frames * 2);
                for (auto& d : c.devices) if (d && !d->bypassed()) d->process(scratch_, frames);
                const float g   = c.ctl->gain.load(std::memory_order_relaxed);
                const float pan = c.ctl->pan.load(std::memory_order_relaxed);
                const float lg  = g * (pan > 0.0f ? 1.0f - pan : 1.0f);
                const float rg  = g * (pan < 0.0f ? 1.0f + pan : 1.0f);
                float peak = 0.0f;
                for (int32_t i = 0; i < frames; ++i) {
                    const float l = scratch_[i * 2] * lg, r = scratch_[i * 2 + 1] * rg;
                    acc_[i * 2] += l; acc_[i * 2 + 1] += r;
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

    // PDC: report chain latency only when compensation is on. Series accumulates
    // along the path; parallel/select align to the longest chain.
    int32_t latencySamples() const override {
        if (!pdc_.load(std::memory_order_relaxed)) return 0;
        if (mode_.load(std::memory_order_relaxed) == 1) {
            RackState* st = live();
            if (!st) return 0;
            int32_t lat = 0;
            for (auto& c : st->chains) for (auto& d : c.devices) if (d) lat += d->latencySamples();
            return lat;
        }
        return maxChainLatency();
    }

    // ---- macros over the plugin-param surface (so they automate/record) -----
    int32_t     pluginParamCount() const override { return kNumMacros; }
    std::string pluginParamId(int32_t i) const override { return macroParamId(i); }
    std::string pluginParamName(int32_t i) const override { return macroParamName(i); }
    float       pluginParamGet(int32_t i) const override { return macroGet(i); }
    void        pluginParamSet(int32_t i, float v) override { macroSet(i, v); }
    int32_t     pluginParamIndexOfId(const std::string& id) const override { return macroParamIndexOfId(id); }

    // ---- persistence + clone (shared blob) ----------------------------------
    std::vector<uint8_t> getState() const override { return serialize(); }
    void setState(const uint8_t* data, int32_t size) override { deserialize(data, size); }
    std::shared_ptr<Device> clone() const override {
        auto r = std::make_shared<RackDevice>();
        r->setSampleRate(sampleRate_, maxBlock_);
        auto blob = serialize();
        r->setState(blob.data(), static_cast<int32_t>(blob.size()));
        return r;
    }

private:
    float input_[kMaxBlock * 2];
    float acc_[kMaxBlock * 2];
    float scratch_[kMaxBlock * 2];
};

} // namespace nota
