// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackInstrument implementation — note fan-out and render bodies.

#include "RackInstrument.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace nota {

void RackInstrument::noteOn(int32_t pitch, float velocity) {
    RackState* st = live();
    if (!st) return;
    const int32_t v = std::clamp((int32_t)std::lround(velocity * 127.0f), 0, 127);
    for (auto& c : st->chains) {
        if (!c.instrument) continue;
        auto* z = c.ctl.get();
        if (pitch < z->keyLo.load(std::memory_order_relaxed) || pitch > z->keyHi.load(std::memory_order_relaxed) ||
            v < z->velLo.load(std::memory_order_relaxed) || v > z->velHi.load(std::memory_order_relaxed))
            continue;
        c.instrument->noteOn(pitch, velocity);
    }
}

void RackInstrument::noteOff(int32_t pitch) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        if (c.instrument) c.instrument->noteOff(pitch);
}

void RackInstrument::allNotesOff() {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        if (c.instrument) c.instrument->allNotesOff();
}

void RackInstrument::noteExpression(int32_t pitch, int32_t dim, float value) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        if (c.instrument) c.instrument->noteExpression(pitch, dim, value);
}

void RackInstrument::controller(int32_t ctl, float value) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        if (c.instrument) c.instrument->controller(ctl, value);
}

bool RackInstrument::supportsMpe() const {
    RackState* st = live();
    if (!st) return false;
    for (auto& c : st->chains)
        if (c.instrument && c.instrument->supportsMpe()) return true;
    return false;
}

void RackInstrument::render(float* out, int32_t frames) {
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
        float peak = 0.0f;
        for (int32_t i = 0; i < frames; ++i) {
            const float l = scratch_[i * 2], r = scratch_[i * 2 + 1];
            out[i * 2] += l * lg;
            out[i * 2 + 1] += r * rg;
            const float a = std::max(std::fabs(l), std::fabs(r));
            if (a > peak) peak = a;
        }
        c.ctl->meter.store(peak * g, std::memory_order_relaxed);
    }
}

std::shared_ptr<Instrument> RackInstrument::clone() const {
    auto r = std::make_shared<RackInstrument>();
    r->setSampleRate(sampleRate_);
    auto blob = serialize();
    r->setState(blob.data(), static_cast<int32_t>(blob.size()));
    return r;
}

} // namespace nota
