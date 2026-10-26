// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackInstrument implementation — note fan-out and render bodies.

#include "RackInstrument.h"

#include "engine/NoteExpression.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace nota {

void RackInstrument::fanOn(int32_t pitch, float velocity, int32_t glideFrom) {
    RackState* st = live();
    if (!st) return;
    const int32_t v = std::clamp((int32_t)std::lround(velocity * 127.0f), 0, 127);
    const float bend = glideFrom >= 0 ? static_cast<float>(glideFrom - pitch) : 0.0f;
    for (auto& c : st->chains) {
        if (!c.instrument) continue;
        auto* z = c.ctl.get();
        if (pitch < z->keyLo.load(std::memory_order_relaxed) || pitch > z->keyHi.load(std::memory_order_relaxed) ||
            v < z->velLo.load(std::memory_order_relaxed) || v > z->velHi.load(std::memory_order_relaxed))
            continue;
        c.instrument->noteOn(pitch, velocity);
        // Right after the note-on a bend lands without smoothing: the note starts at the old pitch.
        if (bend != 0.0f && c.midiDevices.empty() && c.instrument->supportsMpe())
            c.instrument->noteExpression(pitch, ExprBend, bend);
    }
}

void RackInstrument::fanOff(int32_t pitch) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        if (c.instrument) c.instrument->noteOff(pitch);
}

void RackInstrument::monoRemove(int32_t pitch) {
    auto& m = *mono_;
    for (int32_t i = 0; i < m.heldN; ++i)
        if (m.held[i] == pitch) {
            for (int32_t j = i; j + 1 < m.heldN; ++j) m.held[j] = m.held[j + 1];
            --m.heldN;
            return;
        }
}

void RackInstrument::syncMono() {
    auto& m = *mono_;
    const bool mono = rackMono();
    if (mono == m.wasMono) return;
    m.wasMono = mono;
    m.glideLeft = 0;
    if (mono) {
        // Poly → mono: only the last held key keeps sounding.
        for (int32_t i = 0; i + 1 < m.heldN; ++i) fanOff(m.held[i]);
        m.sounding = m.heldN > 0 ? m.held[m.heldN - 1] : -1;
    } else {
        m.sounding = -1; // mono → poly: the sounding key plays on as a poly note
    }
}

void RackInstrument::noteOn(int32_t pitch, float velocity) {
    if (pitch < 0 || pitch > 127) return;
    syncMono();
    auto& m = *mono_;
    monoRemove(pitch);
    if (m.heldN < 128) m.held[m.heldN++] = static_cast<int8_t>(pitch);
    m.heldVel[pitch] = velocity;
    if (!rackMono()) {
        fanOn(pitch, velocity, -1);
        return;
    }
    // Mono, last-note priority: the new key takes over from the sounding one.
    const int32_t prev = m.sounding;
    if (prev >= 0) fanOff(prev);
    const double ms = rackGlide() * 500.0;
    const bool glide = prev >= 0 && prev != pitch && ms > 0.5;
    fanOn(pitch, velocity, glide ? prev : -1);
    m.sounding = pitch;
    m.glideBend = glide ? static_cast<float>(prev - pitch) : 0.0f;
    m.glideTotal = m.glideLeft = glide ? std::max<int64_t>(1, std::llround(ms * 0.001 * sampleRate_)) : 0;
}

void RackInstrument::noteOff(int32_t pitch) {
    if (pitch < 0 || pitch > 127) return;
    syncMono();
    auto& m = *mono_;
    monoRemove(pitch);
    if (!rackMono() || pitch != m.sounding) {
        // Poly, or a key that isn't sounding (superseded, or pressed before mono was on):
        // let it go everywhere — a note-off for a silent pitch is harmless.
        fanOff(pitch);
        if (pitch == m.sounding) m.sounding = -1;
        return;
    }
    fanOff(pitch);
    m.sounding = -1;
    m.glideLeft = 0;
    if (m.heldN > 0) { // fall back to the key still held last, gliding from the released one
        const int32_t back = m.held[m.heldN - 1];
        const double ms = rackGlide() * 500.0;
        const bool glide = ms > 0.5;
        fanOn(back, m.heldVel[back], glide ? pitch : -1);
        m.sounding = back;
        m.glideBend = glide ? static_cast<float>(pitch - back) : 0.0f;
        m.glideTotal = m.glideLeft = glide ? std::max<int64_t>(1, std::llround(ms * 0.001 * sampleRate_)) : 0;
    }
}

void RackInstrument::allNotesOff() {
    auto& m = *mono_;
    m.heldN = 0;
    m.sounding = -1;
    m.glideLeft = 0;
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
    // Mono glide: the sounding note's bend ramps back to 0 (block rate; the voices smooth it).
    syncMono();
    auto& m = *mono_;
    if (m.glideLeft > 0 && m.sounding >= 0) {
        m.glideLeft = std::max<int64_t>(0, m.glideLeft - frames);
        const float bend = m.glideBend * static_cast<float>(m.glideLeft) / static_cast<float>(m.glideTotal);
        for (auto& c : st->chains)
            if (c.instrument && c.midiDevices.empty() && c.instrument->supportsMpe())
                c.instrument->noteExpression(m.sounding, ExprBend, bend);
    }
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
