// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Built-in Nota Forge (device kind 17) — a multi-stage saturation effect (mockup 3m).
// Three saturation stages are ALL visible at once, each with its own algorithm, drive,
// output trim and self-feedback; four routing topologies (Serial / Parallel / Mid-Side /
// Multiband) decide how the stages combine. A global Amount drives into the chain, a Tone
// tilt + Bias (asymmetry) + Width shape the result, and an LFO→Drive / Env→Tone modulation
// section keeps the transfer curve moving. A core Device (JUCE-free), lock-free params —
// all normalized 0..1, denormalized in process(); persistence/automation/clone are generic.

#pragma once

#include "Device.h"
#include "Oversampler.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota {

class Forge : public Device {
public:
    static constexpr int kStages = 3;
    static constexpr int kAlgos = 6;   // 0 Tube, 1 Diode, 2 Tape, 3 Fuzz, 4 Digital, 5 Fold

    enum {
        Amount = 0,  // master input drive (0..1 → 0..+30 dB into the stages)
        Tone,        // post tilt (bipolar, 0.5 = flat; + brighter / − darker)
        Wet,         // dry/wet mix (0..1)
        Output,      // master output gain (0.5 = 0 dB, ±24 dB)
        Bias,        // shaper asymmetry (bipolar, 0.5 = symmetric)
        Width,       // stereo width of the wet signal (0..1 → 0..200 %)
        Routing,     // 0 Serial, 1 Parallel, 2 Mid/Side, 3 Multiband
        LfoDrive,    // LFO → stage drive depth (0..1)
        EnvTone,     // envelope → tone depth (0..1)
        LfoRate,     // LFO rate (exp 0.05..20 Hz free, or a synced division)
        LfoSync,     // 0 = free (Hz), 1 = tempo-synced
        // per-stage block of 5 (Type, Drive, Out, FB, On), ×3
        S1Type, S1Drive, S1Out, S1FB, S1On,
        S2Type, S2Drive, S2Out, S2FB, S2On,
        S3Type, S3Drive, S3Out, S3FB, S3On,
        Oversampling,  // 0..1 → Off / 2× / 4× / 8× (appended last: keeps saved indices stable)
        kNumParams
    };
    static constexpr double kLfoDiv[8] = { 8.0, 4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 0.0625 };

    Forge() {
        p_[Amount].store(0.35f); p_[Tone].store(0.5f); p_[Wet].store(1.0f); p_[Output].store(0.5f);
        p_[Bias].store(0.5f); p_[Width].store(0.5f); p_[Routing].store(0.0f);
        p_[LfoDrive].store(0.0f); p_[EnvTone].store(0.0f); p_[LfoRate].store(0.4f); p_[LfoSync].store(1.0f);
        setStage(0, 0, 0.4f, 0.5f, 0.0f, 1.0f);   // Tube, on
        setStage(1, 1, 0.3f, 0.5f, 0.0f, 0.0f);   // Diode, off
        setStage(2, 4, 0.2f, 0.5f, 0.0f, 0.0f);   // Digital, off
    }

    void setSampleRate(double sr, int32_t maxBlock) override {
        sr_ = sr > 0 ? sr : 44100.0;
        for (auto& z : fb_) z = 0;
        for (auto& z : lpTone_) z = 0;
        for (auto& z : mbLo_) z = 0;
        for (auto& z : mbHi_) z = 0;
        env_ = 0; lfoPhase_ = 0;
        maxFrames_ = maxBlock > 0 ? maxBlock : 4096;
        os_.prepare(maxFrames_);
        dryL_.assign(maxFrames_, 0.0f); dryR_.assign(maxFrames_, 0.0f);
        driveMod_.assign(maxFrames_, 0.0f); tone_.assign(maxFrames_, 0.0f);
    }

    void setTransport(double beatStart, double spb, bool playing) override {
        beatStart_ = beatStart; spb_ = spb > 0 ? spb : 0.0; playing_ = playing;
    }

    // Publish the live modulated LFO value (0..1) so the UI can breathe the transfer curve.
    // Reuses the generic per-device live-scalar channel (no gain reduction on a saturator).
    float gainReductionDb() const override { return lfoLive_.load(std::memory_order_relaxed); }

    void process(float* buf, int32_t frames) override {
        const float amt = std::pow(10.0f, get(Amount) * 30.0f / 20.0f);      // 0..+30 dB pre-gain
        const float toneBase = (get(Tone) - 0.5f) * 2.0f;                    // −1..+1
        const float wet = std::clamp(get(Wet), 0.0f, 1.0f);
        const float outGain = std::pow(10.0f, (get(Output) - 0.5f) * 48.0f / 20.0f);
        const float bias = (get(Bias) - 0.5f) * 2.0f;                        // −1..+1
        const float width = std::clamp(get(Width), 0.0f, 1.0f) * 2.0f;       // 0..2
        const int routing = std::clamp((int)std::lround(get(Routing) * 3.0f), 0, 3);
        const float lfoDepth = std::clamp(get(LfoDrive), 0.0f, 1.0f);
        const float envDepth = std::clamp(get(EnvTone), 0.0f, 1.0f);

        // Per-stage denormalized settings.
        int   type[kStages]; bool on[kStages]; float drive[kStages], outTrim[kStages], fb[kStages];
        for (int s = 0; s < kStages; ++s) {
            type[s] = std::clamp((int)std::lround(get(S1Type + s * 5) * (kAlgos - 1)), 0, kAlgos - 1);
            drive[s] = std::clamp(get(S1Drive + s * 5), 0.0f, 1.0f);
            outTrim[s] = std::pow(10.0f, (get(S1Out + s * 5) - 0.5f) * 24.0f / 20.0f);
            fb[s] = std::clamp(get(S1FB + s * 5), 0.0f, 1.0f) * 0.85f;
            on[s] = get(S1On + s * 5) >= 0.5f;
        }

        // LFO increment (free Hz, or synced division).
        const bool sync = get(LfoSync) >= 0.5f && spb_ > 0.0;
        const double lfoHz = expMap(get(LfoRate), 0.05, 20.0);
        const double divBeats = kLfoDiv[std::clamp((int)std::lround(get(LfoRate) * 7.0f), 0, 7)];
        const double phaseInc = lfoHz / sr_;

        // Oversampling: Off/2×/4×/8×. The nonlinear stages (which alias) run at the
        // higher rate; control (LFO/env), the tone tilt, width and dry/wet stay at
        // base. Multiband crossovers run inside the core, so their coeffs use the OS
        // rate; the post tone tilt stays at base.
        const int osIdx = std::clamp((int)std::lround(get(Oversampling) * 3.0f), 0, 3);
        const int factor = 1 << osIdx;   // 1,2,4,8
        os_.setActive(factor);
        const double osr = sr_ * factor;

        // Tone tilt one-pole (~700 Hz, base rate) + env follower coeffs (base rate).
        const float toneCoef = (float)std::exp(-2.0 * 3.14159265 * 700.0 / sr_);
        const float envAtt = (float)std::exp(-1.0 / (0.005 * sr_));
        const float envRel = (float)std::exp(-1.0 / (0.15 * sr_));
        // Multiband crossover one-pole coeffs (200 Hz / 2 kHz) at the oversampled rate.
        const float loCoef = (float)std::exp(-2.0 * 3.14159265 * 200.0 / osr);
        const float hiCoef = (float)std::exp(-2.0 * 3.14159265 * 2000.0 / osr);

        if (frames > maxFrames_) frames = maxFrames_;

        // --- Base-rate pre-pass: LFO/env → per-sample control, and stash the dry. ---
        for (int32_t i = 0; i < frames; ++i) {
            double phase;
            if (sync) { double beat = beatStart_ + (double)i / spb_; phase = beat / divBeats; phase -= std::floor(phase); }
            else phase = lfoPhase_;
            const float lfo = (float)(0.5 + 0.5 * std::sin(2.0 * 3.14159265 * phase));   // 0..1
            lfoLive_.store(lfo, std::memory_order_relaxed);

            dryL_[i] = buf[i * 2]; dryR_[i] = buf[i * 2 + 1];
            const float det = std::max(std::fabs(buf[i * 2] * amt), std::fabs(buf[i * 2 + 1] * amt));
            if (det > env_) env_ = det + envAtt * (env_ - det); else env_ = det + envRel * (env_ - det);
            tone_[i] = std::clamp(toneBase + envDepth * std::min(env_, 1.0f) * 1.5f, -1.5f, 1.5f);
            driveMod_[i] = lfoDepth * (lfo - 0.5f) * 0.6f;   // ±0.3 to stage drive

            if (!sync) { lfoPhase_ += phaseInc; if (lfoPhase_ >= 1.0) lfoPhase_ -= 1.0; }
        }

        // --- Oversampled nonlinear core: pre-gain + saturation stages. ---
        os_.process(buf, frames, [&](float& l, float& r, int i) {
            l *= amt; r *= amt;
            float wl, wr;
            route(routing, l, r, type, on, drive, outTrim, fb, bias, driveMod_[i], loCoef, hiCoef, wl, wr);
            l = wl; r = wr;
        });

        // --- Base-rate post: tone tilt, width, output, dry/wet. ---
        for (int32_t i = 0; i < frames; ++i) {
            float wl = buf[i * 2], wr = buf[i * 2 + 1];

            lpTone_[0] = wl + toneCoef * (lpTone_[0] - wl);
            lpTone_[1] = wr + toneCoef * (lpTone_[1] - wr);
            wl += tone_[i] * (wl - lpTone_[0]);
            wr += tone_[i] * (wr - lpTone_[1]);

            if (routing != 2) {   // Mid/Side routing already worked in M/S; leave its image
                const float m = 0.5f * (wl + wr), s2 = 0.5f * (wl - wr) * width;
                wl = m + s2; wr = m - s2;
            }

            wl *= outGain; wr *= outGain;
            buf[i * 2]     = dryL_[i] * (1.0f - wet) + wl * wet;
            buf[i * 2 + 1] = dryR_[i] * (1.0f - wet) + wr * wet;
        }
    }

    const char* displayName() const override { return "Nota Forge"; }
    int32_t     builtinKind() const override { return 17; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Amount: return "Amount"; case Tone: return "Tone"; case Wet: return "Wet"; case Output: return "Output";
            case Bias: return "Bias"; case Width: return "Width"; case Routing: return "Routing";
            case LfoDrive: return "LFO Drive"; case EnvTone: return "Env Tone"; case LfoRate: return "LFO Rate"; case LfoSync: return "LFO Sync";
            case S1Type: return "S1 Type"; case S1Drive: return "S1 Drive"; case S1Out: return "S1 Out"; case S1FB: return "S1 FB"; case S1On: return "S1 On";
            case S2Type: return "S2 Type"; case S2Drive: return "S2 Drive"; case S2Out: return "S2 Out"; case S2FB: return "S2 FB"; case S2On: return "S2 On";
            case S3Type: return "S3 Type"; case S3Drive: return "S3 Drive"; case S3Out: return "S3 Out"; case S3FB: return "S3 FB"; case S3On: return "S3 On";
            case Oversampling: return "Oversampling";
            default: return "";
        }
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed); }

    // Static saturation transfer used by the UI curve/harmonics (kept in sync with process()).
    // driveN/biasN are the normalized params; x is the pre-gained input.
    static float shape(int type, float x, float bias) {
        const float b = bias * 0.6f;
        switch (type) {
            case 0: return std::tanh(x + b) - std::tanh(b);                                   // Tube (asymmetric soft)
            case 1: return x + b >= 0 ? std::tanh((x + b) * 1.3f) : std::tanh((x + b) * 0.55f); // Diode (asymmetric hard)
            case 2: return std::tanh(x + b * 0.4f);                                            // Tape (soft symmetric)
            case 3: { float y = (x + b) / (1.0f + std::fabs(x + b)); return std::tanh(y * 3.0f); } // Fuzz
            case 4: return std::clamp(x + b, -1.0f, 1.0f);                                     // Digital (hard clip)
            default: return std::sin((x + b) * 0.9f);                                          // Fold (wavefolder)
        }
    }
    static float driveGain(float driveN) { return 1.0f + driveN * driveN * 20.0f; }

private:
    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    void setStage(int s, float type, float drive, float out, float fb, float on) {
        p_[S1Type + s * 5].store(type / (kAlgos - 1)); p_[S1Drive + s * 5].store(drive);
        p_[S1Out + s * 5].store(out); p_[S1FB + s * 5].store(fb); p_[S1On + s * 5].store(on);
    }

    // One stage on one channel, with self-feedback. `slot` indexes the fb state (stage*2+ch).
    inline float stage(int slot, int type, float driveN, float outTrim, float fbAmt, float bias, float driveMod, float x) {
        const float g = driveGain(std::clamp(driveN + driveMod, 0.0f, 1.0f));
        const float in = (x + fbAmt * fb_[slot]) * g;
        float y = shape(type, in, bias);
        y *= 1.0f / std::sqrt(g);          // gentle auto-makeup so drive doesn't just get loud
        fb_[slot] = y;
        return y * outTrim;
    }

    inline void route(int routing, float l, float r, const int* type, const bool* on, const float* drive,
                      const float* outTrim, const float* fb, float bias, float driveMod,
                      float loCoef, float hiCoef, float& wl, float& wr) {
        auto serial = [&](int ch, float x) {
            for (int s = 0; s < kStages; ++s) if (on[s]) x = stage(s * 2 + ch, type[s], drive[s], outTrim[s], fb[s], bias, driveMod, x);
            return x;
        };
        auto parallel = [&](int ch, float x) {
            float sum = 0; int n = 0;
            for (int s = 0; s < kStages; ++s) if (on[s]) { sum += stage(s * 2 + ch, type[s], drive[s], outTrim[s], fb[s], bias, driveMod, x); ++n; }
            return n ? sum / n : x;
        };
        switch (routing) {
            case 0: wl = serial(0, l); wr = serial(1, r); break;
            case 1: wl = parallel(0, l); wr = parallel(1, r); break;
            case 2: {  // Mid/Side: serial chain on M and S independently
                float m = 0.5f * (l + r), s = 0.5f * (l - r);
                m = serial(0, m); s = serial(1, s);
                wl = m + s; wr = m - s; break;
            }
            default: {  // Multiband: stage i drives band i (low / mid / high)
                wl = multiband(0, l, type, on, drive, outTrim, fb, bias, driveMod, loCoef, hiCoef);
                wr = multiband(1, r, type, on, drive, outTrim, fb, bias, driveMod, loCoef, hiCoef);
            }
        }
    }

    inline float multiband(int ch, float x, const int* type, const bool* on, const float* drive,
                            const float* outTrim, const float* fb, float bias, float driveMod, float loCoef, float hiCoef) {
        float& lo = mbLo_[ch]; float& hi = mbHi_[ch];
        lo = x + loCoef * (lo - x);            // low band (LP 200 Hz)
        const float aboveLo = x - lo;
        hi = aboveLo + hiCoef * (hi - aboveLo);// mid band (LP 2 kHz of the >200 Hz part)
        const float mid = hi;
        const float high = aboveLo - hi;       // high band
        float y = 0;
        y += on[0] ? stage(0 * 2 + ch, type[0], drive[0], outTrim[0], fb[0], bias, driveMod, lo) : lo;
        y += on[1] ? stage(1 * 2 + ch, type[1], drive[1], outTrim[1], fb[1], bias, driveMod, mid) : mid;
        y += on[2] ? stage(2 * 2 + ch, type[2], drive[2], outTrim[2], fb[2], bias, driveMod, high) : high;
        return y;
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float fb_[kStages * 2] = {};       // per stage/channel self-feedback state
    float lpTone_[2] = {};             // tone tilt one-pole
    float mbLo_[2] = {}, mbHi_[2] = {};// multiband crossover one-poles
    float env_ = 0;
    double lfoPhase_ = 0;
    double beatStart_ = 0, spb_ = 0; bool playing_ = false;
    std::atomic<float> lfoLive_{0.5f};

    Oversampler os_;                                   // nonlinear-stage anti-aliasing
    int32_t maxFrames_ = 4096;
    std::vector<float> dryL_, dryR_, driveMod_, tone_; // base-rate stashes (sized in setSampleRate)
};

} // namespace nota
