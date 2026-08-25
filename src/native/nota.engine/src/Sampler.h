// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Sampler (kind 1) — classic sampler-style. Plays one sample pitched by
// MIDI note relative to a root, over a Start/End window, with a loop (off / forward /
// ping-pong, forward loops can run in reverse) and a loop crossfade, reverse, transpose+
// detune, per-voice amp ADSR and a TPT state-variable filter with optional key-tracking,
// a Poly/Mono/Choke voice mode, plus volume/pan. All the continuous controls run through
// the Instrument plugin-param interface (normalized 0..1) so they automate/persist like
// the Nota Synth. `setSample` + kind 1 stay unchanged for Drum/Instrument Rack
// compatibility; the defaults reproduce the old one-shot behavior. Params are APPEND-ONLY.

#pragma once

#include "Instrument.h"
#include "SampleBuffer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <memory>

namespace nota {

class Sampler final : public Instrument {
public:
    enum Param { Volume = 0, Pan, Transpose, Detune, Start, End, Reverse, LoopMode,
                 LoopStart, LoopEnd, Attack, Decay, Sustain, Release, FilterType, Cutoff, Resonance,
                 VoiceMode, LoopXfade, FilterKeyTrack, VelAmount, kNumParams };

    Sampler() {
        pn_[Volume].store(1.0f);   pn_[Pan].store(0.5f);
        pn_[Transpose].store(0.5f); pn_[Detune].store(0.5f);   // 0 st / 0 cents
        pn_[Start].store(0.0f);    pn_[End].store(1.0f);
        pn_[Reverse].store(0.0f);  pn_[LoopMode].store(0.0f);
        pn_[LoopStart].store(0.0f); pn_[LoopEnd].store(1.0f);
        pn_[Attack].store(0.0f);   pn_[Decay].store(0.3f);
        pn_[Sustain].store(1.0f);  pn_[Release].store(0.06f);
        pn_[FilterType].store(0.0f); pn_[Cutoff].store(1.0f); pn_[Resonance].store(0.0f);
        pn_[VoiceMode].store(0.0f);  pn_[LoopXfade].store(0.1f); pn_[FilterKeyTrack].store(0.0f);
        pn_[VelAmount].store(1.0f);
    }

    int32_t kind() const override { return 1; }
    const char* displayName() const override { return "Nota Sampler"; }

    // --- sample (persist / Drum Rack; not a plugin-param) ------------------
    std::shared_ptr<SampleBuffer> sample() const { return sample_; }
    int32_t rootNote() const { return rootNote_.load(std::memory_order_relaxed); }
    void    setRoot(int32_t r) { rootNote_.store(std::clamp(r, 0, 127), std::memory_order_relaxed); }
    bool    loopEnabled() const { return pn_[LoopMode].load(std::memory_order_relaxed) > 0.5f; }
    void setSample(std::shared_ptr<SampleBuffer> s, int32_t rootNote, bool loop) {
        sample_ = std::move(s);
        rootNote_.store(std::clamp(rootNote, 0, 127), std::memory_order_relaxed);
        pn_[LoopMode].store(loop ? 1.0f : 0.0f, std::memory_order_relaxed);   // Drum Rack loop flag → forward loop
    }

    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }

    // Live playback position (0..1 of the sample) of the loudest active voice, or -1
    // if silent — for the UI's real-time cursor. Audio thread writes, UI reads.
    float playPosition() const { return playPos_.load(std::memory_order_relaxed); }

    // Sounding voices, for the shell's voice meter (message-thread read of a snapshot).
    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    // --- plugin-params (automatable) --------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[] = { "volume", "pan", "transpose", "detune", "start", "end", "reverse", "loopmode",
                                     "loopstart", "loopend", "attack", "decay", "sustain", "release", "filtertype", "cutoff", "resonance",
                                     "voicemode", "loopxfade", "keytrack", "velamount" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        static const char* nm[] = { "Volume", "Pan", "Transpose", "Detune", "Start", "End", "Reverse", "Loop",
                                    "Loop Start", "Loop End", "Attack", "Decay", "Sustain", "Release", "Filter", "Cutoff", "Resonance",
                                    "Voices", "Loop Xfade", "Key Track", "Vel→Vol" };
        return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
    }
    float pluginParamGet(int32_t i) const override { return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  pluginParamSet(int32_t i, float v) override { if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (pluginParamId(i) == id) return i;
        return -1;
    }

    std::shared_ptr<Instrument> clone() const override {
        auto s = std::make_shared<Sampler>();
        s->sample_ = sample_; s->rootNote_.store(rootNote_.load(std::memory_order_relaxed), std::memory_order_relaxed);
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    void noteOn(int32_t pitch, float velocity) override {
        if (!sample_ || sample_->empty()) return;
        const int vm = voiceModeOf();
        if (vm == 1) { for (auto& v : voices_) if (v.active && v.stage != Stage::Release) v.stage = Stage::Release; }  // Mono: release others
        else if (vm == 2) { for (auto& v : voices_) v.active = false; }                                                // Choke: hard cut
        Voice* v = findFreeVoice();
        const double startF = std::clamp(pn_[Start].load(std::memory_order_relaxed), 0.0f, 1.0f) * sample_->frames;
        const double endF   = std::clamp(pn_[End].load(std::memory_order_relaxed), 0.0f, 1.0f) * sample_->frames;
        const bool rev = pn_[Reverse].load(std::memory_order_relaxed) > 0.5f;
        v->active = true; v->stage = Stage::Attack; v->env = 0.0f;
        v->pitch = pitch; v->velocity = velocity; v->dir = rev ? -1 : 1;
        v->pos = rev ? std::max(startF, endF - 1.0) : startF;
        v->ic1L = v->ic2L = v->ic1R = v->ic2R = 0.0;
    }
    void noteOff(int32_t pitch) override {
        for (auto& v : voices_) if (v.active && v.pitch == pitch && v.stage != Stage::Release) v.stage = Stage::Release;
    }
    void allNotesOff() override { for (auto& v : voices_) v.active = false; }

    void render(float* out, int32_t frames) override {
        if (!sample_ || sample_->empty()) return;
        const double N = static_cast<double>(sample_->frames);
        double startF = std::clamp(pn_[Start].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
        double endF   = std::clamp(pn_[End].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
        if (endF <= startF + 1) endF = std::min(N, startF + 1);
        double loopA = std::clamp(pn_[LoopStart].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
        double loopB = std::clamp(pn_[LoopEnd].load(std::memory_order_relaxed), 0.0f, 1.0f) * N;
        loopA = std::clamp(loopA, startF, endF); loopB = std::clamp(loopB, startF, endF);
        if (loopB <= loopA + 1) loopB = std::min(endF, loopA + 1);
        const int loopMode = loopModeOf();
        const double loopLen = loopB - loopA;
        // Loop crossfade length (samples), capped to half the loop — forward loop only.
        double xf = pn_[LoopXfade].load(std::memory_order_relaxed) * 0.2 * sampleRate_;
        xf = std::clamp(xf, 0.0, loopLen * 0.5);

        const int32_t root = rootNote_.load(std::memory_order_relaxed);
        const double pitchOff = (pn_[Transpose].load(std::memory_order_relaxed) - 0.5) * 48.0
                              + (pn_[Detune].load(std::memory_order_relaxed) - 0.5) * 1.0;   // ±50 cents
        const double srcRatio = sample_->sourceSampleRate / sampleRate_;
        const float gain = pn_[Volume].load(std::memory_order_relaxed);
        const double pan = pn_[Pan].load(std::memory_order_relaxed) * 2.0 - 1.0;
        const float gl = static_cast<float>(std::cos((pan + 1.0) * kPi / 4.0));
        const float gr = static_cast<float>(std::sin((pan + 1.0) * kPi / 4.0));

        const float atkRate = static_cast<float>(1.0 / (secOf(Attack, 0.0005, 4.0) * sampleRate_));
        const float decRate = static_cast<float>(1.0 / (secOf(Decay, 0.002, 6.0) * sampleRate_));
        const float relRate = static_cast<float>(1.0 / (secOf(Release, 0.002, 6.0) * sampleRate_));
        const float sustain = pn_[Sustain].load(std::memory_order_relaxed);
        const float velAmt  = pn_[VelAmount].load(std::memory_order_relaxed);   // 1 = full velocity→volume, 0 = flat

        // Filter (TPT SVF). Cutoff can key-track the note relative to the root.
        const int fType = std::clamp(static_cast<int>(std::lround(pn_[FilterType].load(std::memory_order_relaxed) * 3.0f)), 0, 3);
        const double baseCut = expMap(pn_[Cutoff].load(std::memory_order_relaxed), 20.0, 20000.0);
        const double kTrack = pn_[FilterKeyTrack].load(std::memory_order_relaxed);
        const double k  = 2.0 - 1.9 * pn_[Resonance].load(std::memory_order_relaxed);

        int nActive = 0;
        for (auto& v : voices_) {
            if (!v.active) continue;
            ++nActive;
            const double inc = std::pow(2.0, (v.pitch - root + pitchOff) / 12.0) * srcRatio;
            // Per-voice filter coefficients (key-tracking shifts cutoff by note).
            double fc = baseCut;
            if (fType > 0 && kTrack > 0.0) fc = baseCut * std::pow(2.0, kTrack * (v.pitch - root) / 12.0);
            const double g  = std::tan(kPi * std::clamp(fc, 20.0, sampleRate_ * 0.49) / sampleRate_);
            const double a1 = 1.0 / (1.0 + g * (g + k)), a2 = g * a1, a3 = g * a2;
            for (int32_t i = 0; i < frames; ++i) {
                // envelope
                switch (v.stage) {
                    case Stage::Attack:  v.env += atkRate; if (v.env >= 1.0f) { v.env = 1.0f; v.stage = Stage::Decay; } break;
                    case Stage::Decay:   v.env -= decRate; if (v.env <= sustain) { v.env = sustain; v.stage = Stage::Sustain; } break;
                    case Stage::Sustain: break;
                    case Stage::Release: v.env -= relRate; if (v.env <= 0.0f) { v.env = 0.0f; v.active = false; } break;
                }
                if (!v.active) break;

                float ls, rs; readInterp(v.pos, ls, rs);
                // Forward-loop crossfade: as we approach loopB, blend in the loop head.
                if (loopMode == 1 && xf > 1.0 && v.dir > 0 && v.pos > loopB - xf) {
                    const double t = (v.pos - (loopB - xf)) / xf;   // 0..1 across the seam
                    float hl, hr; readInterp(v.pos - loopLen, hl, hr);
                    ls = static_cast<float>(ls * (1.0 - t) + hl * t);
                    rs = static_cast<float>(rs * (1.0 - t) + hr * t);
                }

                if (fType > 0) { ls = svf(v.ic1L, v.ic2L, ls, a1, a2, a3, k, fType); rs = svf(v.ic1R, v.ic2R, rs, a1, a2, a3, k, fType); }

                const float amp = v.env * (velAmt * v.velocity + (1.0f - velAmt)) * gain;
                out[i * 2]     += ls * amp * gl;
                out[i * 2 + 1] += rs * amp * gr;

                // advance + loop / end handling
                v.pos += inc * v.dir;
                if (loopMode == 1) {                 // forward loop (either direction)
                    if (v.dir > 0) { if (v.pos >= loopB) v.pos -= loopLen; }
                    else           { if (v.pos <= loopA) v.pos += loopLen; }
                } else if (loopMode == 2) {          // ping-pong
                    if (v.pos >= loopB) { v.pos = loopB - (v.pos - loopB); v.dir = -1; }
                    else if (v.pos <= loopA) { v.pos = loopA + (loopA - v.pos); v.dir = 1; }
                } else {                             // one-shot (respects reverse)
                    if (v.pos >= endF || v.pos < startF) { v.active = false; break; }
                }
            }
        }
        // Publish the loudest active voice's position + the voice count for the UI.
        float bestEnv = -1.0f; double bestPos = 0.0; bool any = false;
        for (auto& v : voices_) if (v.active && v.env > bestEnv) { bestEnv = v.env; bestPos = v.pos; any = true; }
        playPos_.store(any ? static_cast<float>(bestPos / N) : -1.0f, std::memory_order_relaxed);
        activeVoices_.store(nActive, std::memory_order_relaxed);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    enum class Stage { Attack, Decay, Sustain, Release };
    struct Voice {
        bool    active = false;
        int32_t pitch = 0, dir = 1;
        double  pos = 0.0;
        float   velocity = 0.0f, env = 0.0f;
        double  ic1L = 0, ic2L = 0, ic1R = 0, ic2R = 0;
        Stage   stage = Stage::Attack;
    };
    Voice* findFreeVoice() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0];
        for (auto& v : voices_) if (v.env < q->env) q = &v;
        return q;
    }
    // Linear-interpolated stereo read at a fractional sample position.
    void readInterp(double pos, float& l, float& r) const {
        const int64_t i0 = static_cast<int64_t>(pos);
        float l0, r0, l1, r1;
        sample_->readStereo(std::clamp<int64_t>(i0, 0, sample_->frames - 1), l0, r0);
        sample_->readStereo(std::clamp<int64_t>(i0 + 1, 0, sample_->frames - 1), l1, r1);
        const double frac = pos - i0;
        l = static_cast<float>(l0 + (l1 - l0) * frac);
        r = static_cast<float>(r0 + (r1 - r0) * frac);
    }
    int loopModeOf() const { return std::clamp(static_cast<int>(std::lround(pn_[LoopMode].load(std::memory_order_relaxed) * 2.0f)), 0, 2); }
    int voiceModeOf() const { return std::clamp(static_cast<int>(std::lround(pn_[VoiceMode].load(std::memory_order_relaxed) * 2.0f)), 0, 2); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    double secOf(Param p, double lo, double hi) const { return expMap(pn_[p].load(std::memory_order_relaxed), lo, hi); }
    static float svf(double& ic1, double& ic2, float xf, double a1, double a2, double a3, double k, int type) {
        const double x = xf;
        const double v3 = x - ic2, v1 = a1 * ic1 + a2 * v3, v2 = ic2 + a2 * ic1 + a3 * v3;
        ic1 = 2.0 * v1 - ic1; ic2 = 2.0 * v2 - ic2;
        if (type == 1) return static_cast<float>(v2);                 // low-pass
        if (type == 2) return static_cast<float>(x - k * v1 - v2);    // high-pass
        return static_cast<float>(v1);                                // band-pass
    }

    static constexpr int kVoices = 16;
    Voice  voices_[kVoices];
    std::shared_ptr<SampleBuffer> sample_;
    std::atomic<int32_t> rootNote_{60};
    double  sampleRate_ = 44100.0;
    std::atomic<float> playPos_{-1.0f};
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
