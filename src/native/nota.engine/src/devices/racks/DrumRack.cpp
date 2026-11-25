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
            // A pad of the group struck at this same sample (an open and a closed hat on one
            // step) is not cut: both are heard, and the group's next hit chokes them. Before,
            // which of them survived came down to the order of two simultaneous events —
            // with the kit's humanize, a different one every pass (#644).
            for (auto& o : st->chains) {
                if (&o == &c || !o.instrument || o.ctl->hitSerial == renderSerial_) continue;
                if (o.ctl->chokeGroup.load(std::memory_order_relaxed) == grp) o.instrument->allNotesOff();
            }
        }
        c.ctl->hitSerial = renderSerial_;
        c.ctl->retrig = true; // kick the pad decay VCA (audio thread)
        c.instrument->noteOn(tunedNote(*c.ctl, pitch), velocity);
    }
}

void DrumRack::noteOff(int32_t pitch) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        // A pad plays its sample through (#312): the note's length doesn't cut it, only a
        // choke or a retrigger does. A pad that can't end on its own stays gated.
        if (c.instrument && c.ctl->triggerNote.load(std::memory_order_relaxed) == pitch) {
            if (c.instrument->playsOneShot()) c.instrument->noteOffHeldBack(tunedNote(*c.ctl, pitch));
            else c.instrument->noteOff(tunedNote(*c.ctl, pitch));
        }
}

void DrumRack::allNotesOff() {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        if (c.instrument) c.instrument->allNotesOff();
}

// Before a render: the hits noteOn takes from here on are at a later sample (renderSerial_,
// #644), and a silent pad keeps time — its instrument plays the hits it gets into nothing and
// its decay runs on, so unmuting is heard from the pad's next hit. Before, the hit it took
// while silent waited, frozen at its start, and fired at full level on unmute (#650). Out of
// line, so the loop every audible pad runs in render() compiles exactly as before (perf gate).
void DrumRack::beginRender(RackState& st, int32_t frames) {
    ++renderSerial_;
    bool anySolo = false;
    for (auto& c : st.chains) anySolo |= c.ctl->solo.load(std::memory_order_relaxed);
    for (auto& c : st.chains) {
        if (!c.instrument) continue;
        // Mute and solo are read once per render, here: render() uses this, so a toggle from the
        // UI between the two passes can't render a pad twice or not at all.
        const bool silent = c.ctl->mute.load(std::memory_order_relaxed) ||
                            (anySolo && !c.ctl->solo.load(std::memory_order_relaxed));
        c.ctl->silentNow = silent;
        if (!silent || (taps_.any() && taps_.find(c.uid))) continue; // heard, or a ghost key's pad
        if (!c.instrument->isIdle()) {
            std::memset(scratch_, 0, sizeof(float) * frames * 2);
            c.instrument->render(scratch_, frames);
        }
        if (c.ctl->retrig) {
            c.ctl->ampEnv.store(1.0f, std::memory_order_relaxed);
            c.ctl->retrig = false;
        }
        const float dec = c.ctl->decay.load(std::memory_order_relaxed);
        if (dec < 0.999f) {
            const double tauMs = 20.0 * std::pow(100.0, (double)dec);
            const double fall = std::exp(-(double)frames / (tauMs * 0.001 * sampleRate_));
            c.ctl->ampEnv.store(c.ctl->ampEnv.load(std::memory_order_relaxed) * (float)fall, std::memory_order_relaxed);
        }
    }
}

void DrumRack::render(float* out, int32_t frames) {
    const auto done = renderScope();
    const int32_t tapAt = taps_.take(frames) * 2; // this piece's place in the block's key buffers
    RackState* st = live();
    if (!st || frames <= 0 || frames > kMaxBlock || isIdle()) return;
    beginRender(*st, frames);
    const float rv = rackVolume_.load(std::memory_order_relaxed);
    for (auto& c : st->chains) {
        if (!c.instrument) continue;
        // A pad a sidechain key listens to (specs/050) renders even when muted or solo-gated:
        // the "ghost kick" drives the key without being heard.
        const PartTap* tap = taps_.any() ? taps_.find(c.uid) : nullptr;
        const bool silent = c.ctl->silentNow; // as beginRender read it
        if (silent && !tap) { // kept in time by beginRender
            c.ctl->meter.store(0.0f, std::memory_order_relaxed);
            c.ctl->retrig = false;
            continue;
        }
        std::memset(scratch_, 0, sizeof(float) * frames * 2);
        c.instrument->render(scratch_, frames);
        if (tap) addTap(tap->pre + tapAt, scratch_, frames); // Pre: the raw hit, before inserts and decay
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

        // Post for a keyed pad: after inserts, decay and the pad's own gain / pan, before the
        // rack volume. Its own pass (the envelope run from the same start), so the sum loop
        // below is the same code an unkeyed pad runs. A ghost pad (muted / solo-gated) stops
        // here: its envelope moves on, nothing reaches the sum.
        if (tap) {
            const float pl = g * (pan > 0.0f ? 1.0f - pan : 1.0f);
            const float pr = g * (pan < 0.0f ? 1.0f + pan : 1.0f);
            float e = env;
            float* post = tap->post + tapAt;
            for (int32_t i = 0; i < frames; ++i) {
                const float k = shaped ? e : 1.0f;
                post[i * 2] += scratch_[i * 2] * k * pl;
                post[i * 2 + 1] += scratch_[i * 2 + 1] * k * pr;
                if (shaped) e *= coef;
            }
            if (silent) {
                if (shaped) c.ctl->ampEnv.store(e, std::memory_order_relaxed);
                c.ctl->meter.store(0.0f, std::memory_order_relaxed);
                continue;
            }
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
