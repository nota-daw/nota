// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in compressor ("Nota Compressor" — almanac update: curve · motion · sidechain) —
// feed-forward with a smoothed level follower (attack / hold / release, optional
// program-dependent auto-release), a soft-knee gain computer, Peak / RMS / Auto detection,
// look-ahead (reported as latency, so PDC keeps the track aligned), a gain-reduction range
// limit, auto make-up gain, dry/wet mix and five voicing "character" models. The detector
// keys off the track itself or an external sidechain source (External Key), through a
// 2-pole high-pass + low-pass with a shared Q and a detector gain; Listen monitors that
// key. Stereo Link on detects the L/R mean and applies one gain to both channels; off, each
// channel compresses on its own.
//
// Params are raw units (the card and presets read them as such); persist / clone /
// automation are generic through the base Device. Param order is the persisted layout —
// APPEND ONLY. Old projects keep their names and values; the appended ones default to the
// old behaviour (external key on, stereo linked, no hold, Butterworth key filters).
// Telemetry for the card goes through scopeRead (see the S_* layout). A core Device
// (JUCE-free); params are atomic / lock-free and process() never allocates.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Compressor : public Device {
public:
    enum { Threshold = 0, Ratio, Attack, Release, Makeup,
           Knee, Mix, Lookahead, Detection, AutoRelease, AutoGain, Range, Character,
           ScHP, ScLP, ScListen,
           // ---- appended for the almanac update — keep the order, append only ----
           ScGain, Hold, ScQ, External, StereoLink, kNumParams };

    // Scope telemetry layout (read by the card and the MCP read_dynamics tool). After the
    // kScope values come kEnvN input-level points, kEnvN gain-reduction points (dB, one per
    // kEnvMs, oldest→newest) and kKeyN samples of the unfiltered key (oldest→newest).
    enum { S_InPk = 0, S_OutPk, S_InRms, S_OutRms, S_Gr, S_KeyPk, S_AtkMs, S_RelMs,
           S_SampleRate, S_Bpm, S_Latency, S_Cpu, S_ExtKey, S_EnvMs, kScope };
    static constexpr int kEnvN = 2048;      // 2 s of envelope at 1 ms per point
    static constexpr int kKeyN = 2048;      // key signal for the card's spectrum
    static constexpr float kEnvMs = 1.0f;

    static constexpr int kNumChars = 5;     // Clean / Glue / Punch / Opto / FET

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        laSize_ = static_cast<int>(sr_ * 0.011) + 2;   // up to ~10 ms look-ahead
        laBuf_.assign(static_cast<size_t>(laSize_) * 2, 0.0f); laW_ = 0;
        envDecim_ = std::max(1, static_cast<int>(sr_ * kEnvMs * 0.001));
        envCnt_ = 0; envIn_ = 0.0f; envGr_ = 0.0f;
        for (auto& c : ch_) c = Chan{};
        inMs_ = outMs_ = 0.0;
        srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
    }
    void setTransport(double, double samplesPerBeat, bool) override {
        if (samplesPerBeat > 0.0) spb_ = samplesPerBeat;
    }

    void process(float* buf, int32_t frames) override {
        if (frames <= 0) return;
        const auto t0 = std::chrono::steady_clock::now();
        const double sr = sr_;
        const int   chr = charIndex();
        static const float chAtk[kNumChars]  = {1.0f, 1.4f, 0.5f, 1.7f, 0.3f};   // Clean/Glue/Punch/Opto/FET
        static const float chRel[kNumChars]  = {1.0f, 1.7f, 0.8f, 2.2f, 0.6f};
        static const float chKnee[kNumChars] = {0.0f, 6.0f, 1.0f, 8.0f, 2.0f};

        const float threshDb = get(Threshold);
        const float ratio    = std::max(1.0f, get(Ratio));
        const float atkMs    = std::max(0.05f, get(Attack)) * chAtk[chr];
        const float relMs    = std::max(1.0f, get(Release)) * chRel[chr];
        const float makeupDb = get(Makeup);
        const float kneeDb   = std::max(0.0f, get(Knee) + chKnee[chr]);
        const float mix      = std::clamp(get(Mix) * 0.01f, 0.0f, 1.0f);
        const float rangeDb  = get(Range);      // max reduction, dB
        const int   detMode  = detectionMode();
        const bool  autoRel  = on(AutoRelease);
        const bool  autoGain = on(AutoGain);
        const bool  listen   = on(ScListen);
        const bool  linked   = on(StereoLink);
        const bool  useExt   = on(External);

        const double atkC  = std::exp(-1.0 / (atkMs * 0.001 * sr));
        const double relC  = std::exp(-1.0 / (relMs * 0.001 * sr));
        const double relCs = std::exp(-1.0 / (relMs * 4.0 * 0.001 * sr));      // slow stage (auto-release)
        const double rmsC  = std::exp(-1.0 / (0.010 * sr));                    // 10 ms RMS window
        const double lvlC  = std::exp(-1.0 / (0.300 * sr));                    // 300 ms in/out RMS meters
        const float slope  = 1.0f - 1.0f / ratio;
        const int holdN    = static_cast<int>(std::clamp(get(Hold), 0.0f, 500.0f) * 0.001f * sr);

        // Key filters: 2-pole TPT state-variable HP and LP sharing one Q. A cut at its range
        // end is skipped entirely, so the default key is the plain detector signal.
        const float hpHz = std::clamp(get(ScHP), 20.0f, 2000.0f);
        const float lpHz = std::clamp(get(ScLP), 200.0f, 20000.0f);
        const float q    = std::clamp(get(ScQ), 0.5f, 4.0f);
        const bool hpOn = hpHz > 20.5f, lpOn = lpHz < 19999.0f;
        const Svf hpF = Svf::make(hpHz, q, sr), lpF = Svf::make(lpHz, q, sr);

        // Auto make-up estimate: roughly half the reduction at 0 dBFS peaks.
        const float autoMk = autoGain ? std::max(0.0f, -threshDb * slope * 0.5f) : 0.0f;

        const float* sc = (useExt && scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
        // The detector gain: the SC Gain param always, plus the base sidechain gain when an
        // external source keys it (the older, generic sidechain control).
        const float keyGain = dbToLin(get(ScGain) + (sc ? sidechainGainDb() : 0.0f));
        const int la = lookaheadSamples();

        float maxGr = 0.0f, inPk = 0.0f, outPk = 0.0f, keyPk = 0.0f;
        const int nDet = linked ? 1 : 2;
        for (int32_t i = 0; i < frames; ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];
            const float kl = (sc ? sc[i * 2] : l) * keyGain, kr = (sc ? sc[i * 2 + 1] : r) * keyGain;
            const float keyRaw[2] = { linked ? (kl + kr) * 0.5f : kl, kr };

            float gr[2] = {0.0f, 0.0f}, key[2] = {0.0f, 0.0f};
            for (int c = 0; c < nDet; ++c) {
                Chan& s = ch_[c];
                float d = keyRaw[c];
                if (hpOn) d = s.hp.tick(hpF, d, true);
                if (lpOn) d = s.lp.tick(lpF, d, false);
                key[c] = d;

                float det;
                const float pk = std::fabs(d);
                if (detMode == 0) det = pk;
                else {
                    s.rmsE = static_cast<float>(rmsC * s.rmsE + (1.0 - rmsC) * d * d);
                    const float rmsLvl = std::sqrt(s.rmsE);
                    // Auto: RMS scaled to a sine's peak for the body, with the raw peak still able
                    // to catch a transient that stands well above it.
                    det = detMode == 1 ? rmsLvl : std::max(rmsLvl * 1.41421356f, pk * 0.5f);
                }

                if (det > s.env) { s.env = static_cast<float>(atkC * s.env + (1.0 - atkC) * det); s.hold = holdN; }
                else if (s.hold > 0) --s.hold;
                else {
                    const double rc = (autoRel && s.grPrev > 3.0f) ? relCs : relC;
                    s.env = static_cast<float>(rc * s.env + (1.0 - rc) * det);
                }

                const float envDb = 20.0f * std::log10(s.env + 1e-9f);
                const float over = envDb - threshDb;
                float g;
                if (kneeDb > 0.01f && 2.0f * over > -kneeDb && 2.0f * over < kneeDb)
                    g = slope * (over + kneeDb * 0.5f) * (over + kneeDb * 0.5f) / (2.0f * kneeDb);   // soft knee
                else g = over > 0.0f ? slope * over : 0.0f;
                g = std::min(g, rangeDb);
                s.grPrev = g;
                gr[c] = g;
            }
            if (linked) { gr[1] = gr[0]; key[1] = key[0]; }
            const float grMax = std::max(gr[0], gr[1]);
            maxGr = std::max(maxGr, grMax);
            keyPk = std::max(keyPk, std::max(std::fabs(key[0]), std::fabs(key[1])));

            const float gainL = (1.0f - mix) + mix * dbToLin(makeupDb + autoMk - gr[0]);
            const float gainR = (1.0f - mix) + mix * dbToLin(makeupDb + autoMk - gr[1]);

            // Look-ahead: apply the current gain to the delayed audio.
            laBuf_[laW_ * 2] = l; laBuf_[laW_ * 2 + 1] = r;
            int rp = laW_ - la; if (rp < 0) rp += laSize_;
            const float dl = laBuf_[rp * 2], dr = laBuf_[rp * 2 + 1];
            if (++laW_ >= laSize_) laW_ = 0;

            float oL, oR;
            if (listen) { oL = key[0]; oR = key[1]; }
            else { oL = dl * gainL; oR = dr * gainR; }
            buf[i * 2] = oL; buf[i * 2 + 1] = oR;

            // ---- meters: peak + 300 ms RMS on the input (as it enters) and the output ----
            const float inA = std::max(std::fabs(l), std::fabs(r)), outA = std::max(std::fabs(oL), std::fabs(oR));
            inPk = std::max(inPk, inA); outPk = std::max(outPk, outA);
            inMs_ = lvlC * inMs_ + (1.0 - lvlC) * 0.5 * (l * l + r * r);
            outMs_ = lvlC * outMs_ + (1.0 - lvlC) * 0.5 * (oL * oL + oR * oR);

            // ---- envelope ring (1 ms buckets: the loudest input and the deepest reduction) ----
            envIn_ = std::max(envIn_, inA);
            envGr_ = std::max(envGr_, grMax);
            if (++envCnt_ >= envDecim_) {
                const int w = envW_.load(std::memory_order_relaxed);
                envRing_[w] = 20.0f * std::log10(envIn_ + 1e-9f);
                grRing_[w] = envGr_;
                envW_.store((w + 1) % kEnvN, std::memory_order_relaxed);
                envCnt_ = 0; envIn_ = 0.0f; envGr_ = 0.0f;
            }
            // ---- key ring (unfiltered, so the card can draw what the filters take away) ----
            const int kw = keyW_.load(std::memory_order_relaxed);
            keyRing_[kw] = (keyRaw[0] + (linked ? keyRaw[0] : keyRaw[1])) * 0.5f;
            keyW_.store((kw + 1) % kKeyN, std::memory_order_relaxed);
        }
        gr_.store(maxGr, std::memory_order_relaxed);
        scBuf_ = nullptr;

        // ---- telemetry ---------------------------------------------------------------
        inPkA_.store(decayPeak(inPkA_.load(std::memory_order_relaxed), inPk), std::memory_order_relaxed);
        outPkA_.store(decayPeak(outPkA_.load(std::memory_order_relaxed), outPk), std::memory_order_relaxed);
        keyPkA_.store(decayPeak(keyPkA_.load(std::memory_order_relaxed), keyPk), std::memory_order_relaxed);
        inRmsA_.store(static_cast<float>(std::sqrt(inMs_)), std::memory_order_relaxed);
        outRmsA_.store(static_cast<float>(std::sqrt(outMs_)), std::memory_order_relaxed);
        extA_.store(sc ? 1.0f : 0.0f, std::memory_order_relaxed);
        atkA_.store(atkMs, std::memory_order_relaxed);
        relA_.store(autoRel && maxGr > 3.0f ? relMs * 4.0f : relMs, std::memory_order_relaxed);
        bpmA_.store(spb_ > 0.0 ? static_cast<float>(60.0 * sr_ / spb_) : 0.0f, std::memory_order_relaxed);
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        cpuS_ = cpuS_ * 0.9 + (el / (frames / sr_)) * 0.1;
        cpuA_.store(static_cast<float>(cpuS_), std::memory_order_relaxed);
    }

    // Look-ahead delays the audio by this much; the engine's PDC lines the other tracks up.
    int32_t latencySamples() const override { return lookaheadSamples(); }

    float gainReductionDb() const override { return gr_.load(std::memory_order_relaxed); }

    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples < kScope) return 0;
        out[S_InPk] = inPkA_.load(std::memory_order_relaxed);
        out[S_OutPk] = outPkA_.load(std::memory_order_relaxed);
        out[S_InRms] = inRmsA_.load(std::memory_order_relaxed);
        out[S_OutRms] = outRmsA_.load(std::memory_order_relaxed);
        out[S_Gr] = gr_.load(std::memory_order_relaxed);
        out[S_KeyPk] = keyPkA_.load(std::memory_order_relaxed);
        out[S_AtkMs] = atkA_.load(std::memory_order_relaxed);
        out[S_RelMs] = relA_.load(std::memory_order_relaxed);
        out[S_SampleRate] = srA_.load(std::memory_order_relaxed);
        out[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
        out[S_Latency] = static_cast<float>(latencySamples());
        out[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
        out[S_ExtKey] = extA_.load(std::memory_order_relaxed);
        out[S_EnvMs] = kEnvMs;
        int32_t n = kScope;
        if (maxSamples >= kScope + 2 * kEnvN) {
            const int w = envW_.load(std::memory_order_relaxed);
            for (int k = 0; k < kEnvN; ++k) {
                const int idx = (w + k) % kEnvN;
                out[kScope + k] = envRing_[idx];
                out[kScope + kEnvN + k] = grRing_[idx];
            }
            n += 2 * kEnvN;
            if (maxSamples >= kScope + 2 * kEnvN + kKeyN) {
                const int kw = keyW_.load(std::memory_order_relaxed);
                for (int k = 0; k < kKeyN; ++k) out[n + k] = keyRing_[(kw + k) % kKeyN];
                n += kKeyN;
            }
        }
        return n;
    }

    // id 0 — a one-line summary of what the compressor is doing now (the card's status strip,
    // and get_device_text over MCP).
    std::string deviceText(int32_t id) const override {
        if (id != 0) return {};
        static const char* chn[kNumChars] = { "Clean", "Glue", "Punch", "Opto", "FET" };
        static const char* det[3] = { "peak", "RMS", "auto" };
        char b[320];
        const float rng = getParam(Range);
        char rngS[32];
        if (rng >= 47.5f) std::snprintf(rngS, sizeof rngS, "off"); else std::snprintf(rngS, sizeof rngS, "%.0f dB", rng);
        std::snprintf(b, sizeof b,
            "%s - threshold %.1f dB - %.1f:1 - knee %.1f dB - attack %.1f ms / release %.0f ms%s - %s - range %s - mix %.0f %% - GR %.1f dB%s%s%s",
            chn[charIndex()], getParam(Threshold), getParam(Ratio), getParam(Knee), getParam(Attack), getParam(Release),
            getParam(AutoRelease) >= 0.5f ? " (auto)" : "", det[detectionMode()], rngS, getParam(Mix),
            gr_.load(std::memory_order_relaxed),
            extA_.load(std::memory_order_relaxed) > 0.5f ? " - external key" : "",
            getParam(StereoLink) >= 0.5f ? "" : " - unlinked",
            getParam(ScListen) >= 0.5f ? " - LISTEN (key only)" : "");
        return std::string(b);
    }

    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return true; }

    const char* displayName() const override { return "Nota Compressor"; }
    int32_t     builtinKind() const override { return 1; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[] = { "Thresh", "Ratio", "Attack", "Release", "Makeup", "Knee", "Mix",
            "Lookahead", "Detection", "AutoRelease", "AutoGain", "Range", "Character", "SC HP", "SC LP", "SC Listen",
            "SC Gain", "Hold", "SC Q", "External Key", "Stereo Link" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t i) const override {
        switch (i) { case Threshold: return -60.0f; case Ratio: return 1.0f; case Attack: return 0.1f; case Release: return 5.0f;
            case ScHP: return 20.0f; case ScLP: return 200.0f; case ScGain: return -24.0f; case ScQ: return 0.5f; default: return 0.0f; }
    }
    float paramMax(int32_t i) const override {
        switch (i) { case Threshold: return 0.0f; case Ratio: return 20.0f; case Attack: return 100.0f; case Release: return 1000.0f;
            case Makeup: return 24.0f; case Knee: return 24.0f; case Mix: return 100.0f; case Lookahead: return 10.0f;
            case Detection: return 2.0f; case Range: return 48.0f; case Character: return 4.0f; case ScHP: return 2000.0f;
            case ScLP: return 20000.0f; case ScGain: return 24.0f; case Hold: return 500.0f; case ScQ: return 4.0f; default: return 1.0f; }
    }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void setParam(int32_t i, float v) override { if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed); }

    Compressor() {
        set(Threshold, -18.0f); set(Ratio, 3.0f); set(Attack, 10.0f); set(Release, 120.0f); set(Makeup, 0.0f);
        set(Knee, 6.0f); set(Mix, 100.0f); set(Lookahead, 0.0f); set(Detection, 0.0f);
        set(AutoRelease, 0.0f); set(AutoGain, 0.0f); set(Range, 48.0f); set(Character, 0.0f);
        set(ScHP, 20.0f); set(ScLP, 20000.0f); set(ScListen, 0.0f);
        set(ScGain, 0.0f); set(Hold, 0.0f); set(ScQ, 0.7071f); set(External, 1.0f); set(StereoLink, 1.0f);
        std::fill(std::begin(envRing_), std::end(envRing_), -120.0f);
        setSampleRate(44100.0, 0);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;

    // A TPT (Zavalishin) state-variable filter: one set of coefficients, HP or LP out.
    struct Svf {
        float g = 0, k = 1.414f, a1 = 0, a2 = 0, a3 = 0;
        static Svf make(double hz, double q, double sr) {
            Svf s;
            s.g = static_cast<float>(std::tan(kPi * std::clamp(hz, 5.0, sr * 0.45) / sr));
            s.k = static_cast<float>(1.0 / std::max(0.1, q));
            s.a1 = 1.0f / (1.0f + s.g * (s.g + s.k)); s.a2 = s.g * s.a1; s.a3 = s.g * s.a2;
            return s;
        }
    };
    struct SvfState {
        float ic1 = 0, ic2 = 0;
        float tick(const Svf& f, float x, bool highPass) {
            const float v3 = x - ic2;
            const float v1 = f.a1 * ic1 + f.a2 * v3;
            const float v2 = ic2 + f.a2 * ic1 + f.a3 * v3;
            ic1 = 2.0f * v1 - ic1; ic2 = 2.0f * v2 - ic2;
            return highPass ? x - f.k * v1 - v2 : v2;
        }
    };
    struct Chan {
        SvfState hp, lp;
        float env = 0.0f, rmsE = 0.0f, grPrev = 0.0f;
        int hold = 0;
    };

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    bool on(int i) const { return get(i) >= 0.5f; }
    void set(int i, float v) { p_[i].store(v, std::memory_order_relaxed); }
    int charIndex() const { return std::clamp(static_cast<int>(std::lround(getParam(Character))), 0, kNumChars - 1); }
    int detectionMode() const { return std::clamp(static_cast<int>(std::lround(getParam(Detection))), 0, 2); }
    int lookaheadSamples() const {
        return std::clamp(static_cast<int>(getParam(Lookahead) * 0.001 * sr_), 0, std::max(0, laSize_ - 1));
    }
    static float dbToLin(float db) { return std::exp(db * 0.11512925f); }
    static float decayPeak(float held, float now) { return now > held ? now : held * 0.86f; }

    double sr_ = 44100.0, spb_ = 0.0;
    Chan ch_[2];
    std::vector<float> laBuf_; int laSize_ = 0, laW_ = 0;
    double inMs_ = 0.0, outMs_ = 0.0, cpuS_ = 0.0;
    int envDecim_ = 44, envCnt_ = 0;
    float envIn_ = 0.0f, envGr_ = 0.0f;
    float envRing_[kEnvN] = {}, grRing_[kEnvN] = {}, keyRing_[kKeyN] = {};
    std::atomic<int> envW_{0}, keyW_{0};
    std::atomic<float> gr_{0.0f};
    std::atomic<float> inPkA_{0}, outPkA_{0}, keyPkA_{0}, inRmsA_{0}, outRmsA_{0}, extA_{0},
                       atkA_{0}, relA_{0}, srA_{44100.0f}, bpmA_{0}, cpuA_{0};
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr; int32_t scFrames_ = 0;
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
