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

RackInstrument::RackInstrument() : evStore_(new MidiEv[kMaxMidiChains * kChainEvents + 2048]) {
    chainEvs_ = evStore_.get();
    scratchA_ = chainEvs_ + kMaxMidiChains * kChainEvents;
    scratchB_ = scratchA_ + 1024;
}

namespace {
bool evBefore(const MidiEv& a, const MidiEv& b) {
    if (a.off != b.off) return a.off < b.off;
    return !a.on && b.on; // a note-off before a note-on at the same sample
}

// A chain's MIDI effects by identity (0 = none): a fork for an undoable edit shares them, so
// it keeps the signature; adding, removing or replacing one changes it.
template <class Chain> uint64_t midiSig(const Chain& c) {
    if (c.midiDevices.empty()) return 0;
    uint64_t h = 1469598103934665603ull ^ static_cast<uint64_t>(c.midiDevices.size());
    for (const auto& d : c.midiDevices) h = (h ^ reinterpret_cast<uintptr_t>(d.get())) * 1099511628211ull;
    return h | 1; // never 0
}
} // namespace

bool RackInstrument::takesBlockMidi() const {
    RackState* st = live();
    if (!st) return false;
    for (const auto& c : st->chains)
        if (!c.midiDevices.empty()) return true;
    return false;
}

bool RackInstrument::midiChainAt(int32_t c) const {
    RackState* st = live();
    return st && c >= 0 && c < kMaxMidiChains && c < static_cast<int32_t>(st->chains.size()) && !st->chains[c].midiDevices.empty();
}

void RackInstrument::syncChains() {
    RackState* st = live();
    if (!st) return;
    auto& m = *mono_;
    const int32_t n = static_cast<int32_t>(st->chains.size());
    uint64_t h = 1469598103934665603ull ^ static_cast<uint64_t>(n);
    for (int32_t c = 0; c < n; ++c) {
        const auto& ch = st->chains[c];
        h = (h ^ reinterpret_cast<uintptr_t>(ch.instrument.get())) * 1099511628211ull;
        h = (h ^ (c < kMaxMidiChains ? midiSig(ch) : 0)) * 1099511628211ull;
    }
    if (h == m.structure) return;
    const bool first = m.structure == 0;
    m.structure = h;

    // Old index of each chain still here (by its instrument), then the tables at the new places.
    uint16_t route[128] = {};
    uint8_t held[kMaxMidiChains][128] = {};
    const void* ids[kMaxMidiChains] = {};
    uint64_t sigs[kMaxMidiChains] = {};
    for (int32_t c = 0; c < n; ++c) {
        auto& ch = st->chains[c];
        if (!ch.instrument) continue;
        const uint64_t sig = c < kMaxMidiChains ? midiSig(ch) : 0;
        int32_t o = -1;
        for (int32_t k = 0; k < kMaxMidiChains && !first; ++k)
            if (m.chainId[k] == ch.instrument.get()) o = k;
        if (o >= 0 && m.chainSig[o] != 0) {
            if (m.chainSig[o] == sig && c < kMaxMidiChains) {
                // The same MIDI effects at a new place: its held keys and notes move with it.
                std::memcpy(held[c], m.outHeld[o], 128);
                for (int32_t p = 0; p < 128; ++p)
                    if (m.midiRoute[p] & (1u << o)) route[p] |= static_cast<uint16_t>(1u << c);
            } else {
                // Its MIDI effects changed (or it's a plain chain now): end the notes they started
                // and what the remaining ones still hold; a held key plays again when struck again.
                for (int32_t p = 0; p < 128; ++p)
                    if (m.outHeld[o][p]) ch.instrument->noteOff(p);
                for (auto& d : ch.midiDevices)
                    if (d) d->reset();
            }
        } else if (o >= 0 && sig != 0) {
            // A plain chain took MIDI effects: the held keys it played directly end now (its
            // ordinary note-offs skip MIDI chains).
            for (int32_t i = 0; i < m.heldN; ++i) ch.instrument->noteOff(m.held[i]);
        }
        if (c < kMaxMidiChains) {
            ids[c] = ch.instrument.get();
            sigs[c] = sig;
        }
    }
    std::memcpy(m.midiRoute, route, sizeof(route));
    std::memcpy(m.outHeld, held, sizeof(held));
    std::memcpy(m.chainId, ids, sizeof(ids));
    std::memcpy(m.chainSig, sigs, sizeof(sigs));
}

void RackInstrument::deliver(int32_t c, Instrument& inst, const MidiEv& ev) {
    uint8_t& h = mono_->outHeld[c][std::clamp(ev.pitch, 0, 127)];
    if (ev.on) {
        if (h < 255) ++h;
        inst.noteOn(ev.pitch, ev.vel);
    } else {
        if (h > 0) --h;
        inst.noteOff(ev.pitch);
    }
}

void RackInstrument::blockMidi(const MidiEv* evs, int n, int32_t frames, double beatStart, double spb, bool playing) {
    blockPos_ = 0;
    syncChains();
    RackState* st = live();
    const int32_t nc = st ? std::min<int32_t>(static_cast<int32_t>(st->chains.size()), kMaxMidiChains) : 0;
    for (int32_t c = 0; c < kMaxMidiChains; ++c) chainCount_[c] = chainCursor_[c] = 0;
    // Route in block order: a note-on goes to the MIDI chains whose zone holds it, its note-off to
    // the chains its note-on went to — even when the key is played again within the block.
    auto& route = mono_->midiRoute;
    n = std::min(n, 1024);
    for (int i = 0; i < n; ++i) {
        const MidiEv& ev = evs[i];
        const int32_t p = std::clamp(ev.pitch, 0, 127);
        if (ev.on) {
            const int32_t v = std::clamp(static_cast<int32_t>(std::lround(ev.vel * 127.0f)), 0, 127);
            uint16_t mask = 0;
            for (int32_t c = 0; c < nc; ++c) {
                const auto& chain = st->chains[c];
                if (chain.midiDevices.empty() || !chain.instrument) continue;
                const auto& z = *chain.ctl;
                constexpr auto r = std::memory_order_relaxed;
                if (p < z.keyLo.load(r) || p > z.keyHi.load(r) || v < z.velLo.load(r) || v > z.velHi.load(r)) continue;
                mask |= static_cast<uint16_t>(1u << c);
            }
            route[p] |= mask;
            evMask_[i] = mask;
        } else {
            evMask_[i] = route[p];
            route[p] = 0;
        }
    }
    for (int32_t c = 0; c < nc; ++c) {
        auto& chain = st->chains[c];
        if (chain.midiDevices.empty() || !chain.instrument) continue;
        const uint16_t bit = static_cast<uint16_t>(1u << c);
        int m = 0;
        for (int i = 0; i < n; ++i)
            if (evMask_[i] & bit) scratchA_[m++] = evs[i];
        MidiEv* a = scratchA_;
        MidiEv* b = scratchB_;
        for (auto& d : chain.midiDevices) {
            if (!d) continue;
            int out = 0;
            d->process(a, m, b, out, 1024, frames, beatStart, spb, playing);
            std::swap(a, b);
            m = out;
        }
        std::sort(a, a + m, evBefore);
        m = std::min(m, kChainEvents);
        std::copy_n(a, m, chainEvs_ + c * kChainEvents);
        chainCount_[c] = m;
    }
}

void RackInstrument::midiTimeJump(double deltaBeats) {
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains)
        for (auto& d : c.midiDevices)
            if (d) d->timeJump(deltaBeats);
}

void RackInstrument::fanOn(int32_t pitch, float velocity, int32_t glideFrom) {
    RackState* st = live();
    if (!st) return;
    const int32_t v = std::clamp((int32_t)std::lround(velocity * 127.0f), 0, 127);
    const float bend = glideFrom >= 0 ? static_cast<float>(glideFrom - pitch) : 0.0f;
    syncChains();
    const int32_t n = static_cast<int32_t>(st->chains.size());
    for (int32_t ci = 0; ci < n; ++ci) {
        auto& c = st->chains[ci];
        if (!c.instrument || midiChainAt(ci)) continue; // a MIDI chain plays its effects' notes (blockMidi)
        auto* z = c.ctl.get();
        if (pitch < z->keyLo.load(std::memory_order_relaxed) || pitch > z->keyHi.load(std::memory_order_relaxed) ||
            v < z->velLo.load(std::memory_order_relaxed) || v > z->velHi.load(std::memory_order_relaxed))
            continue;
        c.instrument->noteOn(pitch, velocity);
        // Right after the note-on a bend lands without smoothing: the note starts at the old pitch.
        if (bend != 0.0f && c.instrument->supportsMpe())
            c.instrument->noteExpression(pitch, ExprBend, bend);
    }
}

void RackInstrument::fanOff(int32_t pitch) {
    RackState* st = live();
    if (!st) return;
    // Plain chains only: a MIDI chain's effects decide when its notes end (a Length may hold one
    // past the key). A chain that turned into a MIDI chain or back is reconciled by syncChains.
    syncChains();
    const int32_t n = static_cast<int32_t>(st->chains.size());
    for (int32_t ci = 0; ci < n; ++ci)
        if (st->chains[ci].instrument && !midiChainAt(ci)) st->chains[ci].instrument->noteOff(pitch);
}

void RackInstrument::endGlide() {
    auto& m = *mono_;
    if (m.glideLeft > 0 && m.sounding >= 0)
        if (RackState* st = live()) {
            const int32_t n = static_cast<int32_t>(st->chains.size());
            for (int32_t ci = 0; ci < n; ++ci) {
                auto& c = st->chains[ci];
                if (c.instrument && !midiChainAt(ci) && c.instrument->supportsMpe())
                    c.instrument->noteExpression(m.sounding, ExprBend, 0.0f);
            }
        }
    m.glideLeft = 0;
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
    endGlide();
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
    std::memset(m.midiRoute, 0, sizeof(m.midiRoute));
    std::memset(m.outHeld, 0, sizeof(m.outHeld));
    for (int32_t c = 0; c < kMaxMidiChains; ++c) chainCount_[c] = 0; // nothing queued plays after this
    RackState* st = live();
    if (!st) return;
    for (auto& c : st->chains) {
        if (c.instrument) c.instrument->allNotesOff();
        for (auto& d : c.midiDevices)
            if (d) d->reset(); // flush an Arp's held / pending notes
    }
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
    syncChains();
    auto& m = *mono_;
    if (m.glideLeft > 0 && m.sounding >= 0) {
        m.glideLeft = std::max<int64_t>(0, m.glideLeft - frames);
        const float bend = m.glideBend * static_cast<float>(m.glideLeft) / static_cast<float>(m.glideTotal);
        const int32_t n = static_cast<int32_t>(st->chains.size());
        for (int32_t ci = 0; ci < n; ++ci) {
            auto& c = st->chains[ci];
            if (c.instrument && !midiChainAt(ci) && c.instrument->supportsMpe())
                c.instrument->noteExpression(m.sounding, ExprBend, bend);
        }
    }
    for (auto& c : st->chains) {
        if (!c.instrument) continue;
        const bool solo = c.ctl->solo.load(std::memory_order_relaxed);
        const bool mute = c.ctl->mute.load(std::memory_order_relaxed);
        // A chain a sidechain key listens to (specs/050) renders even when muted or solo-gated
        // — the "ghost" chain feeds its key without being heard.
        const PartTap* tap = nTaps_ > 0 ? tapFor(c.uid) : nullptr;
        const bool silent = mute || (anySolo && !solo);
        if (silent && !tap) {
            c.ctl->meter.store(0.0f, std::memory_order_relaxed);
            // A silent MIDI chain still takes its notes, so a note held across an unmute ends.
            const int32_t ci = static_cast<int32_t>(&c - st->chains.data());
            if (midiChainAt(ci)) {
                int32_t& k = chainCursor_[ci];
                const MidiEv* evs = chainEvs_ + ci * kChainEvents;
                for (; k < chainCount_[ci] && evs[k].off < blockPos_ + frames; ++k) deliver(ci, *c.instrument, evs[k]);
            }
            continue;
        }
        std::memset(scratch_, 0, sizeof(float) * frames * 2);
        const int32_t ci = static_cast<int32_t>(&c - st->chains.data());
        if (midiChainAt(ci)) {
            // Play this chain's MIDI-effect output into its instrument at the exact samples.
            int32_t cursor = 0;
            const int32_t end = blockPos_ + frames;
            int32_t& k = chainCursor_[ci];
            const MidiEv* evs = chainEvs_ + ci * kChainEvents;
            while (k < chainCount_[ci]) {
                const MidiEv& ev = evs[k];
                if (ev.off >= end) break;
                const int32_t at = std::clamp(ev.off - blockPos_, 0, frames);
                if (at > cursor) {
                    c.instrument->render(&scratch_[cursor * 2], at - cursor);
                    cursor = at;
                }
                deliver(ci, *c.instrument, ev);
                ++k;
            }
            if (cursor < frames) c.instrument->render(&scratch_[cursor * 2], frames - cursor);
        } else {
            c.instrument->render(scratch_, frames);
        }
        if (tap) addTap(tap->pre, scratch_, frames); // Pre: the chain before its inserts
        for (auto& d : c.devices)
            if (d && !d->bypassed()) d->process(scratch_, frames);
        const float g = c.ctl->gain.load(std::memory_order_relaxed);
        const float pan = c.ctl->pan.load(std::memory_order_relaxed);
        const float pl = g * (pan > 0.0f ? 1.0f - pan : 1.0f); // the chain's own gain / pan
        const float pr = g * (pan < 0.0f ? 1.0f + pan : 1.0f);
        if (tap) // Post: after its inserts and gain / pan, before the rack volume and the sum
            for (int32_t i = 0; i < frames; ++i) {
                tap->post[i * 2] += scratch_[i * 2] * pl;
                tap->post[i * 2 + 1] += scratch_[i * 2 + 1] * pr;
            }
        if (silent) {
            c.ctl->meter.store(0.0f, std::memory_order_relaxed);
            continue;
        }
        const float lg = pl * rv, rg = pr * rv;
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
    blockPos_ += frames; // the next segment of this block starts here
}

// ---- parts (specs/050) ------------------------------------------------------------------
int32_t RackInstrument::partCount() const {
    RackState* st = live();
    return st ? static_cast<int32_t>(st->chains.size()) : 0;
}

int32_t RackInstrument::partIndex(uint64_t id) const {
    RackState* st = live();
    if (!st || id == 0) return -1;
    for (size_t i = 0; i < st->chains.size(); ++i)
        if (st->chains[i].uid == id) return static_cast<int32_t>(i);
    return -1;
}

std::string RackInstrument::partName(int32_t index) const {
    std::string n = chainName(index);
    if (!n.empty()) return n;
    Instrument* in = chainInstrument(index);
    return in ? in->displayName() : std::string{};
}

std::shared_ptr<Instrument> RackInstrument::clone() const {
    auto r = std::make_shared<RackInstrument>();
    r->setSampleRate(sampleRate_);
    auto blob = serialize();
    r->setState(blob.data(), static_cast<int32_t>(blob.size()));
    return r;
}

} // namespace nota
