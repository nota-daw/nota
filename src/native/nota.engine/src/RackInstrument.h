// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackInstrument — a parallel-chain Instrument Rack. A container Instrument whose
// parallel chains each hold a built-in instrument followed by an insert chain of
// built-in devices. Every note is fanned to every chain (no key/velocity zones in
// v1) and the chain outputs are summed. All the chain/macro/snapshot/persistence
// machinery lives in RackCore; this class only adds the Instrument interface.
//
// Because a RackInstrument is just an Instrument, it slots onto an instrument
// track with no change to the engine's render path.

#pragma once

#include <memory>
#include <algorithm>

#include "RackCore.h"

#include <cstring>

namespace nota {

class RackInstrument : public Instrument, public RackCore {
public:
    int32_t kind() const override { return 3; }   // built-in Instrument Rack (project compat)
    const char* displayName() const override { return "Nota Instrument Rack"; }

    void setSampleRate(double sr) override { setRates(sr, kMaxBlock); }

    // Forward the DAW transport to every chain child (nested plugins sync too).
    void setTransportInfo(const TransportInfo& ti) override { forwardTransport(ti); }

    // ---- note fan-out: a note reaches a chain only if it's inside its key/vel zone --
    void noteOn(int32_t pitch, float velocity) override {
        RackState* st = live();
        if (!st) return;
        const int32_t v = std::clamp((int32_t)std::lround(velocity * 127.0f), 0, 127);
        for (auto& c : st->chains) {
            if (!c.instrument) continue;
            auto* z = c.ctl.get();
            if (pitch < z->keyLo.load(std::memory_order_relaxed) || pitch > z->keyHi.load(std::memory_order_relaxed) ||
                v < z->velLo.load(std::memory_order_relaxed) || v > z->velHi.load(std::memory_order_relaxed)) continue;
            c.instrument->noteOn(pitch, velocity);
        }
    }
    void noteOff(int32_t pitch) override {
        RackState* st = live();
        if (!st) return;
        for (auto& c : st->chains) if (c.instrument) c.instrument->noteOff(pitch);
    }
    void allNotesOff() override {
        RackState* st = live();
        if (!st) return;
        for (auto& c : st->chains) if (c.instrument) c.instrument->allNotesOff();
    }

    // ---- render: sum every audible chain into `out` -------------------------
    void render(float* out, int32_t frames) override {
        RackState* st = live();
        if (!st || frames <= 0 || frames > kMaxBlock) return;
        bool anySolo = false;
        for (auto& c : st->chains)
            if (c.ctl->solo.load(std::memory_order_relaxed)) { anySolo = true; break; }
        const float rv = rackVolume_.load(std::memory_order_relaxed);
        for (auto& c : st->chains) {
            if (!c.instrument) continue;
            const bool solo = c.ctl->solo.load(std::memory_order_relaxed);
            const bool mute = c.ctl->mute.load(std::memory_order_relaxed);
            if (mute || (anySolo && !solo)) { c.ctl->meter.store(0.0f, std::memory_order_relaxed); continue; }
            std::memset(scratch_, 0, sizeof(float) * frames * 2);
            c.instrument->render(scratch_, frames);
            for (auto& d : c.devices) if (d && !d->bypassed()) d->process(scratch_, frames);
            const float g   = c.ctl->gain.load(std::memory_order_relaxed);
            const float pan = c.ctl->pan.load(std::memory_order_relaxed);
            const float lg  = g * rv * (pan > 0.0f ? 1.0f - pan : 1.0f);
            const float rg  = g * rv * (pan < 0.0f ? 1.0f + pan : 1.0f);
            float peak = 0.0f;
            for (int32_t i = 0; i < frames; ++i) {
                const float l = scratch_[i * 2], r = scratch_[i * 2 + 1];
                out[i * 2]     += l * lg;
                out[i * 2 + 1] += r * rg;
                const float a = std::max(std::fabs(l), std::fabs(r));
                if (a > peak) peak = a;
            }
            c.ctl->meter.store(peak * g, std::memory_order_relaxed);
        }
    }

    int32_t latencySamples() const override { return maxChainLatency(); }

    // ---- macros over the plugin-param surface (automation-free) -------------
    int32_t     pluginParamCount() const override { return kNumMacros; }
    std::string pluginParamId(int32_t i) const override { return macroParamId(i); }
    std::string pluginParamName(int32_t i) const override { return macroParamName(i); }
    float       pluginParamGet(int32_t i) const override { return macroGet(i); }
    void        pluginParamSet(int32_t i, float v) override { macroSet(i, v); }
    int32_t     pluginParamIndexOfId(const std::string& id) const override { return macroParamIndexOfId(id); }

    // ---- persistence + clone (shared blob) ----------------------------------
    std::vector<uint8_t> getState() const override { return serialize(); }
    void setState(const uint8_t* data, int32_t size) override { deserialize(data, size); }
    std::shared_ptr<Instrument> clone() const override {
        auto r = std::make_shared<RackInstrument>();
        r->setSampleRate(sampleRate_);
        auto blob = serialize();
        r->setState(blob.data(), static_cast<int32_t>(blob.size()));
        return r;
    }

protected:
    float scratch_[kMaxBlock * 2];   // per-chain render buffer (DrumRack reuses it)
};

} // namespace nota
