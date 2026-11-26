// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DrumRack implementation — pad routing and render bodies.

#include "DrumRack.h"

#include "dsp/core/RenderExecutor.h"

#include <algorithm>
#include <array>
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
        if (!chainIdle(c, true)) {
            float* const scratch = arena_.buffer(0);
            std::memset(scratch, 0, sizeof(float) * frames * 2);
            c.instrument->render(scratch, frames);
            for (auto& d : c.devices)
                if (d && !d->bypassed() && (!d->isIdle() || d->requiresActiveProcessing())) d->process(scratch, frames);
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
    const int32_t cap = std::max(1, arena_.capacityFrames());
    for (int32_t off = 0; off < frames;) {
        const int32_t sub = std::min(frames - off, cap);
        renderChunk(*st, out + off * 2, sub, tapAt + off * 2);
        off += sub;
    }
}

void DrumRack::renderChunk(RackState& st, float* out, int32_t frames, int32_t tapAt) {
    float* const scratch = arena_.buffer(0);
    beginRender(st, frames);
    const float rv = rackVolume_.load(std::memory_order_relaxed);

    auto renderPad = [&](RackChain& c, float* dst, const PartTap* tap, bool silent) {
        std::memset(dst, 0, sizeof(float) * frames * 2);
        c.instrument->render(dst, frames);
        if (tap) addTap(tap->pre + tapAt, dst, frames); // Pre: the raw hit, before inserts and decay
        for (auto& d : c.devices)
            if (d && !d->bypassed()) d->process(dst, frames);
        const float g = c.ctl->gain.load(std::memory_order_relaxed);
        const float pan = c.ctl->pan.load(std::memory_order_relaxed);
        const float dec = c.ctl->decay.load(std::memory_order_relaxed);
        const bool shaped = dec < 0.999f;
        if (c.ctl->retrig) {
            c.ctl->ampEnv.store(1.0f, std::memory_order_relaxed);
            c.ctl->retrig = false;
        }
        float env = c.ctl->ampEnv.load(std::memory_order_relaxed);
        float coef = 1.0f;
        if (shaped) {
            const double tauMs = 20.0 * std::pow(100.0, static_cast<double>(dec)); // 20 ms .. 2000 ms
            coef = static_cast<float>(std::exp(-1.0 / (tauMs * 0.001 * sampleRate_)));
            for (int32_t i = 0; i < frames; ++i) {
                dst[i * 2] *= env;
                dst[i * 2 + 1] *= env;
                env *= coef;
            }
            c.ctl->ampEnv.store(env, std::memory_order_relaxed);
        }
        if (tap) {
            const float pl = g * (pan > 0.0f ? 1.0f - pan : 1.0f);
            const float pr = g * (pan < 0.0f ? 1.0f + pan : 1.0f);
            float* post = tap->post + tapAt;
            for (int32_t i = 0; i < frames; ++i) {
                post[i * 2] += dst[i * 2] * pl;
                post[i * 2 + 1] += dst[i * 2 + 1] * pr;
            }
            if (silent) c.ctl->meter.store(0.0f, std::memory_order_relaxed);
        }
    };

    auto accumulatePad = [&](RackChain& c, const float* src) {
        const float g = c.ctl->gain.load(std::memory_order_relaxed);
        const float pan = c.ctl->pan.load(std::memory_order_relaxed);
        const float lg = g * rv * (pan > 0.0f ? 1.0f - pan : 1.0f);
        const float rg = g * rv * (pan < 0.0f ? 1.0f + pan : 1.0f);
        float peak = 0.0f;
        for (int32_t i = 0; i < frames; ++i) {
            const float l = src[i * 2], r = src[i * 2 + 1];
            out[i * 2] += l * lg;
            out[i * 2 + 1] += r * rg;
            const float a = std::max(std::fabs(l), std::fabs(r));
            if (a > peak) peak = a;
        }
        c.ctl->meter.store(peak * g, std::memory_order_relaxed);
    };

    struct PadWork {
        RackChain* chain = nullptr;
        const PartTap* tap = nullptr;
        bool silent = false;
    };
    std::array<PadWork, dsp::ChainScratchBank::kMaxSlots> work;
    int32_t nWork = 0;
    int32_t nChainDevs = 0;
    bool allParallel = true;
    bool overflow = false;

    for (auto& c : st.chains) {
        if (!c.instrument) continue;
        // A pad a sidechain key listens to (specs/050) renders even when muted or solo-gated:
        // the "ghost kick" drives the key without being heard.
        const PartTap* tap = taps_.any() ? taps_.find(c.uid) : nullptr;
        const bool silent = c.ctl->silentNow; // as beginRender read it
        if (silent && !tap) {                 // kept in time by beginRender
            c.ctl->meter.store(0.0f, std::memory_order_relaxed);
            c.ctl->retrig = false;
            continue;
        }
        if (nWork < dsp::ChainScratchBank::kMaxSlots) {
            work[static_cast<size_t>(nWork++)] = {&c, tap, silent};
            for (const auto& d : c.devices)
                if (d && !d->bypassed()) ++nChainDevs;
            if (!chainSupportsParallel(c)) allParallel = false;
        } else {
            overflow = true;
        }
    }

    const bool fanOut = !overflow && (nWork >= 5 || (nWork >= 3 && nChainDevs >= 3)) && allParallel &&
                        parallelArena_.slotCount() >= nWork && parallelArena_.capacityFrames() >= frames;
    if (fanOut) {
        dsp::RenderExecutor::parallelFor(nWork, true, [&](int32_t w) {
            const PadWork& pw = work[static_cast<size_t>(w)];
            renderPad(*pw.chain, parallelArena_.buffer(w), pw.tap, pw.silent);
        });
        for (int32_t w = 0; w < nWork; ++w) {
            const PadWork& pw = work[static_cast<size_t>(w)];
            if (!pw.silent) accumulatePad(*pw.chain, parallelArena_.buffer(w));
        }
    } else {
        for (int32_t w = 0; w < nWork; ++w) {
            const PadWork& pw = work[static_cast<size_t>(w)];
            RackChain& c = *pw.chain;
            const PartTap* tap = pw.tap;
            const bool silent = pw.silent;
            if (!tap) {
                std::memset(scratch, 0, sizeof(float) * frames * 2);
                c.instrument->render(scratch, frames);
                for (auto& d : c.devices)
                    if (d && !d->bypassed()) d->process(scratch, frames);
                const float g = c.ctl->gain.load(std::memory_order_relaxed);
                const float pan = c.ctl->pan.load(std::memory_order_relaxed);
                const float dec = c.ctl->decay.load(std::memory_order_relaxed);
                const bool shaped = dec < 0.999f;
                if (c.ctl->retrig) {
                    c.ctl->ampEnv.store(1.0f, std::memory_order_relaxed);
                    c.ctl->retrig = false;
                }
                float env = c.ctl->ampEnv.load(std::memory_order_relaxed);
                float coef = 1.0f;
                if (shaped) {
                    const double tauMs = 20.0 * std::pow(100.0, static_cast<double>(dec));
                    coef = static_cast<float>(std::exp(-1.0 / (tauMs * 0.001 * sampleRate_)));
                }
                const float lg = g * rv * (pan > 0.0f ? 1.0f - pan : 1.0f);
                const float rg = g * rv * (pan < 0.0f ? 1.0f + pan : 1.0f);
                float peak = 0.0f;
                if (shaped) {
                    for (int32_t i = 0; i < frames; ++i) {
                        const float l = scratch[i * 2] * env;
                        const float r = scratch[i * 2 + 1] * env;
                        env *= coef;
                        out[i * 2] += l * lg;
                        out[i * 2 + 1] += r * rg;
                        const float a = std::max(std::fabs(l), std::fabs(r));
                        if (a > peak) peak = a;
                    }
                    c.ctl->ampEnv.store(env, std::memory_order_relaxed);
                } else {
                    for (int32_t i = 0; i < frames; ++i) {
                        const float l = scratch[i * 2], r = scratch[i * 2 + 1];
                        out[i * 2] += l * lg;
                        out[i * 2 + 1] += r * rg;
                        const float a = std::max(std::fabs(l), std::fabs(r));
                        if (a > peak) peak = a;
                    }
                }
                c.ctl->meter.store(peak * g, std::memory_order_relaxed);
            } else {
                renderPad(c, scratch, tap, silent);
                if (!silent) accumulatePad(c, scratch);
            }
        }
    }
}

std::shared_ptr<Instrument> DrumRack::clone() const {
    auto r = std::make_shared<DrumRack>();
    r->setSampleRate(sampleRate_);
    auto blob = serialize();
    r->setState(blob.data(), static_cast<int32_t>(blob.size()));
    r->ensureBlockCapacity(arena_.capacityFrames());
    return r;
}

} // namespace nota
