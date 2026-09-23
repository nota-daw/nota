// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Valve (device kind 6, was "Amp") — a guitar-amp insert. A preamp
// waveshaper (tanh cascade, per-model drive + bias — or an even-harmonics-only stage)
// feeds a Bass / Middle (sweepable) / Treble / Presence tone stack with Bright and Deep
// switches, then a speaker cabinet (five cabs with their low resonance and presence
// peak; a Dynamic / Condenser / Ribbon mic at a distance, on or off axis, at the cap or
// the edge), low / high cuts, auto gain compensation, a noise gate keyed on the input,
// the master Output and Dry/Wet. Seven voicings from Clean to Heavy plus a Bass amp.
//
//   in ─▶ pre-HP ─▶ bright ─▶ [oversampled preamp] ─▶ tone stack ─▶ deep ─▶ cabinet
//      ─▶ low / high cut ─▶ makeup · auto-comp ─▶ output ─▶ gate ─▶ mix ─▶ out
//
// Header-only, allocation-free after construction. Params 0..13 keep their original raw
// units (Gain / tone / Output 0..10, Model / Cabinet / Mic indices) so older projects and
// presets load unchanged; the appended ones are normalized 0..1 and default to the old
// sound. Persistence / automation / clone flow generically through the base Device.
//
// Telemetry for the card and MCP (scopeRead): kTele live values, then four kResp-point
// magnitude responses in dB on a log grid 30 Hz … 16 kHz — the tone stack now, the tone
// stack flat (knobs at noon, no bright / deep), the cabinet now, the cabinet on axis
// (at the cap, 5 cm) — computed from the same coefficients process() runs.
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.

#pragma once

#include "Device.h"
#include "Oversampler.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <complex>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Amp : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Model = 0, Gain, Bass, Middle, Treble, Presence, Output, Mix,
           CabOn, CabType, Mic, Axis, Gate,
           Oversampling,   // 0..1 → Off/2×/4×/8×
           MidFreq,        // middle peak 200·10^v Hz (0.512 = 650 Hz, the old fixed one)
           MicDistance,    // 1·30^v cm (0.473 = 5 cm = neutral proximity)
           MicPosition,    // 0 cap, 1 edge (darker)
           LowCut,         // 0 off, else 20·15^v Hz (12 dB/oct)
           HighCut,        // 1 off, else 2000·10^v Hz (12 dB/oct)
           EvenOnly,       // preamp adds even harmonics only
           AutoComp,       // output level follows the input's
           Bright,         // bright cap: treble lift before the preamp, strongest at low gain
           Deep,           // low resonance boost after the tone stack
           kNumParams };

    // Telemetry slots (scopeRead).
    enum {
        S_InPk = 0,   // input peak (decaying), linear
        S_OutPk,      // output peak (decaying), linear
        S_Cpu,        // share of real time spent in process()
        S_SampleRate,
        S_Thd,        // THD of the preamp at the test level, 0..1
        S_H1,         // harmonic levels 1..7, dB relative to the fundamental (H1 = 0)
        S_H7 = S_H1 + 6,
        S_TestLvl,    // the level the THD was measured at (the input peak, or −6 dBFS)
        S_CompDb,     // auto-comp gain now, dB
        S_GateGain,   // gate gain now 0..1 (1 = open, or no gate)
        S_AliasDb,    // estimated aliasing of a 2.5 kHz tone at the test level, dB re the fundamental
        S_Drive,      // effective preamp drive
        S_Model,
        S_OsFactor,   // 1 / 2 / 4 / 8
        S_CabLossDb,  // the cabinet now vs on axis at 4 kHz, dB
        S_MidHz,      // middle peak frequency
        S_CabLpHz,    // cabinet band-pass edges
        S_CabHpHz,
        S_GateThrDb,  // gate threshold dBFS (−120 = off)
        S_RespLoHz,   // response grid
        S_RespHiHz,
        S_RespN,
        S_DistCm,     // mic distance
        S_Reserved0, S_Reserved1, S_Reserved2, S_Reserved3, S_Reserved4,
        kTele = 32
    };
    static constexpr int kResp = 96;
    static constexpr double kRespLo = 30.0, kRespHi = 16000.0;
    static constexpr int kScopeTotal = kTele + 4 * kResp;
    enum { A_Reset = 0 };   // deviceAction ids

    Amp() {
        p_[Model].store(2.0f);      // Blues
        p_[Gain].store(3.0f);
        p_[Bass].store(5.0f);
        p_[Middle].store(5.0f);
        p_[Treble].store(5.0f);
        p_[Presence].store(5.0f);
        p_[Output].store(5.0f);
        p_[Mix].store(1.0f);
        p_[CabOn].store(1.0f);      // cabinet on = the pre-rework behaviour
        p_[CabType].store(0.0f);    // 0 = model-matched cab
        p_[Mic].store(0.0f);        // 0 = dynamic
        p_[Axis].store(0.0f);       // on-axis
        p_[Gate].store(0.0f);       // gate off
        p_[MidFreq].store(kMidFreqDefault);
        p_[MicDistance].store(kDistDefault);
        p_[HighCut].store(1.0f);    // open
    }

    void setSampleRate(double sr, int32_t maxBlock) override {
        sr_ = sr > 0 ? sr : 44100.0;
        resetState();
        maxFrames_ = maxBlock > 0 ? maxBlock : 4096;
        os_.prepare(maxFrames_);
        dry_.assign(maxFrames_ * 2, 0.0f);
        srA_.store((float)sr_, std::memory_order_relaxed);
    }

    void deviceAction(int32_t id, int32_t, float) override {
        if (id == A_Reset) resetReq_.store(true, std::memory_order_relaxed);
    }

    void process(float* buf, int32_t frames) override {
        const auto t0 = std::chrono::steady_clock::now();
        if (resetReq_.exchange(false, std::memory_order_relaxed)) resetState();

        const Params P = params();
        const Shaper sh = shaperFor(P);
        const Chain ch = chainFor(P, sr_);
        const float outGain = std::pow(10.0f, (get(Output) - 5.0f) / 5.0f * 12.0f / 20.0f);
        const float mix     = P.mix;
        const float gateThr = P.gate > 0.001f ? std::pow(10.0f, (-75.0f + P.gate * 55.0f) / 20.0f) : 0.0f;
        const double msA    = std::exp(-1.0 / (0.3 * sr_));      // auto-comp RMS window
        const float  compA  = (float)std::exp(-1.0 / (0.05 * sr_));
        const float  dcR    = (float)std::exp(-2.0 * kPi * 8.0 / sr_);

        for (int c = 0; c < 2; ++c) {
            preHP_[c].set(ch.preHP); bright_[c].set(ch.bright);
            bassSh_[c].set(ch.bass); midPk_[c].set(ch.mid); trebSh_[c].set(ch.treb); presSh_[c].set(ch.pres); deep_[c].set(ch.deep);
            cabHP_[c].set(ch.cabHP); cabLP_[c].set(ch.cabLP); cabRes_[c].set(ch.cabRes); cabPk_[c].set(ch.cabPk);
            prox_[c].set(ch.prox); distSh_[c].set(ch.distSh); edge_[c].set(ch.edge);
            lowCut_[c].set(ch.lowCut); highCut_[c].set(ch.highCut);
        }

        // Oversample the preamp waveshaper only (the only aliasing source — the filters are
        // linear and stay at base rate; the pre-HP and bright run before the up-sampling).
        os_.setActive(1 << P.os);
        if (frames > maxFrames_) frames = maxFrames_;
        float inPk = 0.0f, outPk = 0.0f, gateSum = 0.0f;

        // Base-rate pre: stash the dry input, then replace the buffer with what feeds the shaper.
        for (int32_t i = 0; i < frames; ++i) {
            for (int c = 0; c < 2; ++c) {
                const float in = buf[i * 2 + c];
                dry_[i * 2 + c] = in;
                inPk = std::max(inPk, std::fabs(in));
                buf[i * 2 + c] = bright_[c].process(preHP_[c].process(in));
            }
        }

        os_.process(buf, frames, [&](float& l, float& r, int /*i*/) { l = sh.eval(l); r = sh.eval(r); });

        // Base-rate post: DC block, tone stack, deep, cabinet, cuts, makeup / auto-comp, output, gate, dry/wet.
        for (int32_t i = 0; i < frames; ++i) {
            float y[2];
            for (int c = 0; c < 2; ++c) {
                float x = buf[i * 2 + c];
                const float d = x - dcX_[c] + dcR * dcY_[c];
                dcX_[c] = x; dcY_[c] = d; x = d;
                x = bassSh_[c].process(x);
                x = midPk_[c].process(x);
                x = trebSh_[c].process(x);
                x = presSh_[c].process(x);
                x = deep_[c].process(x);
                if (ch.cabOn) {
                    x = cabHP_[c].process(x); x = cabLP_[c].process(x);
                    x = cabRes_[c].process(x); x = cabPk_[c].process(x);
                    x = prox_[c].process(x); x = distSh_[c].process(x); x = edge_[c].process(x);
                }
                x = lowCut_[c].process(x);
                x = highCut_[c].process(x);
                y[c] = x * sh.makeup;
            }
            if (P.autoComp) {
                const float dl = dry_[i * 2], dr = dry_[i * 2 + 1];
                inMs_  = inMs_  * msA + (1.0 - msA) * 0.5 * (dl * dl + dr * dr);
                wetMs_ = wetMs_ * msA + (1.0 - msA) * 0.5 * (y[0] * y[0] + y[1] * y[1]);
                if (inMs_ > 1e-8 && wetMs_ > 1e-10) {
                    const float want = (float)std::clamp(std::sqrt(inMs_ / wetMs_), 0.125, 8.0);   // ±18 dB
                    compG_ = want + (compG_ - want) * compA;
                }
                y[0] *= compG_; y[1] *= compG_;
            } else if (compG_ != 1.0f) {
                compG_ = 1.0f; inMs_ = wetMs_ = 0.0;
            }
            for (int c = 0; c < 2; ++c) {
                float wet = y[c] * outGain;
                const float in = dry_[i * 2 + c];
                if (gateThr > 0.0f) {   // noise gate keyed on the input level
                    const float lvl = std::fabs(in);
                    gateEnv_[c] += (lvl - gateEnv_[c]) * (lvl > gateEnv_[c] ? 0.30f : 0.0015f);
                    const float target = gateEnv_[c] > gateThr ? 1.0f : 0.0f;
                    gateGain_[c] += (target - gateGain_[c]) * (target > gateGain_[c] ? 0.30f : 0.02f);
                    wet *= gateGain_[c];
                } else {
                    gateGain_[c] = 1.0f;
                }
                const float o = in * (1.0f - mix) + wet * mix;
                buf[i * 2 + c] = o;
                outPk = std::max(outPk, std::fabs(o));
            }
            gateSum += 0.5f * (gateGain_[0] + gateGain_[1]);
        }

        const float fall = std::exp(-(float)frames / (float)(0.3 * sr_));   // ~300 ms peak fall
        inPkA_.store(std::max(inPk, inPkA_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);
        outPkA_.store(std::max(outPk, outPkA_.load(std::memory_order_relaxed) * fall), std::memory_order_relaxed);
        compA_.store(compG_, std::memory_order_relaxed);
        if (frames > 0) {
            gateA_.store(gateSum / (float)frames, std::memory_order_relaxed);
            const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
            cpuA_.store((float)cpuS_, std::memory_order_relaxed);
        }
    }

    // Telemetry block, then the four responses (see the header note). Writes as much of that
    // layout as fits in maxSamples. Lock-free; torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        const Params P = params();
        const Shaper sh = shaperFor(P);
        const double sr = sr_;
        float t[kTele] = {};
        t[S_InPk] = inPkA_.load(std::memory_order_relaxed);
        t[S_OutPk] = outPkA_.load(std::memory_order_relaxed);
        t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
        t[S_SampleRate] = (float)sr;
        const float lvl = t[S_InPk] > 0.004f ? std::min(t[S_InPk], 1.0f) : 0.5f;
        t[S_TestLvl] = lvl;
        double h[kHarm + 1] = {};
        harmonics(sh, lvl, h);
        double sq = 0.0;
        for (int k = 2; k <= 7; ++k) sq += h[k] * h[k];
        t[S_Thd] = h[1] > 1e-9 ? (float)(std::sqrt(sq) / h[1]) : 0.0f;
        for (int k = 1; k <= 7; ++k)
            t[S_H1 + k - 1] = h[1] > 1e-9 && h[k] > 1e-9 ? (float)std::max(-120.0, 20.0 * std::log10(h[k] / h[1])) : -120.0f;
        // Aliasing: the harmonics of a 2.5 kHz tone above the (oversampled) Nyquist fold back;
        // with oversampling only what folds below the base Nyquist survives the decimator.
        const int osF = 1 << P.os;
        const double fsOs = sr * osF;
        double al = 0.0;
        for (int k = 2; k <= kHarm; ++k) {
            const double fk = k * kAliasHz;
            if (fk <= 0.5 * fsOs) continue;
            double f = std::fmod(fk, fsOs);
            if (f > 0.5 * fsOs) f = fsOs - f;
            if (f < 0.5 * sr) al += h[k] * h[k];
        }
        t[S_AliasDb] = h[1] > 1e-9 && al > 1e-30 ? (float)std::max(-140.0, 10.0 * std::log10(al / (h[1] * h[1]))) : -140.0f;
        const float cg = compA_.load(std::memory_order_relaxed);
        t[S_CompDb] = P.autoComp && cg > 1e-6f ? 20.0f * std::log10(cg) : 0.0f;
        t[S_GateGain] = P.gate > 0.001f ? gateA_.load(std::memory_order_relaxed) : 1.0f;
        t[S_Drive] = sh.drive;
        t[S_Model] = (float)P.model;
        t[S_OsFactor] = (float)osF;
        const Chain ch = chainFor(P, sr);
        Params ref = P; ref.axis = 0.0f; ref.edge = false; ref.distCm = 5.0;
        const Chain chRef = chainFor(ref, sr);
        t[S_CabLossDb] = (float)(cabDb(ch, sr, 4000.0) - cabDb(chRef, sr, 4000.0));
        t[S_MidHz] = (float)P.midHz;
        t[S_CabLpHz] = (float)ch.cabLpHz;
        t[S_CabHpHz] = (float)ch.cabHpHz;
        t[S_GateThrDb] = P.gate > 0.001f ? -75.0f + P.gate * 55.0f : -120.0f;
        t[S_RespLoHz] = (float)kRespLo;
        t[S_RespHiHz] = (float)kRespHi;
        t[S_RespN] = (float)kResp;
        t[S_DistCm] = (float)P.distCm;
        Params flat = P;
        flat.bass = flat.middle = flat.treble = flat.presence = 5.0f; flat.bright = flat.deep = false;
        const Chain chFlat = chainFor(flat, sr);

        int32_t n = 0;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        for (int k = 0; k < kResp && n < maxSamples; ++k) out[n++] = (float)toneDb(ch, sr, respHz(k));
        for (int k = 0; k < kResp && n < maxSamples; ++k) out[n++] = (float)toneDb(chFlat, sr, respHz(k));
        for (int k = 0; k < kResp && n < maxSamples; ++k) out[n++] = (float)cabDb(ch, sr, respHz(k));
        for (int k = 0; k < kResp && n < maxSamples; ++k) out[n++] = (float)cabDb(chRef, sr, respHz(k));
        return n;
    }

    const char* displayName() const override { return "Nota Valve"; }
    int32_t     builtinKind() const override { return 6; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[kNumParams] = {
            "Model", "Gain", "Bass", "Middle", "Treble", "Presence", "Output", "Mix",
            "Cab On", "Cabinet", "Mic", "Axis", "Gate", "Oversampling",
            "Mid Freq", "Mic Distance", "Mic Position", "Low Cut", "High Cut", "Even Only", "Auto Comp", "Bright", "Deep" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t i) const override {
        switch (i) {
            case Model: return (float)(kModels - 1);
            case CabType: return (float)(kCabs - 1);
            case Mic: return (float)(kMics - 1);
            case Gain: case Bass: case Middle: case Treble: case Presence: case Output: return 10.0f;
            default: return 1.0f;
        }
    }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, paramMax(i)), std::memory_order_relaxed);
    }

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override {
        char b[1024];
        const Params P = params();
        if (id == 0) {
            std::string s = kModelName[P.model];
            std::snprintf(b, sizeof b, " - gain %.1f - bass %.1f / mid %.1f @ %s / treble %.1f / presence %.1f",
                          get(Gain), P.bass, P.middle, hzText(P.midHz).c_str(), P.treble, P.presence);
            s += b;
            if (P.bright) s += " - bright";
            if (P.deep) s += " - deep";
            if (P.even) s += " - even harmonics only";
            if (P.cabOn) {
                std::snprintf(b, sizeof b, " - cab %s, %s mic %.0f cm, %s, %s", kCabName[P.cab], kMicName[P.mic], P.distCm,
                              P.axis <= 0.005f ? "on-axis" : (std::to_string((int)std::lround(P.axis * 100)) + " % off-axis").c_str(),
                              P.edge ? "edge" : "cap");
                s += b;
            } else {
                s += " - no cabinet";
            }
            if (P.lowCutHz > 0) s += " - low cut " + hzText(P.lowCutHz);
            if (P.highCutHz > 0) s += " - high cut " + hzText(P.highCutHz);
            if (P.gate > 0.001f) { std::snprintf(b, sizeof b, " - gate %.0f dB", -75.0f + P.gate * 55.0f); s += b; }
            std::snprintf(b, sizeof b, " - mix %.0f %% - out %+.1f dB - OS %s%s", P.mix * 100.0f, (get(Output) - 5.0f) / 5.0f * 12.0f,
                          kOsName[P.os], P.autoComp ? " - auto-comp" : "");
            s += b;
            return s;
        }
        if (id == 1) {
            float sc[kTele];
            scopeRead(sc, kTele);
            auto db = [](float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; };
            std::snprintf(b, sizeof b,
                          "in %.1f dB - out %.1f dB - THD %.1f %% at %.1f dBFS (2nd %.0f dB, 3rd %.0f dB) - aliasing %.0f dB at %s OS - "
                          "drive %.1f - gate %s - comp %+.1f dB - cab %+.1f dB at 4 kHz vs on-axis",
                          db(sc[S_InPk]), db(sc[S_OutPk]), sc[S_Thd] * 100.0f, db(sc[S_TestLvl]), sc[S_H1 + 1], sc[S_H1 + 2],
                          sc[S_AliasDb], kOsName[P.os], sc[S_Drive],
                          P.gate <= 0.001f ? "off" : sc[S_GateGain] > 0.5f ? "open" : "closed", sc[S_CompDb], sc[S_CabLossDb]);
            return b;
        }
        if (id == 2) {
            return "Model: 0 Clean, 1 Boost, 2 Blues, 3 Rock, 4 Lead, 5 Heavy, 6 Bass. Gain 0..10. Bass / Middle / Treble / Presence 0..10, "
                   "5 = flat (bass +/-12 dB shelf at 110 Hz, middle +/-10 dB peak, treble +/-12 dB shelf at 3 kHz, presence +/-8 dB at 4.5 kHz). "
                   "Output 0..10, 5 = 0 dB, +/-12 dB. Mix 0..1. Cab On 0/1. Cabinet: 0 Match (the model's own), 1 1x12 Open, 2 2x12 Combo, "
                   "3 4x12 Closed, 4 1x15 Bass. Mic: 0 Dynamic, 1 Condenser, 2 Ribbon. Axis 0..1: 0 on-axis, 1 fully off-axis (darker). "
                   "Gate 0..1: 0 off, else threshold -75 + 55*v dBFS. Oversampling: 0 off, 0.333 2x, 0.667 4x, 1 8x. "
                   "The rest are 0..1: Mid Freq 200*10^v Hz (0.512 = 650 Hz). Mic Distance 1*30^v cm (0.473 = 5 cm; closer = more "
                   "proximity bass, farther = thinner and darker). Mic Position: 0 cap, 1 edge (darker). Low Cut: 0 off, else 20*15^v Hz. "
                   "High Cut: 1 off, else 2000*10^v Hz. Even Only: the preamp adds even harmonics only. Auto Comp: output level matched to "
                   "the input. Bright: treble lift before the preamp, strongest at low gain. Deep: low resonance boost. Toggles: >= 0.5 = on.";
        }
        return {};
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kModels = 7, kCabs = 5, kMics = 3;
    static constexpr int kHarm = 96, kDft = 256;
    static constexpr double kAliasHz = 2500.0;
    static constexpr float kMidFreqDefault = 0.5119f;   // log10(650/200)
    static constexpr float kDistDefault = 0.4732f;      // ln 5 / ln 30
    static constexpr const char* kModelName[kModels] = { "Clean", "Boost", "Blues", "Rock", "Lead", "Heavy", "Bass" };
    static constexpr const char* kCabName[kCabs] = { "Match", "1x12 Open", "2x12 Combo", "4x12 Closed", "1x15 Bass" };
    static constexpr const char* kMicName[kMics] = { "dynamic", "condenser", "ribbon" };
    static constexpr const char* kOsName[4] = { "off", "2x", "4x", "8x" };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static std::string hzText(double hz) {
        char b[32];
        if (hz >= 1000.0) std::snprintf(b, sizeof b, "%.1f kHz", hz / 1000.0); else std::snprintf(b, sizeof b, "%.0f Hz", hz);
        return b;
    }
    static double respHz(int k) { return kRespLo * std::pow(kRespHi / kRespLo, (double)k / (kResp - 1)); }

    void resetState() {
        for (int c = 0; c < 2; ++c) {
            preHP_[c].reset(); bright_[c].reset(); bassSh_[c].reset(); midPk_[c].reset(); trebSh_[c].reset(); presSh_[c].reset();
            deep_[c].reset(); cabHP_[c].reset(); cabLP_[c].reset(); cabRes_[c].reset(); cabPk_[c].reset(); prox_[c].reset();
            distSh_[c].reset(); edge_[c].reset(); lowCut_[c].reset(); highCut_[c].reset();
            gateEnv_[c] = 0.0f; gateGain_[c] = 1.0f; dcX_[c] = dcY_[c] = 0.0f;
        }
        inMs_ = wetMs_ = 0.0; compG_ = 1.0f;
    }

    // Per-voicing character: preamp drive, clipping asymmetry + stage count, the
    // pre-distortion low-cut (tightness) and the speaker-cabinet band edges.
    struct AmpModel { float driveMul; float bias; int stages; float preHpHz; float cabHpHz; float cabLpHz; };
    static constexpr AmpModel kModel[kModels] = {
        /* Clean */ { 1.5f, 0.00f, 1,  20.0f,  70.0f, 7000.0f },
        /* Boost */ { 3.0f, 0.05f, 1,  30.0f,  75.0f, 6500.0f },
        /* Blues */ { 6.0f, 0.10f, 1,  45.0f,  80.0f, 6000.0f },
        /* Rock  */ { 12.0f, 0.15f, 2, 70.0f,  85.0f, 5200.0f },
        /* Lead  */ { 22.0f, 0.20f, 2, 95.0f,  90.0f, 5000.0f },
        /* Heavy */ { 40.0f, 0.28f, 3, 120.0f, 95.0f, 4500.0f },
        /* Bass  */ { 4.0f, 0.00f, 1,  20.0f,  40.0f, 3800.0f },
    };
    // Cabinet / mic voicing: multipliers on the model's band-pass edges, plus each cab's low
    // resonance and presence peak. Index 0 = the model-matched cab (no peaks — the old sound).
    //                                        Match   1×12   2×12   4×12   1×15
    static constexpr float kCabLp[kCabs]    = { 1.00f, 0.90f, 1.00f, 1.15f, 0.72f };
    static constexpr float kCabHp[kCabs]    = { 1.00f, 1.10f, 1.00f, 0.90f, 0.70f };
    static constexpr float kCabResHz[kCabs] = { 100.f, 125.f, 105.f,  90.f,  70.f };
    static constexpr float kCabResDb[kCabs] = { 0.0f,  2.0f,  3.0f,  4.5f,  4.0f };
    static constexpr float kCabPkHz[kCabs]  = { 2500.f, 2200.f, 2600.f, 2900.f, 1800.f };
    static constexpr float kCabPkDb[kCabs]  = { 0.0f,  3.0f,  3.0f,  4.0f,  2.0f };
    //                                        Dyn    Cond   Ribbon
    static constexpr float kMicLp[kMics]    = { 1.00f, 1.28f, 0.80f };
    static constexpr float kMicProx[kMics]  = { 2.0f,  0.8f,  3.0f };   // dB of low shelf per halving of the distance under 5 cm

    // A snapshot of the params in musical terms (process() and the message thread alike).
    struct Params {
        int model, cab, mic, os;
        float g01, bass, middle, treble, presence, mix, axis, gate;
        double midHz, distCm, lowCutHz, highCutHz;
        bool cabOn, edge, even, autoComp, bright, deep;
    };
    Params params() const {
        Params P{};
        P.model = std::clamp((int)std::lround(get(Model)), 0, kModels - 1);
        P.cab = std::clamp((int)std::lround(get(CabType)), 0, kCabs - 1);
        P.mic = std::clamp((int)std::lround(get(Mic)), 0, kMics - 1);
        P.os = std::clamp((int)std::lround(get(Oversampling) * 3.0f), 0, 3);
        P.g01 = std::clamp(get(Gain) / 10.0f, 0.0f, 1.0f);
        P.bass = get(Bass); P.middle = get(Middle); P.treble = get(Treble); P.presence = get(Presence);
        P.mix = std::clamp(get(Mix), 0.0f, 1.0f);
        P.axis = std::clamp(get(Axis), 0.0f, 1.0f);
        P.gate = std::clamp(get(Gate), 0.0f, 1.0f);
        P.midHz = expMap(get(MidFreq), 200.0, 2000.0);
        P.distCm = expMap(get(MicDistance), 1.0, 30.0);
        P.lowCutHz = get(LowCut) > 0.005f ? expMap(get(LowCut), 20.0, 300.0) : 0.0;
        P.highCutHz = get(HighCut) < 0.995f ? expMap(get(HighCut), 2000.0, 20000.0) : 0.0;
        P.cabOn = get(CabOn) >= 0.5f;
        P.edge = get(MicPosition) >= 0.5f;
        P.even = get(EvenOnly) >= 0.5f;
        P.autoComp = get(AutoComp) >= 0.5f;
        P.bright = get(Bright) >= 0.5f;
        P.deep = get(Deep) >= 0.5f;
        return P;
    }

    // The memoryless preamp: the model's tanh cascade, or the even-harmonics-only stage.
    struct Shaper {
        float drive = 1.0f, bias = 0.0f, biasDC = 0.0f, makeup = 1.0f, evenAmt = 0.0f;
        int stages = 1;
        bool even = false;
        inline float eval(float x) const {
            if (even) {
                // An even function of the driven input added to the clean one: only even
                // harmonics (and DC, which the DC blocker removes).
                const float pre = x * drive * 0.5f;
                const float e = 0.5f * (std::tanh(pre + 0.6f) + std::tanh(0.6f - pre)) - 0.53705f;   // tanh(0.6)
                return x - e * evenAmt;
            }
            float y = x * drive + bias;
            for (int s = 0; s < stages; ++s) { y = std::tanh(y); if (s < stages - 1) y *= 2.0f; }
            return y - biasDC;
        }
    };
    static Shaper shaperFor(const Params& P) {
        const AmpModel& md = kModel[P.model];
        Shaper s;
        // Drive into the preamp: squared taper so the knob's top half is where the real
        // breakup lives. Makeup counteracts the tanh loudness so voicings match.
        s.drive = md.driveMul * (0.35f + P.g01 * P.g01 * 3.0f);
        s.stages = md.stages;
        s.bias = md.bias;
        s.biasDC = std::tanh(md.bias);
        s.even = P.even;
        if (P.even) {
            s.evenAmt = 0.4f + 1.4f * P.g01;
            s.makeup = 1.0f;
        } else {
            s.makeup = std::clamp(1.6f / std::sqrt(std::max(s.drive, 1e-3f)), 0.2f, 2.5f);
        }
        return s;
    }

    // Every linear filter of the chain, from the params (process() and the responses alike).
    struct Chain {
        struct C { float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0; };
        C preHP, bright, bass, mid, treb, pres, deep;
        C cabHP, cabLP, cabRes, cabPk, prox, distSh, edge, lowCut, highCut;
        bool cabOn = true;
        double cabLpHz = 0, cabHpHz = 0;
    };
    static Chain chainFor(const Params& P, double sr) {
        const AmpModel& md = kModel[P.model];
        Chain c;
        c.preHP  = Biquad::highpass (sr, md.preHpHz, 0.707);
        c.bright = Biquad::highShelf(sr, 2500.0, P.bright ? 6.0 * (1.0 - 0.7 * P.g01) : 0.0);
        c.bass   = Biquad::lowShelf (sr, 110.0,  (P.bass - 5.0f) / 5.0f * 12.0f);
        c.mid    = Biquad::peak     (sr, P.midHz, (P.middle - 5.0f) / 5.0f * 10.0f, 0.70);
        c.treb   = Biquad::highShelf(sr, 3000.0, (P.treble - 5.0f) / 5.0f * 12.0f);
        c.pres   = Biquad::highShelf(sr, 4500.0, (P.presence - 5.0f) / 5.0f * 8.0f);
        c.deep   = Biquad::peak     (sr, 85.0, P.deep ? 5.0 : 0.0, 0.9);
        c.cabOn = P.cabOn;
        c.cabLpHz = std::clamp(md.cabLpHz * kCabLp[P.cab] * kMicLp[P.mic] * (1.0 - P.axis * 0.45), 800.0, sr * 0.45);
        c.cabHpHz = md.cabHpHz * kCabHp[P.cab];
        c.cabHP  = Biquad::highpass (sr, c.cabHpHz, 0.707);
        c.cabLP  = Biquad::lowpass  (sr, c.cabLpHz, 0.707);
        c.cabRes = Biquad::peak     (sr, kCabResHz[P.cab], kCabResDb[P.cab], 1.4);
        c.cabPk  = Biquad::peak     (sr, kCabPkHz[P.cab], kCabPkDb[P.cab] * (1.0 - 0.6 * P.axis), 1.0);
        const double halvings = std::log2(5.0 / std::max(1.0, P.distCm));   // > 0 closer than 5 cm
        c.prox   = Biquad::lowShelf (sr, 150.0, std::clamp(kMicProx[P.mic] * halvings, -6.0, 9.0));
        c.distSh    = Biquad::highShelf(sr, 5000.0, std::min(0.0, halvings) * 2.0);
        c.edge   = Biquad::highShelf(sr, 3000.0, (P.edge ? -4.5 : 0.0) - 6.0 * P.axis);   // edge + off-axis beaming
        c.lowCut  = P.lowCutHz > 0 ? Biquad::highpass(sr, P.lowCutHz, 0.707) : Chain::C{};
        c.highCut = P.highCutHz > 0 ? Biquad::lowpass(sr, P.highCutHz, 0.707) : Chain::C{};
        return c;
    }
    static double magDb(const Chain::C& c, double sr, double hz) {
        const double w = 2.0 * kPi * std::min(hz, sr * 0.499) / sr;
        const std::complex<double> z1 = std::polar(1.0, -w), z2 = z1 * z1;
        const std::complex<double> num = (double)c.b0 + (double)c.b1 * z1 + (double)c.b2 * z2;
        const std::complex<double> den = 1.0 + (double)c.a1 * z1 + (double)c.a2 * z2;
        const double m = std::abs(num) / std::max(1e-12, std::abs(den));
        return 20.0 * std::log10(std::max(m, 1e-9));
    }
    static double toneDb(const Chain& c, double sr, double hz) {
        return magDb(c.preHP, sr, hz) + magDb(c.bright, sr, hz) + magDb(c.bass, sr, hz) + magDb(c.mid, sr, hz)
             + magDb(c.treb, sr, hz) + magDb(c.pres, sr, hz) + magDb(c.deep, sr, hz);
    }
    static double cabDb(const Chain& c, double sr, double hz) {
        double d = magDb(c.lowCut, sr, hz) + magDb(c.highCut, sr, hz);
        if (c.cabOn)
            d += magDb(c.cabHP, sr, hz) + magDb(c.cabLP, sr, hz) + magDb(c.cabRes, sr, hz) + magDb(c.cabPk, sr, hz)
               + magDb(c.prox, sr, hz) + magDb(c.distSh, sr, hz) + magDb(c.edge, sr, hz);
        return d;
    }

    // Harmonic amplitudes 1..kHarm of the preamp driven by a sine of amplitude `a` (DFT, kDft pts).
    static void harmonics(const Shaper& sh, float a, double* h) {
        static const struct Tw { double c[kDft], s[kDft]; Tw() { for (int n = 0; n < kDft; ++n) { c[n] = std::cos(2.0 * kPi * n / kDft); s[n] = std::sin(2.0 * kPi * n / kDft); } } } tw;
        double y[kDft];
        for (int n = 0; n < kDft; ++n) y[n] = sh.eval(a * (float)tw.s[n]);
        for (int k = 1; k <= kHarm; ++k) {
            double re = 0.0, im = 0.0;
            for (int n = 0; n < kDft; ++n) { const int j = (k * n) & (kDft - 1); re += y[n] * tw.c[j]; im += y[n] * tw.s[j]; }
            h[k] = 2.0 * std::sqrt(re * re + im * im) / kDft;
        }
    }

    // RBJ biquad (transposed direct form II). Coeffs computed off-thread of state.
    struct Biquad {
        using Coef = Chain::C;
        float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0, z1 = 0, z2 = 0;
        void reset() { z1 = z2 = 0; }
        void set(const Coef& c) { b0 = c.b0; b1 = c.b1; b2 = c.b2; a1 = c.a1; a2 = c.a2; }
        inline float process(float x) {
            const float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
        static Coef norm(double b0, double b1, double b2, double a0, double a1, double a2) {
            Coef c; c.b0 = (float)(b0 / a0); c.b1 = (float)(b1 / a0); c.b2 = (float)(b2 / a0);
            c.a1 = (float)(a1 / a0); c.a2 = (float)(a2 / a0); return c;
        }
        static double w0(double sr, double f) {
            f = std::clamp(f, 10.0, sr * 0.45);
            return 2.0 * kPi * f / sr;
        }
        static Coef lowpass(double sr, double f, double q) {
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * q);
            return norm((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + a, -2 * cw, 1 - a);
        }
        static Coef highpass(double sr, double f, double q) {
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * q);
            return norm((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + a, -2 * cw, 1 - a);
        }
        static Coef peak(double sr, double f, double dB, double q) {
            if (std::fabs(dB) < 1e-3) return Coef{};
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * q), A = std::pow(10.0, dB / 40.0);
            return norm(1 + a * A, -2 * cw, 1 - a * A, 1 + a / A, -2 * cw, 1 - a / A);
        }
        static Coef lowShelf(double sr, double f, double dB) {
            if (std::fabs(dB) < 1e-3) return Coef{};
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * 0.707), A = std::pow(10.0, dB / 40.0), s = 2 * std::sqrt(A) * a;
            return norm(A * ((A + 1) - (A - 1) * cw + s), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - s),
                        (A + 1) + (A - 1) * cw + s, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - s);
        }
        static Coef highShelf(double sr, double f, double dB) {
            if (std::fabs(dB) < 1e-3) return Coef{};
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * 0.707), A = std::pow(10.0, dB / 40.0), s = 2 * std::sqrt(A) * a;
            return norm(A * ((A + 1) + (A - 1) * cw + s), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - s),
                        (A + 1) - (A - 1) * cw + s, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - s);
        }
    };

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    Biquad preHP_[2], bright_[2], bassSh_[2], midPk_[2], trebSh_[2], presSh_[2], deep_[2];
    Biquad cabHP_[2], cabLP_[2], cabRes_[2], cabPk_[2], prox_[2], distSh_[2], edge_[2], lowCut_[2], highCut_[2];
    float gateEnv_[2] = {0, 0}, gateGain_[2] = {1, 1};
    float dcX_[2] = {0, 0}, dcY_[2] = {0, 0};
    double inMs_ = 0.0, wetMs_ = 0.0;
    float compG_ = 1.0f;
    std::atomic<bool> resetReq_{false};

    // Telemetry.
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{0}, outPkA_{0}, cpuA_{0}, srA_{44100.0f}, compA_{1}, gateA_{1};

    Oversampler os_;                 // preamp-waveshaper anti-aliasing
    int32_t maxFrames_ = 4096;
    std::vector<float> dry_;         // interleaved dry stash (sized in setSampleRate)
};

} // namespace nota
