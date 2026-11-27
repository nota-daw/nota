// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "core/EnginePrelude.h"
#include "model/Graph.h"
#include "model/EngineTypes.h"
#include "model/ArticulationSchedule.h"
#include "render/RenderGraphExecutor.h"
#include <cstdint>
#include <cmath>
#include <atomic>
#include <algorithm>

namespace nota::detail {
namespace render_internal {
using render_detail::kTwoPi;
constexpr float kToneGain = 0.2f;
constexpr float kMetroGain = 0.5f;

// The master pan stage (specs/099): the project pan law with centre at unity (0 dB) — the
// track gains scaled by √2 — and no channel ever boosted above its input, so centre is
// bit-exact pass-through and a full pan never adds level.
inline render_detail::PanGains masterPanGains(float pan, PanLaw law) {
    if (pan == 0.0f) return {1.0f, 0.0f, 0.0f, 1.0f};
    render_detail::PanGains k = render_detail::computePanGains(pan, 1.0f, law);
    constexpr float kRoot2 = 1.41421356f;
    k = {std::min(1.0f, k.ll * kRoot2), k.lr * kRoot2, k.rl * kRoot2, std::min(1.0f, k.rr * kRoot2)};
    return k;
}

// Sort note events by sample offset; at ties, note-offs before note-ons so a
// same-block same-pitch retrigger releases the old voice before the new one.
inline bool midiEvLess(const MidiEv& a, const MidiEv& b) {
    return a.off < b.off || (a.off == b.off && !a.on && b.on);
}

// The engine's sustain pedal (#99) for an instrument without a damper of its own: while the
// pedal is down a released key's note-off waits in inst.pedalHeld; the pedal coming up sends
// them all, and striking a held key again releases it right before the new note-on.
inline bool pedalHolds(const Instrument& inst, int32_t pitch) {
    return pitch >= 0 && pitch < 128 &&
           ((inst.pedalHeld[pitch >> 6].load(std::memory_order_relaxed) >> (pitch & 63)) & 1u);
}
// A note-off the pedal held, sent now with the release velocity its key-up carried.
inline void sendHeldNoteOff(Instrument& inst, int32_t pitch) {
    const uint8_t rv = inst.pedalRelVel[pitch].exchange(0, std::memory_order_relaxed);
    if (rv > 0) inst.noteOffVelocity(pitch, static_cast<float>(rv) / 127.0f);
    else
        inst.noteOff(pitch);
}
inline void sendNoteOn(Instrument& inst, int32_t pitch, float vel) {
    if (pedalHolds(inst, pitch)) {
        auto& h = inst.pedalHeld[pitch >> 6];
        h.store(h.load(std::memory_order_relaxed) & ~(uint64_t{1} << (pitch & 63)), std::memory_order_relaxed);
        sendHeldNoteOff(inst, pitch);
    }
    inst.noteOn(pitch, vel);
}
inline void sendNoteOff(Instrument& inst, int32_t pitch, float releaseVel = 0.0f) {
    if (inst.pedalDown.load(std::memory_order_relaxed) && pitch >= 0 && pitch < 128 && !inst.handlesSustain()) {
        auto& h = inst.pedalHeld[pitch >> 6];
        h.store(h.load(std::memory_order_relaxed) | (uint64_t{1} << (pitch & 63)), std::memory_order_relaxed);
        const long rv = std::lround(std::clamp(releaseVel, 0.0f, 1.0f) * 127.0f);
        inst.pedalRelVel[pitch].store(
            static_cast<uint8_t>(releaseVel > 0.0f ? std::max(1L, rv) : 0L), std::memory_order_relaxed);
        return;
    }
    if (releaseVel > 0.0f) inst.noteOffVelocity(pitch, releaseVel);
    else
        inst.noteOff(pitch);
}
inline void sendController(Instrument& inst, int32_t ctl, float value) {
    if (ctl == kCtlSustain && !inst.handlesSustain()) {
        const bool down = value >= 0.5f;
        inst.pedalDown.store(down, std::memory_order_relaxed);
        if (!down) {
            for (int32_t p = 0; p < 128; ++p)
                if (pedalHolds(inst, p)) sendHeldNoteOff(inst, p);
            inst.pedalHeld[0].store(0, std::memory_order_relaxed);
            inst.pedalHeld[1].store(0, std::memory_order_relaxed);
        }
    }
    inst.controller(ctl, value);
}

// Stop / jump: pedals left down and a bent wheel go back to rest (the chase that follows a jump
// sets them again where the clip has them).
inline void releaseHeldControllers(Instrument& inst) {
    for (int32_t ctl : {64, 65, 66, 67, 68, 69, static_cast<int32_t>(kCtlPitchWheel)}) {
        const float v = inst.deliveredCtl[ctl].load(std::memory_order_relaxed);
        if (std::isnan(v) || v == kCtlRest) continue;
        inst.deliveredCtl[ctl].store(kCtlRest, std::memory_order_relaxed);
        sendController(inst, ctl, kCtlRest);
    }
}

// One expression-wire value into an instrument: a controller (dim >= kCtlDimBase, pitch −1)
// or a note's / the instrument's expression. Controllers are remembered on the instrument for
// the stop reset.
inline void deliverExpr(Instrument& inst, int32_t pitch, int32_t dim, float value) {
    if (dim >= kCtlDimBase) {
        const int32_t ctl = dim - kCtlDimBase;
        if (ctl < 0 || ctl >= kCtlCount) return;
        inst.deliveredCtl[ctl].store(value, std::memory_order_relaxed);
        sendController(inst, ctl, value);
    } else {
        inst.noteExpression(pitch, dim, value);
    }
}

// Drive an instrument through one block: note events and clip-note expression merged by
// sample offset (notes first at a shared offset, so a note's opening expression lands on it),
// rendering the stretches in between (a hosted plug-in: the whole block at once, its events
// stamped with their offsets — see Instrument::takesTimedEvents). `live` runs once, before the first sample is rendered —
// after the offset-0 notes, where live input sits — to apply the block's live expression.
// An instrument switched off (#148) renders its first block off once more, without the block's
// events, fading what was sounding to silence, then releases every voice and held controller;
// after that it isn't rendered at all (no CPU) until it is switched back on, so `dst` stays
// silent. The MIDI before it keeps running. `beat` / `spb` / `rolling` is the clock the track's
// MIDI effects ran on, for the MIDI effects inside the instrument.
// One articulation event (specs/085) into the instrument, on its channel. Keyswitch notes bypass
// the engine's sustain pedal (sendNoteOn/Off): a switch is not a note the player holds.
inline void deliverArt(Instrument& inst, const ArtEv& e) {
    inst.eventChannel = e.channel;
    switch (e.kind) {
    case ArtEvKind::KeyOn: inst.keyswitchOn(e.data1, static_cast<float>(std::max<int>(1, e.data2)) / 127.0f); break;
    case ArtEvKind::KeyOff: inst.keyswitchOff(e.data1); break;
    case ArtEvKind::Control: inst.controller(e.data1, static_cast<float>(e.data2) / 127.0f); break;
    case ArtEvKind::Program: inst.programChange(e.data1); break;
    }
    inst.eventChannel = 0;
}

template <class Live>
bool playEvents(Instrument& inst, float* dst, int32_t frames, const MidiEv* evs, int n, const ExprEv* xev, int xn,
    const ArtEv* aev, int an, double beat, double spb, bool rolling, Live&& live) {
    const bool off = inst.bypassed();
    if (!off) inst.offSettled.store(false, std::memory_order_relaxed);
    else if (inst.offSettled.load(std::memory_order_relaxed)) {
        inst.tapSilence(frames); // the filter graph shows the silence, not the last sound
        return false;
    }
    if (off) n = xn = an = 0; // nothing new starts on the way out
    if (!off && n == 0 && xn == 0 && an == 0 && inst.isIdle() && !inst.needsIdleRender() &&
        !inst.requiresActiveProcessing()) {
        live();
        if (inst.isIdle()) {
            inst.tapSilence(frames);
            return false;
        }
    }
    if (inst.takesBlockMidi()) inst.blockMidi(evs, n, frames, beat, spb, rolling); // a rack's chain MIDI effects (#329)
    int32_t cursor = 0;
    bool liveDone = false;
    bool wroteAudio = false;
    auto renderChunk = [&](float* out, int32_t count) {
        inst.render(out, count);
        if (!inst.outputAlwaysSilent()) wroteAudio = true;
    };
    auto renderTo = [&](int32_t to) {
        if (to <= cursor) return;
        if (!liveDone) {
            live();
            liveDone = true;
        }
        renderChunk(&dst[cursor * 2], to - cursor);
        cursor = to;
    };
    // An instrument that takes timed events renders once: each event is stamped with its
    // offset instead of being rendered up to (#436). The order is the same either way.
    const bool timed = inst.takesTimedEvents();
    auto liveAt0 = [&] {
        if (liveDone) return;
        inst.eventOffset = 0;
        live();
        liveDone = true;
    };
    int k = 0, x = 0, a = 0;
    while (k < n || x < xn || a < an) {
        const int32_t nOff = k < n ? std::clamp(evs[k].off, 0, frames) : INT32_MAX;
        const int32_t xOff = x < xn ? std::clamp(xev[x].off, 0, frames) : INT32_MAX;
        const int32_t aOff = a < an ? std::clamp(aev[a].off, 0, frames) : INT32_MAX;
        const int32_t at = std::min(std::min(nOff, xOff), aOff);
        if (timed) {
            if (at >= frames) break; // after the render, at the next block's start (as segmented)
            if (at > 0) liveAt0();   // live input sits after the offset-0 notes
            inst.eventOffset = at;
        } else {
            renderTo(at);
        }
        if (aOff <= nOff && aOff <= xOff) { // an articulation goes ahead of the note it selects
            deliverArt(inst, aev[a]);
            ++a;
        } else if (nOff <= xOff) {
            if (evs[k].on) sendNoteOn(inst, evs[k].pitch, evs[k].vel);
            else
                sendNoteOff(inst, evs[k].pitch, evs[k].vel);
            ++k;
        } else {
            deliverExpr(inst, xev[x].pitch, xev[x].dim, xev[x].value);
            ++x;
        }
    }
    liveAt0();
    if (timed) {
        inst.eventOffset = 0;
        renderChunk(dst, frames);
        cursor = frames;
        for (; a < an; ++a) deliverArt(inst, aev[a]); // end-of-block events: offset 0 of the next render
        for (; k < n; ++k)
            if (evs[k].on) sendNoteOn(inst, evs[k].pitch, evs[k].vel);
            else
                sendNoteOff(inst, evs[k].pitch);
        for (; x < xn; ++x) deliverExpr(inst, xev[x].pitch, xev[x].dim, xev[x].value);
    }
    renderTo(frames);
    if (off) {
        if (wroteAudio) {
            const float step = frames > 0 ? 1.0f / static_cast<float>(frames) : 1.0f;
            for (int32_t i = 0; i < frames; ++i) {
                const float g = 1.0f - static_cast<float>(i + 1) * step;
                dst[i * 2] *= g;
                dst[i * 2 + 1] *= g;
            }
        }
        inst.allNotesOff();
        releaseHeldControllers(inst);
        inst.offSettled.store(true, std::memory_order_relaxed);
    }
    if (wroteAudio) inst.tapOutput(dst, frames); // the card's filter graph spectrum (#531), while one is open
    else
        inst.tapSilence(frames);
    return wroteAudio;
}

} // namespace render_internal
using render_internal::deliverArt;
using render_internal::deliverExpr;
using render_internal::kMetroGain;
using render_internal::kToneGain;
using render_internal::masterPanGains;
using render_internal::midiEvLess;
using render_internal::playEvents;
using render_internal::releaseHeldControllers;
using render_internal::sendController;
using render_internal::sendNoteOff;
using render_internal::sendNoteOn;
} // namespace nota::detail
