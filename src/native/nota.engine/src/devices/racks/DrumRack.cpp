// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DrumRack implementation — pad routing and render bodies.

#include "DrumRack.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace nota {

void DrumRack::noteOn(int32_t pitch, float velocity) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains) {
        if (!c.instrument || c.ctl->triggerNote.load(std::memory_order_relaxed) != pitch) continue;
        const int32_t grp = c.ctl->chokeGroup.load(std::memory_order_relaxed);
        if (grp > 0) {
            for (auto& o : st->chains) {
                if (&o == &c || !o.instrument) continue;
                if (o.ctl->chokeGroup.load(std::memory_order_relaxed) == grp) o.instrument->allNotesOff();
            }
        }
        c.ctl->retrig = true; // kick the pad decay VCA (audio thread)
        c.instrument->noteOn(tunedNote(*c.ctl, pitch), velocity);
    }
}

void DrumRack::noteOff(int32_t pitch) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        if (c.instrument && c.ctl->triggerNote.load(std::memory_order_relaxed) == pitch)
            c.instrument->noteOff(tunedNote(*c.ctl, pitch));
}

void DrumRack::allNotesOff() {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        if (c.instrument) c.instrument->allNotesOff();
}

void DrumRack::render(float* out, int32_t frames) {
    const auto done = renderScope();
    RackState* st = live();
    if (!st || frames <= 0 || frames > kMaxBlock) return;
    bool anySolo = false;
    for (auto& c : st->chains)
        if (c.ctl->solo.load(std::memory_order_relaxed)) {
            anySolo = true;
            break;
        }
    const float rv = rackVolume_.load(std::memory_order_relaxed);
    for (auto& c : st->chains) {
        if (!c.instrument) continue;
        const bool solo = c.ctl->solo.load(std::memory_order_relaxed);
        const bool mute = c.ctl->mute.load(std::memory_order_relaxed);
        if (mute || (anySolo && !solo)) {
            c.ctl->meter.store(0.0f, std::memory_order_relaxed);
            c.ctl->retrig = false;
            continue;
        }
        std::memset(scratch_, 0, sizeof(float) * frames * 2);
        c.instrument->render(scratch_, frames);
        for (auto& d : c.devices)
            if (d && !d->bypassed()) d->process(scratch_, frames);
        const float g = c.ctl->gain.load(std::memory_order_relaxed);
        const float pan = c.ctl->pan.load(std::memory_order_relaxed);
        const float lg = g * rv * (pan > 0.0f ? 1.0f - pan : 1.0f);
        const float rg = g * rv * (pan < 0.0f ? 1.0f + pan : 1.0f);

        // Pad decay VCA: decay==1 (default) is transparent (sustain); below that,
        // each hit resets the envelope to 1 and it falls with a decay 20 ms..2 s.
        const float dec = c.ctl->decay.load(std::memory_order_relaxed);
        const bool shaped = dec < 0.999f;
        if (c.ctl->retrig) {
            c.ctl->ampEnv.store(1.0f, std::memory_order_relaxed);
            c.ctl->retrig = false;
        }
        float env = c.ctl->ampEnv.load(std::memory_order_relaxed);
        float coef = 1.0f;
        if (shaped) {
            const double tauMs = 20.0 * std::pow(100.0, (double)dec); // 20 ms .. 2000 ms
            coef = (float)std::exp(-1.0 / (tauMs * 0.001 * sampleRate_));
        }

        float peak = 0.0f;
        for (int32_t i = 0; i < frames; ++i) {
            float l = scratch_[i * 2], r = scratch_[i * 2 + 1];
            if (shaped) {
                l *= env;
                r *= env;
                env *= coef;
            }
            out[i * 2] += l * lg;
            out[i * 2 + 1] += r * rg;
            const float a = std::max(std::fabs(l), std::fabs(r));
            if (a > peak) peak = a;
        }
        if (shaped) c.ctl->ampEnv.store(env, std::memory_order_relaxed);
        c.ctl->meter.store(peak * g, std::memory_order_relaxed);
    }
}

std::shared_ptr<Instrument> DrumRack::clone() const {
    auto r = std::make_shared<DrumRack>();
    r->setSampleRate(sampleRate_);
    auto blob = serialize();
    r->setState(blob.data(), static_cast<int32_t>(blob.size()));
    return r;
}

} // namespace nota
