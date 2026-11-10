// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — realtime render core: poll, block render, offline bounce, command/live-MIDI drain, per-track raw renderers, mixer processBlock, metronome & test tone. RT-hot; do not add allocations.

#include "Engine.h"
#include "ArticulationSchedule.h"
#include "dsp/core/ScopedNoDenormals.h"
#include "dsp/math/SimdAudio.h"
#include "devices/racks/DrumRack.h"
#include "ParamResolve.h"
#include "devices/racks/RackDevice.h"
#include "devices/racks/RackInstrument.h"
#include "bridge/PluginHostBridge.h"
#include "media/WarpStream.h"

#include <algorithm>
#include <array>
#include <bit>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <limits>

namespace nota {

namespace {
constexpr double kTwoPi = 6.283185307179586;
constexpr float kToneGain = 0.2f;
constexpr float kMetroGain = 0.5f;
// Track / return pan as a 2×2 matrix (specs/006): out L = ll·L + lr·R, out R = rl·L + rr·R,
// fader volume included. Balance is the equal-power law Nota always had (cross terms 0, the
// far channel fades out). Stereo folds a share b = |pan|/2 of the far channel into the near
// side and takes it off the near channel's own gain, so a mono source (L = R) gets exactly
// the Balance gains, centre is identical, and full pan is the mono fold (L+R)/2.
struct PanGains {
    float ll, lr, rl, rr;
};
inline PanGains panGains(float pan, float vol, PanLaw law) {
    const double theta = (pan + 1.0) * (kTwoPi / 8.0); // 0..pi/2
    const double c = std::cos(theta), s = std::sin(theta);
    if (law == PanLaw::Balance) return {static_cast<float>(c) * vol, 0.0f, 0.0f, static_cast<float>(s) * vol};
    const double b = 0.5 * std::fabs(pan);
    if (pan <= 0.0f)
        return {static_cast<float>(c - b) * vol, static_cast<float>(b) * vol, 0.0f, static_cast<float>(s) * vol};
    return {static_cast<float>(c) * vol, 0.0f, static_cast<float>(b) * vol, static_cast<float>(s - b) * vol};
}

// One track's pan for one segment. Without cross terms or a law crossfade it is the plain
// two-gain multiply (so Balance renders stay bit-identical); during a crossfade the gains
// blend from the old law's to the new law's per sample.
struct PanStage {
    PanGains to, from;
    bool cross, fade;
    float pos, step;
    PanStage(float pan, float vol, PanLaw toLaw, PanLaw fromLaw, float fadePos, float fadeStep)
        : to(panGains(pan, vol, toLaw)), from(to), fade(fadePos < 1.0f), pos(fadePos), step(fadeStep) {
        if (fade) from = panGains(pan, vol, fromLaw);
        cross = fade || to.lr != 0.0f || to.rl != 0.0f;
    }
    void apply(int32_t i, float inL, float inR, float& l, float& r) const {
        if (!cross) {
            l = inL * to.ll;
            r = inR * to.rr;
            return;
        }
        PanGains k = to;
        if (fade) {
            const float x = std::min(1.0f, pos + step * static_cast<float>(i));
            k = {from.ll + (to.ll - from.ll) * x, from.lr + (to.lr - from.lr) * x, from.rl + (to.rl - from.rl) * x,
                from.rr + (to.rr - from.rr) * x};
        }
        l = k.ll * inL + k.lr * inR;
        r = k.rl * inL + k.rr * inR;
    }
};

// Stereo measurement (specs/008): fold one block's mean L², R², L·R into a track's running
// averages; `a` is the per-block smoothing step for τ = 0.3 s at this block length.
inline float stereoBlockStep(int32_t frames, double sr) {
    return static_cast<float>(1.0 - std::exp(-frames / (0.3 * (sr > 0.0 ? sr : 44100.0))));
}
inline void stereoFold(std::atomic<float>& eL, std::atomic<float>& eR, std::atomic<float>& eLR, double sqL, double sqR,
    double sLR, int32_t frames, float a) {
    const double n = frames > 0 ? frames : 1;
    auto fold = [a](std::atomic<float>& e, double mean) {
        const float cur = e.load(std::memory_order_relaxed);
        e.store(cur + a * (static_cast<float>(mean) - cur), std::memory_order_relaxed);
    };
    fold(eL, sqL / n);
    fold(eR, sqR / n);
    fold(eLR, sLR / n);
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
    return pitch >= 0 && pitch < 128 && ((inst.pedalHeld[pitch >> 6].load(std::memory_order_relaxed) >> (pitch & 63)) & 1u);
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
        inst.pedalRelVel[pitch].store(static_cast<uint8_t>(releaseVel > 0.0f ? std::max(1L, rv) : 0L), std::memory_order_relaxed);
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
    case ArtEvKind::KeyOn: inst.noteOn(e.data1, static_cast<float>(std::max<int>(1, e.data2)) / 127.0f); break;
    case ArtEvKind::KeyOff: inst.noteOff(e.data1); break;
    case ArtEvKind::Control: inst.controller(e.data1, static_cast<float>(e.data2) / 127.0f); break;
    case ArtEvKind::Program: inst.programChange(e.data1); break;
    }
    inst.eventChannel = 0;
}

template <class Live>
void playEvents(Instrument& inst, float* dst, int32_t frames, const MidiEv* evs, int n, const ExprEv* xev, int xn,
    const ArtEv* aev, int an, double beat, double spb, bool rolling, Live&& live) {
    const bool off = inst.bypassed();
    if (!off) inst.offSettled.store(false, std::memory_order_relaxed);
    else if (inst.offSettled.load(std::memory_order_relaxed)) {
        inst.tapSilence(frames); // the filter graph shows the silence, not the last sound
        return;
    }
    if (off) n = xn = an = 0; // nothing new starts on the way out
    if (inst.takesBlockMidi()) inst.blockMidi(evs, n, frames, beat, spb, rolling); // a rack's chain MIDI effects (#329)
    int32_t cursor = 0;
    bool liveDone = false;
    auto renderTo = [&](int32_t to) {
        if (to <= cursor) return;
        if (!liveDone) {
            live();
            liveDone = true;
        }
        inst.render(&dst[cursor * 2], to - cursor);
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
        inst.render(dst, frames);
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
        const float step = frames > 0 ? 1.0f / static_cast<float>(frames) : 1.0f;
        for (int32_t i = 0; i < frames; ++i) {
            const float g = 1.0f - static_cast<float>(i + 1) * step;
            dst[i * 2] *= g;
            dst[i * 2 + 1] *= g;
        }
        inst.allNotesOff();
        releaseHeldControllers(inst);
        inst.offSettled.store(true, std::memory_order_relaxed);
    }
    inst.tapOutput(dst, frames); // the card's filter graph spectrum (#531), while one is open
}

// Something on the track that holds sound after its input stops: a device that runs, or a
// plug-in delay compensation line that is delaying (#449).
bool hasLiveDevice(const Track& t) {
    if (t.pdc && !t.pdc->isIdle()) return true;
    for (const auto& d : t.devices)
        if (d && !d->bypassed()) return true;
    return false;
}

// How settled a track's chain is once its output has gone quiet (#449): every live device
// reports idle (Settled); some are hosted plug-ins, whose state the host can't see, and the
// rest are idle (Opaque); or a built-in device still holds sound or never reports idle (Busy).
enum class ChainRest : uint8_t { Settled, Opaque, Busy };
ChainRest chainRest(const Track& t) {
    if (t.pdc && !t.pdc->isIdle()) return ChainRest::Busy;
    ChainRest r = ChainRest::Settled;
    for (const auto& d : t.devices) {
        if (!d || d->bypassed() || d->isIdle()) continue;
        // Only a hosted plug-in itself is opaque (builtinKind < 0); a rack that holds one is
        // built-in and still busy while its own delays hold sound. Audio-thread safe.
        if (d->builtinKind() >= 0) return ChainRest::Busy;
        r = ChainRest::Opaque;
    }
    return r;
}

} // namespace

void Engine::noteExprEvents(
    const Note& note, double onS, double offS, int32_t frames, double spb, ExprEv* out, int& n, int cap) {
    static constexpr int32_t kExprStep = 64; // ~1.3 ms at 48 kHz; the voices smooth between
    if (!note.expr || spb <= 0.0 || offS <= 0.0 || onS >= frames || offS <= onS) return;
    const int32_t first = static_cast<int32_t>(std::max(0.0, onS)); // the note-on's own offset
    const int32_t end = static_cast<int32_t>(std::min<double>(frames, offS));
    if (first >= frames) return;
    const NoteExpr& e = *note.expr;
    for (int32_t d = 0; d < kExprDims; ++d) {
        const auto& c = e.dim[d];
        if (c.empty()) continue;
        auto valueAt = [&](int32_t off) { return e.valueAt(d, (off - onS) / spb); };
        if (n < cap) out[n++] = ExprEv{first, note.pitch, d, valueAt(first)};
        if (c.size() < 2) continue;
        // While the curve moves (one step past its last point, to land on it).
        const double moveFrom = onS + c.front().beat * spb - kExprStep;
        const double moveTo = onS + c.back().beat * spb + kExprStep;
        for (int32_t off = (first / kExprStep + 1) * kExprStep; off < end && n < cap; off += kExprStep)
            if (off >= moveFrom && off <= moveTo) out[n++] = ExprEv{off, note.pitch, d, valueAt(off)};
    }
}

void Engine::clipCtlEvents(const MidiClip& clip, double clipStartS, double clipEndS, double blockStart, int32_t frames,
    double spb, bool chase, ExprEv* out, int& n, int cap) {
    static constexpr int32_t kStep = 64; // as recorded MPE: the instrument smooths between
    if (clip.controllers.empty() || spb <= 0.0) return;
    const double b1 = blockStart + frames;
    const double from = std::max(blockStart, clipStartS), to = std::min(b1, clipEndS);
    if (to <= from) return;
    const int32_t first = static_cast<int32_t>(from - blockStart);
    const int32_t end = std::min(frames, static_cast<int32_t>(std::ceil(to - blockStart)));
    const bool entry =
        chase || (clipStartS >= blockStart - 0.5 && clipStartS < b1 - 0.5); // half-sample slack, as below
    auto beatAt = [&](double off) { return (blockStart + off - clipStartS) / spb; };
    for (const auto& cp : clip.controllers) {
        if (!cp || cp->points.empty()) continue;
        const ControllerCurve& c = *cp;
        const int32_t dim = kCtlDimBase + c.ctl;
        const double firstBeat = c.points.front().beat;
        // Before a curve's first point it has no say: the controller keeps what it had.
        if (entry && beatAt(first) >= firstBeat - 1e-9 && n < cap)
            out[n++] = ExprEv{first, -1, dim, c.valueAt(beatAt(first))};
        // Recorded points sit on block starts (the capture is block-timed): place each on a whole
        // sample, with a hair of slack, so rounding never lets one fall between two blocks.
        for (const auto& p : c.points) {
            if (n >= cap) break;
            const double rel = clipStartS + p.beat * spb - blockStart;
            const int64_t off = static_cast<int64_t>(std::floor(rel + 1e-6));
            if (off < first || off >= end || off >= frames) continue;
            out[n++] = ExprEv{static_cast<int32_t>(off), -1, dim, p.value};
        }
        if (!ctlIsSwitch(c.ctl) && c.movesIn(beatAt(first), beatAt(end)))
            for (int32_t off = (first / kStep + 1) * kStep; off < end && n < cap; off += kStep)
                if (beatAt(off) >= firstBeat) out[n++] = ExprEv{off, -1, dim, c.valueAt(beatAt(off))};
    }
}

void Engine::clipCtlEndResets(const MidiClip& clip, double clipStartS, double clipEndS, double blockStart,
    int32_t frames, ExprEv* out, int& n, int cap) {
    if (clip.controllers.empty() || clipEndS <= clipStartS || clipEndS < blockStart || clipEndS >= blockStart + frames)
        return;
    const double lenBeats = clip.lengthBeats;
    const int32_t off = static_cast<int32_t>(clipEndS - blockStart);
    for (const auto& cp : clip.controllers) {
        if (!cp || cp->points.empty() || n >= cap) continue;
        if (!ctlIsSwitch(cp->ctl) && cp->ctl != kCtlPitchWheel) continue;
        if (cp->points.front().beat > lenBeats || cp->valueAt(lenBeats) == kCtlRest) continue;
        out[n++] = ExprEv{off, -1, kCtlDimBase + cp->ctl, kCtlRest};
    }
}

// Runs the block's sorted note events through the track's MIDI FX chain (bypass =
// passthrough). Ping-pongs between evs and scratch (both size >= 1024); the final
// list is left in evs. beatStart is the block's beat position for the active clock.
int Engine::applyMidiEffects(
    Track& t, MidiEv* evs, int n, MidiEv* scratch, int32_t frames, double beatStart, double spb, bool playing) {
    MidiEv* a = evs;
    MidiEv* b = scratch;
    for (auto& md : t.midiEffects) {
        if (!md) continue;
        int outN = 0;
        md->process(a, n, b, outN, 1024, frames, beatStart, spb, playing);
        std::swap(a, b);
        n = outN;
    }
    if (a != evs)
        for (int i = 0; i < n; ++i) evs[i] = a[i];
    return n;
}

// Map/CC routing: a MIDI effect (the arpeggiator) drives a target audio-device param
// on the same track from its per-step CC lane. Runs each block after the MIDI chain,
// before the device chain processes, so the modulation applies this block.
// An Arp grouped into a rack (a MIDI Effect Rack or an Instrument Rack chain) keeps driving
// its target, and a target grouped into a rack is reached through its nested address
// (specs/044); both walks read published snapshots only.
// The route comes from the published snapshot (Track::ccRoutes), never from the Arp's own fields:
// a group or rack edit retargets those before the new device order is published.
static void applyCcRoute(Track& t, const MidiDevice& md) {
    const Track::CcRoute* rt = nullptr;
    for (auto& r : *t.ccRoutes)
        if (r.arp == &md) { rt = &r; break; }
    if (!rt) return;
    const int di = rt->dev, pi = rt->param;
    if (pi < 0) return;
    Device* d = nullptr;
    if (rt->uid) d = resolveNested(t, di, rt->uid, rt->slot).device;
    else if (di >= 0 && di < static_cast<int>(t.devices.size())) d = t.devices[di].get();
    if (!d || pi >= d->paramCount()) return;
    const float mn = d->paramMin(pi), mx = d->paramMax(pi);
    const float mod = std::clamp(md.ccValue() * md.ccDepth(), 0.0f, 1.0f);
    d->setParam(pi, mn + (mx - mn) * mod);
}

void Engine::applyMidiCcRouting(Track& t) {
    if (!t.ccRoutes) return;
    for (auto& md : t.midiEffects) {
        if (!md) continue;
        applyCcRoute(t, *md);
        if (auto* r = md->asRack()) r->forEachChildMidi([&](MidiDevice& c) { applyCcRoute(t, c); });
    }
    if (auto* r = t.instrument ? t.instrument->asRack() : nullptr)
        r->forEachChildMidi([&](MidiDevice& c) { applyCcRoute(t, c); });
}

void Engine::poll() {
    if (audioRecording_) drainInputQueue(); // keep the capture buffer flowing while recording
    serviceSessionFollow();                 // scene follow + fixed-length session capture

    // Automation write/record (M9-C). Message thread: a transport stop ends any
    // latched gesture, and active gestures are sampled at the live playhead.
    {
        const bool playing = transport_.uiIsPlaying();
        const bool rec = autoRecord_.load(std::memory_order_relaxed);
        if (!playing && wasPlaying_) finishAllWrites(); // stop ends latched writes
        wasPlaying_ = playing;

        // Plugin-GUI gestures (M9-C): grabbing a knob in the plugin's own editor
        // begins/ends a write for that param, so it records (or overrides) just like
        // our own faders — a mouse gesture, so never latched. Instruments/devices are
        // stable across snapshots, so a copied track list stays valid while
        // begin/end republish.
        {
            auto snapshot = authoring_->tracks;
            for (auto& tptr : snapshot) {
                const int32_t tid = tptr->id();
                if (auto& inst = tptr->instrument) {
                    int gb = inst->takePluginGestureBegin();
                    if (gb >= 0) beginAutomationWrite(tid, 3, -1, -1, pluginParamId(tid, -1, gb).c_str(), false);
                    int ge = inst->takePluginGestureEnd();
                    if (ge >= 0) endAutomationWrite(tid, 3, -1, -1, pluginParamId(tid, -1, ge).c_str());
                }
                for (int32_t di = 0; di < static_cast<int32_t>(tptr->devices.size()); ++di) {
                    auto& d = tptr->devices[di];
                    if (!d) continue;
                    int gb = d->takePluginGestureBegin();
                    if (gb >= 0) beginAutomationWrite(tid, 3, di, -1, pluginParamId(tid, di, gb).c_str(), false);
                    int ge = d->takePluginGestureEnd();
                    if (ge >= 0) endAutomationWrite(tid, 3, di, -1, pluginParamId(tid, di, ge).c_str());
                }
            }
        }

        if (playing && rec && !activeWrites_.empty()) sampleAutomationWritesAt(transport_.uiPositionBeats());
    }

    foldRecordedTake();
}

void Engine::foldRecordedTake() {
    // Loop record replace (#29): read the audio thread's mark before draining, so every note of
    // an earlier pass queued ahead of it is in this batch.
    const int32_t replaceUpTo = replaceUpTo_.load(std::memory_order_acquire);
    RecordedNote rn;
    std::vector<RecordedNote> batch;
    while (recorded_.pop(rn))
        if (rn.epoch == takeEpoch_) batch.push_back(rn); // a discarded take's late notes go (specs/078)
    // Expression after the notes: a note's points were all queued before its note-off was.
    collectRecordedExpr();
    {
        RecordedCtl x;
        while (recordedCtl_.pop(x))
            if (x.epoch == takeEpoch_) ctlBatch_.push_back(x);
    } // controllers (specs/020)
    const bool ctlWork = !ctlBatch_.empty() || ctlStopPending_;
    const bool replacePending = replaceUpTo > appliedReplaceUpTo_ && recordSessionScene_ < 0;
    if (batch.empty() && !replacePending && !ctlWork) return;

    // Session-slot recording (M5-4): notes are already loop-local; each lane appends into its
    // slot (several slots record at once, specs/086).
    if (!midiLanes_.empty() && midiLanes_[0].scene >= 0) {
        if (batch.empty() && !ctlWork) return;
        std::vector<RecordedCtl> ctls = std::move(ctlBatch_);
        ctlBatch_.clear();
        const bool stop = ctlStopPending_;
        ctlStopPending_ = false;
        for (size_t k = 0; k < midiLanes_.size(); ++k)
            foldSessionLane(midiLanes_[k], static_cast<int32_t>(k), batch, ctls, stop);
        return;
    }

    // Arrangement: every MIDI take lane folds its own notes and controllers into its clip
    // (specs/086); the loop record replace covers the whole take.
    std::vector<RecordedCtl> ctls = std::move(ctlBatch_);
    ctlBatch_.clear();
    const bool stop = ctlStopPending_;
    ctlStopPending_ = false;
    for (size_t k = 0; k < midiLanes_.size(); ++k)
        foldMidiLane(midiLanes_[k], static_cast<int32_t>(k), batch, ctls, stop, replaceUpTo, replacePending);
    if (replacePending) appliedReplaceUpTo_ = replaceUpTo;
}

// A Session lane's share of a poll: its loop-local notes and controllers into its slot.
bool Engine::foldSessionLane(MidiTakeLane& l, int32_t lane, const std::vector<RecordedNote>& batch,
    const std::vector<RecordedCtl>& ctls, bool stop) {
    bool mine = stop;
    for (const auto& r : batch) mine = mine || r.lane == lane;
    for (const auto& x : ctls) mine = mine || x.lane == lane;
    auto st = l.trackId >= 0 && l.scene >= 0 && mine ? findTrackAuthoring(l.trackId) : nullptr;
    if (!st || l.scene >= static_cast<int32_t>(st->sessionSlots.size())) {
        for (const auto& r : batch)
            if (r.lane == lane) {
                Note gone;
                attachRecordedExpr(gone, exprKey(lane, r.pitch), r.startBeat);
            }
        return false;
    }
    std::swap(ctlFold_, l.ctlFold);
    std::swap(takeCtls_, l.takeCtls);
    struct SwapBack {
        Engine& e;
        MidiTakeLane& l;
        ~SwapBack() {
            std::swap(e.ctlFold_, l.ctlFold);
            std::swap(e.takeCtls_, l.takeCtls);
        }
    } swapBack{*this, l};
    for (const auto& x : ctls)
        if (x.lane == lane) ctlBatch_.push_back(x);
    ctlStopPending_ = stop;
    auto nt = cloneTrack(*st);
    SessionSlot& s = nt->sessionSlots[l.scene];
    const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    for (auto& r : batch) {
        if (r.lane != lane) continue;
        Note n;
        n.pitch = r.pitch;
        n.startBeat = std::fmod(r.startBeat < 0 ? 0 : r.startBeat, L);
        n.lengthBeats = r.lengthBeats > 0.0 ? r.lengthBeats : 0.1;
        // Played on the count-in (#158): a slot has nothing before its start, so a note
        // released before the take began is dropped and one held into it starts there.
        if (r.startBeat < 0.0) {
            const double end = r.startBeat + r.lengthBeats;
            if (end <= 1e-9) {
                Note gone;
                attachRecordedExpr(gone, exprKey(lane, r.pitch), r.startBeat);
                continue;
            }
            n.lengthBeats = end;
        }
        n.velocity = r.velocity;
        attachRecordedExpr(n, exprKey(lane, r.pitch), r.startBeat);
        s.midi.notes.push_back(n);
    }
    foldRecordedCtl(s.midi, L);
    ctlStopPending_ = false;
    republishWithTrackRaw(l.trackId, nt); // grows the live take, not a new undo step
    return true;
}

// One lane's share of a poll: its notes, its controllers (and a pending stop) into its clip.
// The fold state of the lane is swapped into takeNotes_ / ctlFold_ / takeCtls_ for the work.
bool Engine::foldMidiLane(MidiTakeLane& l, int32_t lane, const std::vector<RecordedNote>& batch,
    const std::vector<RecordedCtl>& ctls, bool stop, int32_t replaceUpTo, bool replacePending) {
    bool mine = stop || replacePending;
    for (const auto& r : batch) mine = mine || r.lane == lane;
    for (const auto& x : ctls) mine = mine || x.lane == lane;
    auto t = l.trackId >= 0 && mine ? findTrackAuthoring(l.trackId) : nullptr;
    if (!t || l.clipIndex < 0 || l.clipIndex >= static_cast<int32_t>(t->midiClips.size())) {
        for (const auto& r : batch) // its curves go with its notes
            if (r.lane == lane) {
                Note gone;
                attachRecordedExpr(gone, exprKey(lane, r.pitch), r.startBeat);
            }
        return false;
    }
    std::swap(takeNotes_, l.takeNotes);
    std::swap(ctlFold_, l.ctlFold);
    std::swap(takeCtls_, l.takeCtls);
    struct SwapBack {
        Engine& e;
        MidiTakeLane& l;
        ~SwapBack() {
            std::swap(e.takeNotes_, l.takeNotes);
            std::swap(e.ctlFold_, l.ctlFold);
            std::swap(e.takeCtls_, l.takeCtls);
        }
    } swapBack{*this, l};
    for (const auto& x : ctls)
        if (x.lane == lane) ctlBatch_.push_back(x);
    ctlStopPending_ = stop;

    auto nt = cloneTrack(*t);
    MidiClip& clip = nt->midiClips[l.clipIndex];
    for (auto& r : batch) {
        if (r.lane != lane) continue;
        if (r.pass < replaceUpTo) { // its pass was already replaced: drop it (and its curves)
            Note gone;
            attachRecordedExpr(gone, exprKey(lane, r.pitch), r.startBeat);
            continue;
        }
        // A note that began before the take's clip (played on the count-in, or a key held
        // when Record was pressed, #56) moves the clip's left edge back to it, keeping every
        // note where it is. Clips can't start before 1.1, so a note older than that keeps
        // its head before the clip's start (it sounds from the clip start).
        if (r.startBeat < clip.startBeat) {
            const double newStart = std::max(0.0, r.startBeat);
            const double delta = clip.startBeat - newStart;
            if (delta > 0.0) {
                for (auto& o : clip.notes) o.startBeat += delta;
                clip.lengthBeats += delta;
                clip.startBeat = newStart;
            }
        }
        Note n;
        n.pitch = r.pitch;
        n.startBeat = r.startBeat - clip.startBeat;
        n.lengthBeats = r.lengthBeats > 0.0 ? r.lengthBeats : 0.1;
        n.velocity = r.velocity;
        attachRecordedExpr(n, exprKey(lane, r.pitch), r.startBeat);
        clip.notes.push_back(n);
        takeNotes_.push_back({r.pitch, r.startBeat, r.pass});
        // Grow the clip so freshly recorded notes fall inside it (gating in
        // renderInstrumentRaw drops notes past lengthBeats).
        const double end = n.startBeat + n.lengthBeats;
        if (end > clip.lengthBeats) clip.lengthBeats = end;
    }
    foldRecordedCtl(clip, 0.0);
    // Replace: remove the notes this take recorded on earlier passes — matched by pitch and
    // absolute start, so notes the clip held before the take (or the user drew) stay.
    if (replacePending) {
        auto& notes = clip.notes;
        for (size_t i = 0; i < notes.size();) {
            const double abs = clip.startBeat + notes[i].startBeat;
            auto it = std::find_if(takeNotes_.begin(), takeNotes_.end(), [&](const TakeNote& k) {
                return k.pass < replaceUpTo && k.pitch == notes[i].pitch && std::fabs(k.absStart - abs) < 1e-6;
            });
            if (it == takeNotes_.end()) {
                ++i;
                continue;
            }
            takeNotes_.erase(it);
            // It may be sounding right now (an earlier pass playing back): release its voice.
            Command c;
            c.type = CommandType::ReleaseNote;
            c.i0 = l.trackId;
            c.i1 = notes[i].pitch;
            commands_.push(c);
            notes.erase(notes.begin() + static_cast<std::ptrdiff_t>(i));
        }
        takeNotes_.erase(std::remove_if(takeNotes_.begin(), takeNotes_.end(),
                             [&](const TakeNote& k) { return k.pass < replaceUpTo; }),
            takeNotes_.end());
        // ...and the controller points those passes recorded (the whole pass is replaced).
        for (const TakeCtl& k : takeCtls_) {
            if (k.pass >= replaceUpTo) continue;
            const ControllerCurve* cur = clip.controller(k.ctl);
            if (!cur) continue;
            std::vector<AutomationPoint> pts;
            const double lb = k.absBeat - clip.startBeat;
            for (const auto& p : cur->points)
                if (std::fabs(p.beat - lb) > 1e-6) pts.push_back(p);
            clip.setController(k.ctl, std::move(pts));
        }
        takeCtls_.erase(
            std::remove_if(takeCtls_.begin(), takeCtls_.end(), [&](const TakeCtl& k) { return k.pass < replaceUpTo; }),
            takeCtls_.end());
    }
    republishWithTrackRaw(l.trackId, nt); // grows the live take, not a new undo step
    return true;
}

void Engine::collectRecordedExpr() {
    RecordedExpr x;
    while (recordedExpr_.pop(x)) {
        if (x.epoch != takeEpoch_) continue; // a discarded take's (specs/078)
        auto& curve = pendingExpr_[{exprKey(x.lane, x.pitch), x.startBeat}].dim[x.dim]; // per lane (specs/086)
        if (!curve.empty() && x.beat <= curve.back().beat) curve.back().value = x.value; // same instant
        else
            curve.push_back({x.beat, x.value});
    }
}

// A finished recorded note takes the curve gathered for it, thinned to the points that shape
// it (a block-rate stream is far denser than the gesture needs) — or nothing when every
// dimension stayed neutral (an MPE controller announces a note's resting state too).
void Engine::attachRecordedExpr(Note& n, int32_t pitch, double startBeat) {
    auto it = pendingExpr_.find({pitch, startBeat});
    if (it == pendingExpr_.end()) return;
    auto e = std::make_shared<NoteExpr>();
    static constexpr float kTol[kExprDims] = {0.03f, 0.01f, 0.01f}; // semitones / units
    for (int32_t d = 0; d < kExprDims; ++d) {
        const auto& src = it->second.dim[d];
        bool moved = false;
        for (const auto& p : src)
            if (std::fabs(p.value - kExprNeutral[d]) > kTol[d]) {
                moved = true;
                break;
            }
        if (moved) e->dim[d] = thinExprCurve(src, kTol[d]);
    }
    pendingExpr_.erase(it);
    if (e->empty()) return;
    n.expr = e;
    n.exprId = storeNoteExpr(e);
}

// --- recorded controllers (specs/020) -------------------------------------------
// The audio thread queues every controller sample of the take (RecordedCtl); each poll folds the
// new ones into the take's clip. A controller a pass moves is replaced over the range it touched
// — from its first point in the pass up to the pass end (the loop end, or the record stop) — so
// two passes of the mod wheel never interleave; the controllers a pass didn't move stay, and so
// do the notes (a pedal-only pass re-records only the pedal). The run since the last poll is
// thinned on its own, anchored on the range's last committed point, so a long take stays a
// handful of points per gesture as it grows.

void Engine::resetCtlFold() {
    for (auto& f : ctlFold_) f = CtlFold{};
    ctlBatch_.clear();
    takeCtls_.clear();
    for (auto& l : midiLanes_) {
        for (auto& f : l.ctlFold) f = CtlFold{};
        l.takeCtls.clear();
    }
    ctlStopPending_ = false;
    RecordedCtl x;
    while (recordedCtl_.pop(x)) {}
}

bool Engine::foldRecordedCtl(MidiClip& clip, double slotLen) {
    const bool slot = slotLen > 0.0;
    bool changed = false;
    for (const RecordedCtl& x : ctlBatch_) {
        if (x.ctl < 0) { // a loop pass ended: its ranges run to the loop end
            for (int32_t c = 0; c < kCtlCount; ++c) {
                CtlFold& f = ctlFold_[c];
                if (!f.open || f.pass != x.pass) continue;
                commitCtlRun(clip, c, slotLen, true);
                closeCtlRange(clip, c, x.beat - clip.startBeat, slotLen);
                f.open = false;
                changed = true;
            }
            continue;
        }
        if (x.ctl >= kCtlCount || ctlIsReserved(x.ctl)) continue;
        CtlFold& f = ctlFold_[x.ctl];
        // A Session take wraps at the slot's end: the range closes there and a new one opens.
        const bool wrapped = slot && f.open && x.beat < f.lastAbs - slotLen * 0.5;
        if (f.open && (f.pass != x.pass || wrapped)) {
            commitCtlRun(clip, x.ctl, slotLen, true);
            closeCtlRange(clip, x.ctl, slot ? slotLen : f.lastAbs - clip.startBeat, slotLen);
            f.open = false;
            changed = true;
        }
        if (!f.open) {
            f.open = true;
            f.pass = x.pass;
            f.startAbs = x.beat;
            f.hasCommitted = false;
            f.before = clip.controllerPtr(x.ctl);
        }
        f.lastAbs = x.beat;
        f.run.push_back(x);
    }
    ctlBatch_.clear();
    for (int32_t c = 0; c < kCtlCount; ++c)
        if (!ctlFold_[c].run.empty()) {
            commitCtlRun(clip, c, slotLen, false);
            changed = true;
        }
    if (ctlStopPending_) { // the take stopped: the open ranges end at the stop
        ctlStopPending_ = false;
        for (int32_t c = 0; c < kCtlCount; ++c) {
            CtlFold& f = ctlFold_[c];
            if (!f.open) continue;
            commitCtlRun(clip, c, slotLen, true); // the points held back for thinning
            closeCtlRange(
                clip, c, slot ? std::fmod(std::max(0.0, f.lastAbs), slotLen) : ctlStopAt_ - clip.startBeat, slotLen);
            f.open = false;
            changed = true;
        }
    }
    return changed;
}

void Engine::commitCtlRun(MidiClip& clip, int32_t ctl, double slotLen, bool final) {
    CtlFold& f = ctlFold_[ctl];
    if (f.run.empty()) return;
    const bool slot = slotLen > 0.0;
    // A controller moved before the take's clip (on the count-in) moves its left edge back to
    // it, like a pickup note does; never before 1.1.
    if (!slot) {
        double minAbs = f.run.front().beat;
        for (const auto& x : f.run) minAbs = std::min(minAbs, x.beat);
        if (minAbs < clip.startBeat) {
            const double newStart = std::max(0.0, minAbs);
            const double delta = clip.startBeat - newStart;
            if (delta > 0.0) {
                for (auto& o : clip.notes) o.startBeat += delta;
                clip.shiftControllers(delta);
                clip.lengthBeats += delta;
                clip.startBeat = newStart;
            }
        }
    }
    auto local = [&](double abs) { return slot ? std::fmod(std::max(0.0, abs), slotLen) : abs - clip.startBeat; };

    // The run on the take clock, after the range's last committed point (the thinning anchor).
    std::vector<AutomationPoint> pts;
    if (f.hasCommitted) pts.push_back(f.committed);
    for (const auto& x : f.run) {
        const AutomationPoint p{x.beat, ctlClamp(ctl, x.value), 0.0f};
        if (!pts.empty() && std::fabs(pts.back().beat - p.beat) < 1e-9) pts.back().value = p.value;
        else
            pts.push_back(p);
    }
    f.run.clear();
    const double loAbs = f.hasCommitted ? f.committed.beat : f.startAbs;
    const double hiAbs = pts.back().beat;
    std::vector<AutomationPoint> kept = ctlIsSwitch(ctl) ? dedupeSwitch(pts) : thinCtlCurve(pts, ctlThinTolerance(ctl));
    if (f.hasCommitted && !kept.empty() && std::fabs(kept.front().beat - f.committed.beat) < 1e-9)
        kept.erase(kept.begin());
    // A moving curve keeps its last segment back: the points after the last-but-one kept point
    // wait for the next poll, where the thinning may find them on one longer line.
    // (A long straight or steady stretch is committed at 512 points, so the tail stays small.)
    if (!final && !ctlIsSwitch(ctl) && pts.size() <= 512) {
        const double holdFrom = kept.size() >= 2
                                    ? kept[kept.size() - 2].beat
                                    : (f.hasCommitted ? f.committed.beat : -std::numeric_limits<double>::infinity());
        if (kept.size() >= 2) kept.pop_back();
        else
            kept.clear();
        for (const auto& p : pts)
            if (p.beat > holdFrom + 1e-12 || (kept.empty() && !f.hasCommitted))
                f.run.push_back(RecordedCtl{ctl, p.beat, p.value, f.pass});
        if (kept.empty()) return; // nothing settled yet; the range keeps growing
    }

    // Replace what the clip held over the range so far, then add the run.
    const double loL = local(loAbs), hiL = local(hiAbs);
    std::vector<AutomationPoint> out;
    if (const ControllerCurve* cur = clip.controller(ctl))
        for (const auto& p : cur->points) {
            const bool inRange = f.hasCommitted ? (p.beat > loL + 1e-9 && p.beat <= hiL + 1e-9)
                                                : (p.beat >= loL - 1e-9 && p.beat <= hiL + 1e-9);
            if (!inRange) out.push_back(p);
        }
    // The range's first commit steps out of the old curve: its value holds to just before.
    if (!f.hasCommitted && f.before && !f.before->points.empty() &&
        f.before->points.front().beat < loL - kCtlJunction) {
        const double j = loL - kCtlJunction;
        if (!kept.empty() && f.before->valueAt(j) != kept.front().value) out.push_back({j, f.before->valueAt(j), 0.0f});
    }
    for (const auto& p : kept) {
        out.push_back({local(p.beat), p.value, 0.0f});
        takeCtls_.push_back({ctl, p.beat, f.pass});
    }
    if (!kept.empty()) {
        f.committed = kept.back();
        f.hasCommitted = true;
    }
    clip.setController(ctl, std::move(out));
    // Grow the clip so the controller's last move falls inside it (a pedal released late).
    if (!slot && hiL > clip.lengthBeats) clip.lengthBeats = hiL;
}

void Engine::flushCtlTake() {
    {
        RecordedCtl x;
        while (recordedCtl_.pop(x))
            if (x.epoch == takeEpoch_) ctlBatch_.push_back(x);
    }
    ctlStopPending_ = true;
    ctlStopAt_ = transport_.uiPositionBeats();
    if (!midiLanes_.empty() && midiLanes_[0].scene >= 0) { // every Session slot of the take (specs/086)
        std::vector<RecordedCtl> ctls = std::move(ctlBatch_);
        ctlBatch_.clear();
        ctlStopPending_ = false;
        for (size_t k = 0; k < midiLanes_.size(); ++k)
            foldSessionLane(midiLanes_[k], static_cast<int32_t>(k), {}, ctls, true);
    } else if (!midiLanes_.empty()) { // every MIDI take lane (specs/086)
        std::vector<RecordedCtl> ctls = std::move(ctlBatch_);
        ctlBatch_.clear();
        ctlStopPending_ = false;
        for (size_t k = 0; k < midiLanes_.size(); ++k)
            foldMidiLane(midiLanes_[k], static_cast<int32_t>(k), {}, ctls, true, 0, false);
    }
    resetCtlFold();
}

// End ctl's range at `atLocal` (clip-local): what the clip held between the range's last point
// and there is replaced too; where the old curve goes on past it, a junction point at `atLocal`
// hands back to it with the value it had there.
void Engine::closeCtlRange(MidiClip& clip, int32_t ctl, double atLocal, double slotLen) {
    CtlFold& f = ctlFold_[ctl];
    const ControllerCurve* cur = clip.controller(ctl);
    if (!cur || !f.hasCommitted) return;
    const double lastL =
        slotLen > 0.0 ? std::fmod(std::max(0.0, f.committed.beat), slotLen) : f.committed.beat - clip.startBeat;
    if (atLocal <= lastL + 1e-9) return;
    // The value the curve had there before this range replaced anything (rest when it had none).
    const float old = f.before && !f.before->points.empty() ? f.before->valueAt(atLocal) : kCtlRest;
    std::vector<AutomationPoint> out;
    bool after = false, at = false;
    for (const auto& p : cur->points) {
        if (p.beat > lastL + 1e-9 && p.beat < atLocal - 1e-9) continue;
        if (p.beat >= atLocal - 1e-9) after = true;
        if (std::fabs(p.beat - atLocal) < 1e-9) at = true;
        out.push_back(p);
    }
    if (after && !at) {
        // Hold the range's last value up to the junction, then hand back to the old curve.
        if (!ctlIsSwitch(ctl) && atLocal - kCtlJunction > lastL + 1e-9 && old != f.committed.value)
            out.push_back({atLocal - kCtlJunction, f.committed.value, 0.0f});
        out.push_back({atLocal, old, 0.0f});
    }
    clip.setController(ctl, std::move(out));
    f.before.reset();
}

// --- audio thread ----------------------------------------------------------

void Engine::render(float* out, int32_t numFrames) {
    // Counted before the suspend check (both seq_cst, paired with start()): once start() has
    // suspended rendering and seen the count at zero, no render can be inside the graph.
    rendersInFlight_.fetch_add(1, std::memory_order_seq_cst);
    struct Leave {
        std::atomic<int32_t>& n;
        ~Leave() { n.fetch_sub(1, std::memory_order_release); }
    } leave{rendersInFlight_};
    // start() is still re-preparing the nodes on the control thread: output silence.
    if (renderSuspended_.load(std::memory_order_seq_cst)) {
        std::fill(out, out + static_cast<size_t>(numFrames) * 2, 0.0f);
        return;
    }
    dsp::ScopedNoDenormals noDenormals;
    const auto t0 = std::chrono::steady_clock::now();
    deviceBlock_ = true; // a deadline, whatever the gap since the last block
    processBlock(out, numFrames);
    // DSP load = render time / block budget, smoothed (Phase 11). Live-device
    // path only; renderOffline stays untimed.
    const double sr = sampleRate();
    if (sr > 0.0 && numFrames > 0) {
        const double elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        const double load = elapsed / (numFrames / sr);
        const float prev = cpuLoad_.load(std::memory_order_relaxed);
        cpuLoad_.store(static_cast<float>(prev * 0.9 + load * 0.1), std::memory_order_relaxed);
    }
}

void Engine::renderOffline(float* out, int32_t frames, double sampleRate) {
    if (sampleRate > 0.0) transport_.setSampleRate(sampleRate);
    renderOffline(out, frames);
}

void Engine::renderOffline(float* out, int32_t frames) {
    // Offline rendering re-prepares and processes the graph on the calling thread — with the
    // device live that would race the audio thread. Callers stop the backend first; enforce it.
    if (isRunning()) {
        std::fill(out, out + static_cast<size_t>(frames) * 2, 0.0f);
        return;
    }
    stopPreview(); // audition must never bleed into a bounce (M7-4a)
    if (transport_.sampleRate() <= 0) transport_.setSampleRate(44100.0);
    const double sr = transport_.sampleRate();
    // Re-prepare every node for the render rate ONCE, only when it actually changed (an export
    // bounces in chunks: re-preparing every chunk would reset each instrument/device/plugin's
    // DSP state and click at each block seam). This also re-rates the master + racks + MIDI FX
    // and reconfigures warp streams (their pitch/window sizes are baked from the device rate —
    // a higher export rate would otherwise play warped clips too fast).
    if (sr != preparedSR_) {
        propagateSampleRate(sr);
        reconfigureAllWarpStreams();
        recomputePdc();
        preparedSR_ = sr;
    }
    deviceBlock_ = false; // export/freeze: no deadline, every render thread
    processBlock(out, frames);
}

void Engine::endCountIn() {
    countInActive_ = false;
    uiCountIn_.store(0.0, std::memory_order_relaxed);
    captureGate_.store(true, std::memory_order_relaxed);
}

// A Session take counted in (#158): its slot starts as the transport rolls, on that sample,
// rather than on the next launch quantum. No-op when no take is waiting.
void Engine::launchCountInSlot() {
    if (const int32_t sc = countInLaunchScene_.exchange(-1, std::memory_order_acq_rel); sc >= 0)
        if (auto* sp = recordSlotPlayer_.load(std::memory_order_acquire)) sp->requestLaunch(sc, 0.0);
}

void Engine::drainCommands() {
    Command c;
    while (commands_.pop(c)) {
        switch (c.type) {
        case CommandType::SetToneEnabled: toneEnabled_ = (c.i0 != 0); break;
        case CommandType::SetFrequency: frequency_ = c.f0; break;
        case CommandType::TransportPlay:
            if (c.i0 > 0 && !transport_.isPlaying()) { // count-in first (i0 = bars)
                countInActive_ = true;
                countInPos_ = 0.0;
                countInTotal_ = static_cast<double>(c.i0) * std::max(1, transport_.beatsPerBar());
                lastCountInBeat_ = -1;
                uiCountIn_.store(countInTotal_, std::memory_order_relaxed);
            } else {
                endCountIn();
                transport_.play();
                launchCountInSlot(); // Play pressed during a Session take's count-in
            }
            break;
        case CommandType::TransportStop:
            endCountIn();
            transport_.stop();
            break;
        case CommandType::SetBpm: transport_.setBpm(c.d0); break;
        case CommandType::SetTimeSignature: transport_.setTimeSignature(c.i0, c.i1); break;
        case CommandType::SetLoop: transport_.setLoop(c.i0 != 0, c.d0, c.d1); break;
        case CommandType::SetMetronome: transport_.setMetronome(c.i0 != 0); break;
        case CommandType::ReleaseNote:
            if (c.i1 >= 0 && c.i1 < 128) {
                int k = 0, free = -1;
                while (k < 16 && releaseTrack_[k] != c.i0) {
                    if (free < 0 && releaseTrack_[k] < 0) free = k;
                    ++k;
                }
                if (k == 16) k = free >= 0 ? free : 0; // a full table reuses the first entry
                if (releaseTrack_[k] != c.i0) {
                    releaseTrack_[k] = c.i0;
                    for (auto& w : releasePitches_[k]) w = 0;
                }
                releasePitches_[k][c.i1 >> 5] |= 1u << (c.i1 & 31);
            }
            break;
        case CommandType::TrackNotesOff:
            if (notesOffCount_ < 8) notesOffTracks_[notesOffCount_++] = c.i0;
            break;
        case CommandType::Seek:
            seekedWhileRolling_ |= transport_.isPlaying(); // a jump mid-playback, not Stop→Seek→Play
            transport_.seekBeats(c.d0);
            seekedThisBlock_ = true;
            break;
        case CommandType::TakeRestart: // specs/078: the blocks from here belong to the retried take
            if ((c.i1 & 4) != 0) { // a take started normally: the current epoch, nothing waits
                rtTakeEpoch_ = static_cast<uint32_t>(c.i0);
                rtWaitScene_ = -1;
            } else if ((c.i1 & 2) != 0) { // a Session MIDI take: from when its slot relaunches
                rtPendingEpoch_ = static_cast<uint32_t>(c.i0);
                rtWaitScene_ = static_cast<int32_t>(c.d0);
            } else {
                rtTakeEpoch_ = static_cast<uint32_t>(c.i0);
                rtTakeRestart_ = true;
                rtWaitScene_ = -1;
            }
            if ((c.i1 & 1) != 0) captureGate_.store(true, std::memory_order_release);
            break;
        case CommandType::None: break;
        }
    }
}

// The live-recording preview (#56): copy the notes being held into the take (live input,
// then routed MIDI) behind the sequence counter — odd while writing, so a reader that sees an
// odd or changed value retries. Wait-free and fixed-size; an idle engine with an empty snapshot writes nothing.
void Engine::publishHeldSnapshot(double nowBeat) {
    const bool rec = recording_.load(std::memory_order_relaxed);
    if (!rec && heldCount_.load(std::memory_order_relaxed) == 0) return; // idle: already empty
    const uint32_t s = heldSeq_.load(std::memory_order_relaxed);
    heldSeq_.store(s + 1, std::memory_order_relaxed);
    std::atomic_thread_fence(std::memory_order_release);
    int32_t n = 0;
    if (rec) {
        auto put = [&](int32_t pitch, int32_t source, double start, float vel) {
            HeldSnapSlot& h = heldSnap_[n++];
            h.pitch.store(pitch, std::memory_order_relaxed);
            h.source.store(source, std::memory_order_relaxed);
            h.startBeat.store(start, std::memory_order_relaxed);
            h.velocity.store(vel, std::memory_order_relaxed);
        };
        const RtMidiLane& L = rtMidiLanes_[0]; // the preview shows the first lane's track
        for (int p = 0; p < 128; ++p)
            if (L.pendingActive[p]) put(p, 0, L.pendingStart[p], L.pendingVel[p]);
        for (int p = 0; p < 128; ++p)
            if (L.recRoutedActive[p]) put(p, 1, L.recRoutedStart[p], L.recRoutedVel[p]);
    }
    heldCount_.store(n, std::memory_order_relaxed);
    heldNow_.store(nowBeat, std::memory_order_relaxed);
    heldSeq_.store(s + 2, std::memory_order_release);
}

void Engine::drainLiveMidi(double blockStartBeat, bool playing) {
    liveCount_ = 0;
    liveExprCount_ = 0;
    liveReleaseCount_ = 0;
    const bool rec = recording_.load(std::memory_order_relaxed);

    // When recording into a Session slot, time notes against the slot's own loop
    // clock (localBeats holds this block's start position) instead of the global
    // transport, so overdubs land at the right spot in the loop. During a count-in the
    // transport holds still and notes are timed back from the take start (#56) — for a slot
    // take, back from the slot's start (#158).
    SessionPlayer* slot = recordSlotPlayer_.load(std::memory_order_acquire);
    const double slotLen = recordSlotLen_.load(std::memory_order_relaxed);
    const bool timed = playing || countInActive_; // the timeline has a position for an event
    const double nowBeat = slot ? (countInActive_     ? countInClockOffset()
                                      : slotLen > 0.0 ? std::fmod(slot->localBeats, slotLen)
                                                      : slot->localBeats)
                                : blockStartBeat + countInClockOffset();
    captureNow_ = nowBeat;

    // The take's MIDI lanes (specs/086): a track-addressed note (Nota Remote) records on its own
    // track's lane; ordinary live input on every lane whose MIDI input filter it passes.
    rtMidiLaneCount_ = std::clamp(midiLaneCount_.load(std::memory_order_acquire), 0, kMaxMidiTakeLanes);
    for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
        const int32_t tid = midiLaneTrack_[static_cast<size_t>(k)].load(std::memory_order_relaxed);
        RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(k)];
        if (L.trackId != tid) { // another track's lane now (or closed): start clean
            L.clear();
            L.trackId = tid;
        }
        rtLaneSrc_[k] = midiLaneSrc_[static_cast<size_t>(k)].load(std::memory_order_relaxed);
        rtLaneCh_[k] = midiLaneCh_[static_cast<size_t>(k)].load(std::memory_order_relaxed);
        // The lane's clock: the take's (the first slot, or the transport); a Session slot that
        // joined the take (specs/086) times on its own loop and records once it plays.
        rtLaneNow_[k] = nowBeat;
        rtLaneSlotLen_[k] = slotLen;
        rtLaneSlot_[k] = slot != nullptr;
        rtLaneLive_[k] = true;
        const SessionLaneSlots* ls = laneSlotsHist_.live();
        SessionPlayer* lp = ls ? ls->player[static_cast<size_t>(k)] : nullptr;
        if (k > 0 && lp && lp != slot) {
            const double len = ls->len[static_cast<size_t>(k)];
            rtLaneSlot_[k] = true;
            rtLaneSlotLen_[k] = len;
            rtLaneLive_[k] = lp->playing.load(std::memory_order_relaxed) == ls->scene[static_cast<size_t>(k)];
            rtLaneNow_[k] = len > 0.0 ? std::fmod(lp->localBeats, len) : lp->localBeats;
        }
    }
    // A retried Session MIDI take begins when its slot has relaunched: until then what is
    // played carries the old epoch and stays out (specs/078).
    if (rtWaitScene_ >= 0 && slot && slot->pending.load(std::memory_order_relaxed) != rtWaitScene_ &&
        slot->playing.load(std::memory_order_relaxed) == rtWaitScene_) {
        rtTakeEpoch_ = rtPendingEpoch_;
        rtTakeRestart_ = true;
        rtWaitScene_ = -1;
    }
    // A retry (TakeRestart) starts a fresh take even when this thread never saw recording off.
    const bool restarted = rtTakeRestart_;
    rtTakeRestart_ = false;
    const bool takeStarted = rec && (!prevRecording_ || restarted);
    routedSeedPending_ = takeStarted && !slot; // routed notes held now join the take (captureRoutedNotes)
    // A retried take restarts from a playhead that jumped back: keys still held join it at its
    // start, not at a press beat from the take that was thrown away.
    routedSeedFromNow_ = restarted;
    if (rec != prevRecording_ || restarted)
        for (auto& L : rtMidiLanes_) L.clear();
    if (takeStarted) { // a new take: pass 0, the loop record mode as set now
        recordPass_ = 0;
        passTriggered_ = false;
        passReplace_ = loopRecordReplace_.load(std::memory_order_relaxed);
        replaceUpTo_.store(0, std::memory_order_release);
    }
    prevRecording_ = rec;
    // Every live event plays at offset 0 and the render sorts offs ahead of ons at one
    // offset, so a pitch's on and off landing in the same block (a quick tap, a finger
    // sliding over pads, a phone's notes arriving in one Wi-Fi burst) would play
    // off-then-on and hang the note. A pitch's second event — and anything past the
    // block's capacity — waits for the next block, with everything queued behind it.
    uint32_t seen[4] = {};
    MidiEvent e;
    for (;;) {
        if (liveCarried_) {
            e = liveCarry_;
            liveCarried_ = false;
        } else if (!liveMidi_.pop(e) && !remoteMidi_.pop(e))
            break;
        if (e.expr >= 0) { // expression: keep the latest per (track, pitch, dim), never carried
            int k = 0;
            while (k < liveExprCount_ &&
                   (liveExpr_[k].pitch != e.pitch || liveExpr_[k].expr != e.expr || liveExpr_[k].trackId != e.trackId ||
                       liveExpr_[k].source != e.source || liveExpr_[k].channel != e.channel)) // per input (specs/086)
                ++k;
            if (k < liveExprCount_) liveExpr_[k].velocity = e.velocity;
            else if (liveExprCount_ < kMaxLiveExpr)
                liveExpr_[liveExprCount_++] = e;
            continue;
        }
        if (e.expr == kLiveReleaseExpr) { // a track disarmed: release, the keys stay down
            if (liveReleaseCount_ >= kMaxLiveRelease) {
                liveCarry_ = e;
                liveCarried_ = true;
                break;
            }
            liveRelease_[liveReleaseCount_++] = e.trackId;
            continue;
        }
        const bool valid = e.pitch >= 0 && e.pitch < 128;
        const uint32_t bit = valid ? 1u << (e.pitch & 31) : 0u;
        if (liveCount_ >= kMaxLive || (valid && (seen[e.pitch >> 5] & bit))) {
            liveCarry_ = e;
            liveCarried_ = true;
            break;
        }
        if (valid) seen[e.pitch >> 5] |= bit;
        liveEvents_[liveCount_++] = e;
        if (valid) { // keys down, recording or not (seeds a take started while they're held)
            liveDown_[e.pitch] = e.on;
            if (e.on) {
                livePressBeat_[e.pitch] = timed ? nowBeat : std::numeric_limits<double>::quiet_NaN();
                livePressVel_[e.pitch] = e.velocity;
                livePressTrack_[e.pitch] = e.trackId;
                livePressSource_[e.pitch] = e.source;
                livePressChannel_[e.pitch] = e.channel;
            }
        }
        if (rec && timed && valid)
            for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
                RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(k)];
                // A note the lane holds ends on its off from the input that played it, even if the
                // lane's input changed since.
                if (!laneTakes(k, e) && (e.on || !L.pendingActive[e.pitch] || L.pendingSource[e.pitch] != e.source ||
                                            L.pendingChannel[e.pitch] != e.channel))
                    continue;
                if (e.on) {
                    if (!rtLaneLive_[k]) continue; // its slot hasn't started yet
                    L.pendingActive[e.pitch] = true;
                    L.pendingStart[e.pitch] = rtLaneNow_[k];
                    L.pendingVel[e.pitch] = e.velocity;
                    L.pendingSource[e.pitch] = e.source;
                    L.pendingChannel[e.pitch] = e.channel;
                    if (!rtLaneSlot_[k]) triggerPassReplace();
                } else if (L.pendingActive[e.pitch]) {
                    double len = rtLaneNow_[k] - L.pendingStart[e.pitch];
                    if (rtLaneSlot_[k] && len < 0.0) len += rtLaneSlotLen_[k]; // wrapped past the loop end
                    RecordedNote rn{e.pitch, L.pendingStart[e.pitch], len, L.pendingVel[e.pitch], recordPass_};
                    rn.lane = k;
                    pushRecorded(rn);
                    L.pendingActive[e.pitch] = false;
                }
            }
    }
    // Keys still down once this block's events are in join a take that starts here, from
    // where they started: their press beat if the transport was rolling (or counting in),
    // else the take's first moment — the count-in's first beat, or the record start. A slot
    // take starts them at the slot's current position. Seeding after the drain keeps out a
    // key whose release was already queued when Record was pressed.
    if (takeStarted)
        for (int p = 0; p < 128; ++p) {
            if (!liveDown_[p]) continue;
            MidiEvent held{true, p, livePressVel_[p], livePressTrack_[p]};
            held.source = livePressSource_[p];
            held.channel = livePressChannel_[p];
            for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
                RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(k)];
                if (L.pendingActive[p] || !rtLaneLive_[k] || !laneTakes(k, held)) continue;
                L.pendingActive[p] = true;
                L.pendingStart[p] =
                    (rtLaneSlot_[k] || restarted || std::isnan(livePressBeat_[p])) ? rtLaneNow_[k] : livePressBeat_[p];
                L.pendingVel[p] = livePressVel_[p];
                L.pendingSource[p] = held.source;
                L.pendingChannel[p] = held.channel;
            }
        }
    if (rec && timed && liveExprCount_ > 0) captureLiveExpr(); // on each lane's clock

    // Recorded controllers (specs/020): the block's coalesced instrument-wide controllers that
    // reach the take, on the capture clock. A take starts with every controller already away
    // from rest (a pedal held down when Record is pressed), at the record start.
    // Each lane from the inputs its track listens to: a pedal held on one keyboard doesn't seed
    // the take of a track that plays from another (specs/086).
    if (rec && takeStarted) {
        for (int32_t c = 0; c < kCtlCount; ++c) {
            if (ctlIsReserved(c) || liveCtlTouch_[c].load(std::memory_order_relaxed) == 0) continue; // never moved
            for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
                if (rtMidiLanes_[static_cast<size_t>(k)].trackId < 0 || !rtLaneLive_[k]) continue;
                const float v = laneStartCtl(k, c);
                if (v == kCtlRest) continue;
                RecordedCtl x{c, rtLaneSlot_[k] ? rtLaneNow_[k] : blockStartBeat, v, recordPass_};
                x.lane = k;
                pushRecordedCtl(x);
            }
        }
    }
    if (rec && timed)
        for (int i = 0; i < liveExprCount_; ++i) {
            const MidiEvent& e = liveExpr_[i];
            if (e.expr < kCtlDimBase) continue;
            for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
                if (!rtLaneLive_[k] || !laneTakes(k, e)) continue;
                RecordedCtl x{e.expr - kCtlDimBase, rtLaneNow_[k], e.velocity, recordPass_};
                x.lane = k;
                pushRecordedCtl(x);
            }
        }
}

// Does this block's lane `lane` record `e`? A track-addressed event only on its own track's
// lane; live input through the lane track's MIDI input filter (specs/086).
bool Engine::laneTakes(int32_t lane, const MidiEvent& e) const {
    const int32_t tid = rtMidiLanes_[static_cast<size_t>(lane)].trackId;
    if (tid < 0) return false;
    if (e.trackId >= 0) return e.trackId == tid;
    const int32_t s = rtLaneSrc_[lane], c = rtLaneCh_[lane];
    return (s == Track::kMidiAllInputs || (e.source >= 0 && s == e.source)) && (c == 0 || e.source < 0 || c == e.channel + 1);
}

float Engine::laneStartCtl(int32_t lane, int32_t ctl) const {
    const int32_t tid = rtMidiLanes_[static_cast<size_t>(lane)].trackId;
    const int32_t s = rtLaneSrc_[lane], c = rtLaneCh_[lane];
    uint32_t best = 0;
    float v = kCtlRest;
    auto take = [&](const LiveCtlIn& in) {
        const uint32_t st = in.stamp.load(std::memory_order_relaxed);
        if (st <= best) return;
        best = st;
        v = in.value.load(std::memory_order_relaxed);
    };
    if (liveCtlTrack_[ctl].channel.load(std::memory_order_relaxed) == tid) take(liveCtlTrack_[ctl]);
    for (int32_t i = 0; i < kCtlInputs; ++i) {
        const int32_t src = i - 1; // as laneTakes: no device passes the all-inputs filter only
        const LiveCtlIn& in = liveCtlIn_[i][ctl];
        if (in.stamp.load(std::memory_order_relaxed) <= best) continue;
        if (s != Track::kMidiAllInputs && (src < 0 || s != src)) continue;
        if (c != 0 && src >= 0 && c != in.channel.load(std::memory_order_relaxed) + 1) continue;
        take(in);
    }
    return v;
}

int32_t Engine::midiLaneOfRt(int32_t trackId) const {
    if (!recording_.load(std::memory_order_relaxed)) return -1;
    const int32_t n = std::clamp(midiLaneCount_.load(std::memory_order_acquire), 0, kMaxMidiTakeLanes);
    for (int32_t k = 0; k < n; ++k)
        if (midiLaneTrack_[static_cast<size_t>(k)].load(std::memory_order_relaxed) == trackId) return k;
    return -1;
}

// Recorded MPE: the block's (coalesced) live expression for a note being recorded becomes a
// point on that note's curve, timed from its start. Notes still held keep growing theirs; the
// message thread pairs them with the RecordedNote at note-off (poll). Audio thread.
void Engine::captureLiveExpr() {
    for (int i = 0; i < liveExprCount_; ++i) {
        const MidiEvent& e = liveExpr_[i];
        if (e.pitch < 0 || e.pitch > 127 || e.expr < 0 || e.expr >= kExprDims)
            continue; // instrument-wide: not per note
        for (int32_t k = 0; k < rtMidiLaneCount_; ++k) {
            const RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(k)];
            if (!L.pendingActive[e.pitch] || !laneTakes(k, e)) continue;
            double off = rtLaneNow_[k] - L.pendingStart[e.pitch];
            if (rtLaneSlot_[k] && rtLaneSlotLen_[k] > 0.0 && off < 0.0) off += rtLaneSlotLen_[k]; // wrapped past the loop end
            RecordedExpr x{e.pitch, L.pendingStart[e.pitch], e.expr, static_cast<float>(std::max(0.0, off)), e.velocity};
            x.lane = k;
            pushRecordedExpr(x);
        }
    }
}

// Print routed-in MIDI (a source's post-FX note events) into the record track's take, pairing
// note-on/off across blocks into RecordedNotes at absolute beats. Audio thread; recorded_ has a
// single producer (this + drainLiveMidi both run in processBlock).
// Keeps the routed notes sounding into an armed destination (on/off pairs from its source's
// post-FX MIDI), timed on the capture clock, for seeding a take started while they're held.
void Engine::trackRoutedDown(int32_t ownerId, const MidiEv* evs, int n, double blockStart, double spb, bool timed) {
    if (routedOwner_ != ownerId) {
        for (int p = 0; p < 128; ++p) routedDown_[p] = false;
        routedOwner_ = ownerId;
    }
    for (int k = 0; k < n; ++k) {
        const int p = evs[k].pitch;
        if (p < 0 || p >= 128) continue;
        routedDown_[p] = evs[k].on;
        if (evs[k].on) {
            routedPressBeat_[p] = timed && spb > 0.0 ? (blockStart + evs[k].off) / spb + countInClockOffset()
                                                     : std::numeric_limits<double>::quiet_NaN();
            routedPressVel_[p] = evs[k].vel;
        }
    }
}

void Engine::captureRoutedNotes(
    int32_t lane, const MidiEv* evs, int n, double blockStart, double spb, double clockOffset) {
    RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(lane)];
    for (int k = 0; k < n; ++k) {
        const int p = evs[k].pitch;
        if (p < 0 || p >= 128) continue;
        const double beat = (blockStart + evs[k].off) / spb + clockOffset; // count-in: before the take start
        if (evs[k].on) {
            L.recRoutedActive[p] = true;
            L.recRoutedStart[p] = beat;
            L.recRoutedVel[p] = evs[k].vel;
            if (!recordSlotPlayer_.load(std::memory_order_relaxed)) triggerPassReplace();
        } else if (L.recRoutedActive[p]) {
            RecordedNote rn{
                p, L.recRoutedStart[p], std::max(0.0, beat - L.recRoutedStart[p]), L.recRoutedVel[p], recordPass_};
            rn.lane = lane;
            pushRecorded(rn);
            L.recRoutedActive[p] = false;
        }
    }
    // A take that starts this block takes in the routed notes still sounding after its events:
    // from their press beat if the transport was rolling (or counting in), else from now.
    if (routedSeedPending_) {
        routedSeedPending_ = false;
        const double now = blockStart / spb + clockOffset;
        if (routedOwner_ == L.trackId)
            for (int p = 0; p < 128; ++p) {
                if (!routedDown_[p] || L.recRoutedActive[p]) continue;
                L.recRoutedActive[p] = true;
                L.recRoutedStart[p] = (routedSeedFromNow_ || std::isnan(routedPressBeat_[p])) ? now : routedPressBeat_[p];
                L.recRoutedVel[p] = routedPressVel_[p];
            }
    }
}

// Append a track's own note events (clips while playing + live input when armed/auditioned).
int Engine::gatherInstrumentNotes(
    Track& t, MidiEv* evs, int n, int32_t frames, double blockStart, double spb, bool playing, ExprEv* xev, int* xn) {
    if (playing) {
        // Controllers first: a clip ending here releases its pedal ahead of the next clip's
        // values at the same offset (the render keeps their order).
        if (xev)
            for (const MidiClip& clip : t.midiClips)
                if (clip.active)
                    clipCtlEndResets(clip, clip.startBeat * spb, (clip.startBeat + clip.lengthBeats) * spb, blockStart,
                        frames, xev, *xn, kMaxBlockExpr);
        for (const MidiClip& clip : t.midiClips) {
            // Deactivated clip (key 0): emit NO new note-ons, but still emit note-offs — so a
            // note already sounding when the clip was switched off releases instead of hanging.
            const bool clipActive = clip.active;
            const double clipStart = clip.startBeat * spb;
            const double clipEnd = clipStart + clip.lengthBeats * spb;
            const bool hasVel = !clip.velocityEnvelope.points.empty();
            // Track MIDI offset (ms → samples at the device rate). Playback-only: the shift
            // is applied to the scheduled times below, never to the stored notes.
            // A track without a map and without an offset (most tracks) pays nothing per note here:
            // the per-note articulation lookup is skipped and the shift stays a constant zero.
            const double msToS = transport_.sampleRate() / 1000.0;
            const double trackShiftS = t.midiOffsetMs() * msToS;
            const ArticulationTable* artTable = t.articulationTable.get();
            const auto gatherClip = [&]<bool shifted>() {
                for (const Note& note : clip.notes) {
                    // FR-018: track offset + the note's articulation offset, applied once (specs/085).
                    double shiftS = 0.0;
                    if constexpr (shifted)
                        shiftS = artTable ? trackShiftS + articulationOffsetMs(artTable, note.articulation) * msToS : trackShiftS;
                    // Clip length gates note starts (M4-2): notes outside don't play. A note that
                    // starts before the clip (a count-in pickup recorded into a take at 1.1, #56)
                    // sounds from the clip start for the part that reaches into it.
                    if (note.startBeat >= clip.lengthBeats || note.startBeat + note.lengthBeats <= 0.0) continue;
                    const double nominalOn = clipStart + std::max(0.0, note.startBeat) * spb;
                    // A trimmed clip cuts the tail: a note that runs past the clip's (possibly
                    // shortened) end stops at the boundary instead of sounding its full length.
                    double nominalOff = clipStart + (note.startBeat + note.lengthBeats) * spb;
                    if (nominalOff > clipEnd) nominalOff = clipEnd;
                    const double onS = shifted ? nominalOn + shiftS : nominalOn;
                    const double offS = shifted ? nominalOff + shiftS : nominalOff;
                    // Velocity envelope (M9 follow-up): scale by the curve at the note's
                    // clip-local start beat.
                    float vel = note.velocity;
                    if (hasVel) vel *= std::clamp(clip.velocityEnvelope.valueAt(note.startBeat), 0.0f, 1.0f);
                    if (clipActive && onS >= blockStart && onS < blockStart + frames && n < 1024)
                        evs[n++] = {static_cast<int32_t>(onS - blockStart), true, note.pitch, vel,
                            static_cast<float>((offS - onS) / spb)};
                    // Loop seam: a note still sounding at the loop end releases here, ahead of any
                    // chase re-trigger at the same offset (offs sort first).
                    if (seamOffs_ && onS < seamEndSamples_ && offS >= seamEndSamples_ && n < 1024)
                        evs[n++] = {0, false, note.pitch, 0.0f};
                    // Chase: a note already held at the jump point starts sounding right here.
                    if (chaseNotes_ && clipActive && onS < blockStart && offS > blockStart && n < 1024)
                        evs[n++] = {0, true, note.pitch, vel};
                    if (offS >= blockStart && offS < blockStart + frames && n < 1024)
                        evs[n++] = {static_cast<int32_t>(offS - blockStart), false, note.pitch, 0.0f};
                    // Recorded MPE: the note's curve while it sounds in this block.
                    if (xev && note.expr && clipActive)
                        noteExprEvents(note, (shifted ? clipStart + note.startBeat * spb + shiftS : clipStart + note.startBeat * spb) - blockStart,
                            offS - blockStart, frames, spb,
                            xev, *xn, kMaxBlockExpr); // curve timed from the true start
                }
            };
            if (!artTable && trackShiftS == 0.0) gatherClip.template operator()<false>();
            else gatherClip.template operator()<true>();
            // The clip's controllers (specs/020).
            if (xev && clipActive)
                clipCtlEvents(clip, clipStart, clipEnd, blockStart, frames, spb, chaseNotes_, xev, *xn, kMaxBlockExpr);
        }
    }
    // Notes a loop record replace removed from this track's take: release them (once), unless
    // the player is holding that key live.
    for (int k = 0; k < 16; ++k) {
        if (releaseTrack_[k] != t.id()) continue;
        for (int p = 0; p < 128 && n < 1024; ++p)
            if ((releasePitches_[k][p >> 5] >> (p & 31)) & 1u && !liveDown_[p]) evs[n++] = {0, false, p, 0.0f};
        for (auto& w : releasePitches_[k]) w = 0;
        releaseTrack_[k] = -1;
    }
    // Live notes reach a track when it's armed, or when it's the audition target
    // (so clicking a rack/drum pad plays it without arming/recording).
    // A track-addressed event (Nota Remote) plays only its own track.
    const bool takesUntargeted = t.armed() || t.id() == auditionTrackId_.load(std::memory_order_relaxed);
    return liveNotesFor(t, takesUntargeted, true, takesUntargeted, evs, n);
}

int Engine::liveNotesFor(const Track& t, bool untargeted, bool targeted, bool stillTakes, MidiEv* evs, int n) {
    Track::LiveHeld* held = t.liveHeld.get();
    for (int i = 0; i < liveCount_ && n < 1024; ++i) {
        const MidiEvent& le = liveEvents_[i];
        // A note it is holding still ends after its MIDI input changed (specs/086): the off
        // comes from the old input, which the new filter would turn away. Only that input's
        // off, though: the same key released on another keyboard isn't this note's.
        const bool heldOff = !le.on && held && le.pitch >= 0 && le.pitch < 128 &&
                             ((held->bits[le.pitch >> 6] >> (le.pitch & 63)) & 1u) &&
                             held->source[le.pitch] == le.source && held->channel[le.pitch] == le.channel;
        if (le.trackId >= 0 ? !targeted || le.trackId != t.id()
                            : !untargeted || (!passesMidiInput(t, le) && !heldOff))
            continue;
        evs[n++] = {0, le.on, le.pitch, le.velocity};
        if (!held || le.trackId >= 0 || le.pitch < 0 || le.pitch >= 128) continue;
        const uint64_t bit = 1ull << (le.pitch & 63);
        if (le.on) {
            held->bits[le.pitch >> 6] |= bit;
            held->source[le.pitch] = le.source;
            held->channel[le.pitch] = le.channel;
        } else {
            held->bits[le.pitch >> 6] &= ~bit;
        }
    }
    // Disarmed with keys down (#366): release the live notes it was given, unless it takes
    // live input again (re-armed, or the audition target).
    if (held && held->release) {
        held->release = false;
        if (!stillTakes) {
            for (int h = 0; h < 2; ++h) {
                uint64_t bits = held->bits[h];
                held->bits[h] = 0;
                while (bits && n < 1024) {
                    evs[n++] = {0, false, h * 64 + std::countr_zero(bits), 0.0f};
                    bits &= bits - 1;
                }
            }
        }
    }
    return n;
}

bool Engine::takesLiveEvent(const Track& t, const MidiEvent& e) const {
    if (e.trackId >= 0) return e.trackId == t.id();
    return (t.armed() && passesMidiInput(t, e)) || t.id() == auditionTrackId_.load(std::memory_order_relaxed);
}

// An untargeted live event reaches an armed track only through its MIDI input filter
// (specs/086); the audition target (a pad clicked in a device) takes everything.
bool Engine::passesMidiInput(const Track& t, const MidiEvent& e) const {
    return t.id() == auditionTrackId_.load(std::memory_order_relaxed) || t.acceptsMidiFrom(e.source, e.channel);
}

// Live expression bypasses the MIDI FX chain (it is keyed by the pitch the player holds) and
// reaches an instrument the way live notes do: through its own track, or through the track
// it takes its MIDI from.
void Engine::applyLiveExpression(Graph* g, const Track& t, Instrument& inst) const {
    const Track* src = nullptr;
    if (g && t.midiFromTrackId() >= 0)
        for (const auto& p : g->tracks)
            if (p && p.get() != &t && p->type() == TrackType::Instrument && p->id() == t.midiFromTrackId()) {
                src = p.get();
                break;
            }
    for (int i = 0; i < liveExprCount_; ++i) {
        const MidiEvent& e = liveExpr_[i];
        if (takesLiveEvent(t, e) || (src && takesLiveEvent(*src, e))) deliverExpr(inst, e.pitch, e.expr, e.velocity);
    }
}

// A track's own MIDI *output* for this block: its clips + live input, drum-rack swing, then
// its own MIDI FX chain. This is what feeds the track's instrument and, via routing, any
// destination it forwards to — so the FX (arp etc.) run exactly once per block.
int Engine::computeInstrumentMidi(Track& t, MidiEv* evs, int32_t frames, double blockStart, double spb, bool playing,
    bool arrangementActive, ExprEv* xev, int* xn) {
    // Arrangement clips gather only when the Arrangement is active; live input (armed/audition)
    // is independent, so a session-only jam still lets you play instruments live.
    if (xn) *xn = 0;
    int n = gatherInstrumentNotes(t, evs, 0, frames, blockStart, spb, playing && arrangementActive, xev, xn);
    std::sort(evs, evs + n, midiEvLess);

    // Drum Rack swing + humanize: nudge note-on sample offsets. Off-beat 1/16 hits
    // are pushed late by swing; humanize adds a small random jitter. The instrument
    // already receives these at sample accuracy (segmented render below), so shaping
    // the offsets here is all it takes. Off-events keep their time (drum one-shots
    // ignore note length). Delays that would spill past the block clamp to its end.
    if (auto* dr = dynamic_cast<DrumRack*>(t.instrument.get())) {
        const float sw = dr->swing(), hz = dr->humanize();
        if ((sw > 0.0f || hz > 0.0f) && spb > 0.0) {
            static uint32_t rng = 0x9E3779B9u;
            const double sixteenth = spb * 0.25; // samples per 1/16 note
            for (int k = 0; k < n; ++k) {
                if (!evs[k].on) continue;
                double delay = 0.0;
                if (sw > 0.0f) {
                    const long idx = std::lround((blockStart + evs[k].off) / sixteenth);
                    if (idx & 1L) delay += (double)sw * sixteenth * 0.6; // up to 60% of a 1/16
                }
                if (hz > 0.0f) {
                    rng = rng * 1664525u + 1013904223u;
                    const double j = ((rng >> 9) & 0xFFFF) / 65535.0; // 0..1
                    delay += j * (double)hz * sixteenth * 0.35;
                }
                evs[k].off = std::clamp(evs[k].off + (int32_t)std::llround(delay), 0, frames - 1);
            }
            std::sort(evs, evs + n, midiEvLess);
        }
    }

    // MIDI FX chain (arpeggiator etc.): transform the note events before the
    // instrument. Driven by the transport beat when playing, else the device's own
    // clock. The chain may consume/generate events, so re-sort afterwards.
    if (!t.midiEffects.empty()) {
        MidiEv scratch[1024];
        n = applyMidiEffects(t, evs, n, scratch, frames, spb > 0.0 ? blockStart / spb : 0.0, spb, playing);
        applyMidiCcRouting(t);
        std::sort(evs, evs + n, midiEvLess);
    } else if (t.instrument && t.instrument->asRack())
        applyMidiCcRouting(t); // Arps grouped into the Instrument Rack's chains (specs/044)
    t.updateMidiCv(evs, n); // MIDI→CV modulators read the post-FX note stream
    return n;
}

// MIDI pre-pass: compute each non-session instrument track's post-FX MIDI output into its
// per-position buffer (rs_->blockMidi). Runs once per block so each track's MIDI FX advance once;
// the render pass then feeds instruments from these buffers (own + routed sources').
void Engine::computeBlockMidi(
    Graph* g, int32_t frames, double blockStart, double spb, bool playing, bool arrangementActive) {
    const size_t nt = g ? g->tracks.size() : 0;
    // AR-6: never allocate here — publishRaw pre-sizes all four vectors (and
    // every inner buffer) for each graph swap before it goes live. The guard
    // below is unreachable in practice; it neutralizes counts so no stale
    // MIDI can replay instead of growing.
    const size_t have = std::min({rs_->blockMidi.size(), rs_->blockMidiN.size(), rs_->blockExpr.size(), rs_->blockExprN.size()});
    if (have < nt) {
        for (size_t i = 0; i < have; ++i) {
            rs_->blockMidiN[i] = 0;
            rs_->blockExprN[i] = 0;
        }
        return;
    }
    for (size_t i = 0; i < nt; ++i) {
        rs_->blockMidiN[i] = 0;
        rs_->blockExprN[i] = 0;
        Track& t = *g->tracks[i];
        // Session-active tracks feed via renderSessionSlotRaw (their own clock), so skip here.
        const bool sessionActive = t.sessionPlayer && t.sessionPlayer->playing.load(std::memory_order_relaxed) >= 0;
        if (t.type() != TrackType::Instrument || !t.instrument || sessionActive) continue;
        auto& buf = rs_->blockMidi[i];
        if (buf.size() < 1024) {
            rs_->blockMidiN[i] = 0;
            rs_->blockExprN[i] = 0;
            continue;
        } // unreachable: pre-sized
        auto& xbuf = rs_->blockExpr[i];
        if (xbuf.size() < static_cast<size_t>(kMaxBlockExpr)) {
            rs_->blockMidiN[i] = 0;
            rs_->blockExprN[i] = 0;
            continue;
        }
        rs_->blockMidiN[i] = computeInstrumentMidi(
            t, buf.data(), frames, blockStart, spb, playing, arrangementActive, xbuf.data(), &rs_->blockExprN[i]);
    }
}

// A device that takes a MIDI key (Nota Auto Shift's MIDI target) gets its source track's
// post-FX notes for this block, from the pre-pass (rs_->blockMidi). Audio thread.
void Engine::feedMidiKey(Graph* g, Device& d, int32_t srcTrackId) {
    if (!g) return;
    const size_t nt = std::min(g->tracks.size(), rs_->blockMidiN.size());
    for (size_t j = 0; j < nt; ++j)
        if (g->tracks[j] && g->tracks[j]->id() == srcTrackId) {
            d.setMidiKey(rs_->blockMidi[j].data(), rs_->blockMidiN[j]);
            return;
        }
}

// Renders the instrument's dry stereo (pre-fader) into `dst` (zeroed here) from this block's
// precomputed MIDI: the track's own post-FX output plus the post-FX output of any track routing
// its MIDI here (so a source's arp/MIDI-FX drive this instrument too). Fader/pan and the device
// chain are applied by the caller (processBlock).
void Engine::renderInstrumentRaw(
    Graph* g, Track& t, float* dst, int32_t frames, double blockStart, double spb, bool playing) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    Instrument* inst = t.instrument.get();
    if (!inst) return;
    inst->setTransport(spb > 0.0 ? blockStart / spb : 0.0, spb, playing); // generative synths (Pendulum)

    MidiEv evs[1024];
    int n = 0;
    ExprEv xev[kMaxBlockExpr];
    int xn = 0;
    if (g) {
        const int32_t srcId = t.midiFromTrackId(); // "MIDI In": the track we receive from (-1 = none)
        // Recording the destination prints the routed MIDI into its take (arm the destination).
        const int32_t recLane = srcId >= 0 ? midiLaneOfRt(t.id()) : -1; // its MIDI take lane (specs/086)
        const bool captureRouted = recLane >= 0 && (playing || countInActive_) && spb > 0.0;
        const size_t nt = std::min(g->tracks.size(), rs_->blockMidiN.size());
        for (size_t j = 0; j < nt; ++j) {
            Track* p = g->tracks[j].get();
            if (!p) continue;
            const bool self = (p == &t);
            // Destination reads its source's post-FX output. A muted source still forwards MIDI:
            // its own render is skipped, but its buffer was computed in the pre-pass.
            const bool routed = !self && srcId >= 0 && p->type() == TrackType::Instrument && p->id() == srcId;
            if (!self && !routed) continue;
            const int m = rs_->blockMidiN[j];
            const MidiEv* srcEvs = rs_->blockMidi[j].data();
            for (int k = 0; k < m && n < 1024; ++k) evs[n++] = srcEvs[k];
            if (j < rs_->blockExprN.size())
                for (int k = 0; k < rs_->blockExprN[j] && xn < kMaxBlockExpr; ++k) xev[xn++] = rs_->blockExpr[j][k];
            if (routed && t.armed()) trackRoutedDown(t.id(), srcEvs, m, blockStart, spb, playing || countInActive_);
            if (routed && captureRouted) {
                captureRoutedNotes(recLane, srcEvs, m, blockStart, spb, countInClockOffset());
                // The source's controllers (its clips' curves) print into the take too.
                if (j < rs_->blockExprN.size())
                    for (int k = 0; k < rs_->blockExprN[j]; ++k) {
                        const ExprEv& x = rs_->blockExpr[j][k];
                        if (x.dim < kCtlDimBase) continue;
                        RecordedCtl rc{x.dim - kCtlDimBase, (blockStart + x.off) / spb + countInClockOffset(),
                            x.value, recordPass_};
                        rc.lane = recLane; // this destination's clip, as its notes (specs/086)
                        pushRecordedCtl(rc);
                    }
            }
        }
    } else {
        n = computeInstrumentMidi(t, evs, frames, blockStart, spb, playing,
            arrangementActive_.load(std::memory_order_relaxed), xev, &xn); // fallback: no graph
    }

    std::sort(evs, evs + n, midiEvLess);
    std::stable_sort(xev, xev + xn, [](const ExprEv& a, const ExprEv& b) { return a.off < b.off; });

    // Articulation MIDI (specs/085): the track's own arrangement clips, past its MIDI FX, only to an
    // instrument that takes it. A stack array like the notes' (AR-6); overflow is dropped, never grown.
    ArtEv aev[kMaxBlockArt];
    int an = 0;
    if (t.articulationTable && playing && spb > 0.0 && arrangementActive_.load(std::memory_order_relaxed) &&
        inst->takesArticulations()) {
        ArticulationBlock ab;
        ab.frames = frames;
        ab.blockStart = blockStart;
        ab.spb = spb;
        ab.sampleRate = transport_.sampleRate();
        ab.trackOffsetMs = t.midiOffsetMs();
        ab.chase = chaseNotes_;
        ab.chaseStart = articulationChaseStart_;
        ab.seamOffs = seamOffs_;
        ab.seamEnd = seamEndSamples_;
        an = scheduleArticulations(t.midiClips, *t.articulationTable, ab, aev, kMaxBlockArt);
    }
    playEvents(*inst, dst, frames, evs, n, xev, xn, aev, an, spb > 0.0 ? blockStart / spb : 0.0, spb, playing, [&] {
        if (liveExprCount_ > 0) applyLiveExpression(g, t, *inst);
    });

    // MIDI clip volume envelope (M9 follow-up): scale the instrument output per
    // sample within each clip's time window (0..1 curve in clip-local beats).
    if (playing) {
        for (const MidiClip& clip : t.midiClips) {
            if (!clip.active || clip.volumeEnvelope.points.empty()) continue;
            const double clipStart = clip.startBeat * spb;
            const double clipEnd = clipStart + clip.lengthBeats * spb;
            for (int32_t i = 0; i < frames; ++i) {
                const double p = blockStart + i;
                if (p < clipStart || p >= clipEnd) continue;
                const float g = std::clamp(clip.volumeEnvelope.valueAt((p - clipStart) / spb), 0.0f, 1.0f);
                dst[i * 2] *= g;
                dst[i * 2 + 1] *= g;
            }
        }
    }
}

// Renders the playing Session slot's MIDI (looped) through the instrument into
// `dst` (zeroed here), on its own loop clock. M5-2.
void Engine::renderSessionSlotRaw(Track& t, float* dst, int32_t frames, double spb) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    Instrument* inst = t.instrument.get();
    auto& sp = *t.sessionPlayer;
    const int32_t slot = sp.playing.load(std::memory_order_relaxed);
    if (!inst || slot < 0 || slot >= static_cast<int32_t>(t.sessionSlots.size())) return;
    const SessionSlot& s = t.sessionSlots[slot];
    if (!s.hasClip) return;

    const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    const double p = sp.localBeats;
    const double db = frames / spb;

    MidiEv evs[1024];
    int n = 0;
    ExprEv xev[kMaxBlockExpr];
    int xn = 0;
    const long i0 = static_cast<long>(std::floor(p / L));
    const float velGain = sp.gain;
    // The track's MIDI offset and each note's articulation offset (specs/085, T049), as in the
    // arrangement: the notes stay where they are, they sound shifted. A later shift can bring a note
    // of the previous pass into this block, so that pass is read too — only when something is late,
    // so a slot with no offset plays exactly as before.
    const double msToS = transport_.sampleRate() / 1000.0;
    const ArticulationTable* artTable = t.articulationTable.get();
    const double trackMs = t.midiOffsetMs();
    for (const Note& note : s.midi.notes) {
        if (note.startBeat < 0.0 || note.startBeat >= L) continue;
        const double shiftS = artTable ? (trackMs + articulationOffsetMs(artTable, note.articulation)) * msToS : trackMs * msToS;
        const double shiftBeats = shiftS / spb;
        // This note's on, off, or expression can reach the block from any pass whose shifted
        // lifetime overlaps it. Derive both bounds per note: offsets and long tails can span
        // arbitrarily many short loops, and the block itself may cross several loops.
        const long itLo = std::max(0L, static_cast<long>(std::floor((p - shiftBeats - note.startBeat - note.lengthBeats) / L)));
        long itHi = static_cast<long>(std::floor((p + db - shiftBeats - note.startBeat) / L));
        if (!s.loop) itHi = std::min(itHi, 0L);
        for (long it = itLo; it <= itHi; ++it) {
            const double onAbs = it * L + note.startBeat;
            const double offAbs = onAbs + note.lengthBeats;
            // Unshifted: the block test in beats, as always — the next block starts at exactly p + db,
            // so a note on the boundary falls in one block, never both or neither. Shifted: in samples.
            const bool onIn = shiftS == 0.0 ? onAbs >= p && onAbs < p + db
                                            : (onAbs - p) * spb + shiftS >= 0.0 && (onAbs - p) * spb + shiftS < frames;
            const bool offIn = shiftS == 0.0 ? offAbs >= p && offAbs < p + db
                                             : (offAbs - p) * spb + shiftS >= 0.0 && (offAbs - p) * spb + shiftS < frames;
            if (onIn && n < 1024)
                evs[n++] = {static_cast<int32_t>((onAbs - p) * spb + shiftS), true, note.pitch, note.velocity * velGain,
                    static_cast<float>(note.lengthBeats)};
            if (offIn && n < 1024)
                evs[n++] = {static_cast<int32_t>((offAbs - p) * spb + shiftS), false, note.pitch, 0.0f};
            if (note.expr)
                noteExprEvents(note, (onAbs - p) * spb + shiftS, (offAbs - p) * spb + shiftS, frames, spb, xev, xn,
                    kMaxBlockExpr);
        }
    }
    // The slot clip's controllers, per loop iteration: a release at each iteration's end, then
    // its values from the start of the next (specs/020).
    if (!s.midi.controllers.empty())
        for (long it = std::max(0L, i0 - 1); it <= i0 + 1; ++it) {
            if (!s.loop && it > 0) break;
            const double cs = (it * L - p) * spb, ce = ((it + 1) * L - p) * spb;
            clipCtlEndResets(s.midi, cs, ce, 0.0, frames, xev, xn, kMaxBlockExpr);
        }
    if (!s.midi.controllers.empty())
        for (long it = std::max(0L, i0 - 1); it <= i0 + 1; ++it) {
            if (!s.loop && it > 0) break;
            const double cs = (it * L - p) * spb, ce = ((it + 1) * L - p) * spb;
            clipCtlEvents(s.midi, cs, ce, 0.0, frames, spb, false, xev, xn, kMaxBlockExpr);
        }
    std::stable_sort(xev, xev + xn, [](const ExprEv& a, const ExprEv& b) { return a.off < b.off; });

    // Live monitoring while overdub-recording into this slot (M5-4).
    bool monitoring = recordSlotPlayer_.load(std::memory_order_acquire) == &sp;
    if (!monitoring && recording_.load(std::memory_order_relaxed)) // a slot that joined the take (specs/086)
        if (const SessionLaneSlots* ls = laneSlotsHist_.live())
            for (SessionPlayer* lp : ls->player) monitoring = monitoring || lp == &sp;
    // A phone plays only its own track.
    n = liveNotesFor(t, monitoring, monitoring, monitoring && t.armed(), evs, n);

    std::sort(evs, evs + n, midiEvLess);

    // MIDI FX chain (arpeggiator etc.) on the slot's own loop clock.
    if (!t.midiEffects.empty()) {
        MidiEv scratch[1024];
        n = applyMidiEffects(t, evs, n, scratch, frames, p, spb, true);
        applyMidiCcRouting(t);
        std::sort(evs, evs + n, midiEvLess);
    } else if (t.instrument && t.instrument->asRack())
        applyMidiCcRouting(t); // Arps grouped into the Instrument Rack's chains (specs/044)

    // The monitored live expression, after the offset-0 notes (as in renderInstrumentRaw).
    // The slot's articulation MIDI (specs/085, T042), on the slot's own loop clock, past its MIDI FX.
    ArtEv aev[kMaxBlockArt];
    int an = 0;
    if (artTable && spb > 0.0 && inst->takesArticulations()) {
        ArticulationBlock ab;
        ab.frames = frames;
        ab.blockStart = p * spb;
        ab.spb = spb;
        ab.sampleRate = transport_.sampleRate();
        ab.trackOffsetMs = t.midiOffsetMs();
        // The first block after a launch (playedBeats resets at launch): what is in force where the
        // slot starts — its start, or the outgoing position for a legato launch — goes out, so a
        // switch whose lead falls before the launch isn't lost.
        ab.chase = sp.playedBeats.load(std::memory_order_relaxed) == 0.0;
        ab.releases = &sp.articulationReleases;
        ab.seamOffs = false;
        ab.seamEnd = 0.0;
        an = scheduleSlotArticulations(s.midi, L, s.loop, *artTable, ab, aev, kMaxBlockArt);
    } else if (sp.articulationReleases.count > 0 && inst->takesArticulations()) {
        an = scheduleArticulationReleases(sp.articulationReleases, frames, aev, kMaxBlockArt);
    }
    playEvents(*inst, dst, frames, evs, n, xev, xn, aev, an, p, spb, true, [&] { // the slot's own loop clock
        if (!monitoring) return;
        for (int i = 0; i < liveExprCount_; ++i)
            if ((liveExpr_[i].trackId < 0 && passesMidiInput(t, liveExpr_[i])) || liveExpr_[i].trackId == t.id())
                deliverExpr(*inst, liveExpr_[i].pitch, liveExpr_[i].expr, liveExpr_[i].velocity);
    });
    sp.localBeats = p + db;
}

// Renders an audio track's playing Session slot (pre-fader): the slot's clip — the same
// clip model as the arrangement (region, warp, pitch, reverse, envelopes) — looped over the
// slot's length on the slot's own clock (S-06). A block that crosses the loop end renders
// in two pieces; a one-shot slot stops at the end.
void Engine::renderSessionAudioSlotRaw(Track& t, float* dst, int32_t frames, double spb) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    auto& sp = *t.sessionPlayer;
    const int32_t slot = sp.playing.load(std::memory_order_relaxed);
    if (slot < 0 || slot >= static_cast<int32_t>(t.sessionSlots.size()) || spb <= 0.0) return;
    const SessionSlot& s = t.sessionSlots[slot];
    if (!s.hasClip || !s.audio.sample) return;

    const double L = s.lengthBeats > 0 ? s.lengthBeats : 4.0;
    double p = sp.localBeats;
    int32_t done = 0;
    while (done < frames) {
        if (!s.loop && p >= L) break;
        double local = std::fmod(p, L);
        if (local < 0.0) local += L;
        const double toEnd = (L - local) * spb;
        int32_t seg = static_cast<int32_t>(std::min<double>(frames - done, std::ceil(toEnd - 1e-9)));
        if (seg <= 0) seg = 1;
        mixAudioClip(s.audio, dst + done * 2, seg, local * spb, spb, sp.gain);
        done += seg;
        p += seg / spb;
    }
    sp.localBeats += frames / spb;
}

// Renders a track's dry audio clips (pre-fader) into `dst` (zeroed here). Also drives
// Consolidate's offline bounce (consolidateRange) over a detached clip list.
void Engine::renderAudioClipsRaw(
    const std::vector<AudioClip>& clips, float* dst, int32_t frames, double blockStart, double spb) {
    for (int32_t i = 0; i < frames * 2; ++i) dst[i] = 0.0f;
    for (const AudioClip& clip : clips) mixAudioClip(clip, dst, frames, blockStart, spb, 1.0f);
}

// One clip of renderAudioClipsRaw — shared with Session audio slots, which loop a clip on
// their own clock (renderSessionAudioSlotRaw).
void Engine::mixAudioClip(
    const AudioClip& clip, float* dst, int32_t frames, double blockStart, double spb, float extraGain) {
    if (!clip.active) return; // deactivated clip (key 0): stays on the timeline but silent
    const double sr = transport_.sampleRate();
    const double startSamples = clip.startBeat * spb;
    // Clip envelopes (M9 follow-up): 0..1 volume + -1..1 pan curves in clip-local
    // beats, applied per sample. Empty = fast path (no cost). Pan uses a balance
    // law (unity at centre) so it composes with the track's own pan stage.
    const bool hasVol = !clip.volumeEnvelope.points.empty();
    const bool hasPan = !clip.panEnvelope.points.empty();
    const bool hasAdsr = !clip.adsr.isIdentity();
    const bool hasEnv = hasVol || hasPan || hasAdsr;
    double lenBeats = 0.0; // played length (beats) for the ADSR release — set per path below
    auto applyEnv = [&](double p, float& l, float& r) {
        const double b = (p - startSamples) / spb;
        if (hasAdsr) {
            const float g = clip.adsr.gainAt(b, lenBeats);
            l *= g;
            r *= g;
        }
        if (hasVol) {
            const float g = std::clamp(clip.volumeEnvelope.valueAt(b), 0.0f, 1.0f);
            l *= g;
            r *= g;
        }
        if (hasPan) {
            const float pan = std::clamp(clip.panEnvelope.valueAt(b), -1.0f, 1.0f);
            if (pan > 0.0f) l *= (1.0f - pan);
            else if (pan < 0.0f)
                r *= (1.0f + pan);
        }
    };

    // Warped clips: copy straight from the offline stretch cache.
    // The cache holds the played window [warpPlayStart, warpPlayEndEff] pre-rendered
    // at the device rate, so there is no realtime stretch here — just a memcpy-cheap
    // read (no per-clip-start priming spike). The clip's on-timeline length is the
    // cache's frame count (both are warpPlayLen×spb once rebuilt for the tempo).
    if (clip.warpEnabled && clip.warpCache && clip.warpCache->frames > 0) {
        const WarpCache& wc = *clip.warpCache;
        // A stale cache (an edit's rebuild still running in the background) keeps playing,
        // clipped to the clip's new played length so it never spills past the clip's end.
        // Both edges sit on whole frames rounded from the timeline (not start + a rounded
        // length), so clips that meet — the halves of a split — leave no frame between them.
        const int64_t startF = std::llround(startSamples);
        const int64_t endF =
            std::min<int64_t>(startF + wc.frames, std::llround(startSamples + clip.warpPlayLen() * spb));
        const double clipDeviceLen = static_cast<double>(endF - startF);
        lenBeats = clipDeviceLen / spb;
        const double ovStart = std::max(blockStart, static_cast<double>(startF));
        const double ovEnd = std::min(blockStart + frames, static_cast<double>(endF));
        if (ovEnd <= ovStart) return;
        const int32_t dstOff = static_cast<int32_t>(std::llround(ovStart - blockStart));
        const int32_t segLen = static_cast<int32_t>(std::llround(ovEnd - ovStart));
        if (segLen <= 0 || dstOff < 0 || dstOff + segLen > frames) return;
        const int64_t cacheBase = std::llround(ovStart) - startF; // 0-based into the cache
        const float* csamp = wc.data();
        const float g = clip.gain * extraGain;
        // A split fade is a few milliseconds of attack or release with the sustain left at
        // full level, so the body is unity. It is its own loop, with no envelope test and no
        // helper call: folding the test into the sample loop cost audio-warp-split +11%, and
        // a body loop that reloaded its pointers cost another +3.6%. The extra sample on
        // each edge covers a rounding step; a body sample it pulls in has sustain gain 1.
        // Every other clip takes the original loop below, unchanged.
        if (hasAdsr && clip.adsr.sustain >= 1.0f && !hasVol && !hasPan) {
            int32_t head = 0, tail = segLen;
            if (clip.adsr.attack > 0.0) {
                const double n = startSamples + clip.adsr.attack * spb + 1.0 - ovStart; // j < n
                // Clamp in double before the narrowing cast. The span only covers this
                // block; a long attack must not overflow int32 on the way there.
                const double c = std::clamp(std::ceil(n), 0.0, static_cast<double>(segLen));
                head = static_cast<int32_t>(c);
            }
            if (clip.adsr.release > 0.0) {
                const double n = startSamples + clipDeviceLen - clip.adsr.release * spb - 1.0 - ovStart; // j > n
                const double c = std::clamp(std::floor(n) + 1.0, 0.0, static_cast<double>(segLen));
                tail = static_cast<int32_t>(c);
            }
            if (head > tail) head = tail = segLen; // edges overlap: the whole block fades
            for (int32_t j = 0; j < head; ++j) {
                const int64_t ci = clip.reversed ? wc.frames - 1 - (cacheBase + j) : cacheBase + j;
                if (ci < 0 || ci >= wc.frames) continue;
                float l = csamp[ci * 2], r = csamp[ci * 2 + 1];
                applyEnv(ovStart + j, l, r);
                dst[(dstOff + j) * 2] += l * g;
                dst[(dstOff + j) * 2 + 1] += r * g;
            }
            for (int32_t j = head; j < tail; ++j) {
                const int64_t ci = clip.reversed ? wc.frames - 1 - (cacheBase + j) : cacheBase + j;
                if (ci < 0 || ci >= wc.frames) continue;
                const float l = csamp[ci * 2], r = csamp[ci * 2 + 1];
                dst[(dstOff + j) * 2] += l * g;
                dst[(dstOff + j) * 2 + 1] += r * g;
            }
            for (int32_t j = tail; j < segLen; ++j) {
                const int64_t ci = clip.reversed ? wc.frames - 1 - (cacheBase + j) : cacheBase + j;
                if (ci < 0 || ci >= wc.frames) continue;
                float l = csamp[ci * 2], r = csamp[ci * 2 + 1];
                applyEnv(ovStart + j, l, r);
                dst[(dstOff + j) * 2] += l * g;
                dst[(dstOff + j) * 2 + 1] += r * g;
            }
        } else {
            for (int32_t j = 0; j < segLen; ++j) {
                // Reverse reads the same cache back-to-front: the window is already the
                // played length, so mirroring the index is exact (no resample).
                const int64_t ci = clip.reversed ? wc.frames - 1 - (cacheBase + j) : cacheBase + j;
                if (ci < 0 || ci >= wc.frames) continue;
                float l = csamp[ci * 2], r = csamp[ci * 2 + 1];
                if (hasEnv) applyEnv(ovStart + j, l, r);
                dst[(dstOff + j) * 2] += l * g;
                dst[(dstOff + j) * 2 + 1] += r * g;
            }
        }
        return;
    }
    // Warp enabled but the cache isn't ready yet (a rebuild is pending after an
    // edit/tempo change): stay silent rather than fall through and play the clip
    // unwarped (wrong length/pitch) for the brief window until the cache publishes.
    if (clip.warpEnabled) return;

    // Unwarped clips resample at their natural rate with varispeed transpose
    // folded into the read ratio.
    if (!clip.sample) return;
    const SampleBuffer& sb = *clip.sample;
    const double ratio = sb.sourceSampleRate / sr * clip.pitchRatio();
    const int64_t len = clip.effectiveLength();
    const double clipDeviceLen = len / ratio;
    lenBeats = clipDeviceLen / spb;
    for (int32_t i = 0; i < frames; ++i) {
        const double p = blockStart + i;
        if (p < startSamples || p >= startSamples + clipDeviceLen) continue;
        // Reverse walks the source region from its last frame back to its first;
        // the interpolation is unchanged (srcPos is still a real source position).
        double srcPos = clip.reversed ? clip.sourceOffsetFrames + (len - 1) - (p - startSamples) * ratio
                                      : clip.sourceOffsetFrames + (p - startSamples) * ratio;
        if (srcPos < clip.sourceOffsetFrames) srcPos = clip.sourceOffsetFrames;
        const int64_t i0 = static_cast<int64_t>(srcPos);
        const double frac = srcPos - i0;
        float l0, r0, l1, r1;
        sb.readStereo(i0, l0, r0);
        sb.readStereo(i0 + 1, l1, r1);
        float l = static_cast<float>(l0 + (l1 - l0) * frac) * clip.gain * extraGain;
        float r = static_cast<float>(r0 + (r1 - r0) * frac) * clip.gain * extraGain;
        // Short edge fade (~3 ms) so a clip that starts/ends mid-waveform doesn't click.
        const float eg = static_cast<float>(
            audioClipEdgeGain(p - startSamples, clipDeviceLen, sr, clip.fadeHead, clip.fadeTail));
        l *= eg;
        r *= eg;
        applyEnv(p, l, r);
        dst[i * 2] += l;
        dst[i * 2 + 1] += r;
    }
}

// Freeze playback (M7): fill `dst` from the track's frozen buffer for this segment.
// The buffer is indexed by beat (frame = beat·frozenSpb), so it resolves correctly at
// any render sample rate (an export bounce at a different rate resamples for free).
void Engine::fillFrozen(Track& t, float* dst, int32_t frames, double blockStart, double spb) {
    const auto bufPtr = t.frozenBuf; // copy the shared_ptr (keeps it alive)
    if (!bufPtr || spb <= 0.0 || t.frozenSpb <= 0.0) {
        std::fill_n(dst, frames * 2, 0.0f);
        return;
    }
    const std::vector<float>& buf = *bufPtr;
    const int64_t n = static_cast<int64_t>(buf.size() / 2);
    for (int32_t i = 0; i < frames; ++i) {
        const double beat = (blockStart + i) / spb;
        const double srcF = beat * t.frozenSpb;
        const int64_t i0 = static_cast<int64_t>(srcF);
        if (srcF < 0.0 || i0 >= n) {
            dst[i * 2] = 0.0f;
            dst[i * 2 + 1] = 0.0f;
            continue;
        }
        const int64_t i1 = (i0 + 1 < n) ? i0 + 1 : i0;
        const float fr = static_cast<float>(srcF - i0);
        dst[i * 2] = buf[i0 * 2] + (buf[i1 * 2] - buf[i0 * 2]) * fr;
        dst[i * 2 + 1] = buf[i0 * 2 + 1] + (buf[i1 * 2 + 1] - buf[i0 * 2 + 1]) * fr;
    }
}

// Is this track currently driven by a Session slot (which owns its own loop
// clock)? Such tracks are exempt from the transport-loop / stop voice flush.
static inline bool sessionActiveOf(const Track& t) {
    return t.sessionPlayer && t.sessionPlayer->playing.load(std::memory_order_relaxed) >= 0;
}

// The transport loop just wrapped (audio thread, #29). MIDI FX keep their pattern and pending
// offs across the seam instead of being reset. A take recording into the Arrangement splits each
// note held across the seam: the part played so far ends at the loop end, and the note carries
// on from the loop start (a key pressed just before the seam — a head under 1/64 beat — moves
// to the loop start whole). A Session slot take times notes on its own loop clock: untouched.
void Engine::onLoopWrap(Graph* g, double loopStartBeat, double loopEndBeat) {
    const double jump = loopStartBeat - loopEndBeat;
    if (g)
        for (auto& tptr : g->tracks)
            if (tptr && tptr->instrument && !sessionActiveOf(*tptr)) {
                for (auto& md : tptr->midiEffects)
                    if (md) md->timeJump(jump);
                tptr->instrument->midiTimeJump(jump); // MIDI effects in a rack's chains (#329)
            }

    if (!recording_.load(std::memory_order_relaxed) || recordSlotPlayer_.load(std::memory_order_acquire)) return;
    constexpr double kMinHead = 1.0 / 64.0;
    auto split = [&](int32_t lane, int32_t pitch, double& start, float vel) {
        const double head = loopEndBeat - start;
        RecordedNote rn{pitch, start, head, vel, recordPass_};
        rn.lane = lane;
        if (head >= kMinHead) pushRecorded(rn);
        start = loopStartBeat;
    };
    for (int32_t k = 0; k < rtMidiLaneCount_; ++k) { // every lane of the take (specs/086)
        RtMidiLane& L = rtMidiLanes_[static_cast<size_t>(k)];
        for (int p = 0; p < 128; ++p)
            if (L.pendingActive[p]) split(k, p, L.pendingStart[p], L.pendingVel[p]);
        for (int p = 0; p < 128; ++p)
            if (L.recRoutedActive[p]) split(k, p, L.recRoutedStart[p], L.recRoutedVel[p]);
        // Controllers being replaced over a range stop at the pass end (specs/020).
        RecordedCtl x{-1, loopEndBeat, 0.0f, recordPass_};
        x.lane = k;
        pushRecordedCtl(x);
    }
    // A new pass of the take: the continuations above belong to it (they don't trigger a
    // replace — only a fresh note does), and it takes the loop record mode set right now.
    ++recordPass_;
    passTriggered_ = false;
    passReplace_ = loopRecordReplace_.load(std::memory_order_relaxed);
}

// Replace mode: the first fresh note of a pass after the first removes the take's earlier
// passes. Published once per pass; collectRecorded reads it before draining recorded_, so every
// earlier-pass note queued before it is in hand.
void Engine::triggerPassReplace() {
    if (!passReplace_ || passTriggered_ || recordPass_ <= 0) return;
    passTriggered_ = true;
    replaceUpTo_.store(recordPass_, std::memory_order_release);
}

void Engine::processBlock(float* out, int32_t numFrames) {
    // Every audio-thread read of recordSlotPlayer_ happens inside this block (count-in launch,
    // live MIDI, capture), so this scope keeps the player it points at alive.
    const SnapshotHistory<SessionPlayer>::RenderScope recordSlotScope(recordSlotHist_);
    const SnapshotHistory<SessionLaneSlots>::RenderScope laneSlotScope(laneSlotsHist_); // specs/086
    drainCommands();
    for (int32_t i = 0; i < numFrames * 2; ++i) out[i] = 0.0f;

    const double sr = transport_.sampleRate();
    const double spb = transport_.samplesPerBeat();
    bool playing = transport_.isPlaying(); // flips to true mid-block when a count-in runs out

    drainLiveMidi(transport_.playheadSamples() / spb, playing);

    // The graph, then this block's scratch: publishRaw publishes the scratch first, so it
    // always fits the graph seen here. Both held until their scopes end the render epoch.
    const SnapshotHistory<Graph>::RenderScope graphScope(graphHist_);
    Graph* g = graphHist_.live();
    const SnapshotHistory<RenderScratch>::RenderScope scratchScope(scratchHist_);
    rs_ = scratchHist_.live();

    auto flushArrangementVoices = [&] {
        if (g)
            for (auto& tptr : g->tracks)
                if (tptr->instrument && !sessionActiveOf(*tptr)) {
                    tptr->instrument->allNotesOff();
                    releaseHeldControllers(*tptr->instrument); // a clip's pedal / wheel (specs/020)
                    for (auto& md : tptr->midiEffects)
                        if (md) md->reset(); // flush arp held/pending
                }
    };
    // Flush stuck instrument voices on a play→stop edge (pause/stop), or on a seek while
    // rolling: otherwise a held note keeps ringing forever since its note-off never arrives.
    const bool seeked = seekedThisBlock_, seekedRolling = seekedWhileRolling_;
    seekedThisBlock_ = seekedWhileRolling_ = false;
    if ((!playing && renderWasPlaying_) || (playing && renderWasPlaying_ && seekedRolling)) flushArrangementVoices();
    // A device moved between tracks (specs/043): the moved instrument's held notes, or the
    // voices an arp drove on the track it left, stop here, on the thread that owns them.
    if (g)
        for (int32_t k = 0; k < notesOffCount_; ++k)
            for (auto& tptr : g->tracks)
                if (tptr->id() == notesOffTracks_[k]) {
                    if (tptr->instrument) {
                        tptr->instrument->allNotesOff();
                        releaseHeldControllers(*tptr->instrument);
                    }
                    for (auto& md : tptr->midiEffects)
                        if (md) md->reset();
                }
    notesOffCount_ = 0;
    // Tracks disarmed with keys down (#366): they release on this render.
    if (g)
        for (int k = 0; k < liveReleaseCount_; ++k)
            for (auto& tptr : g->tracks)
                if (tptr->id() == liveRelease_[k] && tptr->liveHeld) tptr->liveHeld->release = true;
    // Note chase: when playback starts, the playhead jumps (seek) or the loop wraps, clip
    // notes that began before the playhead but are still held get re-triggered at the
    // jump point — otherwise a note you start in the middle of stays silent.
    bool chase = playing && (!renderWasPlaying_ || seeked);
    renderWasPlaying_ = playing;

    // Split the block at loop boundaries so the loop is sample-accurate: nothing
    // past the loop end sounds, and the playhead lands exactly on loopStart so a
    // note at the loop start (beat 0) retriggers every cycle.
    int32_t done = 0;
    int32_t loopSeams[16];
    int32_t nLoopSeams = 0; // block offsets where the loop wrapped
    double presentationStart = transport_.playheadSamples();
    int32_t presentationFrames = 0, presentationLead = 0;
    while (done < numFrames) {
        if (playing && transport_.isLooping() && transport_.playheadSamples() >= transport_.loopEndSamples() - 0.5) {
            // No voice flush here: clip notes crossing the loop end get note-offs in the next
            // segment (seamOffs_), so live-held keys keep sounding and tails ring out (#29).
            seamEndSamples_ = transport_.playheadSamples();
            transport_.wrapToLoopStart();
            if (spb > 0.0) onLoopWrap(g, transport_.loopStartSamples() / spb, seamEndSamples_ / spb);
            seamOffs_ = true;
            chase = true;
            if (nLoopSeams < 16) loopSeams[nLoopSeams++] = done; // declick this seam below
        }
        int32_t seg = std::min(numFrames - done, kMaxBlock);
        if (playing && transport_.isLooping()) {
            const double toEnd = transport_.loopEndSamples() - transport_.playheadSamples();
            if (toEnd > 0.5 && toEnd < seg) seg = std::max(1, static_cast<int32_t>(std::lround(toEnd)));
        }
        // End the segment where the count-in runs out, so the transport rolls on that sample.
        if (countInActive_ && spb > 0.0) {
            const double left = (countInTotal_ - countInPos_) * spb;
            if (left < seg) seg = std::max(1, static_cast<int32_t>(std::lround(left)));
        }
        const double segStart = transport_.playheadSamples();
        if (playing) {
            if (presentationFrames == 0) { presentationStart = segStart; presentationLead = done; }
            presentationFrames += seg;
        }
        chaseNotes_ = chase;
        if (chase) articulationChaseStart_ = segStart;
        if (g && sr > 0.0) mixGraph(g, out + done * 2, seg, segStart, playing, spb);
        chaseNotes_ = chase = seamOffs_ = false;
        bool countInDone = false;
        if (playing) renderMetronome(out + done * 2, seg, segStart);
        else if (countInActive_)
            countInDone = renderCountIn(out + done * 2, seg, spb);
        transport_.advanceBy(seg);
        done += seg;
        if (countInDone) {
            endCountIn();
            transport_.play();
            launchCountInSlot();
            // Let the metronome click the beat we launch on (not when starting mid-beat).
            const double b = transport_.playheadSamples() / spb;
            const int64_t fb = static_cast<int64_t>(std::floor(b));
            lastBeatEmitted_ = (b - fb < 1e-6) ? fb - 1 : fb;
            playing = chase = renderWasPlaying_ = true;
        }
    }

    // Declick each loop seam: the playhead jumps and sounding voices are cut at the
    // wrap, so the mix has a hard discontinuity there. Fade the mix down over the last
    // ~1.5 ms before the seam and up over the first ~1.5 ms after it — a tiny notch that
    // removes the click for instruments and audio alike, every cycle.
    if (nLoopSeams > 0) {
        const int32_t kD = std::max(8, std::min(96, static_cast<int32_t>(sr * 0.0015)));
        for (int32_t si = 0; si < nLoopSeams; ++si) {
            const int32_t s = loopSeams[si];
            const int32_t kb = std::min(kD, s), ka = std::min(kD, numFrames - s);
            for (int32_t m = 0; m < kb; ++m) { // fade out into the seam
                const double gN = 0.5 - 0.5 * std::cos(kTwoPi * 0.5 * (double)(kb - m) / (kb + 1));
                out[(s - kb + m) * 2] *= (float)gN;
                out[(s - kb + m) * 2 + 1] *= (float)gN;
            }
            for (int32_t m = 0; m < ka; ++m) { // fade in out of the seam
                const double gN = 0.5 - 0.5 * std::cos(kTwoPi * 0.5 * (double)(m + 1) / (ka + 1));
                out[(s + m) * 2] *= (float)gN;
                out[(s + m) * 2 + 1] *= (float)gN;
            }
        }
    }

    publishHeldSnapshot(captureNow_); // live + routed capture for this block done (#56)

    renderTone(out, numFrames);
    renderPreview(out, numFrames); // audition voice (M7-4a), pre-master

    // Master effect chain (post-mix, pre-fader): run the whole mix through the master
    // track's devices, in kMaxBlock chunks so each device sees at most a full block.
    if (g && g->masterTrack) {
        // The master runs after the transport advanced past this block; recover the
        // block's start beat so tempo-synced master devices line up.
        const double masterStartBeat = spb > 0.0 ? transport_.positionBeats() - numFrames / spb : 0.0;
        for (auto& d : g->masterTrack->devices) {
            if (!d || d->bypassed()) continue;
            for (int32_t off = 0; off < numFrames; off += kMaxBlock) {
                const int32_t n = std::min(numFrames - off, kMaxBlock);
                if (spb > 0.0) d->setTransport(masterStartBeat + off / spb, spb, playing);
                d->process(out + off * 2, n);
            }
        }
    }

    const float mv = masterVolume_.load(std::memory_order_relaxed);
    float mpkL = 0.0f, mpkR = 0.0f;
    double msqL = 0.0, msqR = 0.0, msLR = 0.0; // msLR: Stereo Map (specs/008)
    for (int32_t i = 0; i < numFrames; ++i) {
        // Master safety net: a NaN slips straight through std::clamp (every compare
        // with NaN is false, so clamp returns it unchanged) and hits the speakers as
        // full-scale noise. Squash any non-finite sample to silence *before* clamping
        // so a single bad value from a device/feedback path can't blast the output.
        float l = out[i * 2] * mv;
        float r = out[i * 2 + 1] * mv;
        if (!std::isfinite(l)) l = 0.0f;
        if (!std::isfinite(r)) r = 0.0f;
        l = std::clamp(l, -1.0f, 1.0f);
        r = std::clamp(r, -1.0f, 1.0f);
        out[i * 2] = l;
        out[i * 2 + 1] = r;
        const float al = std::fabs(l), ar = std::fabs(r);
        if (al > mpkL) mpkL = al;
        if (ar > mpkR) mpkR = ar;
        msqL += l * (double)l;
        msqR += r * (double)r;
        msLR += l * (double)r;
    }
    stereoFold(masterStereoL_, masterStereoR_, masterStereoLR_, msqL, msqR, msLR, numFrames,
        stereoBlockStep(numFrames, transport_.sampleRate()));
    masterPeakL_.store(mpkL, std::memory_order_relaxed);
    masterPeakR_.store(mpkR, std::memory_order_relaxed);
    transport_.publishPresentation(presentationStart, presentationFrames, presentationLead,
        deviceBlock_ && backend_ ? std::max(numFrames, backend_->bufferFrames()) : numFrames);
    masterRmsL_.store(numFrames ? static_cast<float>(std::sqrt(msqL / numFrames)) : 0.0f, std::memory_order_relaxed);
    masterRmsR_.store(numFrames ? static_cast<float>(std::sqrt(msqR / numFrames)) : 0.0f, std::memory_order_relaxed);
}

// Fill `dst` with what a monitoring audio track hears: the hardware input pulled for this
// segment, or its source track's previous-block post-fader route tap. Guards against the
// obvious feedback loops (itself, master, or a group it sits inside) by staying silent.
void Engine::renderMonitorInput(Graph* g, const Track& t, float* dst, int32_t frames) {
    const int32_t src = t.recordInputSource();
    if (src == 0) {
        // The track's own column(s) of this segment's capture (specs/086); a mono pick sounds
        // in the middle (#340), a channel the layout doesn't carry is silent.
        const size_t w = static_cast<size_t>(std::max(layoutAudio_.count, 0));
        const int32_t cL = layoutAudio_.column(t.inputFirst());
        const int32_t cR = t.inputWidth() == 2 ? layoutAudio_.column(t.inputFirst() + 1) : cL;
        const float* in = monitorHwBuf_.data();
        for (int32_t i = 0; i < frames; ++i) {
            const size_t row = static_cast<size_t>(i) * w;
            dst[i * 2] = cL >= 0 ? in[row + static_cast<size_t>(cL)] : 0.0f;
            dst[i * 2 + 1] = cR >= 0 ? in[row + static_cast<size_t>(cR)] : 0.0f;
        }
        return;
    }
    std::fill_n(dst, frames * 2, 0.0f);
    if (src < 0 || src == t.id()) return;
    for (int32_t a = t.groupId(), hops = 0; a >= 0 && hops < 64; ++hops) { // ancestor group?
        if (a == src) return;
        int32_t parent = -1;
        for (auto& o : g->tracks)
            if (o->id() == a) {
                parent = o->groupId();
                break;
            }
        a = parent;
    }
    const int32_t slot = routeSlotForTrack(src);
    if (slot >= 0) std::copy_n(routeBus_[slot].data(), frames * 2, dst);
}

// Per-leaf DSP for Pass 1 : render the track's dry stereo (pre-fader,
// pre-device) into dst. Per-track state only (plus read-only shared) —
// bit-identical in pool tasks and inline. Session clocks (shared followRng_)
// advance in the serial pre-pass, never here.
void Engine::renderLeafDry(Track& t, float* dst, const LeafWork& w, Graph* g, int32_t frames, double blockStart,
    double spb, bool playing, const TransportInfo& ti, bool arrangementActive) {
    // 1) Render the track's dry stereo (pre-fader).
    switch (w.path) {
    case LeafPath::Frozen: fillFrozen(t, dst, frames, blockStart, spb); break;
    case LeafPath::Monitor: renderMonitorInput(g, t, dst, frames); break;
    case LeafPath::SessionInst:
        feedInstrumentKeys(g, t, frames); // a Session clip plays the same instrument: its keys too
        feedPartTaps(t); // a Session clip plays the same pads: their keys too (specs/050)
        renderSessionSlotRaw(t, dst, frames, spb);
        if (t.instrument) t.instrument->setPartTaps(nullptr, 0);
        sessionPostRender(t, frames / spb);
        break;
    case LeafPath::SessionAudio:
        renderSessionAudioSlotRaw(t, dst, frames, spb);
        sessionPostRender(t, frames / spb);
        break;
    case LeafPath::Instrument:
        if (t.instrument) {
            t.instrument->setTransportInfo(ti); // hosted-plugin sync
            feedInstrumentKeys(g, t, frames);
        }
        feedPartTaps(t);
        renderInstrumentRaw(g, t, dst, frames, blockStart, spb, playing);
        if (t.instrument) t.instrument->setPartTaps(nullptr, 0);
        break;
    case LeafPath::AudioClips: renderAudioClipsRaw(t.clips, dst, frames, blockStart, spb); break;
    case LeafPath::Tail: std::fill_n(dst, frames * 2, 0.0f); break; // the chain alone rings on
    case LeafPath::Skip: break; // unreachable (pre-pass filters Skip out)
    }
}

// Parts of a track's instrument that keys listen to (specs/050): hand it where each one's Pre /
// Post goes this block (it copies the table; a block may render in pieces). The Next buses are
// written by this track alone, so the parallel leaf render stays race-free. Audio thread.
void Engine::feedPartTaps(const Track& t) {
    if (!t.instrument) return;
    Instrument::PartTap taps[kMaxRoutes];
    int32_t nTaps = 0;
    for (int32_t s = 0, n = routeSlotCount_.load(std::memory_order_acquire); s < n; ++s) {
        if (routeSlotTrackId_[s].load(std::memory_order_relaxed) != t.id()) continue;
        const uint64_t part = routeSlotPart_[s].load(std::memory_order_relaxed);
        if (part != 0) taps[nTaps++] = {part, routeBusPreNext_[s].data(), routeBusNext_[s].data()};
    }
    t.instrument->setPartTaps(taps, nTaps);
}

// The key buffer for a consumer: its route slot's tap, nullptr for none — or, for a part key
// whose part is gone (specs/050), silence: a detector handed nullptr keys off its own input,
// and the key is shown as missing, not as Internal.
const float* Engine::keyBuffer(int32_t src, uint64_t part, bool pre) const {
    const int32_t s = routeSlotFor(src, part);
    if (s >= 0) return pre ? routeBusPre_[s].data() : routeBus_[s].data();
    return part != 0 ? silentKey_.data() : nullptr;
}

// Hand a device its sidechain key for this block (the source track's previous-block
// signal, pre- or post-FX tap) or its MIDI key; a rack passes keys on to its chain
// children, each with its own source (specs/044). Audio thread.
void Engine::feedDeviceKey(Graph* g, Device& d, int32_t ownTrackId, int32_t frames) {
    const int32_t src = d.sidechainSourceTrackId();
    if (src >= 0) {
        d.setSidechain(keyBuffer(src, d.sidechainPart(), d.sidechainTapPre() != 0), frames);
        if (d.wantsMidiKey()) feedMidiKey(g, d, src);
    } else if (d.wantsOwnMidiKey())
        feedMidiKey(g, d, ownTrackId); // no key track: this track's notes
    if (auto* r = d.asRack()) feedRackChildKeys(g, *r, ownTrackId, frames);
}

// React (Nota Flux): hand a listening instrument its source track's previous-block, post-fader
// signal just before it renders; and the keyed devices and instruments inside an Instrument
// Rack's chains theirs (specs/044). Audio thread.
void Engine::feedInstrumentKeys(Graph* g, Track& t, int32_t frames) {
    if (!t.instrument) return;
    const int32_t iSc = t.instrument->sidechainSourceTrackId();
    if (iSc >= 0) t.instrument->setSidechain(keyBuffer(iSc, t.instrument->sidechainPart(), false), frames);
    if (auto* r = t.instrument->asRack()) feedRackChildKeys(g, *r, t.id(), frames);
}

void Engine::feedRackChildKeys(Graph* g, const RackCore& r, int32_t ownTrackId, int32_t frames) {
    r.forEachChildDevice([&](Device& c) {
        if (!c.bypassed() && !c.asRack()) feedDeviceKey(g, c, ownTrackId, frames);
    });
    r.forEachChildInstrument([&](Instrument& i) {
        const int32_t src = i.sidechainSourceTrackId();
        if (src < 0) return;
        i.setSidechain(keyBuffer(src, i.sidechainPart(), false), frames);
    });
}

// Step 2/2b for Pass 1: insert effect chain (M3) + PDC, in place. Verbatim
// moved from the old inline loop (sidechain taps read the previous block,
// tempo sync, hosted-plugin sync, frozen tracks skip baked-in devices).
void Engine::processLeafDevices(Track& t, float* buf, const LeafWork& w, Graph* g, int32_t frames, double blockStart,
    double spb, bool playing, const TransportInfo& ti) {
    // 2) Insert effect chain (M3), in place. Hand each sidechain consumer
    //    its source track's signal (previous block) just before it runs —
    //    the pre-FX or post-FX tap per the device's setting (Phase D). A frozen
    //    track skips this: its buffer already baked in the devices.
    if (!w.frozenActive)
        for (auto& d : t.devices)
            if (d && !d->bypassed()) {
                feedDeviceKey(g, *d, t.id(), frames);
                if (spb > 0.0) d->setTransport(blockStart / spb, spb, playing); // tempo-synced devices
                d->setTransportInfo(ti);                                        // hosted-plugin sync
                d->process(buf, frames);
            }

    // 2b) Plugin delay compensation (M3-7): align to the latest track.
    // ( note: the old flat CompensationDelay already early-returns on
    // !ready_/d<=0, so an engine-level skip would save two loads at most —
    // left verbatim; migrating Track to dsp::CompensationDelay is separate.)
    if (!w.frozenActive && t.pdc) t.pdc->process(buf, frames);
}

void Engine::mixGraph(Graph* g, float* out, int32_t frames, double blockStart, bool playing, double spb) {
    {
        // Pan law (specs/006): a change of the live law starts a 10 ms crossfade of the pan
        // gains; flipping back mid-fade reverses it from where it is, so nothing jumps. A law
        // that came with a load / reset is taken at once (nothing of the old project sounds).
        if (panLawSnap_.exchange(false, std::memory_order_acq_rel)) {
            panLawFrom_ = panLawTo_ = g->panLaw;
            panLawPos_ = 1.0f;
        } else if (g->panLaw != panLawTo_) {
            if (panLawPos_ < 1.0f && g->panLaw == panLawFrom_) {
                std::swap(panLawFrom_, panLawTo_);
                panLawPos_ = 1.0f - panLawPos_;
            } else {
                panLawFrom_ = panLawTo_;
                panLawTo_ = g->panLaw;
                panLawPos_ = 0.0f;
            }
        }
        const double srLaw = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
        const float panLawStep = static_cast<float>(1.0 / (0.010 * srLaw));
        const float stereoA = stereoBlockStep(frames, srLaw); // Stereo Map averages (specs/008)

        // Automation (M9): read-mode block-rate eval — write each active lane's
        // value into its target atomic before the mix reads it. Always runs (also
        // when stopped: the playhead is static, so scrub shows automation too).
        if (spb > 0.0) applyAutomation(g, blockStart / spb);

        // CV modulation (Phase 3): drive modulated device params to base∘mod for this
        // segment. The device atomic holds the live modulated value continuously (no
        // restore), so the UI shows the movement smoothly; the base lives in the link.
        if (spb > 0.0) {
            const double srMod = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
            updateModulators(g, blockStart / spb, blockStart / srMod, frames / srMod); // stateful + Math
            applyModulation(g, blockStart / spb, blockStart / srMod);
        }

        // Transport snapshot for this segment: pushed to every instrument/device
        // before it runs so hosted plugins drive their own tempo-synced behaviour off
        // the DAW clock (AudioPlayHead). Built-ins ignore it; racks forward to children.
        const TransportInfo ti = transport_.transportInfo(blockStart);

        // Internal resampling (record from another track/send/master): tap the source's
        // post-fader output into the input ring while a take is rolling.
        // Shut while counting in, and while a retried take waits for its TakeRestart (specs/078).
        const uint32_t capEp = captureEpoch_.load(std::memory_order_acquire); // before the gate
        const int32_t recSrc = (countInActive_ || !captureGate_.load(std::memory_order_acquire))
                                   ? 0
                                   : internalRecordSource_.load(std::memory_order_relaxed);
        if (recSrc != 0) markCaptureEpoch(rtCaptureEpoch_, capEp, /*hardware=*/false);

        // Live input monitoring: this segment's hardware input (shared by every track
        // monitoring it), and the set of tracks whose output some monitoring track hears —
        // those keep rendering while solo-gated, feeding only their route tap / record tap.
        pullMonitorInput(frames);
        constexpr int kMaxFeeds = 16;
        int32_t feedIds[kMaxFeeds];
        int numFeeds = 0;
        if (recSrc > 0) feedIds[numFeeds++] = recSrc;
        for (auto& t : g->tracks)
            if (t->type() == TrackType::Audio && t->monitor() && t->recordInputSource() > 0 && numFeeds < kMaxFeeds)
                feedIds[numFeeds++] = t->recordInputSource();
        auto feedsMonitor = [&](int32_t id) {
            for (int i = 0; i < numFeeds; ++i)
                if (feedIds[i] == id) return true;
            return false;
        };
        bool anySolo = false;
        int32_t numReturns = 0;
        for (auto& t : g->tracks) {
            if (t->solo()) anySolo = true;
            if (t->type() == TrackType::Return) ++numReturns;
        }
        if (numReturns > kMaxReturns) numReturns = kMaxReturns;
        for (int b = 0; b < numReturns; ++b) // clear send buses for this block (M6-1)
            std::fill_n(returnBus_[b].data(), frames * 2, 0.0f);
        // Phase B/D: clear this block's route-write buffers (post + pre) for active slots.
        for (int s = 0, n = routeSlotCount_.load(std::memory_order_acquire); s < n; ++s)
            if (routeSlotTrackId_[s].load(std::memory_order_relaxed) >= 0) {
                std::fill_n(routeBusNext_[s].data(), frames * 2, 0.0f);
                std::fill_n(routeBusPreNext_[s].data(), frames * 2, 0.0f);
            }

        // Group submix setup: discover Group tracks, their effective (ancestor-folded)
        // mute/solo, and depth. Leaves fold into their parent group's bus (else master);
        // groups are then processed deepest-first so children land before their parent.
        Track* grpT[kMaxGroups];
        int32_t grpId[kMaxGroups], grpParent[kMaxGroups], grpDepth[kMaxGroups];
        bool grpMuteE[kMaxGroups], grpSoloE[kMaxGroups];
        int numGroups = 0, maxDepth = 0;
        for (auto& tptr : g->tracks) {
            if (tptr->type() != TrackType::Group || numGroups >= kMaxGroups) continue;
            grpT[numGroups] = tptr.get();
            grpId[numGroups] = tptr->id();
            grpParent[numGroups] = tptr->groupId();
            ++numGroups;
        }
        auto slotOf = [&](int32_t id) -> int {
            if (id < 0) return -1;
            for (int i = 0; i < numGroups; ++i)
                if (grpId[i] == id) return i;
            return -1;
        };
        for (int i = 0; i < numGroups; ++i) {
            bool m = false, s = false;
            int depth = 0;
            for (int32_t a = grpId[i]; a >= 0;) { // walk self + ancestor groups
                int si = slotOf(a);
                if (si < 0) break;
                m |= grpT[si]->mute();
                s |= grpT[si]->solo();
                ++depth;
                a = grpParent[si];
            }
            grpMuteE[i] = m;
            grpSoloE[i] = s;
            grpDepth[i] = depth;
            if (depth > maxDepth) maxDepth = depth;
        }
        for (int i = 0; i < numGroups; ++i) std::fill_n(groupBus_[i].data(), frames * 2, 0.0f);
        // A leaf's effective mute/solo folds in any ancestor group's flag.
        auto leafMute = [&](Track& t) {
            if (t.mute()) return true;
            int s = slotOf(t.groupId());
            return s >= 0 && grpMuteE[s];
        };
        auto leafSolo = [&](Track& t) {
            if (t.solo()) return true;
            int s = slotOf(t.groupId());
            return s >= 0 && grpSoloE[s];
        };

        // Session vs Arrangement (M5): when the Arrangement isn't active (a session-only jam),
        // tracks with no launched session clip stay silent instead of playing their timeline.
        const bool arrangementActive = arrangementActive_.load(std::memory_order_relaxed);

        // MIDI pre-pass: each instrument track's post-FX output for this block, so the render
        // below can feed instruments their own notes plus any routed-in source's notes.
        computeBlockMidi(g, frames, blockStart, spb, playing, arrangementActive);

        // Pass 1: leaf tracks -> parent group bus (or master), tapping post-fader sends.
        //
        // DSP-heavy eligible leaves render in parallel via pool_ (each
        // into its worker's scratch); a serial accumulate in track order keeps
        // the mix bit-identical (same elementwise ops, same order). Shared
        // writers (record queues are SPSC), hardware monitoring and hosted
        // plugins stay on the verbatim serial path.
        const size_t ntracks = g->tracks.size();
        // Per-track render storage (two halves: dry + post). Falls back to
        // all-serial when undersized (unreachable: sized per graph swap).
        const size_t stride = (size_t)frames * 2;
        const bool parallelOk = rs_->leafBuf.size() >= ntracks * stride * 2 && (uint64_t)frames * 2 <= mixTmp_.size() &&
                                rs_->leafWork.size() >= ntracks;
        const int32_t capTrackId = freezeCaptureTrackId_.load(std::memory_order_relaxed);
        const bool recArmed = recording_.load(std::memory_order_relaxed);
        for (size_t li = 0; li < ntracks; ++li) {
            Track& t = *g->tracks[li];
            LeafWork& w = rs_->leafWork[li];
            w = LeafWork{};
            if (t.type() == TrackType::Return) continue; // return buses run in pass 2
            if (t.type() == TrackType::Group) continue;  // group submixes run in pass 1.5
            w.t = &t;
            // Freeze (M7): a frozen track plays its captured buffer instead of the live
            // instrument + device chain (sounds only while rolling, like an audio clip).
            const bool frozenActive = t.frozen() && t.frozenBuf;
            // While this track is being frozen, render + capture it regardless of the
            // mixer mute/solo gate (freeze captures the sound the track makes, not the mix)
            // — otherwise a soloed sibling would punch silent gaps into the buffer.
            const bool capturing = (t.id() == capTrackId);
            const bool audible = !leafMute(t) && (!anySolo || leafSolo(t));
            // Soloing a monitoring (or recording) track must not silence what it listens to.
            const bool feeding = !audible && !leafMute(t) && feedsMonitor(t.id());
            // A sidechain source keeps rendering for its taps when muted or solo-gated, the way a
            // console's pre-fader send outlives the channel mute: a vocoder's voice track can be
            // muted (or the pad soloed) and still drive the bands. Its post-fader tap is silent
            // while it is muted.
            const bool keying = !audible && trackFeedsRoute(t.id()); // whole track or a part (specs/050)
            const bool postTapMuted = keying && leafMute(t);
            // Input monitoring (Monitor "In"): the track plays its record-input source live
            // in place of its clips — hardware input this segment, or the source track's
            // post-fader tap from the previous one. Master (-1) would feed back: silent.
            const bool monitoring = !frozenActive && !capturing && t.type() == TrackType::Audio && t.monitor();

            // 0) Session view (M5-2): apply quantized launch/stop; a playing slot
            //    overrides this track's arrangement content. Requests apply on every track,
            //    heard or not: a muted, solo-gated, monitoring or frozen track would otherwise
            //    keep a launch queued forever (#343). Only the sound is skipped there — a
            //    frozen or capturing track plays its buffer / arrangement, a monitoring one its
            //    input. Serial: session clocks share followRng_ across tracks (deterministic
            //    order = the old interleaved order).
            bool sessionActive = false;
            if (auto& sp = t.sessionPlayer) {
                // Follow actions + Repeat launch mode queue the next request before it applies.
                if (playing) sessionPreApply(t, frames / spb);
                if (sp->maybeApply(
                        blockStart / spb, frames / spb, launchQuant_.load(std::memory_order_relaxed), seamOffs_)) {
                    if (t.instrument) t.instrument->allNotesOff();
                    for (auto& md : t.midiEffects)
                        if (md) md->reset(); // clock switch → flush arp
                }
                sessionActive =
                    !frozenActive && !capturing && !monitoring && sp->playing.load(std::memory_order_relaxed) >= 0;
            }
            if (!audible && !capturing && !feeding && !keying) {
                // Silent this block, but a playing slot keeps its place in the loop, so it is
                // in time again when the track is unmuted (and follow actions keep counting).
                if (sessionActive) {
                    // The clip moves on without its note-offs reaching the instrument: release
                    // what it holds, or a note crossed while silent would sound on after unmute.
                    // Once, on going silent: a hosted plug-in queues each release until it renders.
                    if (t.instrument && !t.sessionSilentReleased) t.instrument->allNotesOff();
                    // Those voices were flushed; an old latching release must not survive the
                    // silent interval and cut a new momentary hold after mute/solo is lifted.
                    t.sessionPlayer->articulationReleases.count = 0;
                    t.sessionSilentReleased = true;
                    t.sessionPlayer->localBeats += frames / spb;
                    sessionPostRender(t, frames / spb);
                }
                t.tailQuiet() = -1; // muted: its tail is gone, not saved for the unmute
                t.setMeter(0, 0, 0, 0);
                t.stereoFold(0, 0, 0, frames, stereoA);
                continue;
            }
            t.sessionSilentReleased = false;
            w.audible = audible;
            w.capturing = capturing;
            w.feeding = feeding;
            w.toMix = audible || capturing; // false: render only for the taps
            w.postTapMuted = postTapMuted;
            w.monitoring = monitoring;
            w.frozenActive = frozenActive;
            w.sessionActive = sessionActive;

            // Render-path selection (same condition chain as the old inline loop).
            if (frozenActive) {
                if (!playing) {
                    t.setMeter(0, 0, 0, 0);
                    t.stereoFold(0, 0, 0, frames, stereoA);
                    continue;
                } // frozen sounds only while rolling
                w.path = LeafPath::Frozen;
            } else if (w.monitoring) {
                w.path = LeafPath::Monitor;
            } else if (sessionActive && t.type() == TrackType::Instrument) {
                w.path = LeafPath::SessionInst;
            } else if (sessionActive && t.type() == TrackType::Audio) {
                w.path = LeafPath::SessionAudio;
            } else if (t.type() == TrackType::Instrument) {
                w.path = LeafPath::Instrument;
            } else if (playing && arrangementActive) {
                w.path = LeafPath::AudioClips;
            } else if (t.tailQuiet() >= 0 && hasLiveDevice(t)) {
                w.path = LeafPath::Tail; // its effects ring out on silence (#449)
            } else {
                t.tailQuiet() = -1;
                t.setMeter(0, 0, 0, 0);
                t.stereoFold(0, 0, 0, frames, stereoA);
                continue;
            } // stopped / session-only audio track, its tail played out: nothing to do

            // Destination: this leaf's parent group's submix bus, else the master mix.
            const int destSlot = slotOf(t.groupId());
            w.dest = destSlot >= 0 ? groupBus_[destSlot].data() : out;
            const int32_t routeSlot = routeSlotForTrack(t.id()); // Phase B: this track is a sidechain source?
            w.route = routeSlot >= 0 ? routeBusNext_[routeSlot].data() : nullptr;
            w.anySend = false; // post-fader send taps (M6-1)
            for (int b = 0; b < numReturns; ++b) {
                w.sends[b] = t.send(b);
                if (w.sends[b] != 0.0f) w.anySend = true;
            }
            w.tapRec = (recSrc > 0 && t.id() == recSrc); // internal resampling source

            // Parallel eligibility is an instance's processing contract, not its
            // hosted identity: independent audited VST3 adapters own their state
            // just like built-ins. Unknown formats and hosted-containing racks
            // remain serial. Shared-queue writers (record queues are SPSC) must
            // stay on the caller regardless of processor capability.
            bool eligible = parallelOk;
            const int32_t srcId = t.midiFromTrackId();
            if (srcId >= 0 && midiLaneOfRt(t.id()) >= 0 && (playing || countInActive_) && recArmed && spb > 0.0)
                eligible = false;                          // captureRouted destination prints into shared takes
            if (srcId >= 0 && t.armed()) eligible = false; // routed source prints while armed
            if (eligible && t.instrument && !t.instrument->supportsParallelRender()) eligible = false;
            if (eligible)
                for (auto& d : t.devices)
                    if (d && !d->bypassed() && !d->supportsParallelRender()) {
                        eligible = false;
                        break;
                    }
            w.eligible = eligible;
        }

        // Parallel DSP phase: eligible leaves render dry into their own
        // rs_->leafBuf region, copy to the post half, run devices in place
        // there. Regions are per-track (never shared), so completion order
        // cannot mix signals; the serial accumulate below fixes mix order.
        if (parallelOk) {
            int m = 0;
            for (size_t li = 0; li < ntracks; ++li) {
                const LeafWork& w = rs_->leafWork[li];
                if (w.t && w.path != LeafPath::Skip && w.eligible) rs_->parIdx[(size_t)m++] = (int)li;
            }
            // Offline (export, freeze: no deadline): every thread, finish fast. With a
            // device: this thread alone unless it can't keep up (see rtParallel_); then
            // enough threads to finish within kRtSizeLoad of the buffer.
            // renderOffline never runs beside a live device, so deviceBlock_ alone tells the
            // two apart. (The gap between blocks can't: an export's 8192-frame chunks arrive
            // with the encoder's work between them, and an overloaded callback arrives late.)
            const bool offline = !deviceBlock_;
            const double periodNs = 1e9 * frames / std::max(1.0, transport_.sampleRate());
            int helpers = -1;
            if (!offline) {
                helpers = 0;
                if (rtParallel_ && (rtParallelLeftNs_ -= periodNs) <= 0) {
                    if (parWorkNs_ <= periodNs) {
                        rtParallel_ = false; // even the over-reading bound fits: try alone
                        leafWorkNs_ = parWorkNs_;
                        rtProbeLeftNs_ = kRtProbeSeconds * 1e9;
                    } else {
                        rtParallelLeftNs_ = rtHoldNs_; // still over: stay, no blind drop
                    }
                }
                if (rtParallel_) {
                    const double work = (double)std::max(leafWorkNs_, parWorkNs_);
                    const double target = kRtSizeLoad * periodNs;
                    int threads = (int)std::ceil(work / target);
                    // Track jobs are indivisible: four 1.4 ms jobs cannot meet a
                    // 2.67 ms deadline on three threads even though their total
                    // divided by three fits. List scheduling's upper bound is
                    // work / threads + (1 - 1 / threads) * largestJob. Use it to
                    // size helpers without sorting jobs or allocating in the callback.
                    const double peak = std::min(work, (double)leafPeakNs_);
                    if (peak > 0) {
                        const int available = std::min(m, pool_->workerCount() + 1);
                        if (peak < target) {
                            // T >= (work - peak) / (target - peak): the smallest count
                            // whose list-scheduling bound still fits the target.
                            const double needed = std::ceil((work - peak) / (target - peak));
                            threads = std::max(threads, (int)std::min((double)available, needed));
                        } else {
                            // The largest job alone overflows the target: no count meets
                            // the bound. Every extra worker still lowers it (the small
                            // jobs run beside the heavy one instead of queueing in front),
                            // so take the whole pool rather than park on the average.
                            threads = std::max(threads, available);
                        }
                    }
                    helpers = std::clamp(threads - 1, 1, std::max(1, m - 1));
                }
            }
            pool_->dispatch(
                m,
                [&](int wid, int j) {
                    const size_t i = (size_t)rs_->parIdx[(size_t)j];
                    LeafWork& w = rs_->leafWork[i];
                    const auto t0 = std::chrono::steady_clock::now();
                    w.wid = wid;
                    float* base = leafBase(i, stride);
                    renderLeafDry(*w.t, base, w, g, frames, blockStart, spb, playing, ti, arrangementActive);
                    std::copy_n(base, stride, base + stride);
                    processLeafDevices(*w.t, base + stride, w, g, frames, blockStart, spb, playing, ti);
                    w.workNs = std::chrono::duration_cast<std::chrono::nanoseconds>(
                        std::chrono::steady_clock::now() - t0).count();
                },
                helpers);
            // The single-thread cost, from blocks this thread rendered alone, and the
            // pool's summed task time while it runs (see leafWorkNs_).
            if (!offline && m > 0) {
                int64_t work = 0;
                int64_t peak = 0;
                bool alone = true;
                for (int j = 0; j < m; ++j) {
                    const LeafWork& w = rs_->leafWork[(size_t)rs_->parIdx[(size_t)j]];
                    work += w.workNs;
                    peak = std::max(peak, w.workNs);
                    alone = alone && w.wid == pool_->workerCount();
                }
                leafPeakNs_ += (peak - leafPeakNs_) / 16;
                if (rtParallel_) {
                    parWorkNs_ += (work - parWorkNs_) / 16;
                } else {
                    // Just left the pool: one block over its period proves the bound wrong.
                    // Rejoin at once and wait twice as long before the next try.
                    const bool probeMissed = rtProbeLeftNs_ > 0 && (double)work > periodNs;
                    if (alone) leafWorkNs_ += (work - leafWorkNs_) / 16;
                    if (probeMissed || (alone && leafWorkNs_ > periodNs)) {
                        if (probeMissed) rtHoldNs_ = std::min(2 * rtHoldNs_, kRtMaxHoldSeconds * 1e9);
                        rtParallel_ = true;
                        rtParallelLeftNs_ = rtHoldNs_;
                        rtProbeLeftNs_ = 0;
                        parWorkNs_ = std::max(leafWorkNs_, work);
                    } else if (rtProbeLeftNs_ > 0 && (rtProbeLeftNs_ -= periodNs) <= 0) {
                        rtHoldNs_ = kRtHoldSeconds * 1e9; // the probe held: back to the short hold
                    }
                }
            }
        }

        // Serial accumulate in track order (bit-identical: same elementwise
        // ops in the same order; SIMD adds/scales are exact per element).
        for (size_t li = 0; li < ntracks; ++li) {
            LeafWork& w = rs_->leafWork[li];
            if (!w.t || w.path == LeafPath::Skip) continue;
            Track& t = *w.t;
            // pre = dry pre-device (pre-FX tap source), post = post-device
            // (fader/capture source). Parallel tracks read both halves of
            // their own rs_->leafBuf region; serial tracks render dry, tap, then
            // run devices in place — the original per-track order.
            float* pre = nullptr;
            float* post = nullptr;
            bool parallelDsp = false;
            if (w.eligible && parallelOk) {
                float* base = leafBase(li, stride);
                pre = base;
                post = base + stride;
                parallelDsp = true;
            } else {
                renderLeafDry(t, scratch_.data(), w, g, frames, blockStart, spb, playing, ti, arrangementActive);
                pre = post = scratch_.data();
            }

            // 1b) Pre-FX tap (Phase D): if this track feeds a sidechain, capture its
            //     dry pre-fader signal now, before its own devices colour it.
            {
                const int32_t preSlot = routeSlotForTrack(t.id());
                if (preSlot >= 0)
                    simd_audio::addStereoScaled(routeBusPreNext_[preSlot].data(), pre, 1.0f, 1.0f, frames);
            }
            if (!parallelDsp) processLeafDevices(t, scratch_.data(), w, g, frames, blockStart, spb, playing, ti);

            // 2b') Effect tails (#449): a block an audio track plays (re)starts its tail; on
            //      the tail path the chain's output is watched until it has been quiet long
            //      enough, and the track goes back to costing nothing.
            if (w.path == LeafPath::Tail) {
                int64_t& quiet = t.tailQuiet();
                if (simd_audio::peakAbs(post, frames * 2) > kTailFloor) quiet = 0;
                else quiet += frames;
                if (quiet > 0) {
                    const double sr = transport_.sampleRate();
                    const ChainRest rest = chainRest(t);
                    if (rest == ChainRest::Settled ||
                        (rest == ChainRest::Opaque && quiet >= static_cast<int64_t>(kTailQuietSeconds * sr)) ||
                        quiet >= static_cast<int64_t>(kTailMaxQuietSeconds * sr))
                        quiet = -1;
                }
            } else if (w.path == LeafPath::AudioClips || w.path == LeafPath::SessionAudio || w.path == LeafPath::Monitor)
                t.tailQuiet() = 0;

            // 2c) Freeze capture (M7): stash this track's post-device, pre-fader signal
            //     into the freeze buffer. Sequential from a seek(0) render, so the write
            //     cursor tracks the playhead frame.
            if (w.capturing) {
                int64_t c = freezeCursor_;
                for (int32_t i = 0; i < frames && c < freezeCapFrames_; ++i, ++c) {
                    freezeCaptureBuf_[c * 2] = post[i * 2];
                    freezeCaptureBuf_[c * 2 + 1] = post[i * 2 + 1];
                }
                freezeCursor_ = c;
            }

            // 3) Channel fader + equal-power pan into the master mix + sends.
            const float vol = t.volume();
            const float pan = std::clamp(t.pan(), -1.0f, 1.0f);
            const PanStage ps(pan, vol, panLawTo_, panLawFrom_, panLawPos_, panLawStep); // project pan law
            float* dest = w.dest;
            float* route = w.route;
            float pkL = 0.0f, pkR = 0.0f;
            double sqL = 0.0, sqR = 0.0, sLR = 0.0; // sLR: Stereo Map (specs/008)
            int64_t recDropped = 0;
            if (!ps.cross) {
                // Static pan law: vectorized (exact per element), meters scalar.
                // NOTE: dest/sends only when toMix (solo-gated feeders tap only).
                simd_audio::scaleStereo(mixTmp_.data(), post, ps.to.ll, ps.to.rr, frames);
                const float* m = mixTmp_.data();
                if (route && !w.postTapMuted) simd_audio::addStereoScaled(route, m, 1.0f, 1.0f, frames);
                if (w.toMix) {
                    simd_audio::addStereoScaled(dest, m, 1.0f, 1.0f, frames);
                    if (w.anySend)
                        for (int b = 0; b < numReturns; ++b)
                            if (w.sends[b] != 0.0f)
                                simd_audio::addStereoScaled(returnBus_[b].data(), m, w.sends[b], w.sends[b], frames);
                }
                for (int32_t i = 0; i < frames; ++i) {
                    const float l = m[i * 2], r = m[i * 2 + 1];
                    if (w.tapRec && !internalQueue_.push(InputFrame{l, r})) ++recDropped; // record this track's output
                    const float al = std::fabs(l), ar = std::fabs(r);
                    if (al > pkL) pkL = al;
                    if (ar > pkR) pkR = ar;
                    sqL += l * (double)l;
                    sqR += r * (double)r;
                    sLR += l * (double)r;
                }
            } else {
                for (int32_t i = 0; i < frames; ++i) {
                    float l, r;
                    ps.apply(i, post[i * 2], post[i * 2 + 1], l, r);
                    if (w.tapRec && !internalQueue_.push(InputFrame{l, r})) ++recDropped; // record this track's output
                    if (route && !w.postTapMuted) {
                        route[i * 2] += l;
                        route[i * 2 + 1] += r;
                    } // tap post-fader → route bus
                    if (!w.toMix) continue; // solo-gated feeder: taps only
                    dest[i * 2] += l;
                    dest[i * 2 + 1] += r;
                    if (w.anySend)
                        for (int b = 0; b < numReturns; ++b)
                            if (w.sends[b] != 0.0f) {
                                returnBus_[b][i * 2] += l * w.sends[b];
                                returnBus_[b][i * 2 + 1] += r * w.sends[b];
                            }
                    const float al = std::fabs(l), ar = std::fabs(r);
                    if (al > pkL) pkL = al;
                    if (ar > pkR) pkR = ar;
                    sqL += l * (double)l;
                    sqR += r * (double)r;
                    sLR += l * (double)r;
                }
            }
            if (recDropped) inputDroppedFrames_.fetch_add(recDropped, std::memory_order_relaxed);
            if (!w.toMix) { // muted / solo-gated: nothing reaches the master, the averages decay
                t.setMeter(0, 0, 0, 0);
                t.stereoFold(0.0, 0.0, 0.0, frames, stereoA);
                continue;
            }
            t.stereoFold(sqL, sqR, sLR, frames, stereoA);
            t.setMeter(
                pkL, pkR, static_cast<float>(std::sqrt(sqL / frames)), static_cast<float>(std::sqrt(sqR / frames)));
        }

        // An armed audio track that isn't monitoring meters its input instead (#352): the
        // level to set before a take, without hearing it. Pre-fader, like a console's input
        // meter; a monitoring track already meters what it hears. Nothing here is heard, so
        // the sources monitoring refuses (they would feed back) meter too: the master (its
        // previous block's level) and a group the track sits in (its post-fader tap).
        for (auto& tptr : g->tracks) {
            Track& t = *tptr;
            if (t.type() != TrackType::Audio || !t.armed() || (t.frozen() && t.frozenBuf)) continue;
            if (t.monitor() && t.id() != capTrackId) continue;
            const int32_t src = t.recordInputSource();
            if (src == -1) {
                t.setMeter(masterPeakL_.load(std::memory_order_relaxed), masterPeakR_.load(std::memory_order_relaxed),
                    masterRmsL_.load(std::memory_order_relaxed), masterRmsR_.load(std::memory_order_relaxed));
                continue;
            }
            const float* in = scratch_.data();
            if (src > 0 && src != t.id()) {
                const int32_t slot = routeSlotForTrack(src);
                if (slot >= 0) in = routeBus_[slot].data();
                else std::fill_n(scratch_.data(), frames * 2, 0.0f);
            } else renderMonitorInput(g, t, scratch_.data(), frames);
            float pkL = 0.0f, pkR = 0.0f;
            double sqL = 0.0, sqR = 0.0;
            for (int32_t i = 0; i < frames; ++i) {
                const float l = in[i * 2], r = in[i * 2 + 1];
                pkL = std::max(pkL, std::fabs(l));
                pkR = std::max(pkR, std::fabs(r));
                sqL += l * (double)l;
                sqR += r * (double)r;
            }
            t.setMeter(
                pkL, pkR, static_cast<float>(std::sqrt(sqL / frames)), static_cast<float>(std::sqrt(sqR / frames)));
        }

        // Pass 1.5: group submixes. Process deepest-first so a nested group folds into its
        // parent's bus before the parent reads it; top-level groups fold into the master.
        for (int depth = maxDepth; depth >= 1; --depth)
            for (int gi = 0; gi < numGroups; ++gi) {
                if (grpDepth[gi] != depth) continue;
                Track& t = *grpT[gi];
                if (grpMuteE[gi]) {
                    t.setMeter(0, 0, 0, 0);
                    continue;
                } // muted group (or muted ancestor)

                std::copy_n(groupBus_[gi].data(), frames * 2, scratch_.data()); // this group's summed children
                {
                    const int32_t preSlot = routeSlotForTrack(t.id()); // Phase D pre-FX tap
                    if (preSlot >= 0)
                        simd_audio::addStereoScaled(
                            routeBusPreNext_[preSlot].data(), scratch_.data(), 1.0f, 1.0f, frames);
                }
                for (auto& d : t.devices)
                    if (d && !d->bypassed()) {
                        feedDeviceKey(g, *d, t.id(), frames);
                        if (spb > 0.0) d->setTransport(blockStart / spb, spb, playing);
                        d->setTransportInfo(ti);
                        d->process(scratch_.data(), frames);
                    }
                // PDC left verbatim (see renderLeafDsp note).
                if (t.pdc) t.pdc->process(scratch_.data(), frames);

                const float vol = t.volume();
                const float pan = std::clamp(t.pan(), -1.0f, 1.0f);
                // Unity-center balance (not the −3 dB equal-power leaf law) so that grouping is
                // level-transparent: the group just sums its already-panned children and passes
                // them through at unity when centred.
                const float gl = (pan <= 0.0f ? 1.0f : 1.0f - pan) * vol;
                const float gr = (pan >= 0.0f ? 1.0f : 1.0f + pan) * vol;
                const int pslot = slotOf(grpParent[gi]);
                float* dest = pslot >= 0 ? groupBus_[pslot].data() : out; // parent group bus, else master
                const int32_t routeSlot = routeSlotForTrack(t.id());
                float* route = routeSlot >= 0 ? routeBusNext_[routeSlot].data() : nullptr;
                const bool tapRec = (recSrc > 0 && t.id() == recSrc);
                float pkL = 0.0f, pkR = 0.0f;
                double sqL = 0.0, sqR = 0.0;
                int64_t recDropped = 0;
                // vectorized (exact per element); meters stay scalar.
                simd_audio::scaleStereo(mixTmp_.data(), scratch_.data(), gl, gr, frames);
                {
                    const float* m = mixTmp_.data();
                    simd_audio::addStereoScaled(dest, m, 1.0f, 1.0f, frames);
                    if (route) simd_audio::addStereoScaled(route, m, 1.0f, 1.0f, frames);
                    for (int32_t i = 0; i < frames; ++i) {
                        const float l = m[i * 2], r = m[i * 2 + 1];
                        if (tapRec && !internalQueue_.push(InputFrame{l, r})) ++recDropped;
                        const float al = std::fabs(l), ar = std::fabs(r);
                        if (al > pkL) pkL = al;
                        if (ar > pkR) pkR = ar;
                        sqL += l * (double)l;
                        sqR += r * (double)r;
                    }
                }
                if (recDropped) inputDroppedFrames_.fetch_add(recDropped, std::memory_order_relaxed);
                t.setMeter(
                    pkL, pkR, static_cast<float>(std::sqrt(sqL / frames)), static_cast<float>(std::sqrt(sqR / frames)));
            }

        // Pass 2: return buses -> device chain -> master (M6-1). Exempt from the
        // solo gate (a soloed track's reverb/delay still sounds); a return only
        // honours its own mute. Runs even when stopped so effect tails ring out.
        for (auto& tptr : g->tracks) {
            Track& t = *tptr;
            if (t.type() != TrackType::Return) continue;
            const int32_t bus = t.returnIndex();
            if (bus < 0 || bus >= numReturns || t.mute()) {
                t.setMeter(0, 0, 0, 0);
                t.stereoFold(0, 0, 0, frames, stereoA);
                continue;
            }

            std::copy_n(returnBus_[bus].data(), frames * 2, scratch_.data());
            {
                const int32_t preSlot = routeSlotForTrack(t.id()); // Phase D pre-FX tap
                if (preSlot >= 0)
                    simd_audio::addStereoScaled(routeBusPreNext_[preSlot].data(), scratch_.data(), 1.0f, 1.0f, frames);
            }
            for (auto& d : t.devices)
                if (d && !d->bypassed()) {
                    feedDeviceKey(g, *d, t.id(), frames);
                    if (spb > 0.0) d->setTransport(blockStart / spb, spb, playing);
                    d->setTransportInfo(ti); // hosted-plugin sync
                    d->process(scratch_.data(), frames);
                }
            // PDC left verbatim (see renderLeafDsp note).
            if (t.pdc) t.pdc->process(scratch_.data(), frames);

            const float vol = t.volume();
            const float pan = std::clamp(t.pan(), -1.0f, 1.0f);
            const PanStage ps(pan, vol, panLawTo_, panLawFrom_, panLawPos_, panLawStep); // project pan law
            const int32_t routeSlot = routeSlotForTrack(t.id());
            float* route = routeSlot >= 0 ? routeBusNext_[routeSlot].data() : nullptr;
            const bool tapRec = (recSrc > 0 && t.id() == recSrc); // internal resampling source (a return)
            float pkL = 0.0f, pkR = 0.0f;
            double sqL = 0.0, sqR = 0.0, sLR = 0.0; // sLR: Stereo Map (specs/008)
            int64_t recDropped = 0;
            // vectorized static-pan path (exact per element); crossfeed/
            // morphing pan laws stay scalar; meters stay scalar either way.
            if (!ps.cross) {
                simd_audio::scaleStereo(mixTmp_.data(), scratch_.data(), ps.to.ll, ps.to.rr, frames);
                const float* m = mixTmp_.data();
                simd_audio::addStereoScaled(out, m, 1.0f, 1.0f, frames);
                if (route) simd_audio::addStereoScaled(route, m, 1.0f, 1.0f, frames);
                for (int32_t i = 0; i < frames; ++i) {
                    const float l = m[i * 2], r = m[i * 2 + 1];
                    if (tapRec && !internalQueue_.push(InputFrame{l, r})) ++recDropped;
                    const float al = std::fabs(l), ar = std::fabs(r);
                    if (al > pkL) pkL = al;
                    if (ar > pkR) pkR = ar;
                    sqL += l * (double)l;
                    sqR += r * (double)r;
                    sLR += l * (double)r;
                }
            } else {
                for (int32_t i = 0; i < frames; ++i) {
                    float l, r;
                    ps.apply(i, scratch_[i * 2], scratch_[i * 2 + 1], l, r);
                    out[i * 2] += l;
                    out[i * 2 + 1] += r;
                    if (tapRec && !internalQueue_.push(InputFrame{l, r})) ++recDropped;
                    if (route) {
                        route[i * 2] += l;
                        route[i * 2 + 1] += r;
                    }
                    const float al = std::fabs(l), ar = std::fabs(r);
                    if (al > pkL) pkL = al;
                    if (ar > pkR) pkR = ar;
                    sqL += l * (double)l;
                    sqR += r * (double)r;
                    sLR += l * (double)r;
                }
            }
            if (recDropped) inputDroppedFrames_.fetch_add(recDropped, std::memory_order_relaxed);
            t.stereoFold(sqL, sqR, sLR, frames, stereoA);
            t.setMeter(
                pkL, pkR, static_cast<float>(std::sqrt(sqL / frames)), static_cast<float>(std::sqrt(sqR / frames)));
        }

        // Internal resampling from the master bus: capture the fully-mixed output.
        if (recSrc == -1) {
            int64_t recDropped = 0;
            for (int32_t i = 0; i < frames; ++i)
                if (!internalQueue_.push(InputFrame{out[i * 2], out[i * 2 + 1]})) ++recDropped;
            if (recDropped) inputDroppedFrames_.fetch_add(recDropped, std::memory_order_relaxed);
        }

        // Phase B/D: publish this block's route taps (post + pre) for next block.
        for (int s = 0, n = routeSlotCount_.load(std::memory_order_acquire); s < n; ++s)
            if (routeSlotTrackId_[s].load(std::memory_order_relaxed) >= 0) {
                routeBus_[s].swap(routeBusNext_[s]);
                routeBusPre_[s].swap(routeBusPreNext_[s]);
            }

        if (panLawPos_ < 1.0f) panLawPos_ = std::min(1.0f, panLawPos_ + panLawStep * static_cast<float>(frames));
    }
}

void Engine::renderClick(float* out, int32_t i, int clickLen, double sr) {
    if (clickRemaining_ <= 0) return;
    const float env = static_cast<float>(clickRemaining_) / static_cast<float>(clickLen);
    const float s = static_cast<float>(std::sin(clickPhase_)) * env * kMetroGain;
    out[i * 2] += s;
    out[i * 2 + 1] += s;
    clickPhase_ += kTwoPi * clickFreq_ / sr;
    if (clickPhase_ >= kTwoPi) clickPhase_ -= kTwoPi;
    --clickRemaining_;
}

void Engine::renderMetronome(float* out, int32_t numFrames, double blockStartSamples) {
    if (!transport_.metronomeEnabled()) return;
    const double sr = transport_.sampleRate();
    const double spb = transport_.samplesPerBeat();
    const int beatsPerBar = transport_.beatsPerBar();
    const int clickLen = static_cast<int>(0.03 * sr);

    for (int32_t i = 0; i < numFrames; ++i) {
        const double p = blockStartSamples + i;
        if (p >= 0.0) {
            const int64_t beat = static_cast<int64_t>(p / spb);
            if (beat != lastBeatEmitted_) {
                lastBeatEmitted_ = beat;
                clickRemaining_ = clickLen;
                clickPhase_ = 0.0;
                clickFreq_ = (beat % beatsPerBar == 0) ? 1500.0 : 1000.0;
            }
        }
        renderClick(out, i, clickLen, sr);
    }
}

// Count-in clicks: same voice as the metronome, accent on each bar's first beat. Sounds
// regardless of the metronome switch; the transport is stopped meanwhile.
bool Engine::renderCountIn(float* out, int32_t numFrames, double spb) {
    const double sr = transport_.sampleRate();
    const int beatsPerBar = std::max(1, transport_.beatsPerBar());
    const int clickLen = static_cast<int>(0.03 * sr);
    const double inc = spb > 0.0 ? 1.0 / spb : 0.0;

    for (int32_t i = 0; i < numFrames; ++i) {
        if (countInPos_ < countInTotal_) {
            const int64_t beat = static_cast<int64_t>(countInPos_ + 1e-9);
            if (beat != lastCountInBeat_) {
                lastCountInBeat_ = beat;
                clickRemaining_ = clickLen;
                clickPhase_ = 0.0;
                clickFreq_ = (beat % beatsPerBar == 0) ? 1500.0 : 1000.0;
            }
        }
        renderClick(out, i, clickLen, sr);
        countInPos_ += inc;
    }
    uiCountIn_.store(std::max(0.0, countInTotal_ - countInPos_), std::memory_order_relaxed);
    return inc <= 0.0 || countInPos_ >= countInTotal_ - 0.5 * inc;
}

void Engine::renderTone(float* out, int32_t numFrames) {
    const double sr = transport_.sampleRate();
    const float target = toneEnabled_ ? 1.0f : 0.0f;
    if (sr <= 0.0 || (target == 0.0f && toneLevel_ == 0.0f)) {
        tonePhase_ = 0.0;
        return;
    }
    const double inc = kTwoPi * static_cast<double>(frequency_) / sr;
    const float step = static_cast<float>(1.0 / (sr * 0.01)); // ~10 ms fade in/out
    for (int32_t i = 0; i < numFrames; ++i) {
        toneLevel_ = target > toneLevel_ ? std::min(target, toneLevel_ + step) : std::max(target, toneLevel_ - step);
        const float s = static_cast<float>(std::sin(tonePhase_)) * kToneGain * toneLevel_;
        out[i * 2] += s;
        out[i * 2 + 1] += s;
        tonePhase_ += inc;
        if (tonePhase_ >= kTwoPi) tonePhase_ -= kTwoPi;
    }
}

// #436: an instrument that takes timed events (a hosted plug-in) renders once per block, each
// event stamped with its offset, in the same order the segmented render delivers them; an
// instrument that doesn't is still rendered in segments with every event at offset 0.
bool Engine::timedEventsSelfTest() {
    struct Probe final : Instrument {
        bool timed = false;
        std::vector<int32_t> renders;                // frames per render() call
        std::vector<std::array<int32_t, 4>> events; // kind (1 on, 0 off, 2 ctl, 3 live), pitch, offset, renders so far
        bool takesTimedEvents() const override { return timed; }
        void setSampleRate(double) override {}
        void noteOn(int32_t p, float) override { events.push_back({1, p, eventOffset, (int32_t)renders.size()}); }
        void noteOff(int32_t p) override { events.push_back({0, p, eventOffset, (int32_t)renders.size()}); }
        void allNotesOff() override {}
        void controller(int32_t ctl, float) override {
            events.push_back({2, ctl, eventOffset, (int32_t)renders.size()});
        }
        void render(float*, int32_t frames) override { renders.push_back(frames); }
    };
    constexpr int32_t frames = 64;
    // Off before on at a shared offset (midiEvLess order); the last note-off sits on the block's end.
    const MidiEv evs[] = {{0, true, 60, 1.0f}, {10, false, 60, 0.0f}, {10, true, 62, 1.0f}, {frames, false, 62, 0.0f}};
    const ExprEv xev[] = {{20, -1, kCtlDimBase + 1, 0.5f}};
    float dst[frames * 2] = {};
    auto run = [&](Probe& p) {
        playEvents(p, dst, frames, evs, 4, xev, 1, nullptr, 0, 0.0, 0.0, false,
            [&] { p.events.push_back({3, -1, p.eventOffset, (int32_t)p.renders.size()}); });
    };
    using E = std::array<int32_t, 4>;

    Probe timed;
    timed.timed = true;
    run(timed);
    const std::vector<E> wantTimed = {
        {1, 60, 0, 0}, {3, -1, 0, 0}, {0, 60, 10, 0}, {1, 62, 10, 0}, {2, 1, 20, 0}, {0, 62, 0, 1}};
    if (timed.renders != std::vector<int32_t>{frames} || timed.events != wantTimed || timed.eventOffset != 0)
        return false;

    Probe seg;
    run(seg);
    const std::vector<E> wantSeg = {
        {1, 60, 0, 0}, {3, -1, 0, 0}, {0, 60, 0, 1}, {1, 62, 0, 1}, {2, 1, 0, 2}, {0, 62, 0, 3}};
    return seg.renders == std::vector<int32_t>{10, 10, 44} && seg.events == wantSeg;
}

// specs/085: the articulation scheduler, block by block at exact samples, and delivery through
// playEvents. Returns 0 on success, else the number of the first check that failed.
int32_t Engine::articulationSelfTest() {
    constexpr double sr = 48000.0, spb = 24000.0; // 120 BPM
    constexpr int32_t B = 512;
    using AK = ArticulationActionKind;
    // 1 Long: latching, keyswitches 24/1 + 35/1 + CC58=81. 2 Legato: latching, CC58=76, lead 30 ms,
    // offset -150 ms. 3 Pizz: momentary, keyswitch 30/127, channel 2. Default = 1.
    const ArticulationAction actions[] = {
        {AK::Keyswitch, 0, 24, 1}, {AK::Keyswitch, 0, 35, 1}, {AK::ControlChange, 0, 58, 81},
        {AK::ControlChange, 0, 58, 76},
        {AK::Keyswitch, 2, 30, 127},
    };
    const ArticulationEntry entries[] = {
        {1, ArticulationTrigger::Latching, 0.0, 0.0, 0, 3},
        {2, ArticulationTrigger::Latching, -150.0, 30.0, 3, 1},
        {3, ArticulationTrigger::Momentary, 0.0, 0.0, 4, 1},
    };
    auto table = ArticulationTable::build(1, entries, 3, actions, 5);
    if (!table) return 1;

    auto makeClip = [&](std::vector<std::pair<double, int32_t>> notes, double len = 1.0) {
        MidiClip c;
        c.startBeat = 0.0;
        c.lengthBeats = 8.0;
        for (auto [beat, art] : notes) {
            Note n;
            n.pitch = 60;
            n.startBeat = beat;
            n.lengthBeats = len;
            n.articulation = art;
            c.notes.push_back(n);
        }
        c.articulations = buildArticulationTimeline(c, table);
        return c;
    };
    // Beat 0 Long, beat 1 Long again (no new switch), beat 2 Legato, beat 3 Pizz (momentary),
    // beat 4 a note with no articulation (the default: Long again, a change from Legato).
    std::vector<MidiClip> clips{makeClip({{0.0, 1}, {1.0, 1}, {2.0, 2}, {3.0, 3}, {4.0, 0}})};

    struct Abs {
        double at;
        ArtEvKind kind;
        int d1, d2, ch;
    };
    // Play [from, to) in blocks; the first block is a chase when `chase`.
    auto play = [&](double from, double to, double trackMs, bool chase, std::vector<Abs>& got, int* dropped = nullptr) {
        ArtEv buf[kMaxBlockArt];
        for (double s = from; s < to; s += B) {
            ArticulationBlock b{B, s, spb, sr, trackMs, chase && s == from, false, 0.0};
            b.chaseStart = chase ? from : -1.0;
            int d = 0;
            const int n = scheduleArticulations(clips, *table, b, buf, kMaxBlockArt, &d);
            if (dropped) *dropped += d;
            for (int i = 0; i < n; ++i) got.push_back({s + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
        }
    };
    auto count = [](const std::vector<Abs>& v, ArtEvKind k, int d1) {
        int c = 0;
        for (auto& e : v) c += e.kind == k && e.d1 == d1;
        return c;
    };
    auto at = [](const std::vector<Abs>& v, ArtEvKind k, int d1, int d2 = -1) {
        for (auto& e : v)
            if (e.kind == k && e.d1 == d1 && (d2 < 0 || e.d2 == d2)) return e.at;
        return -1.0;
    };

    // ---- a straight run from the start ----
    std::vector<Abs> run;
    play(0.0, 6 * spb, 0.0, true, run);
    // 2: Long at 0 in map order — key 24, key 35, CC58=81 — and its keys released 20 ms later.
    if (run.size() < 3 || run[0].at != 0.0 || run[0].kind != ArtEvKind::KeyOn || run[0].d1 != 24 || run[1].d1 != 35 ||
        run[2].kind != ArtEvKind::Control || run[2].d1 != 58 || run[2].d2 != 81)
        return 2;
    if (at(run, ArtEvKind::KeyOff, 24) != 960.0 || at(run, ArtEvKind::KeyOff, 35) != 960.0) return 3;
    // 4: the second Long note sends nothing (still in force): Long's keys go out twice in all — at
    // beat 0 and again at beat 4 where the default (Long) follows Legato.
    if (count(run, ArtEvKind::KeyOn, 24) != 2) return 4;
    // 5: Legato: offset -150 ms and lead 30 ms → 48000 − 7200 − 1440 = 39360.
    if (at(run, ArtEvKind::Control, 58, 76) != 39360.0) return 5;
    // 6: Pizz is momentary on channel 2: pressed with its note (beat 3), released at its end.
    double pOn = -1, pOff = -1;
    for (auto& e : run)
        if (e.d1 == 30 && e.ch == 2) (e.kind == ArtEvKind::KeyOn ? pOn : pOff) = e.at;
    if (pOn != 72000.0 || pOff != 96000.0) return 6;
    // 7: the default after Legato: Long again at beat 4.
    if (at(run, ArtEvKind::Control, 58, 81) != 0.0) return 7; // first one at 0 …
    {
        int n81 = 0;
        for (auto& e : run) n81 += e.kind == ArtEvKind::Control && e.d1 == 58 && e.d2 == 81 && e.at == 96000.0;
        if (n81 != 1) return 7; // … and one at beat 4
    }
    // 8: nothing else: 2 × Long (3 actions + 2 releases), Legato (1), Pizz (on + off).
    if (run.size() != 2 * 5 + 1 + 2) return 8;

    // ---- a track offset moves every switch with the notes ----
    std::vector<Abs> shifted;
    play(0.0, 6 * spb, -100.0, true, shifted);
    if (at(shifted, ArtEvKind::Control, 58, 76) != 39360.0 - 4800.0) return 9;
    double sOn = -1;
    for (auto& e : shifted)
        if (e.d1 == 30 && e.kind == ArtEvKind::KeyOn) sOn = e.at;
    if (sOn != 72000.0 - 4800.0) return 10;

    // ---- a seek into the Legato phrase: what is in force goes out once, at the landing ----
    std::vector<Abs> seek;
    const double land = 2.5 * spb; // 60000: past Legato's switch (39360), before Pizz
    play(land, land + 4 * B, 0.0, true, seek);
    if (seek.size() != 1 || seek[0].at != land || seek[0].kind != ArtEvKind::Control || seek[0].d2 != 76) return 11;
    // 12: without a chase, nothing is re-sent mid-phrase.
    std::vector<Abs> quiet;
    play(land, land + 4 * B, 0.0, false, quiet);
    if (!quiet.empty()) return 12;
    // 13: landing inside a held momentary note chases its switch too.
    std::vector<Abs> inPizz;
    play(3.5 * spb, 3.5 * spb + B, 0.0, true, inPizz);
    if (count(inPizz, ArtEvKind::KeyOn, 30) != 1 || at(inPizz, ArtEvKind::KeyOn, 30) != 3.5 * spb) return 13;

    // ---- a loop wrap onto a change at the loop start: no stale chase ahead of it ----
    {
        std::vector<Abs> wrap;
        ArtEv buf[kMaxBlockArt];
        ArticulationBlock b{B, 4 * spb, spb, sr, 0.0, true, true, 6 * spb}; // looping 4..6, just wrapped
        const int n = scheduleArticulations(clips, *table, b, buf, kMaxBlockArt);
        for (int i = 0; i < n; ++i) wrap.push_back({b.blockStart + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
        // 14: Long's change lands on the block's first sample; the chase (Legato, in force before) is skipped.
        if (count(wrap, ArtEvKind::Control, 58) != 1 || at(wrap, ArtEvKind::Control, 58, 81) != 4 * spb) return 14;
    }
    // 15: a seam lets go of a key pressed just before it.
    {
        std::vector<MidiClip> one{makeClip({{0.0, 1}})};
        ArtEv buf[kMaxBlockArt];
        ArticulationBlock b{B, 0.0, spb, sr, 0.0, false, true, 100.0}; // the key went down at 0, the seam came at 100
        const int n = scheduleArticulations(one, *table, b, buf, kMaxBlockArt);
        int offsAt0 = 0;
        for (int i = 0; i < n; ++i) offsAt0 += buf[i].kind == ArtEvKind::KeyOff && buf[i].off == 0;
        if (offsAt0 != 2) return 15;
    }
    // 16: releases sort before presses at the same sample.
    {
        std::vector<MidiClip> tight{makeClip({{0.0, 1}, {0.04, 3}}, 0.01)}; // Pizz 20 ms in = Long's release
        ArtEv buf[kMaxBlockArt];
        ArticulationBlock b{2048, 0.0, spb, sr, 0.0, false, false, 0.0};
        const int n = scheduleArticulations(tight, *table, b, buf, kMaxBlockArt);
        int firstOn = -1, lastOff = -1;
        for (int i = 0; i < n; ++i)
            if (buf[i].off == 960) {
                if (buf[i].kind == ArtEvKind::KeyOff) lastOff = i;
                else if (firstOn < 0) firstOn = i;
            }
        if (firstOn < 0 || lastOff < 0 || lastOff > firstOn) return 16;
    }
    // 17: a full buffer drops and counts, never overruns.
    {
        ArtEv small[2];
        int dropped = 0;
        ArticulationBlock b{B, 0.0, spb, sr, 0.0, false, false, 0.0};
        const int n = scheduleArticulations(clips, *table, b, small, 2, &dropped);
        if (n != 2 || dropped != 1) return 17;
    }
    // 18: an inactive clip sends nothing.
    {
        std::vector<MidiClip> off = clips;
        off[0].active = false;
        ArtEv buf[kMaxBlockArt];
        ArticulationBlock b{B, 0.0, spb, sr, 0.0, true, false, 0.0};
        if (scheduleArticulations(off, *table, b, buf, kMaxBlockArt) != 0) return 18;
    }

    // ---- delivery: ahead of the note at the same offset, on its channel, channel reset after ----
    struct Probe final : Instrument {
        std::vector<std::array<int32_t, 5>> events; // kind (1 on, 0 off, 2 cc, 3 program), d1, d2, offset, channel
        bool articulations = false;
        bool takesTimedEvents() const override { return true; }
        bool takesArticulations() const override { return articulations; }
        void setSampleRate(double) override {}
        void noteOn(int32_t p, float v) override { events.push_back({1, p, (int32_t)std::lround(v * 127.0f), eventOffset, eventChannel}); }
        void noteOff(int32_t p) override { events.push_back({0, p, 0, eventOffset, eventChannel}); }
        void allNotesOff() override {}
        void controller(int32_t c, float v) override { events.push_back({2, c, (int32_t)std::lround(v * 127.0f), eventOffset, eventChannel}); }
        void programChange(int32_t p) override { events.push_back({3, p, 0, eventOffset, eventChannel}); }
        void noteExpression(int32_t p, int32_t, float v) override {
            events.push_back({4, p, (int32_t)std::lround(v * 127.0f), eventOffset, eventChannel});
        }
        void render(float*, int32_t) override {}
    };
    Probe p;
    if (p.takesArticulations()) return 19; // the default is no: a built-in never gets keyswitches
    const MidiEv evs[] = {{100, true, 60, 0.5f}};
    const ArtEv aev[] = {{100, 0, ArtEvKind::KeyOn, 0, 24, 1}, {100, 1, ArtEvKind::Control, 5, 58, 81}, {100, 2, ArtEvKind::Program, 0, 12, 0}};
    float dst[B * 2] = {};
    playEvents(p, dst, B, evs, 1, nullptr, 0, aev, 3, 0.0, spb, true, [] {});
    using E5 = std::array<int32_t, 5>;
    const std::vector<E5> want = {{1, 24, 1, 100, 0}, {2, 58, 81, 100, 5}, {3, 12, 0, 100, 0}, {1, 60, 64, 100, 0}};
    if (p.events != want) return 20;
    if (p.eventChannel != 0) return 21;

    // 22: changing only a note's articulation makes the published timeline stale (publishRaw
    // rebuilds it), and the rebuilt one switches there.
    {
        MidiClip c = makeClip({{0.0, 1}, {1.0, 1}});
        MidiClip edited = c;
        edited.notes[1].articulation = 2;
        if (articulationNotesKey(c) == articulationNotesKey(edited)) return 22;
        if (!articulationTimelineCurrent(c, table) || articulationTimelineCurrent(edited, table)) return 22;
        auto rebuilt = buildArticulationTimeline(edited, table);
        if (rebuilt->changes.size() != 2 || rebuilt->changes[1].entryId != 2) return 22;
    }

    // ---- Session slots (T042): the slot's clip looped every 2 beats on its own clock ----
    {
        MidiClip slot = makeClip({{0.0, 1}, {1.0, 2}}); // Long at 0, Legato at 1, each pass
        constexpr double L = 2.0;
        auto playSlot = [&](bool loop, double from, double to, std::vector<Abs>& got) {
            ArtEv buf[kMaxBlockArt];
            for (double s = from; s < to; s += B) {
                ArticulationBlock b{B, s, spb, sr, 0.0, false, false, 0.0};
                const int n = scheduleSlotArticulations(slot, L, loop, *table, b, buf, kMaxBlockArt);
                for (int i = 0; i < n; ++i) // the last block runs past `to`: count only what falls before it
                    if (s + buf[i].off < to) got.push_back({s + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
            }
        };
        std::vector<Abs> looped;
        playSlot(true, 0.0, 3 * L * spb, looped);
        // 23: each pass switches to Long at its start and to Legato (−150 ms, lead 30 ms) after —
        // three passes, three of each, at their places (Legato: pass k at k·48000 + 24000 − 8640).
        int longs = 0, legatos = 0;
        bool placed = true;
        for (auto& e : looped) {
            if (e.kind == ArtEvKind::KeyOn && e.d1 == 24) { ++longs; placed &= std::fmod(e.at, L * spb) == 0.0; }
            if (e.kind == ArtEvKind::Control && e.d2 == 76) { ++legatos; placed &= std::fmod(e.at, L * spb) == 24000.0 - 8640.0; }
        }
        if (longs != 3 || legatos != 3 || !placed) return 23;
        // 24: a one-shot slot plays its single pass.
        std::vector<Abs> once;
        playSlot(false, 0.0, 3 * L * spb, once);
        if (count(once, ArtEvKind::KeyOn, 24) != 1 || count(once, ArtEvKind::Control, 58) != 2) return 24;
        // 25: a launch sends the switch whose lead falls before it, and an early switch reaches back
        // across the loop end. Legato on beat 0 (−150 ms, lead 30 ms) and Long on beat 1 of each pass:
        // the launch block's chase sends Legato at 0; after that, each pass's Legato goes out at the
        // end of the pass before (k·48000 − 8640) and its Long at k·48000 + 24000.
        MidiClip early = makeClip({{0.0, 2}, {1.0, 1}});
        std::vector<Abs> back;
        ArtEv buf[kMaxBlockArt];
        for (double s = 0.0; s < 2 * L * spb; s += B) {
            ArticulationBlock b{B, s, spb, sr, 0.0, s == 0.0, false, 0.0};
            const int n = scheduleSlotArticulations(early, L, true, *table, b, buf, kMaxBlockArt);
            for (int i = 0; i < n; ++i)
                if (s + buf[i].off < 2 * L * spb) back.push_back({s + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
        }
        std::vector<double> legatoAt, longAt;
        for (auto& e : back) {
            if (e.kind == ArtEvKind::Control && e.d2 == 76) legatoAt.push_back(e.at);
            if (e.kind == ArtEvKind::KeyOn && e.d1 == 24) longAt.push_back(e.at);
        }
        if (legatoAt != std::vector<double>{0.0, L * spb - 8640.0, 2 * L * spb - 8640.0} ||
            longAt != std::vector<double>{24000.0, L * spb + 24000.0})
            return 25;
    }

    // 26: a chase in a block shorter than 20 ms lets go exactly 20 ms later, in a subsequent block.
    {
        std::vector<Abs> got;
        const double land = 0.5 * spb; // inside the first Long phrase
        play(land, land + 3 * B, 0.0, true, got);
        if (count(got, ArtEvKind::KeyOn, 24) != 1 || count(got, ArtEvKind::KeyOff, 24) != 1 ||
            at(got, ArtEvKind::KeyOn, 24) != land || at(got, ArtEvKind::KeyOff, 24) != land + 960.0) return 26;
    }

    // 27: overlapping clips share the instrument's state. Clip A: Long at beats 0 and 2; clip B:
    // Legato at beat 1. Beat 2's Long must be sent again — B changed the articulation in between.
    {
        std::vector<MidiClip> overlap{makeClip({{0.0, 1}, {2.0, 1}}), makeClip({{1.0, 2}})};
        std::vector<Abs> got;
        ArtEv buf[kMaxBlockArt];
        for (double s = 0.0; s < 3 * spb; s += B) {
            ArticulationBlock b{B, s, spb, sr, 0.0, s == 0.0, false, 0.0};
            const int n = scheduleArticulations(overlap, *table, b, buf, kMaxBlockArt);
            for (int i = 0; i < n; ++i) got.push_back({s + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
        }
        int longs = 0, legatos = 0;
        for (auto& e : got) {
            longs += e.kind == ArtEvKind::Control && e.d2 == 81;
            legatos += e.kind == ArtEvKind::Control && e.d2 == 76;
        }
        if (longs != 2 || legatos != 1 || at(got, ArtEvKind::Control, 58, 81) != 0.0) return 27;
    }

    // 28: two overlapping momentary notes on the same key: the shorter one ending doesn't let go of
    // the key the longer one still holds. Pizz (key 30, channel 2) 3.0–5.0 and 3.5–3.75 beats.
    {
        MidiClip c;
        c.startBeat = 0.0;
        c.lengthBeats = 8.0;
        for (auto [start, len] : {std::pair{3.0, 2.0}, std::pair{3.5, 0.25}}) {
            Note n;
            n.pitch = 60;
            n.startBeat = start;
            n.lengthBeats = len;
            n.articulation = 3;
            c.notes.push_back(n);
        }
        c.articulations = buildArticulationTimeline(c, table);
        std::vector<MidiClip> one{c};
        std::vector<Abs> got;
        ArtEv buf[kMaxBlockArt];
        for (double s = 0.0; s < 6 * spb; s += B) {
            ArticulationBlock b{B, s, spb, sr, 0.0, false, false, 0.0};
            const int n = scheduleArticulations(one, *table, b, buf, kMaxBlockArt);
            for (int i = 0; i < n; ++i) got.push_back({s + buf[i].off, buf[i].kind, buf[i].data1, buf[i].data2, buf[i].channel});
        }
        int earlyOff = 0, finalOff = 0;
        for (auto& e : got)
            if (e.kind == ArtEvKind::KeyOff && e.d1 == 30) {
                earlyOff += e.at == 3.75 * spb;
                finalOff += e.at == 5.0 * spb;
            }
        if (earlyOff != 0 || finalOff != 1) return 28;
    }

    // 29: a short slot with a large offset: a one-beat loop (24000 samples = 500 ms) with a +1500 ms
    // momentary articulation — each pass's switch lands three passes later and must not be lost.
    {
        const ArticulationAction late[] = {{AK::Keyswitch, 0, 40, 100}};
        const ArticulationEntry lateE[] = {{1, ArticulationTrigger::Momentary, 1500.0, 0.0, 0, 1}};
        auto lateTable = ArticulationTable::build(0, lateE, 1, late, 1);
        if (!lateTable) return 29;
        MidiClip slot;
        slot.lengthBeats = 1.0;
        Note n;
        n.pitch = 60;
        n.startBeat = 0.0;
        n.lengthBeats = 0.25;
        n.articulation = 1;
        slot.notes.push_back(n);
        slot.articulations = buildArticulationTimeline(slot, lateTable);
        std::vector<double> ons;
        ArtEv buf[kMaxBlockArt];
        for (double s = 0.0; s < 6 * spb; s += B) {
            ArticulationBlock b{B, s, spb, sr, 0.0, false, false, 0.0};
            const int cnt = scheduleSlotArticulations(slot, 1.0, true, *lateTable, b, buf, kMaxBlockArt);
            for (int i = 0; i < cnt; ++i)
                if (buf[i].kind == ArtEvKind::KeyOn && s + buf[i].off < 6 * spb) ons.push_back(s + buf[i].off);
        }
        // Passes 0, 1, 2 land at 72000, 96000, 120000; pass 3 would land at 144000, past the window.
        if (ons != std::vector<double>{72000.0, 96000.0, 120000.0}) return 29;

        // 30: a 31.25 ms loop requires looking 48 passes back for this same offset. Its release
        // must also survive. Read just the block containing the first on, then the first off.
        slot.lengthBeats = 0.0625;
        slot.notes[0].lengthBeats = 0.03125;
        slot.articulations = buildArticulationTimeline(slot, lateTable);
        for (bool release : {false, true}) {
            const double due = 72000.0 + (release ? 750.0 : 0.0);
            ArticulationBlock b{B, due - 100, spb, sr, 0.0, false, false, 0.0};
            const int cnt = scheduleSlotArticulations(slot, 0.0625, true, *lateTable, b, buf, kMaxBlockArt);
            if (cnt != 1 || buf[0].off != 100 || buf[0].kind != (release ? ArtEvKind::KeyOff : ArtEvKind::KeyOn)) return 30;
        }
    }

    // 31: different momentary articulations in separate clips can hold the same explicit key.
    {
        ArticulationAction keys[] = {{AK::Keyswitch, 2, 30, 100}, {AK::Keyswitch, 2, 30, 100}};
        const ArticulationEntry entries[] = {{1, ArticulationTrigger::Momentary, 0, 0, 0, 1},
            {2, ArticulationTrigger::Momentary, 0, 0, 1, 1}};
        for (bool defaultChannel : {false, true}) {
            keys[0].channel = defaultChannel ? 0 : 2;
            keys[1].channel = defaultChannel ? 1 : 2;
            auto shared = ArticulationTable::build(0, entries, 2, keys, 2);
            auto a = makeClip({{0.0, 1}}, 2.0), b = makeClip({{0.5, 2}}, 0.5);
            a.articulations = buildArticulationTimeline(a, shared);
            b.articulations = buildArticulationTimeline(b, shared);
            ArtEv buf[kMaxBlockArt];
            ArticulationBlock block{B, spb - 100, spb, sr};
            if (scheduleArticulations({a, b}, *shared, block, buf, kMaxBlockArt) != 0) return 31;
            block.blockStart = 2 * spb - 100;
            const int cnt = scheduleArticulations({a, b}, *shared, block, buf, kMaxBlockArt);
            if (cnt != 1 || buf[0].off != 100 || buf[0].kind != ArtEvKind::KeyOff || buf[0].channel != keys[0].channel) return 31;
        }
    }

    // Exercise Session rendering itself, capturing the events delivered to a timed instrument.
    auto slotEngine = std::make_unique<Engine>();
    slotEngine->transport_.setSampleRate(sr);
    Track track(1, TrackType::Instrument);
    auto probe = std::make_shared<Probe>();
    track.instrument = probe;
    track.sessionSlots.resize(1);
    track.sessionPlayer->playing.store(0);
    auto& slot = track.sessionSlots[0];
    slot.hasClip = true;
    slot.lengthBeats = 1.0;
    Note note;
    note.startBeat = 0.5;
    note.lengthBeats = 0.25;
    note.articulation = 1;
    auto expr = std::make_shared<NoteExpr>();
    expr->dim[ExprPressure] = {{0.0f, 0.25f}, {0.25f, 0.75f}};
    note.expr = expr;
    slot.midi.notes = {note};
    // 32: both positive and negative track + articulation offsets move opening expression with
    // its note. It starts at 0.25 pressure at the note-on, never partway through the curve.
    for (double offset : {-100.0, 100.0}) {
        const ArticulationEntry entry{1, ArticulationTrigger::Latching, offset, 0, 0, 0};
        track.articulationTable = ArticulationTable::build(0, &entry, 1, nullptr, 0);
        track.setMidiOffsetMs(offset);
        track.sessionPlayer->localBeats = offset < 0 ? 0.09375 : 0.890625;
        probe->events.clear();
        slotEngine->renderSessionSlotRaw(track, dst, B, spb);
        const int32_t at = offset < 0 ? 150 : 225;
        const E5 on{1, 60, 102, at, 0}, pressure{4, 60, 32, at, 0};
        if (std::find(probe->events.begin(), probe->events.end(), on) == probe->events.end() ||
            std::find(probe->events.begin(), probe->events.end(), pressure) == probe->events.end()) return 32;
    }
    // 33: a note release more than one pass after its start must still reach the instrument.
    track.articulationTable.reset();
    track.setMidiOffsetMs(0);
    slot.midi.notes[0].expr.reset();
    slot.midi.notes[0].startBeat = 0;
    slot.midi.notes[0].lengthBeats = 3;
    track.sessionPlayer->localBeats = 3.0 - 0.015625;
    probe->events.clear();
    slotEngine->renderSessionSlotRaw(track, dst, B, spb);
    if (std::find(probe->events.begin(), probe->events.end(), E5{0, 60, 0, 375, 0}) == probe->events.end()) return 33;

    // 34: even one-frame render segments preserve the full chased-key hold.
    {
        ArtEv buf[kMaxBlockArt];
        const double land = 0.5 * spb;
        ArticulationBlock b{1, land, spb, sr, 0.0, true};
        b.chaseStart = land;
        if (scheduleArticulations(clips, *table, b, buf, kMaxBlockArt) != 3) return 34;
        b.chase = false;
        b.blockStart = land + 960;
        const int cnt = scheduleArticulations(clips, *table, b, buf, kMaxBlockArt);
        if (cnt != 2 || buf[0].kind != ArtEvKind::KeyOff || buf[0].off != 0 || buf[1].kind != ArtEvKind::KeyOff) return 34;
    }
    // 35: launch and legato launch restore a pre-launch switch through the real Session renderer,
    // and its release reaches the next block rather than being shortened to this block.
    {
        const ArticulationAction key{AK::Keyswitch, 0, 24, 100};
        const ArticulationEntry entry{1, ArticulationTrigger::Latching, 0, 30, 0, 1};
        track.articulationTable = ArticulationTable::build(0, &entry, 1, &key, 1);
        slot.midi.notes[0].lengthBeats = 1;
        slot.midi.articulations = buildArticulationTimeline(slot.midi, track.articulationTable);
        probe->articulations = true;
        for (bool legato : {false, true}) {
            track.sessionPlayer->localBeats = legato ? 0.5 : 0.0;
            track.sessionPlayer->requestLaunch(0, 0, legato);
            if (!track.sessionPlayer->maybeApply(0, B / spb, 0)) return 35;
            probe->events.clear();
            slotEngine->renderSessionSlotRaw(track, dst, B, spb);
            if (std::find(probe->events.begin(), probe->events.end(), E5{1, 24, 100, 0, 0}) == probe->events.end()) return 35;
            track.sessionPlayer->publish(B / spb);
            probe->events.clear();
            slotEngine->renderSessionSlotRaw(track, dst, B, spb);
            if (std::find(probe->events.begin(), probe->events.end(), E5{0, 24, 0, 448, 0}) == probe->events.end()) return 35;
        }
    }
    // 36: Session releases count rendered frames, even when tempo changes between the press and
    // release at a nonzero legato position. Check both faster and slower tempo, natural switches,
    // and clearing the map while the key is held. Every release still lands at frame 960.
    {
        const ArticulationAction keys[] = {{AK::Keyswitch, 0, 24, 100}, {AK::Keyswitch, 0, 25, 100}};
        const ArticulationEntry entries[] = {{1, ArticulationTrigger::Latching, 0, 0, 0, 1},
            {2, ArticulationTrigger::Latching, 0, 0, 1, 1}};
        const auto mapped = ArticulationTable::build(0, entries, 2, keys, 2);
        slot.midi.notes = makeClip({{0.0, 1}, {0.5, 2}}, 0.25).notes;
        slot.midi.articulations = buildArticulationTimeline(slot.midi, mapped);
        for (bool chase : {false, true})
            for (double nextSpb : {spb / 2, spb * 2})
                for (bool clearMap : {false, true}) {
                    track.articulationTable = mapped;
                    track.sessionPlayer->localBeats = chase ? 8.125 : 8.0;
                    track.sessionPlayer->requestLaunch(0, 0, true);
                    if (!track.sessionPlayer->maybeApply(0, B / spb, 0)) return 36;
                    if (!chase) track.sessionPlayer->playedBeats.store(1.0); // normal beat-8 switch, not a chase
                    probe->events.clear();
                    slotEngine->renderSessionSlotRaw(track, dst, B, spb);
                    if (std::count(probe->events.begin(), probe->events.end(), E5{1, 24, 100, 0, 0}) != 1 ||
                        track.sessionPlayer->articulationReleases.count != 1) return 36;
                    track.sessionPlayer->publish(B / spb);
                    if (clearMap) track.articulationTable.reset();
                    probe->events.clear();
                    slotEngine->renderSessionSlotRaw(track, dst, B, nextSpb);
                    if (std::count(probe->events.begin(), probe->events.end(), E5{0, 24, 0, 448, 0}) != 1 ||
                        track.sessionPlayer->articulationReleases.count != 0) return 36;
                }
    }
    // 37: a bounded pending queue never presses a key whose release can't be retained. Due offs
    // have priority, and an output buffer too small for them carries the rest to the next block.
    {
        std::vector<ArticulationAction> keys;
        for (int i = 0; i <= kMaxBlockArt; ++i) keys.push_back({AK::Keyswitch, 1 + i / 128, i % 128, 100});
        const ArticulationEntry entry{1, ArticulationTrigger::Latching, 0, 0, 0, static_cast<int32_t>(keys.size())};
        auto many = ArticulationTable::build(0, &entry, 1, keys.data(), static_cast<int32_t>(keys.size()));
        MidiClip clip = makeClip({{0.0, 1}});
        clip.articulations = buildArticulationTimeline(clip, many);
        ArticulationReleaseState pending;
        ArticulationBlock b{1, 0, spb, sr};
        b.releases = &pending;
        ArtEv buf[kMaxBlockArt];
        int dropped = 0;
        if (scheduleSlotArticulations(clip, 8, true, *many, b, buf, kMaxBlockArt, &dropped) != kMaxBlockArt ||
            dropped != 1 || pending.count != kMaxBlockArt) return 37;
        if (scheduleArticulationReleases(pending, 960, buf, kMaxBlockArt) != kMaxBlockArt || pending.count != 0) return 37;
        for (int i = 0; i < kMaxBlockArt; ++i)
            if (buf[i].kind != ArtEvKind::KeyOff || buf[i].off != 959 || buf[i].channel != keys[i].channel ||
                buf[i].data1 != keys[i].data1) return 37;
        pending.pending[0] = {0, 2, 30};
        pending.pending[1] = {0, 3, 31};
        pending.count = 2;
        if (scheduleArticulationReleases(pending, 1, buf, 1) != 1 || pending.count != 1 || buf[0].data1 != 30) return 37;
        if (scheduleArticulationReleases(pending, 1, buf, 1) != 1 || pending.count != 0 || buf[0].data1 != 31) return 37;
        track.sessionPlayer->articulationReleases.count = 1;
        track.sessionPlayer->requestLaunch(0, 0, true);
        if (!track.sessionPlayer->maybeApply(0, 1, 0) || track.sessionPlayer->articulationReleases.count != 0) return 37;
        track.sessionPlayer->articulationReleases.count = 1;
        track.sessionPlayer->requestStopNow();
        if (!track.sessionPlayer->maybeApply(0, 1, 0) || track.sessionPlayer->articulationReleases.count != 0) return 37;
    }
    // 38: mute/solo gating flushes an old latching hold. Its obsolete release must never resume
    // after unmuting and cut a new momentary hold of the same key.
    for (bool soloGate : {false, true}) {
        auto engine = std::make_unique<Engine>();
        const int id = engine->addInstrumentTrack(), other = engine->addAudioTrack();
        auto target = engine->cloneTrack(*engine->findTrackAuthoring(id));
        auto capture = std::make_shared<Probe>();
        capture->articulations = true;
        target->instrument = capture;
        const ArticulationAction key{AK::Keyswitch, 0, 24, 100};
        const ArticulationEntry entries[] = {{1, ArticulationTrigger::Latching, 0, 0, 0, 1},
            {2, ArticulationTrigger::Momentary, 0, 0, 0, 1}};
        target->articulationTable = ArticulationTable::build(0, entries, 2, &key, 1);
        target->sessionSlots.resize(1);
        auto& clip = target->sessionSlots[0];
        clip.hasClip = true;
        clip.lengthBeats = 4;
        clip.midi.notes = makeClip({{0.0, 1}, {0.046875, 2}}, 1.0).notes; // momentary starts at frame 1125
        engine->republishWithTrack(id, target);
        engine->setLaunchQuant(0);
        engine->transportPlay();
        engine->launchSlot(id, 0);
        engine->renderOffline(dst, B, sr); // first 512 frames: latching key has 448 frames left
        if (target->sessionPlayer->articulationReleases.count != 1) return 38;
        if (soloGate) engine->setTrackSolo(other, true); else engine->setTrackMute(id, true);
        engine->renderOffline(dst, 256, sr); // the mute flushes voices; the slot still advances
        if (soloGate) engine->setTrackSolo(other, false); else engine->setTrackMute(id, false);
        capture->events.clear();
        engine->renderOffline(dst, B, sr); // momentary key at offset 357, obsolete off would be 448
        if (target->sessionPlayer->articulationReleases.count != 0 ||
            std::count(capture->events.begin(), capture->events.end(), E5{1, 24, 100, 357, 0}) != 1 ||
            std::any_of(capture->events.begin(), capture->events.end(), [](const E5& e) { return e[0] == 0 && e[1] == 24; })) return 38;
    }
    return 0;
}

} // namespace nota
