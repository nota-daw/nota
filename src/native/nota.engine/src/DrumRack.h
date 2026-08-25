// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DrumRack — a pad-per-slot Drum Rack. It's an Instrument Rack whose chains are
// pads: each chain has a trigger note (RackCore::ChainControls::triggerNote) and
// only fires when that note plays. Parallel chains, macros, persistence and the
// summing/render skeleton are inherited from RackInstrument/RackCore; the Drum
// Rack adds pad-specific note routing and per-pad shaping:
//   • choke groups — a hit on a pad cuts the other pads sharing its group (mono).
//   • tune         — the pad transposes the note it hands to its instrument.
//   • decay        — a per-pad amp VCA (retriggered on each hit) shortens the tail.
// Kit-level swing/humanize live in RackCore and are applied by the engine's event
// scheduler (Engine_Render), which already fires noteOn sample-accurately.

#pragma once

#include <memory>
#include <cstring>
#include <algorithm>

#include "RackInstrument.h"

#include <cmath>

namespace nota {

class DrumRack final : public RackInstrument {
public:
    int32_t kind() const override { return 4; }   // built-in Drum Rack (project compat)
    const char* displayName() const override { return "Nota Drum Rack"; }

    // Route a note only to the pad(s) assigned to it, applying choke + tune and
    // arming the pad's decay envelope.
    void noteOn(int32_t pitch, float velocity) override {
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
            c.ctl->retrig = true;   // kick the pad decay VCA (audio thread)
            c.instrument->noteOn(tunedNote(*c.ctl, pitch), velocity);
        }
    }
    void noteOff(int32_t pitch) override {
        RackState* st = live();
        if (!st) return;
        for (auto& c : st->chains)
            if (c.instrument && c.ctl->triggerNote.load(std::memory_order_relaxed) == pitch)
                c.instrument->noteOff(tunedNote(*c.ctl, pitch));
    }
    void allNotesOff() override {
        RackState* st = live();
        if (!st) return;
        for (auto& c : st->chains) if (c.instrument) c.instrument->allNotesOff();
    }

    // Sum every audible pad, applying the per-pad decay VCA. Mirrors
    // RackInstrument::render with the pad envelope folded into the mix loop.
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
            if (mute || (anySolo && !solo)) { c.ctl->meter.store(0.0f, std::memory_order_relaxed); c.ctl->retrig = false; continue; }
            std::memset(scratch_, 0, sizeof(float) * frames * 2);
            c.instrument->render(scratch_, frames);
            for (auto& d : c.devices) if (d && !d->bypassed()) d->process(scratch_, frames);
            const float g   = c.ctl->gain.load(std::memory_order_relaxed);
            const float pan = c.ctl->pan.load(std::memory_order_relaxed);
            const float lg  = g * rv * (pan > 0.0f ? 1.0f - pan : 1.0f);
            const float rg  = g * rv * (pan < 0.0f ? 1.0f + pan : 1.0f);

            // Pad decay VCA: decay==1 (default) is transparent (sustain); below that,
            // each hit resets the envelope to 1 and it falls with a decay 20 ms..2 s.
            const float dec    = c.ctl->decay.load(std::memory_order_relaxed);
            const bool  shaped = dec < 0.999f;
            if (c.ctl->retrig) { c.ctl->ampEnv = 1.0f; c.ctl->retrig = false; }
            float env  = c.ctl->ampEnv;
            float coef = 1.0f;
            if (shaped) {
                const double tauMs = 20.0 * std::pow(100.0, (double)dec);   // 20 ms .. 2000 ms
                coef = (float)std::exp(-1.0 / (tauMs * 0.001 * sampleRate_));
            }

            float peak = 0.0f;
            for (int32_t i = 0; i < frames; ++i) {
                float l = scratch_[i * 2], r = scratch_[i * 2 + 1];
                if (shaped) { l *= env; r *= env; env *= coef; }
                out[i * 2]     += l * lg;
                out[i * 2 + 1] += r * rg;
                const float a = std::max(std::fabs(l), std::fabs(r));
                if (a > peak) peak = a;
            }
            if (shaped) c.ctl->ampEnv = env;
            c.ctl->meter.store(peak * g, std::memory_order_relaxed);
        }
    }

    std::shared_ptr<Instrument> clone() const override {
        auto r = std::make_shared<DrumRack>();
        r->setSampleRate(sampleRate_);
        auto blob = serialize();
        r->setState(blob.data(), static_cast<int32_t>(blob.size()));
        return r;
    }

private:
    static int32_t tunedNote(const ChainControls& ctl, int32_t pitch) {
        return std::clamp(pitch + ctl.tune.load(std::memory_order_relaxed), 0, 127);
    }
};

} // namespace nota
