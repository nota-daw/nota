// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MidiRack implementation — routing notes into chains, running each chain's MIDI effects,
// and merging their output (see MidiRack.h).

#include "MidiRack.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>

namespace nota {

namespace {

// Block order: by sample offset, a note-off before a note-on at the same offset (so a
// retriggered pitch ends before it starts again).
bool before(const MidiEv& a, const MidiEv& b) {
    if (a.off != b.off) return a.off < b.off;
    return !a.on && b.on;
}

} // namespace

MidiRack::MidiRack() {
    addChain(-1); // a new rack starts with one empty chain
    setRackMerge(true);
    setRackThru(true);
}

const char* MidiRack::paramName(int32_t index) const {
    if (index < 0 || index >= kNumMacros) return "";
    auto& buf = const_cast<std::array<char[24], kNumMacros>&>(paramNames_)[index];
    const std::string n = macroName(index); // message thread (editors, automation lanes)
    std::snprintf(buf, sizeof(buf), "%s", n.c_str());
    return buf;
}

std::shared_ptr<MidiDevice> MidiRack::clone() const {
    auto r = std::make_shared<MidiRack>();
    r->setRates(sampleRate_, maxBlock_);
    const auto blob = serialize();
    r->deserialize(blob.data(), static_cast<int32_t>(blob.size()));
    r->setBypassed(bypassed());
    return r;
}

uint64_t MidiRack::structureOf(const RackState& st) const {
    // Chains and their MIDI effects, by identity: a fork for an undoable edit shares the
    // devices, so it keeps the signature; adding, removing or moving anything changes it.
    uint64_t h = 1469598103934665603ull ^ static_cast<uint64_t>(st.chains.size());
    for (const auto& c : st.chains) {
        h = (h ^ static_cast<uint64_t>(c.midiDevices.size())) * 1099511628211ull;
        for (const auto& d : c.midiDevices) h = (h ^ reinterpret_cast<uintptr_t>(d.get())) * 1099511628211ull;
    }
    return h;
}

void MidiRack::emitMerged(const MidiEv& ev, MidiEv* out, int& nOut, int maxOut) {
    auto& rt = *rt_;
    const int32_t p = std::clamp(ev.pitch, 0, 127);
    if (ev.on) {
        // Merge: a pitch already sounding from another source isn't started again.
        if (rackMerge() && rt.held[p] > 0) {
            if (rt.held[p] < 255) ++rt.held[p];
            return;
        }
        if (rt.held[p] < 255) ++rt.held[p];
    } else {
        if (rt.held[p] == 0) return; // nothing of ours sounds there
        --rt.held[p];
        if (rackMerge() && rt.held[p] > 0) return; // another source still holds it
    }
    if (nOut < maxOut) out[nOut++] = ev;
}

void MidiRack::cutAll(const RackState& st, MidiEv* out, int& nOut, int maxOut) {
    auto& rt = *rt_;
    for (int32_t p = 0; p < 128; ++p) {
        while (rt.held[p] > 0) {
            if (nOut < maxOut) out[nOut++] = MidiEv{0, false, p, 0.0f};
            if (rackMerge()) rt.held[p] = 0;
            else --rt.held[p];
        }
        rt.route[p] = 0;
        rt.thru[p] = false;
    }
    rt.clearOut();
    for (const auto& c : st.chains)
        for (const auto& d : c.midiDevices)
            if (d) d->reset();
}

void MidiRack::process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames, double beatStart,
    double spb, bool playing) {
    nOut = 0;
    const auto done = renderScope();
    RackState* st = live();
    if (bypassed() || !st) {
        for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i];
        return;
    }
    auto& rt = *rt_;

    // A different set of chains or devices: end what this rack holds rather than guess.
    const uint64_t sig = structureOf(*st);
    if (sig != rt.structure) {
        if (rt.structure != 0) cutAll(*st, out, nOut, maxOut);
        rt.structure = sig;
    }

    const int32_t nChains = std::min<int32_t>(static_cast<int32_t>(st->chains.size()), kMaxChains);
    bool anySolo = false;
    for (int32_t c = 0; c < nChains; ++c)
        if (st->chains[c].ctl->solo.load(std::memory_order_relaxed)) anySolo = true;
    const bool select = rackMode() == 2;
    const int32_t selector = static_cast<int32_t>(std::lround(chainSelect() * 127.0f));

    // 1. Route the input: a note-on goes to every chain that takes it; its note-off follows it
    //    to the same chains, whatever changed in between.
    int nPool = 0;
    auto pool = [&](const MidiEv& ev, int16_t chain) {
        if (nPool < kPoolEvents) {
            pool_[nPool] = Tagged{ev, chain, nPool};
            ++nPool;
        }
    };
    for (int i = 0; i < nIn; ++i) {
        const MidiEv& ev = in[i];
        const int32_t p = std::clamp(ev.pitch, 0, 127);
        if (ev.on) {
            const int32_t v = std::clamp(static_cast<int32_t>(std::lround(ev.vel * 127.0f)), 0, 127);
            uint16_t mask = 0;
            for (int32_t c = 0; c < nChains; ++c) {
                const auto& z = *st->chains[c].ctl;
                constexpr auto r = std::memory_order_relaxed;
                if (z.mute.load(r) || (anySolo && !z.solo.load(r))) continue;
                if (p < z.keyLo.load(r) || p > z.keyHi.load(r) || v < z.velLo.load(r) || v > z.velHi.load(r)) continue;
                if (select && (selector < z.selLo.load(r) || selector > z.selHi.load(r))) continue;
                mask |= static_cast<uint16_t>(1u << c);
            }
            rt.route[p] |= mask;
            if (mask == 0 && rackThru()) {
                rt.thru[p] = true;
                pool(ev, -1);
            }
        } else if (rt.thru[p]) {
            rt.thru[p] = false;
            pool(ev, -1);
        }
    }

    // 2. Each chain: its share of the input through its MIDI effects, then transpose and
    //    velocity scale on the way out.
    for (int32_t c = 0; c < nChains; ++c) {
        auto& chain = st->chains[c];
        const uint16_t bit = static_cast<uint16_t>(1u << c);
        int n = 0;
        for (int i = 0; i < nIn; ++i) {
            const int32_t p = std::clamp(in[i].pitch, 0, 127);
            if ((rt.route[p] & bit) && n < kMaxEvents) bufA_[n++] = in[i];
        }
        MidiEv* a = bufA_;
        MidiEv* b = bufB_;
        for (auto& d : chain.midiDevices) {
            if (!d) continue;
            int m = 0;
            d->process(a, n, b, m, kMaxEvents, frames, beatStart, spb, playing);
            std::swap(a, b);
            n = m;
        }
        const int32_t tr = chain.ctl->transpose.load(std::memory_order_relaxed);
        const float vs = chain.ctl->velScale.load(std::memory_order_relaxed);
        float activity = chain.ctl->meter.load(std::memory_order_relaxed) * 0.85f;
        for (int i = 0; i < n; ++i) {
            MidiEv ev = a[i];
            const int32_t src = std::clamp(ev.pitch, 0, 127);
            if (ev.on) {
                if (vs <= 0.0f) continue; // Volume at 0: this chain sends nothing
                const int32_t q = std::clamp(src + tr, 0, 127);
                if (rt.outPitch[c][src] >= 0) { // a retrigger: end the one it replaces
                    MidiEv off = ev;
                    off.on = false;
                    off.pitch = rt.outPitch[c][src];
                    pool(off, static_cast<int16_t>(c));
                }
                rt.outPitch[c][src] = static_cast<int8_t>(q);
                ev.pitch = q;
                ev.vel = std::clamp(ev.vel * vs, 1.0f / 127.0f, 1.0f);
                activity = 1.0f;
            } else {
                const int32_t q = rt.outPitch[c][src];
                if (q < 0) continue; // never started (silent chain) or already ended
                rt.outPitch[c][src] = -1;
                ev.pitch = q;
            }
            pool(ev, static_cast<int16_t>(c));
        }
        chain.ctl->meter.store(activity, std::memory_order_relaxed);
    }
    // The route of a released key is done once every chain has seen its note-off.
    for (int i = 0; i < nIn; ++i)
        if (!in[i].on) rt.route[std::clamp(in[i].pitch, 0, 127)] = 0;

    // 3. Merge in time order.
    std::sort(pool_, pool_ + nPool, [](const Tagged& x, const Tagged& y) {
        if (before(x.ev, y.ev)) return true;
        if (before(y.ev, x.ev)) return false;
        return x.seq < y.seq;
    });
    for (int i = 0; i < nPool; ++i) emitMerged(pool_[i].ev, out, nOut, maxOut);
}

void MidiRack::reset() {
    auto& rt = *rt_;
    std::memset(rt.route, 0, sizeof(rt.route));
    std::memset(rt.thru, 0, sizeof(rt.thru));
    std::memset(rt.held, 0, sizeof(rt.held));
    rt.clearOut();
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        for (auto& d : c.midiDevices)
            if (d) d->reset();
}

void MidiRack::timeJump(double deltaBeats) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        for (auto& d : c.midiDevices)
            if (d) d->timeJump(deltaBeats);
}

} // namespace nota
