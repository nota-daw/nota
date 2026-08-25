// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Operator — the built-in 4-operator FM synth (kind 9), in the spirit of classic 4-operator FM synths
// Operator: four operators (A B C D), each a phase-modulated oscillator with its own
// coarse/fine frequency ratio, output level, waveform and ADSR envelope. Eight feed-
// forward algorithms route who modulates whom (chains, stacks, parallel/additive), with
// self-feedback on operator A. The summed carriers pass a subtractive TPT filter
// (LP/HP/BP) before the master volume — FM timbre + classic filter sculpting in one.
//
//   A ─┐            (per algorithm) modulators bend the phase of their target;
//   B ─┼─▶ [algo] ─▶ carriers ─▶ filter ─▶ volume ─▶ out
//   C ─┤            op A can feed back into itself
//   D ─┘
//
// All params ride the Instrument plugin-param interface (normalized 0..1, stable ids)
// → automation / persist / clone for free. Header-only, allocation-free after
// construction; per-sample voice loop keeps FM + envelopes smooth. Name & DSP are Nota's.

#pragma once

#include "Instrument.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota {

class OperatorSynth final : public Instrument {
public:
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    // Per-op block = Coarse, Fine, Level, Wave, Attack, Decay, Sustain, Release.
    enum Param {
        Algo = 0, Feedback,
        ACoarse, AFine, ALevel, AWave, AA, AD, AS, AR,
        BCoarse, BFine, BLevel, BWave, BA, BD, BS, BR,
        CCoarse, CFine, CLevel, CWave, CA, CD, CS, CR,
        DCoarse, DFine, DLevel, DWave, DA, DD, DS, DR,
        FilterType, FilterFreq, FilterReso, Volume,
        // --- appended for the mockup-3g rework (old projects default these to neutral) ---
        FmDepth,    // global modulation-index macro (0.5 = ×1, matches the pre-macro fixed depth)
        Glide,      // portamento time (0 = off)
        VelToFm,    // velocity → FM depth amount (0 = off, classic FM dynamics)
        KeyLevel,   // keyboard tracking of modulator level (0 = off; higher notes → less FM)
        Mono,       // voice mode: <0.5 poly, ≥0.5 monophonic (legato with glide)
        kNumParams
    };
    static constexpr int kNumAlgos = 11;
    // Field offsets within an op block (op base = ACoarse + op*8).
    enum { F_Coarse = 0, F_Fine, F_Level, F_Wave, F_A, F_D, F_S, F_R };

    OperatorSynth() {
        set(Algo, 0.0f); set(Feedback, 0.0f);
        // A gentle electric-piano-ish default: D is the carrier, C modulates it 1:1.
        auto op = [&](int o, float coarse, float lvl, float wave, float a, float d, float s, float r) {
            const int base = ACoarse + o * 8;
            set((Param)(base + F_Coarse), coarse); set((Param)(base + F_Fine), 0.5f);
            set((Param)(base + F_Level), lvl); set((Param)(base + F_Wave), wave);
            set((Param)(base + F_A), a); set((Param)(base + F_D), d); set((Param)(base + F_S), s); set((Param)(base + F_R), r);
        };
        // coarse 0.0667 ≈ ratio 1 (index 1 of 16); levels: D carrier, C modulator.
        op(0, 0.0667f, 0.0f, 0.0f, 0.0f, 0.4f, 0.0f, 0.3f);   // A (silent)
        op(1, 0.0667f, 0.0f, 0.0f, 0.0f, 0.4f, 0.0f, 0.3f);   // B (silent)
        op(2, 0.0667f, 0.45f, 0.0f, 0.0f, 0.45f, 0.0f, 0.3f); // C modulator
        op(3, 0.0667f, 1.0f, 0.0f, 0.0f, 0.5f, 0.6f, 0.35f);  // D carrier
        set(FilterType, 0.0f); set(FilterFreq, 0.85f); set(FilterReso, 0.1f);
        set(Volume, 0.8f);
        set(FmDepth, 0.5f);   // ×1 — identical to the pre-macro fixed depth
        set(Glide, 0.0f); set(VelToFm, 0.0f); set(KeyLevel, 0.0f); set(Mono, 0.0f);
    }

    int32_t kind() const override { return 9; }
    const char* displayName() const override { return "Nota Operator"; }

    void setSampleRate(double sr) override {
        sampleRate_ = sr > 0 ? sr : 44100.0;
        std::memset(ring_, 0, sizeof(ring_)); specF0_.store(0.0f, std::memory_order_relaxed);
    }

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[] = {
            "algo", "feedback",
            "acoarse", "afine", "alevel", "awave", "aatk", "adec", "asus", "arel",
            "bcoarse", "bfine", "blevel", "bwave", "batk", "bdec", "bsus", "brel",
            "ccoarse", "cfine", "clevel", "cwave", "catk", "cdec", "csus", "crel",
            "dcoarse", "dfine", "dlevel", "dwave", "datk", "ddec", "dsus", "drel",
            "filtype", "filfreq", "filreso", "volume",
            "fmdepth", "glide", "veltofm", "keylevel", "mono" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        static const char* nm[] = {
            "Algorithm", "Feedback",
            "A Coarse", "A Fine", "A Level", "A Wave", "A Attack", "A Decay", "A Sustain", "A Release",
            "B Coarse", "B Fine", "B Level", "B Wave", "B Attack", "B Decay", "B Sustain", "B Release",
            "C Coarse", "C Fine", "C Level", "C Wave", "C Attack", "C Decay", "C Sustain", "C Release",
            "D Coarse", "D Fine", "D Level", "D Wave", "D Attack", "D Decay", "D Sustain", "D Release",
            "Filter Type", "Filter Freq", "Filter Reso", "Volume",
            "FM Depth", "Glide", "Vel to FM", "Key to Level", "Mono" };
        return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
    }
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (pluginParamId(i) == id) return i;
        return -1;
    }

    // ---- project state ----
    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b(kNumParams * sizeof(float));
        for (int i = 0; i < kNumParams; ++i) {
            float v = pn_[i].load(std::memory_order_relaxed);
            std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
        }
        return b;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data) return;
        const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
        for (int i = 0; i < n; ++i) {
            float v; std::memcpy(&v, data + i * sizeof(float), sizeof(float));
            if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        }
    }
    std::shared_ptr<Instrument> clone() const override {
        auto s = std::make_shared<OperatorSynth>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    // ---- note events ----
    void noteOn(int32_t pitch, float velocity) override {
        const bool mono    = get(Mono) >= 0.5f;
        const bool glideOn = get(Glide) > 0.0f;
        Voice* v = mono ? &voices_[0] : findFreeVoice();
        // Mono + glide + a still-sounding voice → legato: keep phases/envelopes, just retune.
        const bool legato = mono && glideOn && v->active && v->stage[0] != Stage::Off;
        v->active = true; v->pitch = pitch; v->vel = std::clamp(velocity, 0.0f, 1.0f);
        v->freq = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
        v->curFreq = legato ? v->curFreq
                   : (glideOn && lastFreq_ > 0.0) ? lastFreq_ : v->freq;
        lastFreq_ = v->freq;
        if (!legato) {
            v->fbLast = 0.0; v->s1 = v->s2 = 0.0;
            for (int o = 0; o < 4; ++o) { v->phase[o] = 0.0; v->env[o] = 0.0f; v->stage[o] = Stage::Attack; }
        }
    }
    void noteOff(int32_t pitch) override {
        for (auto& v : voices_)
            if (v.active && v.pitch == pitch && v.stage[0] != Stage::Release)
                for (int o = 0; o < 4; ++o) v.stage[o] = Stage::Release;
    }
    void allNotesOff() override { for (auto& v : voices_) v.active = false; }

    void render(float* out, int32_t frames) override {
        const int   algo = std::clamp((int)std::lround(get(Algo) * (kNumAlgos - 1.0f)), 0, kNumAlgos - 1);
        const int8_t* tgt = kAlgo[algo];
        const double fb  = get(Feedback) * 0.9;
        const double fmMul  = get(FmDepth) * 2.0;   // 0.5 ⇒ ×1 (neutral, matches the old fixed depth)
        const double velToFm = get(VelToFm);
        const double keyLevel = get(KeyLevel);
        double ratio[4], fineMul[4], level[4], atk[4], dec[4], sus[4], rel[4]; int wave[4];
        for (int o = 0; o < 4; ++o) {
            const int base = ACoarse + o * 8;
            ratio[o]   = kRatio[std::clamp((int)std::lround(get((Param)(base + F_Coarse)) * 15.0f), 0, 15)];
            fineMul[o] = std::exp2(((double)get((Param)(base + F_Fine)) - 0.5) * 0.08);   // ±~0.7 semitone
            level[o]   = get((Param)(base + F_Level));
            wave[o]    = std::clamp((int)std::lround(get((Param)(base + F_Wave)) * 3.0f), 0, 3);
            atk[o]     = rateOf((Param)(base + F_A), 0.001, 4.0);
            dec[o]     = rateOf((Param)(base + F_D), 0.002, 6.0);
            sus[o]     = get((Param)(base + F_S));
            rel[o]     = rateOf((Param)(base + F_R), 0.002, 8.0);
        }
        const int    ftype = std::clamp((int)std::lround(get(FilterType) * 2.0f), 0, 2);
        const double fbase = expMap(get(FilterFreq), 60.0, 18000.0);
        const double fk = std::clamp(2.0 - 1.9 * get(FilterReso), 0.05, 2.0);
        const double volume = get(Volume);
        const double invSr = 1.0 / sampleRate_;
        // Glide coefficient: one-pole approach toward the target frequency.
        const double glideT = get(Glide) > 0.0f ? expMap(get(Glide), 0.005, 1.2) : 0.0;
        const double gCoef = glideT > 0.0 ? (1.0 - std::exp(-1.0 / (glideT * sampleRate_))) : 1.0;

        // Filter coeffs (shared L/R per voice — recomputed per block, static cutoff).
        const double fg = std::tan(kPi * std::min(fbase, sampleRate_ * 0.49) * invSr);
        const double fa1 = 1.0 / (1.0 + fg * (fg + fk));

        double f0 = 0.0;   // frequency of the freshest active voice → spectrum base + label
        for (int32_t i = 0; i < frames; ++i) {
            float mono = 0.0f;
            for (auto& v : voices_) {
                if (!v.active) continue;
                // Advance glide toward the target pitch.
                v.curFreq += (v.freq - v.curFreq) * gCoef;
                // Velocity → FM depth and keyboard → modulator level (timbre controls).
                const double velFac = 1.0 - velToFm * (1.0 - (double)v.vel);
                const double keyFac = keyLevel > 0.0 ? std::exp2(-keyLevel * (v.pitch - 60) / 24.0) : 1.0;
                const double modScale = kFmIndex * fmMul * velFac * keyFac;
                double opOut[4]; double modIn[4] = {0, 0, 0, 0}; double carrier = 0.0; int nc = 0;
                bool anyOn = false;
                for (int o = 0; o < 4; ++o) {
                    advanceEnv(v.env[o], v.stage[o], (float)atk[o], (float)dec[o], (float)sus[o], (float)rel[o]);
                    if (v.stage[o] != Stage::Off) anyOn = true;
                    double pmod = modIn[o];
                    if (o == 0 && fb > 0.0) pmod += fb * v.fbLast;
                    const double s = waveform(wave[o], v.phase[o] + pmod) * level[o] * v.env[o];
                    opOut[o] = s;
                    v.phase[o] += v.curFreq * ratio[o] * fineMul[o] * invSr;
                    if (v.phase[o] >= 1.0) v.phase[o] -= std::floor(v.phase[o]);
                    const int t = tgt[o];
                    if (t < 4) modIn[t] += s * modScale;
                    else { carrier += s; ++nc; }
                }
                v.fbLast = opOut[0];
                if (!anyOn) { v.active = false; continue; }
                if (f0 <= 0.0) f0 = v.curFreq;
                double y = nc > 0 ? carrier / std::sqrt((double)nc) : 0.0;
                // Subtractive filter (TPT SVF).
                const double v3 = y - v.s2;
                const double v1 = fa1 * v.s1 + fa1 * fg * v3;
                const double v2 = v.s2 + fg * v1;
                v.s1 = 2.0 * v1 - v.s1; v.s2 = 2.0 * v2 - v.s2;
                double fout = ftype == 1 ? (y - fk * v1 - v2) : ftype == 2 ? v1 : v2;   // HP / BP / LP
                mono += (float)(fout * v.vel);
            }
            // Spectrum-analysis tap (pre-master timbre signal).
            ring_[ringW_ & (kRing - 1)] = mono;
            ringW_.fetch_add(1, std::memory_order_relaxed);
            const float o = mono * (float)volume * 0.4f;
            out[i * 2] += o; out[i * 2 + 1] += o;
        }
        if (f0 > 0.0) specF0_.store((float)f0, std::memory_order_relaxed);
    }

    // Harmonic spectrum of the current patch at the played note (message-thread read):
    // Goertzel magnitude at k·f0 for k = 1..N over the analysis ring, normalized 0..1.
    int32_t scopeRead(float* outv, int32_t maxN) const override {
        if (!outv || maxN <= 0) return 0;
        const double f0 = specF0_.load(std::memory_order_relaxed);
        const int N = std::min(maxN, kSpecBins);
        if (f0 <= 0.0) { for (int k = 0; k < N; ++k) outv[k] = 0.0f; return N; }
        const double sr = sampleRate_;
        const uint32_t head = ringW_.load(std::memory_order_relaxed);
        double mag[kSpecBins]; double mx = 1e-9;
        for (int k = 0; k < N; ++k) {
            const double freq = (k + 1) * f0;
            if (freq > sr * 0.45) { mag[k] = 0.0; continue; }
            const double w = 2.0 * kPi * freq / sr;
            const double cw = 2.0 * std::cos(w);
            double s0 = 0.0, s1 = 0.0, s2 = 0.0;
            for (int n = 0; n < kRing; ++n) {
                const double x = ring_[(head + n) & (kRing - 1)];
                s0 = x + cw * s1 - s2; s2 = s1; s1 = s0;
            }
            const double re = s1 - s2 * std::cos(w), im = s2 * std::sin(w);
            mag[k] = std::sqrt(re * re + im * im);
            if (mag[k] > mx) mx = mag[k];
        }
        const double inv = 1.0 / mx;
        for (int k = 0; k < N; ++k) outv[k] = (float)std::clamp(mag[k] * inv, 0.0, 1.0);
        return N;
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr int kVoices = 12;
    static constexpr double kFmIndex = 6.0;   // modulator output → phase-mod depth (cycles)

    // Feed-forward FM algorithms: tgt[op] = op index it modulates (must be > op) or 4 = output.
    // 11 topologies (mockup 3g); the managed card mirrors this table + its descriptions.
    static constexpr int8_t kAlgo[kNumAlgos][4] = {
        {1, 2, 3, 4},   //  0: A→B→C→D→out (single chain)
        {2, 2, 3, 4},   //  1: A·B → C → D (dual stack)
        {1, 3, 3, 4},   //  2: A→B→D, C→D (two mods, one carrier)
        {1, 3, 4, 4},   //  3: A→B→D; C,D carriers (bell)
        {3, 3, 3, 4},   //  4: A·B·C → D (triple modulator)
        {1, 4, 3, 4},   //  5: A→B, C→D (two 2-op voices; B,D carriers)
        {3, 4, 4, 4},   //  6: A→D; B,C,D carriers (additive + mod)
        {1, 2, 4, 4},   //  7: A→B→C; C,D carriers (bright)
        {4, 3, 4, 4},   //  8: B→D; A,C,D carriers
        {2, 4, 4, 4},   //  9: A→C; B,C,D carriers
        {4, 4, 4, 4},   // 10: all parallel (additive / organ)
    };
    static constexpr double kRatio[16] = {
        0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16 };

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct Voice {
        bool    active = false;
        int32_t pitch = 0;
        float   vel = 0.0f;
        double  freq = 0.0;      // target frequency
        double  curFreq = 0.0;   // glide-ramped frequency actually used
        double  phase[4] = {0, 0, 0, 0};
        float   env[4] = {0, 0, 0, 0};
        Stage   stage[4] = {Stage::Off, Stage::Off, Stage::Off, Stage::Off};
        double  fbLast = 0.0;
        double  s1 = 0.0, s2 = 0.0;   // filter state
    };
    Voice voices_[kVoices];

    void  set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(Param p, double lo, double hi) const {
        return static_cast<float>(1.0 / (expMap(get(p), lo, hi) * sampleRate_));
    }

    static double waveform(int w, double ph) {
        switch (w) {
            case 1: { double f = ph - std::floor(ph); return 4.0 * std::fabs(f - 0.5) - 1.0; }   // tri
            case 2: { double f = ph - std::floor(ph); return 2.0 * f - 1.0; }                    // saw
            case 3: { double f = ph - std::floor(ph); return f < 0.5 ? 1.0 : -1.0; }             // square
            default: return std::sin(kTwoPi * ph);                                               // sine
        }
    }
    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel) {
        switch (st) {
            case Stage::Attack:  env += atk; if (env >= 1.0f) { env = 1.0f; st = Stage::Decay; } break;
            case Stage::Decay:   env -= dec; if (env <= sus)  { env = sus;  st = Stage::Sustain; } break;
            case Stage::Sustain: break;
            case Stage::Release: env -= rel; if (env <= 0.0f) { env = 0.0f; st = Stage::Off; } break;
            case Stage::Off:     break;
        }
    }
    Voice* findFreeVoice() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0];
        for (auto& v : voices_) if (v.env[3] < q->env[3]) q = &v;   // steal by carrier-D env
        return q;
    }

    double sampleRate_ = 44100.0;
    double lastFreq_ = 0.0;   // last note-on target, for glide
    std::atomic<float> pn_[kNumParams];

    // Spectrum analysis (lock-free best-effort, viz only).
    static constexpr int kRing = 2048;       // power of two
    static constexpr int kSpecBins = 32;     // harmonic bars reported
    float ring_[kRing] = {};
    std::atomic<uint32_t> ringW_{0};
    std::atomic<float> specF0_{0.0f};
};

} // namespace nota
