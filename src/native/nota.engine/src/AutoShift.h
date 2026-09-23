// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Auto Shift (device kind 10) — a real-time vocal pitch corrector. Stages:
//   1. DETECT — an NSDF pitch tracker (McLeod: normalized square difference, first peak
//               above 90% of the global max, parabolic refinement) over the mono sum, run
//               every hop. The autocorrelation goes through an FFT, so the window can grow
//               to cover low voices; the lag search is limited to the Det Low … Det High
//               note range and Det Sens sets the voicing threshold. Unvoiced frames with a
//               high zero-crossing rate are flagged as sibilants.
//   2. TARGET — the smoothed pitch (Human blends in a slow note centre, so vibrato is kept
//               around the corrected note) is pulled toward the nearest note of the key's
//               scale, a Custom 12-note set, or — Key Source MIDI — the notes another
//               track is playing (routed through the sidechain source: Note / Scale mode,
//               Latch, Oct Lock, Glide within the MIDI Bend range). Amount, Range, Shift
//               and Fine finish the target; Speed glides the retune.
//   3. SHIFT  — a pitch-synchronous overlap-add (TD-PSOLA) shifter: two-period Hann grains
//               taken a whole period apart and laid out at the new period. Each grain is
//               resampled by the formant factor, so Formant (preserve 0..1) keeps the
//               vocal tract where it was and Formant Shift moves it on its own. At unity
//               grains land where they were taken → transparent. Skip Sibilants passes
//               sibilants unshifted. The shifted path follows the dry level (a slow level
//               match, ±6 dB). The dry path is delayed by the same fixed latency (reported
//               for PDC), so Mix never combs.
// Learn: a decaying, duration-weighted histogram of the sung pitch classes (about 16 bars)
// is matched against Krumhansl key profiles; Key Source Auto follows the best key, and
// deviceAction 1 runs a one-shot Learn that commits it.
//
// scopeRead layout: kTele telemetry floats (S_*), then the 12-bin pitch-class histogram
// (max = 1), then three kHist histories (oldest first): detected pitch, output pitch and
// the MIDI target (MIDI note numbers, 0 = none). gainReductionDb() keeps publishing the
// detected pitch / 127 (the original LIVE feed).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = reset the analysis (histogram + history), 1 = Learn (iarg 1 start,
// 0 stop and commit, 2 cancel).
// Header-only, allocation-free after setSampleRate. Params normalized 0..1 → persist /
// clone / automation flow generically through the base Device. APPEND ONLY.

#pragma once

#include "Device.h"
#include "MidiDevice.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>

namespace nota {

class AutoShift : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY. 0..9 are the
    // original params (Formant = how much of the formant envelope is preserved 0..1).
    enum {
        Key = 0, Scale, Amount, Speed, Shift, Mix, Range, Formant, KeySource, Follow,
        Human, Fine, FormantShift, DetLow, DetHigh, DetSens, SkipSibilants, CustomScale,
        NoteC, NoteCs, NoteD, NoteDs, NoteE, NoteF, NoteFs, NoteG, NoteGs, NoteA, NoteAs, NoteB,
        MidiMode, MidiLatch, MidiOctLock, MidiGlide, MidiBendRange, MidiBend,
        kNumParams
    };

    // Telemetry block (scopeRead, first kTele floats).
    enum {
        S_DetMidi = 0,   // smoothed detected pitch (MIDI, fractional), 0 = unvoiced
        S_TargetMidi,    // target note (MIDI, may glide), 0 = none
        S_OutMidi,       // output pitch now (detected + the applied shift), 0 = unvoiced
        S_CorrCents,     // correction being applied now, cents (excl. Shift / Fine)
        S_Conf,          // detector clarity 0..1
        S_Hz,            // detected frequency
        S_Voiced,        // 1 = pitched
        S_Sibilant,      // 1 = an unvoiced, noisy frame (sibilant)
        S_SampleRate,
        S_Cpu,           // share of real time spent in process()
        S_Latency,       // samples
        S_MidiNote,      // MIDI target note (before octave / glide), -1 = none
        S_MidiVel,       // its velocity 0..127
        S_MidiHeld,      // notes held on the source now
        S_MidiLive,      // 1 = a MIDI source fed this device in the last block
        S_Learning,      // 1 = a one-shot Learn is running
        S_LearnKey,      // best key 0..11 (-1 = not enough singing yet)
        S_LearnScale,    // its scale: 1 Major, 2 Minor
        S_LearnMatch,    // its profile correlation 0..1
        S_Learn2Key,     // runner-up
        S_Learn2Scale,
        S_Learn2Match,
        S_InScale,       // share of the sung notes that lie in the active scale 0..1
        S_AnalysisSec,   // seconds of voiced singing the histogram holds (effective)
        S_WindowSec,     // history span, seconds
        S_HistN,         // points per history
        S_Mask,          // active 12-bit pitch-class mask (absolute, C = bit 0)
        S_Key,           // active key 0..11
        S_Ratio,         // pitch ratio now
        S_InDb,          // input level (detector window RMS), dBFS
        S_Bars,          // analysis length, bars
        S_MidiMask,      // pitch classes held on the MIDI source (absolute)
        kTele = 32
    };
    static constexpr int kHist = 384;
    static constexpr int kScope = kTele + 12 + 3 * kHist;
    enum { A_ResetAnalysis = 0, A_Learn = 1 };

    AutoShift() {
        p_[Key].store(0.0f);          // C
        p_[Scale].store(0.25f);       // Major (0..4 in .25 steps)
        p_[Amount].store(1.0f);       // full correction
        p_[Speed].store(0.15f);       // fairly fast retune
        p_[Shift].store(0.5f);        // no manual transpose
        p_[Mix].store(1.0f);
        p_[Range].store(0.3636f);     // ±5 st correction range (1 + v*11)
        p_[Formant].store(0.0f);      // formants move with the pitch (the original shifter)
        p_[KeySource].store(0.5f);    // Manual (0 Auto / 0.5 Manual / 1 MIDI)
        p_[Follow].store(0.0f);       // don't follow a scale device
        p_[Human].store(0.0f);        // hard correction
        p_[Fine].store(0.5f);         // 0 cents
        p_[FormantShift].store(0.5f); // no formant shift
        p_[DetLow].store(12.0f / 72.0f);   // C2 (24 + 72v)
        p_[DetHigh].store(60.0f / 72.0f);  // C6
        p_[DetSens].store(0.7f);      // voicing threshold 0.5 (the original)
        p_[SkipSibilants].store(0.0f);
        p_[CustomScale].store(0.0f);
        for (int i = 0; i < 12; ++i) p_[NoteC + i].store((kScaleMask[1] >> i) & 1 ? 1.0f : 0.0f);   // C major
        p_[MidiMode].store(0.0f);     // Note
        p_[MidiLatch].store(1.0f);
        p_[MidiOctLock].store(0.0f);
        p_[MidiGlide].store(0.49f);   // ~60 ms
        p_[MidiBendRange].store(1.0f / 3.0f);   // ±2 st (6v)
        p_[MidiBend].store(1.0f);
        setSampleRate(44100.0, 0);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        lat_ = std::clamp((int)std::lround(sr_ * 0.016), 64, kBuf / 4);
        hop_ = sr_ >= 88200.0 ? 512 : 256;
        baseWin_ = sr_ >= 88200.0 ? 2048 : 1024;
        for (int k = 0; k < kFftMax / 2; ++k) {
            twC_[(size_t)k] = (float)std::cos(-2.0 * kPi * k / kFftMax);
            twS_[(size_t)k] = (float)std::sin(-2.0 * kPi * k / kFftMax);
        }
        lvlA_ = 1.0 - std::exp(-1.0 / (0.08 * sr_));
        lvlB_ = 1.0 - std::exp(-1.0 / (0.05 * sr_));
        srA_.store((float)sr_, std::memory_order_relaxed);
        resetState();
    }

    int32_t latencySamples() const override { return lat_; }

    // The MIDI source rides on the sidechain source slot: pick an instrument track and the
    // engine hands this device that track's notes each block (setMidiKey).
    int32_t sidechainSourceTrackId() const override { return srcTrack_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { srcTrack_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float*, int32_t) override {}
    bool    acceptsSidechain() const override { return true; }
    bool    wantsMidiKey() const override { return true; }
    void    setMidiKey(const MidiEv* evs, int32_t n) override { midiEvs_ = evs; midiN_ = n; midiFed_ = true; }

    void setTransportInfo(const TransportInfo& ti) override {
        if (ti.bpm > 1.0) bpm_ = ti.bpm;
        if (ti.tsNum > 0) tsNum_ = ti.tsNum;
    }

    // Instantaneous smoothed detected pitch (MIDI/127), 0 = unvoiced — the original LIVE feed.
    float gainReductionDb() const override { return pitchPub_.load(std::memory_order_relaxed); }

    void deviceAction(int32_t id, int32_t iarg, float) override {
        if (id == A_ResetAnalysis) resetReq_.store(true, std::memory_order_relaxed);
        else if (id == A_Learn) learnReq_.store(iarg == 1 ? 1 : iarg == 2 ? 3 : 2, std::memory_order_relaxed);   // 1 start, 2 commit, 3 cancel
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        if (resetReq_.exchange(false, std::memory_order_relaxed)) resetAnalysis();
        switch (learnReq_.exchange(0, std::memory_order_relaxed)) {
            case 1: resetAnalysis(); learning_ = true; break;
            case 2: if (learning_) commitLearn(); learning_ = false; break;
            case 3: learning_ = false; break;
            default: break;
        }
        takeMidi();

        const float mix = std::clamp(get(Mix), 0.0f, 1.0f);
        const double tau = expMap(get(Speed), 0.002, 0.30);                 // retune glide (s)
        const double smooth = 1.0 - std::exp(-1.0 / (tau * sr_));
        const double pres = std::clamp((double)get(Formant), 0.0, 1.0);
        const double fsh = std::pow(2.0, ((double)get(FormantShift) - 0.5) * 2.0);   // ±1 octave

        for (int32_t i = 0; i < frames; ++i) {
            const float inL = buf[i * 2], inR = buf[i * 2 + 1];
            const int w = (int)(n_ & kMask);
            ring_[0][(size_t)w] = inL; ring_[1][(size_t)w] = inR;
            det_[(size_t)w] = 0.5f * (inL + inR);
            ++n_;
            if (++hopCnt_ >= hop_) { hopCnt_ = 0; detect(); }

            const int64_t to = n_ - 1 - lat_;           // the sample leaving now (input time)
            const Rec& rc = recAt(to);
            curSemi_ += (rc.semi - curSemi_) * smooth;

            // Lay out every grain whose first sample is due.
            for (int guard = 0; guard < 8; ++guard) {
                const double r = rc.sib ? 1.0 : std::pow(2.0, curSemi_ / 12.0);
                const double fr = rc.sib ? 1.0 : std::clamp(std::pow(r, 1.0 - pres) * fsh, 0.5, 2.0);
                pg_ = std::clamp(rc.period, pg_ * 0.97, pg_ * 1.03);
                const double halfOut = pg_ / fr;
                if (ns_ < (double)to - halfOut) ns_ = (double)to + halfOut * 0.5;   // lost sync (start / reset)
                if (ns_ - halfOut > (double)to + 1.0) break;
                grain(ns_, pg_, r, fr, rc.sib, to);
                ns_ += pg_ / r;
            }

            const int o = (int)(to & kMask);
            float wetL = acc_[0][(size_t)o], wetR = acc_[1][(size_t)o];
            acc_[0][(size_t)o] = 0.0f; acc_[1][(size_t)o] = 0.0f;
            const float dryL = to >= 0 ? ring_[0][(size_t)o] : 0.0f, dryR = to >= 0 ? ring_[1][(size_t)o] : 0.0f;
            // Level match: re-spaced grains add up louder or softer than the source depending on
            // the ratio and the waveform, so the shifted path follows the (aligned) dry level —
            // slowly (~80 ms), within ±6 dB, held through silence. At unity it stays at 1.
            eDry_ += ((double)dryL * dryL + (double)dryR * dryR - eDry_) * lvlA_;
            eWet_ += ((double)wetL * wetL + (double)wetR * wetR - eWet_) * lvlA_;
            if (eDry_ > 1e-9) gAuto_ += (std::clamp(std::sqrt(eDry_ / std::max(eWet_, 1e-12)), 0.5, 2.0) - gAuto_) * lvlB_;
            wetL *= (float)gAuto_; wetR *= (float)gAuto_;
            buf[i * 2]     = dryL * (1.0f - mix) + wetL * mix;
            buf[i * 2 + 1] = dryR * (1.0f - mix) + wetR * mix;
        }
        ratioA_.store((float)std::pow(2.0, curSemi_ / 12.0), std::memory_order_relaxed);
        midiEvs_ = nullptr; midiN_ = 0;
        midiLiveA_.store(midiFed_ ? 1.0f : 0.0f, std::memory_order_relaxed);
        midiFed_ = false;
        if (frames > 0) {
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    // Telemetry, the pitch-class histogram, then the three histories (oldest first).
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        float t[kTele] = {};
        for (int k = 0; k < kTele; ++k) t[k] = tele_[(size_t)k].load(std::memory_order_relaxed);
        t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
        t[S_Latency] = (float)lat_;
        t[S_MidiLive] = midiLiveA_.load(std::memory_order_relaxed);
        t[S_Ratio] = ratioA_.load(std::memory_order_relaxed);
        t[S_HistN] = (float)kHist;
        t[S_WindowSec] = (float)(kHist * hop_ / sr_);
        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        for (int k = 0; k < 12 && n < maxSamples; ++k) out[n++] = pcPub_[(size_t)k].load(std::memory_order_relaxed);
        const int hw = histW_.load(std::memory_order_acquire);
        for (int s = 0; s < 3; ++s)
            for (int k = 0; k < kHist && n < maxSamples; ++k) out[n++] = hist_[(size_t)s][(size_t)((hw + k) % kHist)];
        return n;
    }

    const char* displayName() const override { return "Nota Auto Shift"; }
    int32_t     builtinKind() const override { return 10; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[kNumParams] = {
            "Key", "Scale", "Amount", "Speed", "Shift", "Mix", "Range", "Formant", "Key Source", "Follow Scale",
            "Human", "Fine", "Formant Shift", "Det Low", "Det High", "Det Sens", "Skip Sibilants", "Custom Scale",
            "Note C", "Note C#", "Note D", "Note D#", "Note E", "Note F", "Note F#", "Note G", "Note G#", "Note A", "Note A#", "Note B",
            "MIDI Mode", "MIDI Latch", "MIDI Oct Lock", "MIDI Glide", "MIDI Bend Range", "MIDI Bend" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

    std::string deviceText(int32_t id) const override {
        static const char* keys[12] = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        static const char* scales[5] = { "Chromatic", "Major", "Minor", "Penta Maj", "Penta Min" };
        static const char* srcs[3] = { "auto", "manual", "MIDI" };
        char b[768];
        float t[kTele];
        scopeRead(t, kTele);
        if (id == 0) {
            const int src = keySrc();
            std::string s;
            if (src == 2) {
                std::snprintf(b, sizeof b, "MIDI target - %s%s%s - glide %.0f ms%s",
                              get(MidiMode) >= 0.5f ? "scale" : "note", get(MidiLatch) >= 0.5f ? " - latch" : "",
                              get(MidiOctLock) >= 0.5f ? " - oct lock" : "", expMap(get(MidiGlide), 5.0, 800.0),
                              get(MidiBend) >= 0.5f ? (" - bends within " + stText(get(MidiBendRange) * 6.0)).c_str() : " - steps");
                s = b;
                s += sidechainSourceTrackId() >= 0 ? " - source routed" : " - no MIDI source";
            } else {
                s = std::string(keys[keyIdx()]) + " " + (get(CustomScale) >= 0.5f ? "Custom" : scales[scaleIdx()]) + " - " + srcs[src];
            }
            std::snprintf(b, sizeof b, " - amount %.0f %% - speed %.0f ms - range ±%.0f st - human %.0f %%",
                          get(Amount) * 100.0f, expMap(get(Speed), 2.0, 300.0), 1.0 + get(Range) * 11.0, get(Human) * 100.0f);
            s += b;
            std::snprintf(b, sizeof b, " - shift %+.0f st %+.0f ct - formant %s, shift %+.0f %% - mix %.0f %%",
                          ((double)get(Shift) - 0.5) * 24.0, ((double)get(Fine) - 0.5) * 200.0,
                          get(Formant) >= 0.5f ? "preserved" : "moves", ((double)get(FormantShift) - 0.5) * 200.0, get(Mix) * 100.0f);
            s += b;
            std::snprintf(b, sizeof b, " - detect %s..%s sens %.0f %%", noteText(detLowMidi()).c_str(), noteText(detHighMidi()).c_str(), get(DetSens) * 100.0f);
            s += b;
            if (get(SkipSibilants) >= 0.5f) s += " - skip sibilants";
            if (get(Follow) >= 0.5f) s += " - follows a Nota Scale";
            return s;
        }
        if (id == 1) {
            std::string s;
            if (t[S_Voiced] > 0.5f) {
                std::snprintf(b, sizeof b, "voiced - %s %+.0f ct (%.1f Hz, clarity %.0f %%)", noteText((int)std::lround(t[S_DetMidi])).c_str(),
                              (t[S_DetMidi] - std::round(t[S_DetMidi])) * 100.0f, t[S_Hz], t[S_Conf] * 100.0f);
                s = b;
                if (t[S_TargetMidi] > 0.5f) {
                    std::snprintf(b, sizeof b, " - target %s - correcting %+.0f ct", noteText((int)std::lround(t[S_TargetMidi])).c_str(), t[S_CorrCents]);
                    s += b;
                } else s += " - no target";
            } else s = t[S_Sibilant] > 0.5f ? "sibilant" : "unvoiced";
            std::snprintf(b, sizeof b, " - ratio %.3f - in %.1f dB", t[S_Ratio], t[S_InDb]);
            s += b;
            if (t[S_MidiLive] > 0.5f || keySrc() == 2) {
                if (t[S_MidiNote] >= 0.0f) std::snprintf(b, sizeof b, " - MIDI %s vel %.0f (%.0f held)", noteText((int)t[S_MidiNote]).c_str(), t[S_MidiVel], t[S_MidiHeld]);
                else std::snprintf(b, sizeof b, " - MIDI none held");
                s += b;
            }
            std::snprintf(b, sizeof b, " - %.0f %% in scale over %.1f s", t[S_InScale] * 100.0f, t[S_AnalysisSec]);
            s += b;
            if (t[S_LearnKey] >= 0.0f) {
                std::snprintf(b, sizeof b, " - best %s %s %.0f %%, next %s %s %.0f %%", keys[(int)t[S_LearnKey] % 12], t[S_LearnScale] > 1.5f ? "Minor" : "Major",
                              t[S_LearnMatch] * 100.0f, keys[(int)t[S_Learn2Key] % 12], t[S_Learn2Scale] > 1.5f ? "Minor" : "Major", t[S_Learn2Match] * 100.0f);
                s += b;
            }
            if (t[S_Learning] > 0.5f) s += " - LEARNING";
            std::snprintf(b, sizeof b, " - latency %d smp", lat_);
            s += b;
            return s;
        }
        if (id == 2) {
            return "All params 0..1. Key: round(v*11) = C..B. Scale: 0 Chromatic, .25 Major, .5 Minor, .75 Penta Maj, 1 Penta Min. "
                   "Custom Scale on: the Note C..Note B toggles (absolute pitch classes) are the scale. Amount 0..100 %. "
                   "Speed 2*150^v ms retune glide (0 = hard tune). Range: 1 + 11v semitones, the largest correction. "
                   "Human: keeps vibrato and drift around the corrected note (0 = flat). Shift: (v-0.5)*24 st. Fine: (v-0.5)*200 cents. "
                   "Formant: how much of the formant envelope is preserved while shifting (1 = natural). Formant Shift: (v-0.5)*200 %, "
                   "±100 % = one octave. Mix 0..100 %. Key Source: 0 Auto (follows the sung key), 0.5 Manual, 1 MIDI (the notes of "
                   "the track routed as the sidechain source are the target). Follow Scale: the card copies a Nota Scale's key and scale. "
                   "Det Low / Det High: detection range, MIDI 24 + 72v (C1..C7). Det Sens: voicing sensitivity. Skip Sibilants: "
                   "unvoiced noisy frames pass unshifted. MIDI Mode: 0 Note (the last held note), 1 Scale (held notes form the scale). "
                   "MIDI Latch: keep the target between notes. MIDI Oct Lock: the note's own octave (off = the octave nearest the voice). "
                   "MIDI Glide: 5*160^v ms. MIDI Bend: moves between notes glide when within MIDI Bend Range (6v st); wider leaps jump. "
                   "Toggles: >= 0.5 = on.";
        }
        return {};
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kBuf = 16384;          // audio / output ring (power of two)
    static constexpr int64_t kMask = kBuf - 1;
    static constexpr int kWinMax = 4096;        // largest analysis window
    static constexpr int kFftMax = 2 * kWinMax;
    static constexpr int kRec = 64;             // per-hop decisions, timestamped
    // Chromatic / Major / Minor / Pentatonic Major / Pentatonic Minor (bit i = i semitones above the key).
    static constexpr uint16_t kScaleMask[5] = { 0x0FFF, 0x0AB5, 0x05AD, 0x0295, 0x04A9 };
    // Krumhansl–Kessler key profiles.
    static constexpr double kMajProf[12] = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    static constexpr double kMinProf[12] = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

    struct Rec { int64_t at = 0; double semi = 0.0; double period = 200.0; bool sib = false; };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    int keyIdx() const { return std::clamp((int)std::lround(get(Key) * 11.0f), 0, 11); }
    int scaleIdx() const { return std::clamp((int)std::lround(get(Scale) * 4.0f), 0, 4); }
    int keySrc() const { const float v = get(KeySource); return v < 0.25f ? 0 : v < 0.75f ? 1 : 2; }
    int detLowMidi() const { return 24 + (int)std::lround(get(DetLow) * 72.0f); }
    int detHighMidi() const { return 24 + (int)std::lround(get(DetHigh) * 72.0f); }
    static uint16_t rot(uint16_t m, int key) { key = ((key % 12) + 12) % 12; return (uint16_t)(((m << key) | (m >> (12 - key))) & 0x0FFF); }
    uint16_t scaleMask() const {
        if (get(CustomScale) >= 0.5f) {
            uint16_t m = 0;
            for (int i = 0; i < 12; ++i) if (get(NoteC + i) >= 0.5f) m |= (uint16_t)(1u << i);
            return m;
        }
        return rot(kScaleMask[scaleIdx()], keyIdx());
    }
    static std::string noteText(int m) {
        static const char* nn[12] = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return std::string(nn[((m % 12) + 12) % 12]) + std::to_string(m / 12 - 1);
    }
    static std::string stText(double st) { char b[24]; std::snprintf(b, sizeof b, "%.1f st", st); return b; }

    // Nearest note whose pitch class is in `mask` (absolute); ties go to the closer side.
    static double snapMask(double m, uint16_t mask) {
        if (!mask) return -1.0;
        const int base = (int)std::lround(m);
        int best = base; double bd = 1e9;
        for (int d = -6; d <= 6; ++d) {
            const int note = base + d;
            if (!(mask & (uint16_t)(1u << (((note % 12) + 12) % 12)))) continue;
            const double dist = std::fabs(note - m);
            if (dist < bd) { bd = dist; best = note; }
        }
        return bd < 1e8 ? (double)best : -1.0;
    }

    void resetAnalysis() {
        pcw_.fill(0.0); totalW_ = 0.0; hopsSinceKey_ = 0;
        for (auto& h : hist_) h.fill(0.0f);
        for (auto& p : pcPub_) p.store(0.0f, std::memory_order_relaxed);
        tele_[S_LearnKey].store(-1.0f, std::memory_order_relaxed);
        tele_[S_Learn2Key].store(-1.0f, std::memory_order_relaxed);
        tele_[S_AnalysisSec].store(0.0f, std::memory_order_relaxed);
    }

    void resetState() {
        for (auto& r : ring_) r.fill(0.0f);
        for (auto& a : acc_) a.fill(0.0f);
        det_.fill(0.0f);
        n_ = 0; hopCnt_ = 0;
        const double pu = sr_ * 0.005;
        for (auto& r : rec_) r = Rec{ 0, 0.0, pu, false };
        recW_ = 0;
        pg_ = pu; ns_ = 0.0; la_ = 0.0; lastP_ = 0.0; curSemi_ = 0.0;
        eDry_ = eWet_ = 0.0; gAuto_ = 1.0;
        sm_ = center_ = 60.0; have_ = false; midiT_ = -1.0;
        held_.fill(0); order_.fill(-1); orderN_ = 0; lastNote_ = -1; lastVel_ = 0; latchMask_ = 0;
        histW_.store(0, std::memory_order_relaxed);
        for (auto& t : tele_) t.store(0.0f, std::memory_order_relaxed);
        tele_[S_MidiNote].store(-1.0f, std::memory_order_relaxed);
        tele_[S_InDb].store(-120.0f, std::memory_order_relaxed);
        tele_[S_Bars].store(16.0f, std::memory_order_relaxed);
        resetAnalysis();
        learning_ = false;
        pitchPub_.store(0.0f, std::memory_order_relaxed);
    }

    // The decision in force for input time `t`: the latest record stamped at or before it.
    const Rec& recAt(int64_t t) const {
        for (int k = 0; k < kRec; ++k) {
            const Rec& r = rec_[(size_t)((recW_ - 1 - k + kRec) % kRec)];
            if (r.at <= t) return r;
        }
        return rec_[(size_t)(recW_ % kRec)];
    }

    // --- MIDI target ------------------------------------------------------------
    void takeMidi() {
        if (!midiEvs_) return;
        for (int32_t k = 0; k < midiN_; ++k) {
            const MidiEv& e = midiEvs_[k];
            if (e.pitch < 0 || e.pitch > 127) continue;
            if (e.on && e.vel > 0.0f) {
                held_[(size_t)e.pitch] = (uint8_t)std::clamp((int)std::lround(e.vel * 127.0f), 1, 127);
                dropOrder(e.pitch);
                if (orderN_ < (int)order_.size()) order_[(size_t)orderN_++] = e.pitch;
                lastNote_ = e.pitch; lastVel_ = held_[(size_t)e.pitch];
            } else {
                held_[(size_t)e.pitch] = 0;
                dropOrder(e.pitch);
            }
        }
        uint16_t m = 0;
        for (int k = 0; k < orderN_; ++k) m |= (uint16_t)(1u << (order_[(size_t)k] % 12));
        if (m) latchMask_ = m;
    }
    void dropOrder(int p) {
        int w = 0;
        for (int k = 0; k < orderN_; ++k) if (order_[(size_t)k] != p) order_[(size_t)w++] = order_[(size_t)k];
        orderN_ = w;
    }

    // --- FFT (radix-2, in place) ----------------------------------------------------
    void fft(int n, bool inverse) {
        for (int i = 1, j = 0; i < n; ++i) {
            int bit = n >> 1;
            for (; j & bit; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { std::swap(re_[(size_t)i], re_[(size_t)j]); std::swap(im_[(size_t)i], im_[(size_t)j]); }
        }
        for (int len = 2; len <= n; len <<= 1) {
            const int step = kFftMax / len;
            for (int i = 0; i < n; i += len)
                for (int k = 0; k < len / 2; ++k) {
                    const float wr = twC_[(size_t)(k * step)], wi = inverse ? -twS_[(size_t)(k * step)] : twS_[(size_t)(k * step)];
                    const size_t a = (size_t)(i + k), b2 = (size_t)(i + k + len / 2);
                    const float xr = re_[b2] * wr - im_[b2] * wi, xi = re_[b2] * wi + im_[b2] * wr;
                    re_[b2] = re_[a] - xr; im_[b2] = im_[a] - xi;
                    re_[a] += xr; im_[a] += xi;
                }
        }
    }

    // --- detection + target (every hop) -----------------------------------------------
    void detect() {
        const double hopSec = hop_ / sr_;
        const int lo = std::min(detLowMidi(), detHighMidi() - 1), hi = std::max(detHighMidi(), lo + 1);
        const double fLo = 440.0 * std::pow(2.0, (lo - 69) / 12.0), fHi = 440.0 * std::pow(2.0, (hi - 69) / 12.0);
        const int maxLag = std::clamp((int)std::ceil(sr_ / fLo) + 1, 8, kWinMax / 2 - 2);
        const int minLag = std::clamp((int)std::floor(sr_ / fHi) - 1, 2, maxLag - 4);
        int win = baseWin_;
        while (win < 2 * maxLag && win < kWinMax) win <<= 1;
        const int nfft = 2 * win;

        double e = 0.0; int zc = 0; float prev = 0.0f;
        for (int i = 0; i < win; ++i) {
            const float x = det_[(size_t)((n_ - win + i) & kMask)];
            re_[(size_t)i] = x; im_[(size_t)i] = 0.0f;
            e += (double)x * x;
            if (i > 0 && ((x >= 0.0f) != (prev >= 0.0f))) ++zc;
            prev = x;
        }
        for (int i = win; i < nfft; ++i) { re_[(size_t)i] = 0.0f; im_[(size_t)i] = 0.0f; }
        const double rms = std::sqrt(e / win);
        const float inDb = rms > 1e-6 ? (float)(20.0 * std::log10(rms)) : -120.0f;
        const double zcrHz = zc * 0.5 * sr_ / win;
        const bool loud = inDb > -60.0f;

        double globalMax = 0.0; int bestLag = 0; double clarity = 0.0;
        if (loud) {
            fft(nfft, false);
            for (int i = 0; i < nfft; ++i) { re_[(size_t)i] = re_[(size_t)i] * re_[(size_t)i] + im_[(size_t)i] * im_[(size_t)i]; im_[(size_t)i] = 0.0f; }
            fft(nfft, true);
            // prefix energy for the NSDF normaliser
            pe_[0] = 0.0;
            for (int i = 0; i < win; ++i) { const double x = det_[(size_t)((n_ - win + i) & kMask)]; pe_[(size_t)i + 1] = pe_[(size_t)i] + x * x; }
            for (int lag = std::max(1, minLag - 1); lag <= maxLag + 1 && lag < win; ++lag) {
                const double r = re_[(size_t)lag] / nfft;
                const double m = pe_[(size_t)(win - lag)] + (pe_[(size_t)win] - pe_[(size_t)lag]);
                const double v = m > 1e-9 ? 2.0 * r / m : 0.0;
                nsdf_[(size_t)lag] = (float)v;
                if (lag >= minLag && lag <= maxLag && v > globalMax) globalMax = v;
            }
            if (globalMax > 0.0) {
                const double thr = 0.90 * globalMax;
                for (int lag = minLag; lag <= maxLag; ++lag)
                    if (nsdf_[(size_t)lag] > thr && nsdf_[(size_t)lag] >= nsdf_[(size_t)lag - 1] && nsdf_[(size_t)lag] >= nsdf_[(size_t)lag + 1]) { bestLag = lag; break; }
            }
            clarity = bestLag > 0 ? nsdf_[(size_t)bestLag] : 0.0;
        }
        const double vThr = std::clamp(0.95 - 0.64 * (double)get(DetSens), 0.3, 0.95);
        const bool voiced = loud && bestLag > 0 && globalMax > vThr;
        const bool sib = loud && !voiced && zcrHz > 2500.0;

        // Target.
        const int src = keySrc();
        uint16_t mask = scaleMask();
        double tgt = -1.0, detMidi = 0.0, hz = 0.0, period = sr_ * 0.005;
        const double shiftSt = ((double)get(Shift) - 0.5) * 24.0 + ((double)get(Fine) - 0.5) * 2.0;
        double semi = shiftSt, corrApplied = 0.0;
        int midiNote = -1;
        if (src == 2) {
            const bool latch = get(MidiLatch) >= 0.5f;
            midiNote = orderN_ > 0 ? order_[(size_t)orderN_ - 1] : latch ? lastNote_ : -1;
            mask = orderN_ > 0 ? heldMask() : latch ? latchMask_ : 0;
        }
        if (voiced) {
            double lagF = bestLag;
            const double nm1 = nsdf_[(size_t)bestLag - 1], n0 = nsdf_[(size_t)bestLag], np1 = nsdf_[(size_t)bestLag + 1];
            const double den = nm1 - 2 * n0 + np1;
            if (std::fabs(den) > 1e-9) lagF += 0.5 * (nm1 - np1) / den;
            hz = sr_ / std::max(1.0, lagF);
            period = std::clamp(lagF, 16.0, (double)kBuf / 8.0);
            detMidi = std::clamp(69.0 + 12.0 * std::log2(hz / 440.0), 12.0, 120.0);
            const double kc = 1.0 - std::exp(-hopSec / 0.12);
            if (!have_) { sm_ = center_ = detMidi; have_ = true; }
            else { sm_ += (detMidi - sm_) * 0.5; center_ += (sm_ - center_) * kc; }
            const double h = std::clamp((double)get(Human), 0.0, 1.0);
            const double ref = sm_ * (1.0 - h) + center_ * h;   // Human: correct the note centre, keep the wobble

            if (src == 2) {
                double t = -1.0;
                if (get(MidiMode) >= 0.5f) t = snapMask(ref, mask);
                else if (midiNote >= 0) t = get(MidiOctLock) >= 0.5f ? midiNote : midiNote + 12.0 * std::round((ref - midiNote) / 12.0);
                if (t < 0.0) midiT_ = -1.0;
                else {
                    const bool bend = get(MidiBend) >= 0.5f;
                    const double br = get(MidiBendRange) * 6.0 + 0.01;
                    if (midiT_ < 0.0 || !bend || std::fabs(t - midiT_) > br) midiT_ = t;
                    else midiT_ += (t - midiT_) * (1.0 - std::exp(-hopSec / (expMap(get(MidiGlide), 5.0, 800.0) * 0.001)));
                }
                tgt = midiT_;
            } else tgt = snapMask(ref, mask);

            if (tgt >= 0.0) {
                const double rangeSt = 1.0 + (double)std::clamp(get(Range), 0.0f, 1.0f) * 11.0;
                corrApplied = std::clamp((double)get(Amount) * (tgt - ref), -rangeSt, rangeSt);
            }
            semi = corrApplied + shiftSt;

            // Key analysis: duration-weighted pitch classes, decaying over ~16 bars of singing.
            const double barSec = 60.0 / std::max(20.0, bpm_) * std::max(1, tsNum_);
            const double decay = std::exp(-hopSec / (16.0 * barSec));
            for (auto& w : pcw_) w *= decay;
            totalW_ = totalW_ * decay + hopSec * clarity;
            pcw_[(size_t)(((int)std::lround(sm_) % 12 + 12) % 12)] += hopSec * clarity;
        } else {
            have_ = false;
            if (src == 2 && get(MidiLatch) < 0.5f && orderN_ == 0) midiT_ = -1.0;
            if (sib && get(SkipSibilants) >= 0.5f) semi = 0.0;
        }
        const bool sibSkip = sib && get(SkipSibilants) >= 0.5f;

        // Timestamp the decision at the window's centre: the shifter applies it to that audio.
        Rec& r = rec_[(size_t)(recW_ % kRec)];
        r.at = n_ - win / 2; r.semi = semi; r.period = period; r.sib = sibSkip;
        ++recW_;

        // Key analysis → Learn / Auto.
        if (++hopsSinceKey_ >= 8) { hopsSinceKey_ = 0; analyseKey(src); }

        // Telemetry + history.
        const double outMidi = voiced ? sm_ + 12.0 * std::log2(std::max(1e-6, std::pow(2.0, curSemi_ / 12.0))) : 0.0;
        tele_[S_DetMidi].store(voiced ? (float)sm_ : 0.0f, std::memory_order_relaxed);
        tele_[S_TargetMidi].store(voiced && tgt >= 0.0 ? (float)tgt : 0.0f, std::memory_order_relaxed);
        tele_[S_OutMidi].store((float)outMidi, std::memory_order_relaxed);
        tele_[S_CorrCents].store((float)(corrApplied * 100.0), std::memory_order_relaxed);
        tele_[S_Conf].store((float)clarity, std::memory_order_relaxed);
        tele_[S_Hz].store((float)hz, std::memory_order_relaxed);
        tele_[S_Voiced].store(voiced ? 1.0f : 0.0f, std::memory_order_relaxed);
        tele_[S_Sibilant].store(sib ? 1.0f : 0.0f, std::memory_order_relaxed);
        tele_[S_MidiNote].store((float)midiNote, std::memory_order_relaxed);
        tele_[S_MidiVel].store(midiNote >= 0 ? (float)(held_[(size_t)midiNote] ? held_[(size_t)midiNote] : lastVel_) : 0.0f, std::memory_order_relaxed);
        tele_[S_MidiHeld].store((float)orderN_, std::memory_order_relaxed);
        tele_[S_MidiMask].store((float)(orderN_ > 0 ? heldMask() : latchMask_), std::memory_order_relaxed);
        tele_[S_Learning].store(learning_ ? 1.0f : 0.0f, std::memory_order_relaxed);
        tele_[S_Mask].store((float)mask, std::memory_order_relaxed);
        tele_[S_Key].store((float)keyIdx(), std::memory_order_relaxed);
        tele_[S_InDb].store(inDb, std::memory_order_relaxed);
        pitchPub_.store(voiced ? (float)(sm_ / 127.0) : 0.0f, std::memory_order_relaxed);
        const int hw = histW_.load(std::memory_order_relaxed);
        hist_[0][(size_t)hw] = voiced ? (float)sm_ : 0.0f;
        hist_[1][(size_t)hw] = (float)outMidi;
        hist_[2][(size_t)hw] = src == 2 && midiT_ >= 0.0 ? (float)midiT_ : 0.0f;
        histW_.store((hw + 1) % kHist, std::memory_order_release);
    }

    uint16_t heldMask() const {
        uint16_t m = 0;
        for (int k = 0; k < orderN_; ++k) m |= (uint16_t)(1u << (order_[(size_t)k] % 12));
        return m;
    }

    static double profileCorr(const std::array<double, 12>& h, const double* prof, int key) {
        double mh = 0, mp = 0;
        for (int i = 0; i < 12; ++i) { mh += h[(size_t)i]; mp += prof[i]; }
        mh /= 12; mp /= 12;
        double num = 0, dh = 0, dp = 0;
        for (int pc = 0; pc < 12; ++pc) {
            const double a = h[(size_t)pc] - mh, b = prof[((pc - key) % 12 + 12) % 12] - mp;
            num += a * b; dh += a * a; dp += b * b;
        }
        return dh > 1e-12 && dp > 1e-12 ? num / std::sqrt(dh * dp) : 0.0;
    }

    void analyseKey(int src) {
        double mx = 0.0;
        for (double w : pcw_) mx = std::max(mx, w);
        for (int i = 0; i < 12; ++i) pcPub_[(size_t)i].store(mx > 0 ? (float)(pcw_[(size_t)i] / mx) : 0.0f, std::memory_order_relaxed);
        tele_[S_AnalysisSec].store((float)totalW_, std::memory_order_relaxed);
        const uint16_t mask = scaleMask();
        double in = 0.0, all = 0.0;
        for (int i = 0; i < 12; ++i) { all += pcw_[(size_t)i]; if (mask & (1u << i)) in += pcw_[(size_t)i]; }
        tele_[S_InScale].store(all > 0 ? (float)(in / all) : 0.0f, std::memory_order_relaxed);
        if (totalW_ < 0.4 || mx <= 0.0) {
            tele_[S_LearnKey].store(-1.0f, std::memory_order_relaxed);
            tele_[S_Learn2Key].store(-1.0f, std::memory_order_relaxed);
            return;
        }
        int bk = 0, bs = 1, k2 = 0, s2 = 2; double bc = -2, c2 = -2;
        for (int s = 1; s <= 2; ++s)
            for (int k = 0; k < 12; ++k) {
                const double c = profileCorr(pcw_, s == 1 ? kMajProf : kMinProf, k);
                if (c > bc) { c2 = bc; k2 = bk; s2 = bs; bc = c; bk = k; bs = s; }
                else if (c > c2) { c2 = c; k2 = k; s2 = s; }
            }
        best_ = { bk, bs, bc };
        tele_[S_LearnKey].store((float)bk, std::memory_order_relaxed);
        tele_[S_LearnScale].store((float)bs, std::memory_order_relaxed);
        tele_[S_LearnMatch].store((float)std::max(0.0, bc), std::memory_order_relaxed);
        tele_[S_Learn2Key].store((float)k2, std::memory_order_relaxed);
        tele_[S_Learn2Scale].store((float)s2, std::memory_order_relaxed);
        tele_[S_Learn2Match].store((float)std::max(0.0, c2), std::memory_order_relaxed);

        // Auto: follow the sung key once there's enough of it (hysteresis against flapping).
        if (src != 0 || learning_ || get(CustomScale) >= 0.5f || totalW_ < 3.0) return;
        const int sc = scaleIdx();
        if (sc == 1 || sc == 2) {
            const double cur = profileCorr(pcw_, sc == 1 ? kMajProf : kMinProf, keyIdx());
            if ((bk != keyIdx() || bs != sc) && bc > cur + 0.03) {
                p_[Key].store(bk / 11.0f, std::memory_order_relaxed);
                p_[Scale].store(bs == 1 ? 0.25f : 0.5f, std::memory_order_relaxed);
            }
        } else if (sc != 0) {
            int best = keyIdx(); double be = -1;
            for (int k = 0; k < 12; ++k) {
                const uint16_t m = rot(kScaleMask[sc], k);
                double e = 0; for (int i = 0; i < 12; ++i) if (m & (1u << i)) e += pcw_[(size_t)i];
                if (e > be + 1e-9) { be = e; best = k; }
            }
            if (best != keyIdx()) p_[Key].store(best / 11.0f, std::memory_order_relaxed);
        }
    }

    void commitLearn() {
        analyseKey(-1);
        if (totalW_ < 0.4 || best_.key < 0) return;
        p_[Key].store(best_.key / 11.0f, std::memory_order_relaxed);
        p_[Scale].store(best_.scale == 1 ? 0.25f : 0.5f, std::memory_order_relaxed);
        p_[CustomScale].store(0.0f, std::memory_order_relaxed);
        if (keySrc() == 0) p_[KeySource].store(0.5f, std::memory_order_relaxed);
    }

    // --- TD-PSOLA grain ------------------------------------------------------------------
    // Synthesis mark s (input time), period P, pitch ratio r, formant factor fr. The analysis
    // mark is the one a whole number of periods from the last, nearest s, whose grain is
    // fully written; `snap` takes it at s (sibilants).
    void grain(double s, double P, double r, double fr, bool snap, int64_t to) {
        (void)r;
        const double halfIn = P, halfOut = P / fr;
        // A whole number of periods (the last grain's, so a gliding period stays exact at unity)
        // on from the last mark. Near unity a leftover offset from an earlier shift relaxes by
        // ≤ 0.3 % of a period per grain (a few cents at most), so an in-tune voice ends up
        // taken exactly where it lands. Sibilants have no period to keep: take them in place.
        const double step = lastP_ > 0.0 ? lastP_ : P;
        double m = std::round((s - la_) / step);
        if (m < 0.0 || m > 64.0) { la_ = s; m = 0.0; }
        double A = la_ + m * step;
        if (snap) A = s;
        else if (std::fabs(r - 1.0) < 0.003) A += std::clamp(s - A, -0.003 * P, 0.003 * P);
        const double newest = (double)(n_ - 2);
        for (int g = 0; g < 16 && A + halfIn > newest; ++g) A -= step;
        const double oldest = (double)(n_ - kBuf + 4);
        if (A - halfIn < oldest) A = oldest + halfIn;
        la_ = A; lastP_ = P;
        int64_t k0 = (int64_t)std::ceil(s - halfOut), k1 = (int64_t)std::floor(s + halfOut);
        if (k0 < to) k0 = to;
        if (k1 > to + kBuf / 2) k1 = to + kBuf / 2;
        const double invH = 1.0 / halfOut;
        for (int64_t k = k0; k <= k1; ++k) {
            const double u = (double)k - s;
            const float w = (float)(0.5 + 0.5 * std::cos(kPi * u * invH));
            const double x = A + u * fr;
            const int64_t xi = (int64_t)std::floor(x);
            const float fx = (float)(x - (double)xi);
            const size_t i0 = (size_t)(xi & kMask), i1 = (size_t)((xi + 1) & kMask), o = (size_t)(k & kMask);
            acc_[0][o] += w * (ring_[0][i0] + (ring_[0][i1] - ring_[0][i0]) * fx);
            acc_[1][o] += w * (ring_[1][i0] + (ring_[1][i1] - ring_[1][i0]) * fx);
        }
    }

    double sr_ = 44100.0;
    int lat_ = 706, hop_ = 256, baseWin_ = 1024;
    std::atomic<float> p_[kNumParams] = {};
    std::array<std::array<float, kBuf>, 2> ring_{};
    std::array<std::array<float, kBuf>, 2> acc_{};
    std::array<float, kBuf> det_{};
    std::array<float, kFftMax> re_{}, im_{};
    std::array<float, kFftMax / 2> twC_{}, twS_{};
    std::array<double, kWinMax + 1> pe_{};
    std::array<float, kWinMax> nsdf_{};
    std::array<Rec, kRec> rec_{};
    int64_t recW_ = 0;
    int64_t n_ = 0;
    int hopCnt_ = 0;
    double pg_ = 220.0, ns_ = 0.0, la_ = 0.0, lastP_ = 0.0, curSemi_ = 0.0;
    double eDry_ = 0.0, eWet_ = 0.0, gAuto_ = 1.0, lvlA_ = 0.0003, lvlB_ = 0.0005;   // level match
    double sm_ = 60.0, center_ = 60.0, midiT_ = -1.0;
    bool have_ = false;

    // MIDI source
    std::atomic<int32_t> srcTrack_{-1};
    const MidiEv* midiEvs_ = nullptr;
    int32_t midiN_ = 0;
    bool midiFed_ = false;
    std::array<uint8_t, 128> held_{};
    std::array<int, 16> order_{};
    int orderN_ = 0, lastNote_ = -1, lastVel_ = 0;
    uint16_t latchMask_ = 0;

    // key analysis
    std::array<double, 12> pcw_{};
    double totalW_ = 0.0;
    int hopsSinceKey_ = 0;
    struct Best { int key = -1, scale = 1; double corr = 0.0; } best_;
    bool learning_ = false;
    double bpm_ = 120.0; int tsNum_ = 4;

    // UI feeds
    std::array<std::atomic<float>, kTele> tele_{};
    std::array<std::atomic<float>, 12> pcPub_{};
    std::array<std::array<float, kHist>, 3> hist_{};
    std::atomic<int> histW_{0};
    std::atomic<float> pitchPub_{0.0f}, srA_{44100.0f}, cpuA_{0.0f}, ratioA_{1.0f}, midiLiveA_{0.0f};
    std::atomic<bool> resetReq_{false};
    std::atomic<int> learnReq_{0};
    double cpuS_ = 0.0;
};

} // namespace nota
