// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Length — a MIDI effect (midiKind 3), reworked to mockup 3b. Forces every note to a
// chosen length: Sync (tempo division × gate), ms (absolute) or Gate % (proportion of how
// long the note was actually held). The forced note can trigger On note-on (normal) or On
// note-off (the note fires when the key is released). Length is modulated per note by
// velocity, key and randomness; Legato retriggers held notes and Clip Limit stops a forced
// note from outlasting the played note. Off scheduling uses a pending queue in absolute
// beats (tempo-safe), like the arpeggiator.
//
// Param layout is APPEND-ONLY: 0 Rate / 1 Gate stay put so old projects load unchanged.

#pragma once

#include "MidiDevice.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <memory>

namespace nota {

class MidiNoteLength final : public MidiDevice {
public:
    enum Param {
        PRate = 0, PGate, PMode, PMs, PPercent, PTrigger,
        PVelToLen, PKeyToLen, PRandom, PLegato, PClipLimit,
        kNumParams
    };
    // Sync divisions (beats): 1/16, 1/8, 1/8 dotted, 1/4.
    static constexpr double kDiv[4] = { 0.25, 0.5, 0.75, 1.0 };

    MidiNoteLength() { p_[PRate].store(1.0f); p_[PGate].store(1.0f); p_[PMs].store(250.0f); p_[PPercent].store(100.0f); }

    int32_t midiKind() const override { return 3; }
    const char* displayName() const override { return "Nota Length"; }

    void setSampleRate(double sr, int32_t) override { sr_ = sr > 0 ? sr : 44100.0; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case PRate: return "Rate"; case PGate: return "Gate"; case PMode: return "Mode"; case PMs: return "Ms";
            case PPercent: return "Percent"; case PTrigger: return "Trigger"; case PVelToLen: return "Vel to Len";
            case PKeyToLen: return "Key to Len"; case PRandom: return "Random"; case PLegato: return "Legato"; case PClipLimit: return "Clip Limit";
            default: return "";
        }
    }
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t i) const override {
        switch (i) {
            case PRate: return 3.0f; case PGate: return 2.0f; case PMode: return 2.0f; case PMs: return 2000.0f;
            case PPercent: return 200.0f; default: return 1.0f;   // trigger / modifiers / legato / cliplimit
        }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }
    void  reset() override { nPending_ = 0; nHeld_ = 0; intBeat_ = 0.0; }

    std::shared_ptr<MidiDevice> clone() const override {
        auto c = std::make_shared<MidiNoteLength>();
        for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
        c->setBypassed(bypassed());
        return c;
    }

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut,
                 int32_t frames, double beatStart, double spb, bool playing) override {
        nOut = 0;
        if (bypassed()) { for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i]; intBeat_ = playing ? beatStart : intBeat_; return; }
        const double b0 = playing ? beatStart : intBeat_;
        const double b1 = b0 + frames / spb;
        const int  mode = std::clamp((int)std::lround(p_[PMode].load(std::memory_order_relaxed)), 0, 2);
        const bool onOff = p_[PTrigger].load(std::memory_order_relaxed) >= 0.5f;   // fire on note-off
        const bool legato = p_[PLegato].load(std::memory_order_relaxed) >= 0.5f;
        const bool clip = p_[PClipLimit].load(std::memory_order_relaxed) >= 0.5f;

        // Emit due offs from earlier blocks.
        for (int i = 0; i < nPending_;) {
            if (pending_[i].beat < b1) {
                int32_t o = off(std::max(0.0, pending_[i].beat - b0), spb, frames);
                if (nOut < maxOut) out[nOut++] = { o, false, pending_[i].pitch, 0.0f };
                pending_[i] = pending_[--nPending_];
            } else ++i;
        }

        for (int i = 0; i < nIn; ++i) {
            const MidiEv& e = in[i];
            const double evBeat = b0 + (double)e.off / spb;
            if (e.on) {
                addHeld(e.pitch, evBeat, e.vel);
                if (!onOff) {
                    if (legato) cutAll(e.off, out, nOut, maxOut);
                    if (nOut < maxOut) out[nOut++] = e;                         // pass the on through
                    if (mode != 2) fireOff(e.pitch, evBeat, len(e.vel, e.pitch, 0.0, mode, spb), b0, b1, spb, frames, out, nOut, maxOut);
                    // Gate% waits for the note-off to know the held length.
                }
            } else {
                Held* h = findHeld(e.pitch);
                const float hv = h ? h->vel : 0.8f;
                const double heldBeats = h ? std::max(0.0, evBeat - h->onBeat) : 0.25;
                const double onBeat = h ? h->onBeat : evBeat;
                if (onOff) {
                    if (legato) cutAll(e.off, out, nOut, maxOut);
                    if (nOut < maxOut) out[nOut++] = { e.off, true, e.pitch, hv };   // fire the note on release
                    fireOff(e.pitch, evBeat, len(hv, e.pitch, heldBeats, mode, spb), b0, b1, spb, frames, out, nOut, maxOut);
                } else if (mode == 2) {
                    fireOff(e.pitch, onBeat, len(hv, e.pitch, heldBeats, 2, spb), b0, b1, spb, frames, out, nOut, maxOut);
                } else if (clip) {
                    if (cancelPending(e.pitch) && nOut < maxOut) out[nOut++] = { e.off, false, e.pitch, 0.0f };  // don't outlast the played note
                }
                // else: swallow the incoming off (fixed forced length wins)
                removeHeld(e.pitch);
            }
        }
        intBeat_ = b1;
    }

private:
    // Length in beats for one note, with the velocity / key / random modifiers.
    double len(float vel, int pitch, double heldBeats, int mode, double spb) {
        double L;
        if (mode == 2)      L = heldBeats * (std::clamp(p_[PPercent].load(std::memory_order_relaxed), 1.0f, 200.0f) / 100.0);
        else if (mode == 1) L = (std::max(1.0f, p_[PMs].load(std::memory_order_relaxed)) / 1000.0) * sr_ / spb;   // ms → beats
        else                L = kDiv[std::clamp((int)std::lround(p_[PRate].load(std::memory_order_relaxed)), 0, 3)]
                              * std::clamp(p_[PGate].load(std::memory_order_relaxed), 0.05f, 2.0f);
        L *= 1.0 + (double)p_[PVelToLen].load(std::memory_order_relaxed) * ((double)vel - 0.5) * 2.0;
        L *= 1.0 + (double)p_[PKeyToLen].load(std::memory_order_relaxed) * ((pitch - 60) / 36.0);
        const float rnd = p_[PRandom].load(std::memory_order_relaxed);
        if (rnd > 0.0f) L *= 1.0 + (double)rnd * ((double)frand() - 0.5) * 2.0;
        return std::max(0.01, L);
    }
    void fireOff(int pitch, double onBeat, double lenBeats, double b0, double b1, double spb, int frames,
                 MidiEv* out, int& nOut, int maxOut) {
        const double offBeat = onBeat + lenBeats;
        if (offBeat < b1) { if (nOut < maxOut) out[nOut++] = { off(std::max(0.0, offBeat - b0), spb, frames), false, pitch, 0.0f }; }
        else if (nPending_ < kMaxPending) pending_[nPending_++] = { pitch, offBeat };
    }
    void cutAll(int32_t offInBlock, MidiEv* out, int& nOut, int maxOut) {
        for (int i = 0; i < nPending_; ++i) if (nOut < maxOut) out[nOut++] = { offInBlock, false, pending_[i].pitch, 0.0f };
        nPending_ = 0;
    }
    bool cancelPending(int32_t pitch) {
        for (int i = 0; i < nPending_; ++i) if (pending_[i].pitch == pitch) { pending_[i] = pending_[--nPending_]; return true; }
        return false;
    }
    struct Held { int32_t pitch; double onBeat; float vel; };
    Held* findHeld(int32_t pitch) { for (int i = 0; i < nHeld_; ++i) if (held_[i].pitch == pitch) return &held_[i]; return nullptr; }
    void addHeld(int32_t pitch, double onBeat, float vel) { if (auto* h = findHeld(pitch)) { h->onBeat = onBeat; h->vel = vel; return; } if (nHeld_ < kMaxHeld) held_[nHeld_++] = { pitch, onBeat, vel }; }
    void removeHeld(int32_t pitch) { for (int i = 0; i < nHeld_; ++i) if (held_[i].pitch == pitch) { held_[i] = held_[--nHeld_]; return; } }
    static int32_t off(double beats, double spb, int32_t frames) {
        long v = (long)std::floor(beats * spb + 0.5); if (v < 0) v = 0; if (v >= frames) v = frames - 1; return (int32_t)v;
    }
    float frand() { rng_ ^= rng_ << 13; rng_ ^= rng_ >> 17; rng_ ^= rng_ << 5; return (rng_ & 0xFFFFFF) / (float)0x1000000; }

    struct Off { int32_t pitch; double beat; };
    static constexpr int kMaxPending = 128, kMaxHeld = 64;
    Off  pending_[kMaxPending]; int nPending_ = 0;
    Held held_[kMaxHeld]; int nHeld_ = 0;
    double intBeat_ = 0.0, sr_ = 44100.0;
    uint32_t rng_ = 0x9E3779B9u;
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
