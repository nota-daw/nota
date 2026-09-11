// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Prism — three-band dynamics (in the spirit of Ableton's Multiband Dynamics). The
// input is split by two Linkwitz-Riley 4th-order crossovers (TPT state-variable filters,
// the low band allpass-corrected so the bands sum flat) into Low / Mid / High — or two
// bands (Low + Mid, the Mid running to 20 kHz) or one (Mid, full band). Each band has two
// independent processors driven by its own level detector (peak or RMS, stereo-linked):
//   ABOVE — downward compression over a threshold (ratio 1:1 … ∞:1, soft knee), and
//   BELOW — downward expansion (ratio > 1) or upward compression (ratio < 1) under a
//           second threshold, limited to ±Floor dB,
// each with its own attack / release (optional program-dependent auto release on ABOVE),
// then a per-band gain. Amount scales every ratio (0 % = no dynamics). Around them: an
// external sidechain (band-split through its own crossovers, so each band keys off the
// matching band of the key), lookahead (reported as latency for PDC), auto makeup, band
// solo, sidechain listen, a phase-coherent Mix (the dry path is the unprocessed band sum),
// output gain and a soft clipper at −0.3 dBFS.
//
// All params are normalized 0..1 (APPEND ONLY — the order is the persisted layout); persist /
// clone / automation are generic through the base Device. Telemetry for the card:
// scopeRead packs meters (S_*); layerWave 0/1 = recent input/output mono samples (the
// spectrum), 2 + 2b / 3 + 2b = band b's input-peak / detector-level trace (~6 ms per point).

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota {

namespace prism {

constexpr double kPi = 3.14159265358979323846;
constexpr float kSqrt2 = 1.41421356f;

inline double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }

// Butterworth-Q (k = √2) TPT state-variable filter (Cytomic). LR4 = two cascaded stages.
struct Coef { float a1 = 1, a2 = 0, a3 = 0; };
inline Coef coef(double hz, double sr) {
    const double g = std::tan(kPi * std::clamp(hz, 10.0, sr * 0.45) / sr);
    Coef c;
    c.a1 = static_cast<float>(1.0 / (1.0 + g * (g + kSqrt2)));
    c.a2 = static_cast<float>(g * c.a1);
    c.a3 = static_cast<float>(g * c.a2);
    return c;
}
struct Svf {
    float ic1 = 0, ic2 = 0;
    void reset() { ic1 = ic2 = 0; }
    // bp = v1, lp = v2; hp = x − √2·bp − lp; allpass = x − 2√2·bp.
    inline void tick(float x, const Coef& c, float& bp, float& lp) {
        const float v3 = x - ic2;
        const float v1 = c.a1 * ic1 + c.a2 * v3;
        const float v2 = ic2 + c.a2 * ic1 + c.a3 * v3;
        ic1 = 2.0f * v1 - ic1; ic2 = 2.0f * v2 - ic2;
        bp = v1; lp = v2;
    }
    inline float lp(float x, const Coef& c) { float b, l; tick(x, c, b, l); return l; }
    inline float hp(float x, const Coef& c) { float b, l; tick(x, c, b, l); return x - kSqrt2 * b - l; }
    inline float ap(float x, const Coef& c) { float b, l; tick(x, c, b, l); return x - 2.0f * kSqrt2 * b; }
};

// One channel's band splitter. LR4 low/high share their first stage (its lp feeds the
// low LR4, its hp the high LR4); in 3-band mode the low band passes the second
// crossover's allpass so Low + Mid + High sums to a flat-magnitude allpass.
struct Splitter {
    Svf a1, b1, c1, ap2, a2, b2, c2;
    void reset() { for (Svf* s : { &a1, &b1, &c1, &ap2, &a2, &b2, &c2 }) s->reset(); }
    inline void run(float x, int nb, const Coef& k1, const Coef& k2, float out[3]) {
        if (nb <= 1) { out[0] = 0.0f; out[1] = x; out[2] = 0.0f; return; }
        float bp, lp;
        a1.tick(x, k1, bp, lp);
        const float hp = x - kSqrt2 * bp - lp;
        const float low = b1.lp(lp, k1);
        const float rest = c1.hp(hp, k1);
        if (nb == 2) { out[0] = low; out[1] = rest; out[2] = 0.0f; return; }
        out[0] = ap2.ap(low, k2);
        float bp2, lp2;
        a2.tick(rest, k2, bp2, lp2);
        const float hp2 = rest - kSqrt2 * bp2 - lp2;
        out[1] = b2.lp(lp2, k2);
        out[2] = c2.hp(hp2, k2);
    }
};

// Soft-knee static curve: 0 below −W/2, quadratic across the knee, linear (= u) above.
inline float knee(float u, float w) {
    if (w > 0.01f) {
        if (2.0f * u < -w) return 0.0f;
        if (2.0f * u <= w) { const float t = u + 0.5f * w; return t * t / (2.0f * w); }
        return u;
    }
    return u > 0.0f ? u : 0.0f;
}

} // namespace prism

class Prism final : public Device {
public:
    static constexpr int kBands = 3;
    // Global params.
    enum { Amount = 0, Output, Bands, XoverLow, XoverHigh, Detect, Lookahead, AutoMakeup, SoftClip,
           ScListen, Solo, PeakHold, Mix, kGlobal };
    // Per-band fields (band b's field f lives at kGlobal + b * kPerBand + f).
    enum { AboveThresh = 0, AboveRatio, Attack, Release, Gain, BelowOn, BelowThresh, BelowRatio,
           BelowAttack, BelowRelease, Floor, Knee, AutoRelease, kPerBand };
    static constexpr int kNumParams = kGlobal + kBands * kPerBand;   // 52
    static constexpr int P(int band, int field) { return kGlobal + band * kPerBand + field; }

    // Packed scope telemetry (lock-free, UI-polled).
    enum { S_Gr0 = 0, S_Gr1, S_Gr2, S_Boost0, S_Boost1, S_Boost2, S_Level0, S_Level1, S_Level2,
           S_OutL, S_OutR, S_InPeak, S_Cpu, S_SampleRate, S_Latency, S_ScActive, S_ClipDb, S_Bands,
           S_XoverLow, S_XoverHigh, kScope };

    static constexpr int kSpecLen = 2048;   // spectrum rings (power of two)
    static constexpr int kTraceLen = 256;   // detector traces

    // Musical mappings (shared with the card / presets via the comments in FactoryPresetCatalog).
    static double threshAboveDb(float v) { return -60.0 + 60.0 * v; }
    static double threshBelowDb(float v) { return -80.0 + 80.0 * v; }
    static double ratioBelow(float v) { return std::pow(4.0, (v - 0.5) * 2.0); }        // 0.25 … 4
    static double timeMs(float v, bool release) { return release ? prism::expMap(v, 5.0, 3000.0) : prism::expMap(v, 0.1, 300.0); }
    static double xoverHz(float v) { return prism::expMap(v, 20.0, 20000.0); }

    Prism() {
        static const float glob[kGlobal] = {
            1.0f, 0.5f, 0.0f, 0.318f, 0.693f,   // amount 100 % · output 0 dB · 3 bands · 180 Hz · 2.4 kHz
            0.0f, 0.0f, 0.0f, 0.0f,             // peak · lookahead 0 · auto makeup off · soft clip off
            0.0f, 0.0f, 0.0f, 1.0f };           // listen off · no solo · peak hold off · mix 100 %
        for (int i = 0; i < kGlobal; ++i) p_[i].store(glob[i], std::memory_order_relaxed);
        // Attack / release per band: Low 20 / 200 ms, Mid 10 / 150 ms, High 5 / 100 ms.
        static const float atk[kBands] = { 0.6618f, 0.5752f, 0.4886f };
        static const float rel[kBands] = { 0.5767f, 0.5317f, 0.4683f };
        for (int b = 0; b < kBands; ++b) {
            const float def[kPerBand] = {
                0.7f, 0.5f, atk[b], rel[b], 0.5f,   // above −18 dB · 2:1 · attack · release · gain 0 dB
                0.0f, 0.5f, 0.6214f,                // below off · −40 dB · 1.4:1
                0.7124f, 0.6401f,                   // below attack 30 ms · release 300 ms
                0.5f, 0.25f, 0.0f };                // floor 24 dB · knee 6 dB · auto release off
            for (int f = 0; f < kPerBand; ++f) p_[P(b, f)].store(def[f], std::memory_order_relaxed);
        }
        setSampleRate(44100.0, 0);
    }

    // ---- lifecycle ---------------------------------------------------------------
    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
        laSize_ = static_cast<int>(sr_ * 0.0105) + 2;   // up to 10 ms lookahead
        for (auto& d : delay_) d.assign(static_cast<size_t>(laSize_) * 2, 0.0f);
        laW_ = 0;
        traceDiv_ = std::max(1, static_cast<int>(std::lround(sr_ * 0.006)));
        traceCount_ = 0;
        for (auto& s : split_) s.reset();
        scSplit_.reset();
        for (int b = 0; b < kBands; ++b) {
            BandState& s = st_[b];
            s = BandState{};
            s.thrA = static_cast<float>(threshAboveDb(get(P(b, AboveThresh))));
            s.thrB = static_cast<float>(threshBelowDb(get(P(b, BelowThresh))));
            s.gain = static_cast<float>((get(P(b, Gain)) - 0.5f) * 48.0f);
        }
        xl_ = static_cast<float>(std::log(xoverHz(get(XoverLow))));
        xh_ = static_cast<float>(std::log(xoverHz(get(XoverHigh))));
        outS_ = 1.0f; mixS_ = get(Mix);
    }

    int32_t latencySamples() const override {
        return std::clamp(static_cast<int>(std::lround(get(Lookahead) * 10.0 * 0.001 * sr_)), 0, laSize_ - 1);
    }

    // ---- sidechain ------------------------------------------------------------------
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }

    // ---- DSP ---------------------------------------------------------------------
    void process(float* buf, int32_t frames) override {
        if (frames <= 0) return;
        const auto t0 = std::chrono::steady_clock::now();
        const double sr = sr_;

        // ---- block parameters ----
        const float amount = get(Amount);
        const float outTarget = std::pow(10.0f, (get(Output) - 0.5f) * 48.0f / 20.0f);
        const int nb = 3 - std::clamp(static_cast<int>(std::lround(get(Bands) * 2.0f)), 0, 2);
        const bool rms = get(Detect) >= 0.5f;
        const int la = latencySamples();
        const bool autoMk = get(AutoMakeup) >= 0.5f;
        const bool clip = get(SoftClip) >= 0.5f;
        const bool listen = get(ScListen) >= 0.5f;
        int solo = std::clamp(static_cast<int>(std::lround(get(Solo) * 3.0f)), 0, 3) - 1;
        const float mixTarget = get(Mix);
        bool active[kBands] = { nb >= 2, true, nb >= 3 };
        if (solo >= 0 && !active[solo]) solo = -1;

        const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
        const float scGain = std::pow(10.0f, sidechainGainDb() / 20.0f);

        struct BandCfg { float thrA, slopeA, atkA, relA, relFast, relSlow, slowAtk, gain, thrB, slopeB, atkB, relB, floor, knee; bool below, autoRel; };
        BandCfg cfg[kBands];
        auto coefMs = [sr](double ms) { return static_cast<float>(std::exp(-1.0 / (std::max(0.01, ms) * 0.001 * sr))); };
        for (int b = 0; b < kBands; ++b) {
            BandCfg& c = cfg[b];
            c.thrA = static_cast<float>(threshAboveDb(get(P(b, AboveThresh))));
            c.slopeA = std::clamp(get(P(b, AboveRatio)), 0.0f, 1.0f) * amount;          // 1 − 1/ratio
            const double atk = timeMs(get(P(b, Attack)), false), rel = timeMs(get(P(b, Release)), true);
            c.atkA = coefMs(atk); c.relA = coefMs(rel);
            c.relFast = coefMs(rel * 0.4); c.relSlow = coefMs(rel * 2.5); c.slowAtk = coefMs(std::max(atk * 4.0, 60.0));
            c.autoRel = get(P(b, AutoRelease)) >= 0.5f;
            const float mk = autoMk ? 0.5f * c.slopeA * -c.thrA : 0.0f;
            c.gain = (get(P(b, Gain)) - 0.5f) * 48.0f + mk;
            c.below = get(P(b, BelowOn)) >= 0.5f;
            c.thrB = static_cast<float>(threshBelowDb(get(P(b, BelowThresh))));
            c.slopeB = c.below ? static_cast<float>(ratioBelow(get(P(b, BelowRatio))) - 1.0) * amount : 0.0f;
            c.atkB = coefMs(timeMs(get(P(b, BelowAttack)), false));
            c.relB = coefMs(timeMs(get(P(b, BelowRelease)), true));
            c.floor = get(P(b, Floor)) * 48.0f;
            c.knee = get(P(b, Knee)) * 24.0f;
        }

        // Crossover targets (log Hz), kept ≥ 1/3 octave apart.
        float fl = static_cast<float>(xoverHz(get(XoverLow))), fh = static_cast<float>(xoverHz(get(XoverHigh)));
        fl = std::clamp(fl, 30.0f, 12000.0f); fh = std::clamp(fh, 40.0f, 16000.0f);
        if (fh < fl * 1.26f) { fh = std::min(16000.0f, fl * 1.26f); fl = std::min(fl, fh / 1.26f); }
        const float tl = std::log(fl), th = std::log(fh);
        const float xs = static_cast<float>(1.0 - std::exp(-static_cast<double>(kCtrl) / (0.02 * sr)));
        const float sm = static_cast<float>(1.0 - std::exp(-1.0 / (0.015 * sr)));
        const float rmsC = static_cast<float>(std::exp(-1.0 / (0.010 * sr)));
        constexpr float kDbPerLn = 8.68588964f, kLnPerDb = 0.115129255f;
        const float clipC = 0.96605088f, clipT = 0.75f * clipC;   // −0.3 dBFS ceiling, soft from −2.8 dBFS

        prism::Coef k1 = prism::coef(std::exp(xl_), sr), k2 = prism::coef(std::exp(xh_), sr);
        int ctrl = 0;
        float grMax[kBands] = {}, boostMax[kBands] = {};
        float outPk[2] = { 0.0f, 0.0f }, inPk = 0.0f, clipDb = 0.0f;
        const int laSize = laSize_;

        for (int32_t i = 0; i < frames; ++i) {
            if (--ctrl <= 0) {
                ctrl = kCtrl;
                xl_ += xs * (tl - xl_); xh_ += xs * (th - xh_);
                k1 = prism::coef(std::exp(xl_), sr); k2 = prism::coef(std::exp(xh_), sr);
            }
            const float l = buf[i * 2], r = buf[i * 2 + 1];
            inPk = std::max(inPk, std::max(std::fabs(l), std::fabs(r)));
            float bl[3], br[3], kb[3];
            split_[0].run(l, nb, k1, k2, bl);
            split_[1].run(r, nb, k1, k2, br);
            if (sc) scSplit_.run((sc[i * 2] + sc[i * 2 + 1]) * 0.5f * scGain, nb, k1, k2, kb);

            float wetL = 0, wetR = 0, dryL = 0, dryR = 0, lisL = 0, lisR = 0;
            for (int b = 0; b < kBands; ++b) {
                if (!active[b]) continue;
                const BandCfg& c = cfg[b];
                BandState& s = st_[b];
                // --- detector (stereo-linked) ---
                const float kl = sc ? kb[b] : bl[b], kr = sc ? kb[b] : br[b];
                float rect;
                if (rms) { s.ms = rmsC * s.ms + (1.0f - rmsC) * 0.5f * (kl * kl + kr * kr); rect = std::sqrt(s.ms); }
                else rect = std::max(std::fabs(kl), std::fabs(kr));
                if (c.autoRel) {
                    s.envA = rect + (rect > s.envA ? c.atkA : c.relFast) * (s.envA - rect);
                    s.envS = rect + (rect > s.envS ? c.slowAtk : c.relSlow) * (s.envS - rect);
                } else {
                    s.envA = rect + (rect > s.envA ? c.atkA : c.relA) * (s.envA - rect);
                    s.envS = s.envA;
                }
                s.envB = rect + (rect > s.envB ? c.atkB : c.relB) * (s.envB - rect);
                // --- gain computer (dB; thresholds / slopes / gain glide against zipper) ---
                s.thrA += sm * (c.thrA - s.thrA); s.slopeA += sm * (c.slopeA - s.slopeA);
                s.thrB += sm * (c.thrB - s.thrB); s.slopeB += sm * (c.slopeB - s.slopeB);
                s.gain += sm * (c.gain - s.gain);
                const float lvA = kDbPerLn * std::log(std::max(1e-6f, std::max(s.envA, s.envS)));
                float dyn = -s.slopeA * prism::knee(lvA - s.thrA, c.knee);
                if (c.below || std::fabs(s.slopeB) > 1e-5f) {
                    const float lvB = kDbPerLn * std::log(std::max(1e-6f, s.envB));
                    dyn += std::clamp(-s.slopeB * prism::knee(s.thrB - lvB, c.knee), -c.floor, c.floor);
                }
                s.level = lvA; s.dyn = dyn;
                if (dyn < 0.0f) grMax[b] = std::max(grMax[b], -dyn); else boostMax[b] = std::max(boostMax[b], dyn);
                const float g = std::exp((dyn + s.gain) * kLnPerDb);
                // --- lookahead: gain from now, audio from la samples ago ---
                // (always written, so switching lookahead on picks up recent audio)
                float dl = bl[b], dr = br[b];
                float* d = delay_[b].data();
                d[laW_ * 2] = dl; d[laW_ * 2 + 1] = dr;
                if (la > 0) {
                    int rp = laW_ - la; if (rp < 0) rp += laSize;
                    dl = d[rp * 2]; dr = d[rp * 2 + 1];
                }
                if (solo < 0 || b == solo) {
                    wetL += dl * g; wetR += dr * g; dryL += dl; dryR += dr;
                    lisL += kl; lisR += kr;
                }
                // --- trace ---
                s.trPk = std::max(s.trPk, std::max(std::fabs(bl[b]), std::fabs(br[b])));
            }
            if (++laW_ >= laSize) laW_ = 0;

            float yl, yr;
            if (listen) { yl = lisL; yr = lisR; }
            else {
                mixS_ += sm * (mixTarget - mixS_);
                yl = dryL + mixS_ * (wetL - dryL); yr = dryR + mixS_ * (wetR - dryR);
            }
            outS_ += sm * (outTarget - outS_);
            yl *= outS_; yr *= outS_;
            if (clip) {
                const float al = std::fabs(yl), ar = std::fabs(yr);
                auto sat = [clipC, clipT](float a) { return a <= clipT ? a : clipT + (clipC - clipT) * std::tanh((a - clipT) / (clipC - clipT)); };
                if (al > clipT) { const float c2 = sat(al); clipDb = std::max(clipDb, kDbPerLn * std::log(al / c2)); yl = std::copysign(c2, yl); }
                if (ar > clipT) { const float c2 = sat(ar); clipDb = std::max(clipDb, kDbPerLn * std::log(ar / c2)); yr = std::copysign(c2, yr); }
            }
            buf[i * 2] = yl; buf[i * 2 + 1] = yr;
            outPk[0] = std::max(outPk[0], std::fabs(yl)); outPk[1] = std::max(outPk[1], std::fabs(yr));

            // spectrum rings (mono, pre / post)
            const uint32_t w = specW_;
            inRing_[w & (kSpecLen - 1)] = 0.5f * (l + r);
            outRing_[w & (kSpecLen - 1)] = 0.5f * (yl + yr);
            specW_ = w + 1;
            if (++traceCount_ >= traceDiv_) {
                traceCount_ = 0;
                const uint32_t tw = traceW_;
                for (int b = 0; b < kBands; ++b) {
                    trIn_[b][tw & (kTraceLen - 1)] = active[b] ? st_[b].trPk : 0.0f;
                    trDet_[b][tw & (kTraceLen - 1)] = active[b] ? std::max(st_[b].envA, st_[b].envS) : 0.0f;
                    st_[b].trPk = 0.0f;
                }
                traceW_ = tw + 1;
            }
        }
        specWA_.store(specW_, std::memory_order_release);
        traceWA_.store(traceW_, std::memory_order_release);
        scBuf_ = nullptr;
        // Inactive bands forget their state so re-enabling starts clean.
        for (int b = 0; b < kBands; ++b) if (!active[b]) { st_[b].envA = st_[b].envS = st_[b].envB = st_[b].ms = 0.0f; st_[b].dyn = 0.0f; st_[b].level = -120.0f; }

        // ---- meters + CPU ----
        const float dec = static_cast<float>(std::exp(-frames / (0.3 * sr)));
        auto hold = [dec](std::atomic<float>& a, float v) { a.store(std::max(v, a.load(std::memory_order_relaxed) * dec), std::memory_order_relaxed); };
        for (int b = 0; b < kBands; ++b) {
            gr_[b].store(grMax[b], std::memory_order_relaxed);
            boost_[b].store(boostMax[b], std::memory_order_relaxed);
            level_[b].store(st_[b].level, std::memory_order_relaxed);
        }
        hold(mOutL_, outPk[0]); hold(mOutR_, outPk[1]); hold(mIn_, inPk);
        clipA_.store(clipDb, std::memory_order_relaxed);
        bandsA_.store(nb, std::memory_order_relaxed);
        scActA_.store(sc != nullptr, std::memory_order_relaxed);
        xlA_.store(std::exp(xl_), std::memory_order_relaxed); xhA_.store(std::exp(xh_), std::memory_order_relaxed);
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        cpuS_ = cpuS_ * 0.9 + (el / (frames / sr)) * 0.1;
        cpuA_.store(static_cast<float>(cpuS_), std::memory_order_relaxed);
    }

    // ---- identity / params ---------------------------------------------------------
    const char* displayName() const override { return "Nota Prism"; }
    int32_t     builtinKind() const override { return 21; }
    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[kNumParams] = {
            "Amount", "Output", "Bands", "Crossover Low", "Crossover High", "Detect", "Lookahead", "Auto Makeup",
            "Soft Clip", "SC Listen", "Solo", "Peak Hold", "Mix",
            "Low Above Thresh", "Low Above Ratio", "Low Attack", "Low Release", "Low Gain", "Low Below On", "Low Below Thresh",
            "Low Below Ratio", "Low Below Attack", "Low Below Release", "Low Floor", "Low Knee", "Low Auto Release",
            "Mid Above Thresh", "Mid Above Ratio", "Mid Attack", "Mid Release", "Mid Gain", "Mid Below On", "Mid Below Thresh",
            "Mid Below Ratio", "Mid Below Attack", "Mid Below Release", "Mid Floor", "Mid Knee", "Mid Auto Release",
            "High Above Thresh", "High Above Ratio", "High Attack", "High Release", "High Gain", "High Below On", "High Below Thresh",
            "High Below Ratio", "High Below Attack", "High Below Release", "High Floor", "High Knee", "High Auto Release" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

    // ---- telemetry ---------------------------------------------------------------
    // Summary for the shell meter: the deepest reduction across the bands.
    float gainReductionDb() const override {
        float g = 0.0f;
        for (int b = 0; b < kBands; ++b) g = std::max(g, gr_[b].load(std::memory_order_relaxed));
        return g;
    }
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples < kScope) return 0;
        for (int b = 0; b < kBands; ++b) {
            out[S_Gr0 + b] = gr_[b].load(std::memory_order_relaxed);
            out[S_Boost0 + b] = boost_[b].load(std::memory_order_relaxed);
            out[S_Level0 + b] = level_[b].load(std::memory_order_relaxed);
        }
        out[S_OutL] = mOutL_.load(std::memory_order_relaxed);
        out[S_OutR] = mOutR_.load(std::memory_order_relaxed);
        out[S_InPeak] = mIn_.load(std::memory_order_relaxed);
        out[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
        out[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        out[S_Latency] = static_cast<float>(latencySamples());
        out[S_ScActive] = scActA_.load(std::memory_order_relaxed) ? 1.0f : 0.0f;
        out[S_ClipDb] = clipA_.load(std::memory_order_relaxed);
        out[S_Bands] = static_cast<float>(bandsA_.load(std::memory_order_relaxed));
        out[S_XoverLow] = xlA_.load(std::memory_order_relaxed);
        out[S_XoverHigh] = xhA_.load(std::memory_order_relaxed);
        return kScope;
    }
    // Rings, oldest → newest (torn reads are fine for a visualizer): 0 input / 1 output mono
    // samples (≤ 2048), 2 + 2b band b's input peak, 3 + 2b its detector level (linear, ≤ 256).
    int32_t layerWave(int32_t layer, float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        if (layer == 0 || layer == 1) {
            const float* ring = layer == 0 ? inRing_ : outRing_;
            const uint32_t w = specWA_.load(std::memory_order_acquire);
            const int32_t n = std::min<int32_t>(maxSamples, kSpecLen);
            const uint32_t start = w - static_cast<uint32_t>(n);
            for (int32_t k = 0; k < n; ++k) out[k] = ring[(start + static_cast<uint32_t>(k)) & (kSpecLen - 1)];
            return n;
        }
        const int b = (layer - 2) / 2;
        if (b < 0 || b >= kBands) return 0;
        const float* ring = ((layer - 2) & 1) ? trDet_[b] : trIn_[b];
        const uint32_t w = traceWA_.load(std::memory_order_acquire);
        const int32_t n = std::min<int32_t>(maxSamples, kTraceLen);
        const uint32_t start = w - static_cast<uint32_t>(n);
        for (int32_t k = 0; k < n; ++k) out[k] = ring[(start + static_cast<uint32_t>(k)) & (kTraceLen - 1)];
        return n;
    }

private:
    static constexpr int kCtrl = 16;   // crossover coefficient refresh (samples)

    struct BandState {
        float ms = 0, envA = 0, envS = 0, envB = 0;
        float thrA = -18, slopeA = 0, thrB = -40, slopeB = 0, gain = 0;
        float level = -120, dyn = 0, trPk = 0;
    };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    prism::Splitter split_[2], scSplit_;
    BandState st_[kBands];
    std::vector<float> delay_[kBands];
    int laSize_ = 2, laW_ = 0;
    float xl_ = 5.2f, xh_ = 7.8f, outS_ = 1.0f, mixS_ = 1.0f;
    const float* scBuf_ = nullptr; int32_t scFrames_ = 0;
    std::atomic<int32_t> scTrackId_{-1};

    // telemetry
    float inRing_[kSpecLen] = {}, outRing_[kSpecLen] = {};
    uint32_t specW_ = 0; std::atomic<uint32_t> specWA_{0};
    float trIn_[kBands][kTraceLen] = {}, trDet_[kBands][kTraceLen] = {};
    uint32_t traceW_ = 0; std::atomic<uint32_t> traceWA_{0};
    int traceDiv_ = 256, traceCount_ = 0;
    std::atomic<float> gr_[kBands] = {}, boost_[kBands] = {}, level_[kBands] = {};
    std::atomic<float> mOutL_{0}, mOutR_{0}, mIn_{0}, cpuA_{0}, srA_{44100}, clipA_{0}, xlA_{180}, xhA_{2400};
    std::atomic<int32_t> bandsA_{3};
    std::atomic<bool> scActA_{false};
    double cpuS_ = 0.0;
};

} // namespace nota
