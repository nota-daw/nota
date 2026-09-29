// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Length — a MIDI effect (midiKind 3), almanac rework (mockups 1a / 1b). Forces every
// note to a chosen length: Sync (a tempo division), ms (absolute) or Gate % (a proportion of
// how long the note was actually held). The forced note starts from the note-on (normal) or
// from the note-off (it fires when the key is released, the length added after the played
// note). Length is shaped per note by velocity and key (bipolar: + makes loud / low notes
// longer, − the opposite) and by Random (± spread per note). Legato stretches each note to
// the start of the next one; Clip length limit stops a forced note from crossing the bar
// line it started in (4/4). Off scheduling uses a pending queue in absolute beats
// (tempo-safe), like the arpeggiator. Gate % reads a clip note's length from the note-on
// (MidiEv::dur), so it can also shorten; a note played live is measured at its note-off.
//
// Param layout is APPEND-ONLY. 0 Rate / 1 Gate are the pre-almanac sync controls (4 rates ×
// gate): Division (11) replaced Rate — the managed loader maps an old Rate onto it — and
// Gate stays as a hidden multiplier (1 = off) so old projects keep their sound.

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
        PDivision, PView,
        kNumParams
    };
    // Sync divisions (beats): 1/32, 1/16, 1/8, 1/4, 1/2, 1/1, 1/8 dotted, 1/4 triplet.
    static constexpr int kNumDiv = 8;
    static constexpr double kDiv[kNumDiv] = { 0.125, 0.25, 0.5, 1.0, 2.0, 4.0, 0.75, 2.0 / 3.0 };
    static constexpr double kBar = 4.0;   // beats per bar for Legato / Clip (4/4)

    MidiNoteLength() {
        p_[PRate].store(1.0f); p_[PGate].store(1.0f); p_[PMs].store(250.0f); p_[PPercent].store(100.0f);
        p_[PDivision].store(2.0f);   // 1/8 — what the old default (Rate 1/8 × Gate 1) played
    }

    int32_t midiKind() const override { return 3; }
    const char* displayName() const override { return "Nota Length"; }

    void setSampleRate(double sr, int32_t) override { sr_ = sr > 0 ? sr : 44100.0; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case PRate: return "Rate"; case PGate: return "Gate"; case PMode: return "Mode"; case PMs: return "Ms";
            case PPercent: return "Percent"; case PTrigger: return "Trigger"; case PVelToLen: return "Vel to Len";
            case PKeyToLen: return "Key to Len"; case PRandom: return "Random"; case PLegato: return "Legato";
            case PClipLimit: return "Clip Limit"; case PDivision: return "Division"; case PView: return "View";
            default: return "";
        }
    }
    float paramMin(int32_t i) const override {
        switch (i) {
            case PMs: case PPercent: return 10.0f;
            case PVelToLen: case PKeyToLen: return -1.0f;
            default: return 0.0f;
        }
    }
    float paramMax(int32_t i) const override {
        switch (i) {
            case PRate: return 3.0f; case PGate: return 2.0f; case PMode: return 2.0f; case PMs: return 2000.0f;
            case PPercent: return 200.0f; case PDivision: return (float)(kNumDiv - 1);
            default: return 1.0f;   // trigger / modifiers / legato / clip / view
        }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }
    void  reset() override { nPending_ = 0; nHeld_ = 0; nSound_ = 0; intBeat_ = 0.0; sounding_.store(0, std::memory_order_relaxed); }

    // Editor / MCP telemetry: [0] last input pitch, [1] its velocity 0..1, [2] its held length
    // (beats, −1 = still held), [3] last finished output pitch, [4] its actual length (beats),
    // [5] notes sounding now, [6] notes finished since the device was created.
    int32_t midiScope(float* out, int32_t maxN) const override {
        const float v[7] = { (float)lastInPitch_.load(std::memory_order_relaxed), lastInVel_.load(std::memory_order_relaxed),
                             lastInHeld_.load(std::memory_order_relaxed), (float)lastOutPitch_.load(std::memory_order_relaxed),
                             lastOutLen_.load(std::memory_order_relaxed), (float)sounding_.load(std::memory_order_relaxed),
                             (float)finished_.load(std::memory_order_relaxed) };
        const int n = std::min<int32_t>(maxN, 7);
        for (int i = 0; i < n; ++i) out[i] = v[i];
        return n;
    }
    int32_t midiLastIn() const override { return lastInPitch_.load(std::memory_order_relaxed); }
    int32_t midiLastOut() const override { return lastOutPitch_.load(std::memory_order_relaxed); }

    std::shared_ptr<MidiDevice> clone() const override {
        auto c = std::make_shared<MidiNoteLength>();
        for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
        c->setBypassed(bypassed());
        return c;
    }

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut,
                 int32_t frames, double beatStart, double spb, bool playing) override {
        nOut = 0;
        const double b0 = playing ? beatStart : intBeat_;
        const double b1 = b0 + frames / spb;
        if (bypassed()) {
            // Release what we still hold, then pass through untouched.
            for (int i = 0; i < nPending_ && nOut < maxOut; ++i) out[nOut++] = { 0, false, pending_[i].pitch, 0.0f };
            nPending_ = 0; nHeld_ = 0; nSound_ = 0; sounding_.store(0, std::memory_order_relaxed);
            for (int i = 0; i < nIn && nOut < maxOut; ++i) out[nOut++] = in[i];
            intBeat_ = b1;
            return;
        }
        const int  mode = std::clamp((int)std::lround(p_[PMode].load(std::memory_order_relaxed)), 0, 2);
        const bool onOff = p_[PTrigger].load(std::memory_order_relaxed) >= 0.5f;   // start from the note-off
        const bool legato = p_[PLegato].load(std::memory_order_relaxed) >= 0.5f;
        const bool clip = p_[PClipLimit].load(std::memory_order_relaxed) >= 0.5f;

        // Offs falling due before `until` go out at their own time (never before the block).
        auto flushDue = [&](double until) {
            for (int k = 0; k < nPending_;) {
                const double off = pending_[k].off;
                if (off < until) emitOff(k, std::max(b0, off), b0, spb, frames, out, nOut, maxOut);
                else ++k;
            }
        };

        for (int i = 0; i < nIn; ++i) {
            const MidiEv& e = in[i];
            const double evBeat = b0 + (double)e.off / spb;
            flushDue(evBeat);
            if (e.on) {
                addHeld(e.pitch, evBeat, e.vel);
                lastInPitch_.store(e.pitch, std::memory_order_relaxed); lastInVel_.store(e.vel, std::memory_order_relaxed);
                lastInHeld_.store(-1.0f, std::memory_order_relaxed);
                if (onOff) continue;                                            // fires on release
                start(e.pitch, e.vel, evBeat, b0, spb, frames, legato, out, nOut, maxOut);
                if (legato || mode != 2) schedule(e.pitch, evBeat, legato ? -1.0 : len(e.vel, e.pitch, 0.0, mode, spb), clip, legato);
                else if (e.dur > 0.0f) schedule(e.pitch, evBeat, len(e.vel, e.pitch, e.dur, 2, spb), clip, false);   // a clip note: length known
                // else Gate % waits for the note-off to know the held length (played live).
            } else {
                Held* h = findHeld(e.pitch);
                const float hv = h ? h->vel : 0.8f;
                const double onBeat = h ? h->onBeat : evBeat;
                const double heldBeats = std::max(0.0, evBeat - onBeat);
                if (h) lastInHeld_.store((float)heldBeats, std::memory_order_relaxed);
                removeHeld(e.pitch);
                if (onOff) {
                    start(e.pitch, hv, evBeat, b0, spb, frames, legato, out, nOut, maxOut);
                    schedule(e.pitch, evBeat, legato ? -1.0 : len(hv, e.pitch, heldBeats, mode, spb), clip, legato);
                } else if (mode == 2 && !legato && findPending(e.pitch) < 0 && h && sounding(e.pitch)) {
                    schedule(e.pitch, onBeat, len(hv, e.pitch, heldBeats, 2, spb), clip, false);
                }
                // else: swallow the incoming off (the forced length wins)
            }
            // An off already overdue (Gate % below 100 % learns the length at the note-off) goes out now.
            for (int k = 0; k < nPending_;) {
                if (pending_[k].off <= evBeat) emitOff(k, evBeat, b0, spb, frames, out, nOut, maxOut);
                else ++k;
            }
        }
        flushDue(b1);
        intBeat_ = b1;
    }

private:
    // Length in beats for one note, with the velocity / key / random modifiers (mirrors the card's model).
    double len(float vel, int pitch, double heldBeats, int mode, double spb) {
        double L;
        if (mode == 2)      L = heldBeats * (std::clamp(p_[PPercent].load(std::memory_order_relaxed), 1.0f, 200.0f) / 100.0);
        else if (mode == 1) L = (std::clamp(p_[PMs].load(std::memory_order_relaxed), 1.0f, 2000.0f) / 1000.0) * sr_ / spb;   // ms → beats
        else                L = kDiv[std::clamp((int)std::lround(p_[PDivision].load(std::memory_order_relaxed)), 0, kNumDiv - 1)]
                              * std::clamp(p_[PGate].load(std::memory_order_relaxed), 0.05f, 2.0f);
        const double velAmt = std::clamp(p_[PVelToLen].load(std::memory_order_relaxed), -1.0f, 1.0f);
        const double keyAmt = std::clamp(p_[PKeyToLen].load(std::memory_order_relaxed), -1.0f, 1.0f);
        L *= std::max(0.05, 1.0 + velAmt * ((double)vel * 127.0 - 64.0) / 64.0);   // + : loud notes longer
        L *= std::max(0.05, 1.0 - keyAmt * (pitch - 60) / 24.0);                    // + : low notes longer
        const float rnd = std::clamp(p_[PRandom].load(std::memory_order_relaxed), 0.0f, 1.0f);
        if (rnd > 0.0f) L *= 1.0 + (double)rnd * ((double)frand() * 2.0 - 1.0);
        return std::max(1.0 / 256.0, L);
    }
    static double barEnd(double beat) { return (std::floor(beat / kBar + 1e-9) + 1.0) * kBar; }

    // A forced note starts: legato cuts whatever still sounds, a same-pitch note is retriggered.
    void start(int32_t pitch, float vel, double beat, double b0, double spb, int32_t frames, bool legato,
               MidiEv* out, int& nOut, int maxOut) {
        for (int k = 0; k < nPending_;) {
            if (legato || pending_[k].pitch == pitch) emitOff(k, beat, b0, spb, frames, out, nOut, maxOut);
            else ++k;
        }
        if (nOut < maxOut) out[nOut++] = { at(beat, b0, spb, frames), true, pitch, vel };
        sounding_.fetch_add(1, std::memory_order_relaxed);
        if (nSound_ < kMaxPending) sound_[nSound_++] = { pitch, beat };
    }
    // Queue the off: lenBeats < 0 = legato (held until the next note starts, at most to the bar line).
    void schedule(int32_t pitch, double onBeat, double lenBeats, bool clip, bool legato) {
        double off = legato || lenBeats < 0 ? barEnd(onBeat) : onBeat + lenBeats;
        if (clip) off = std::min(off, barEnd(onBeat));
        if (nPending_ < kMaxPending) pending_[nPending_++] = { pitch, onBeat, std::max(off, onBeat + 1.0 / 256.0) };
    }
    void emitOff(int k, double beat, double b0, double spb, int32_t frames, MidiEv* out, int& nOut, int maxOut) {
        const Off p = pending_[k];
        pending_[k] = pending_[--nPending_];
        if (nOut < maxOut) out[nOut++] = { at(beat, b0, spb, frames), false, p.pitch, 0.0f };
        lastOutPitch_.store(p.pitch, std::memory_order_relaxed);
        lastOutLen_.store((float)std::max(0.0, beat - p.on), std::memory_order_relaxed);
        finished_.fetch_add(1, std::memory_order_relaxed);
        for (int s = 0; s < nSound_; ++s) if (sound_[s].pitch == p.pitch) { sound_[s] = sound_[--nSound_]; break; }
        sounding_.store(nSound_, std::memory_order_relaxed);
    }
    bool sounding(int32_t pitch) const { for (int s = 0; s < nSound_; ++s) if (sound_[s].pitch == pitch) return true; return false; }
    int findPending(int32_t pitch) const { for (int i = 0; i < nPending_; ++i) if (pending_[i].pitch == pitch) return i; return -1; }

    struct Held { int32_t pitch; double onBeat; float vel; };
    Held* findHeld(int32_t pitch) { for (int i = 0; i < nHeld_; ++i) if (held_[i].pitch == pitch) return &held_[i]; return nullptr; }
    void addHeld(int32_t pitch, double onBeat, float vel) { if (auto* h = findHeld(pitch)) { h->onBeat = onBeat; h->vel = vel; return; } if (nHeld_ < kMaxHeld) held_[nHeld_++] = { pitch, onBeat, vel }; }
    void removeHeld(int32_t pitch) { for (int i = 0; i < nHeld_; ++i) if (held_[i].pitch == pitch) { held_[i] = held_[--nHeld_]; return; } }
    static int32_t at(double beat, double b0, double spb, int32_t frames) {
        long v = (long)std::floor((beat - b0) * spb + 0.5); if (v < 0) v = 0; if (v >= frames) v = frames - 1; return (int32_t)v;
    }
    float frand() { rng_ ^= rng_ << 13; rng_ ^= rng_ >> 17; rng_ ^= rng_ << 5; return (rng_ & 0xFFFFFF) / (float)0x1000000; }

    struct Off { int32_t pitch; double on, off; };
    struct Sound { int32_t pitch; double on; };
    static constexpr int kMaxPending = 128, kMaxHeld = 64;
    Off   pending_[kMaxPending]; int nPending_ = 0;
    Sound sound_[kMaxPending];   int nSound_ = 0;    // forced notes started and not yet released
    Held  held_[kMaxHeld];       int nHeld_ = 0;
    double intBeat_ = 0.0, sr_ = 44100.0;
    uint32_t rng_ = 0x9E3779B9u;
    std::atomic<float> p_[kNumParams];
    std::atomic<int32_t> lastInPitch_{-1}, lastOutPitch_{-1}, sounding_{0}, finished_{0};
    std::atomic<float> lastInVel_{0.0f}, lastInHeld_{-1.0f}, lastOutLen_{0.0f};
};

} // namespace nota
