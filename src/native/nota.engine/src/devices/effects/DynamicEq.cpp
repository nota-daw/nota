// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Dynamic EQ-8 implementation. The
// per-sample detector level and all block-rate dB/pitch mapping use FastMath
// (linToDb/exp2f); filter states are shared dsp::BiquadCascade sections;
// silent stretches auto-sleep via the isIdle() contract.

#include "DynamicEq.h"

#include "dsp/math/FastMath.h"
#include "dsp/math/SimdAudio.h"

namespace nota {
namespace {

// Fast one-pole coefficient from a time constant: 1 - exp(-1/(t*sr)).
// NOTE: block-rate ballistics keep std::exp (exact; ~20 calls/block is noise
// next to the per-sample loop). FastMath covers the per-sample detector level
// (linToDb) and all pitch/dB mapping (log2f/exp2f/dbToLin).
inline double stdCoef(double tSec, double sr) {
    return 1.0 - std::exp(-1.0 / (tSec * sr));
}

} // namespace

DynamicEq::DynamicEq() {
    // Neutral default: musically spread bands, all flat — adding the device is
    // transparent. Every band carries working dynamics (−24 dB, 6 dB cut, 10 / 120 ms)
    // so switching a band to Duck or Lift reacts at once.
    band(0, 1, LowCut, 30.0f, 0.0f, 0.71f);
    band(1, 1, Bell, 120.0f, 0.0f, 1.00f);
    band(2, 1, Bell, 800.0f, 0.0f, 0.90f);
    band(3, 1, Bell, 3000.0f, 0.0f, 1.50f);
    band(4, 0, Bell, 200.0f, 0.0f, 1.00f);
    band(5, 0, Bell, 6000.0f, 0.0f, 1.20f);
    band(6, 1, HighShelf, 10000.0f, 0.0f, 0.71f);
    band(7, 1, HighCut, 20000.0f, 0.0f, 0.71f);
    p_[Output].store(0.0f);
    p_[Sidechain].store(0.0f);
    p_[Solo].store(0.0f);
    p_[Dynamics].store(1.0f);
    for (int b = 0; b < kBands; ++b) p_[KeyBase + b].store(0.0f);
    for (int b = 0; b < kBands; ++b) {
        lvlA_[b].store(-120.0f);
        gr_[b].store(0.0f);
    }
    setSampleRate(44100.0, 0);
}

void DynamicEq::setSampleRate(double sr, int32_t /*maxBlock*/) {
    std::lock_guard<std::mutex> lock(mx_);
    sr_ = sr > 0 ? sr : 44100.0;
    srA_.store((float)sr_, std::memory_order_relaxed);
    for (auto& bd : bands_) bd.reset();
    for (auto& st : detSect_) st.reset();
    for (int b = 0; b < kBands; ++b) {
        detEnv_[b] = 0.0;
        dynDb_[b] = 0.0;
        lvlDisp_[b] = -120.0;
    }
    primed_ = false;
    inPk_ = outPk_ = 0.0f;
    quietSamples_ = 0;
    idle_ = false;
    fftN_ = sr_ > 50000.0 ? 8192 : 4096;
    size_t ring = 1;
    while (ring < (size_t)fftN_ * 2) ring <<= 1;
    ring_.assign(ring, 0.0f);
    ringMask_ = (uint32_t)(ring - 1);
    ringW_.store(0, std::memory_order_relaxed);
    fft_.resize((size_t)fftN_);
    win_.assign((size_t)fftN_, 0.0f);
    tBuf_.assign((size_t)fftN_, 0.0f);
    fBuf_.assign((size_t)fftN_ / 2, {});
    for (int i = 0; i < fftN_; ++i) win_[(size_t)i] = (float)(0.5 - 0.5 * std::cos(2.0 * 3.14159265358979 * i / fftN_));
    lastAnaS_ = -1.0;
    lastAnaW_ = 0;
    anaValid_ = false;
    for (int b = 0; b < kSpec; ++b) {
        aSpec_[b] = 0.0;
        spec_[b] = -120.0f;
    }
}

void DynamicEq::wake() {
    idle_ = false;
    quietSamples_ = 0;
}

bool DynamicEq::bandDyn(int b) const {
    const int ty = typeOf(b);
    return get(Dynamics) >= 0.5f && bandOn(b) && (ty == LowShelf || ty == Bell || ty == HighShelf) &&
           modeOf(b) != DynOff;
}

void DynamicEq::band(int b, float on, int type, float freq, float gain, float q, int mode, float thr, float range,
    float atk, float rel) {
    p_[b * kPerBand + On].store(on);
    p_[b * kPerBand + Type].store(static_cast<float>(type));
    p_[b * kPerBand + Freq].store(freq);
    p_[b * kPerBand + Gain].store(gain);
    p_[b * kPerBand + Q].store(q);
    p_[b * kPerBand + Mode].store(static_cast<float>(mode));
    p_[b * kPerBand + Thresh].store(thr);
    p_[b * kPerBand + Range].store(range);
    p_[b * kPerBand + Attack].store(atk);
    p_[b * kPerBand + Release].store(rel);
}

dsp::BiquadCoeffs DynamicEq::buildOne(int type, double sr, double f0, double gainDb, double q) {
    // NOTE: Eq type ids mapped explicitly (dsp::BiquadType ordering differs).
    switch (type) {
    case LowCut: return dsp::BiquadCascade::highpass(sr, f0, q);
    case LowShelf: return dsp::BiquadCascade::lowShelf(sr, f0, gainDb);
    case Notch: return dsp::BiquadCascade::notch(sr, f0, q);
    case HighShelf: return dsp::BiquadCascade::highShelf(sr, f0, gainDb);
    case HighCut: return dsp::BiquadCascade::lowpass(sr, f0, q);
    default: return dsp::BiquadCascade::bell(sr, f0, gainDb, q);
    }
}

void DynamicEq::process(float* buf, int32_t frames) {
    const auto t0 = std::chrono::steady_clock::now();
    if (frames <= 0) return;
    const double sr = sr_;
    const float* sc = (scBuf_ && scFrames_ >= frames) ? scBuf_ : nullptr;
    const double scGain = fast_math::dbToLin(sidechainGainDb());
    const bool allExt = get(Sidechain) >= 0.5f;
    const bool dynMaster = get(Dynamics) >= 0.5f;
    const double outGain = fast_math::dbToLin((float)clampd(get(Output), -18.0, 18.0));

    // Idle skip: silent input on settled filters leaves the buffer untouched.
    // The sidechain handoff is still consumed (it is only valid for this block).
    // Sleep only when the key is silent too — otherwise the frozen detector
    // envelopes would wake up stale against a live sidechain.
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    const bool keySilent = sc == nullptr || simd_audio::isSilent(sc, frames * 2);
    uint32_t w0 = ringW_.load(std::memory_order_relaxed);
    const bool ringOk0 = !ring_.empty();
    if (inSilent && keySilent && idle_) {
        scBuf_ = nullptr;
        scFrames_ = 0;
        keyLive_.store(sc ? 1.0f : 0.0f, std::memory_order_relaxed);
        if (ringOk0) {
            for (int32_t i = 0; i < frames; ++i) ring_[(w0++) & ringMask_] = 0.0f;
            ringW_.store(w0, std::memory_order_release);
        }
        const double blockS = frames / sr;
        const float fall = (float)std::exp(-blockS / 0.3);
        inPk_ *= fall;
        outPk_ *= fall;
        inPkA_.store(db(inPk_), std::memory_order_relaxed);
        outPkA_.store(db(outPk_), std::memory_order_relaxed);
        return;
    }

    // Per-band config + detector coefficients (recomputed per block).
    bool on[kBands], dyn[kBands], hasGain[kBands], ext[kBands];
    int mode[kBands], type[kBands];
    double f0[kBands], gStat[kBands], q[kBands], thr[kBands], range[kBands], atkC[kBands], relC[kBands];
    double tF[kBands], tG[kBands], tQ[kBands]; // glide targets: log2 Hz, dB, log2 Q
    bool glMoving[kBands] = {}, moving = false;
    dsp::BiquadCoeffs det[kBands];
    for (int b = 0; b < kBands; ++b) {
        on[b] = bp(b, On) > 0.5f;
        type[b] = typeOf(b);
        hasGain[b] = (type[b] == LowShelf || type[b] == Bell || type[b] == HighShelf);
        tF[b] = fast_math::log2f((float)clampd(bp(b, Freq), 20.0, sr * 0.49));
        tG[b] = clampd(bp(b, Gain), -18.0, 18.0);
        tQ[b] = fast_math::log2f((float)clampd(bp(b, Q), 0.1, 18.0));
        mode[b] = modeOf(b);
        thr[b] = clampd(bp(b, Thresh), -60.0, 6.0);
        range[b] = clampd(bp(b, Range), -18.0, 18.0);
        const double atkMs = clampd(bp(b, Attack), 0.1, 300.0);
        const double relMs = clampd(bp(b, Release), 5.0, 2000.0);
        atkC[b] = stdCoef(atkMs * 0.001, sr);
        relC[b] = stdCoef(relMs * 0.001, sr);
        ext[b] = sc && (allExt || get(KeyBase + b) >= 0.5f);
    }
    // Solo: audition a single band (mute the rest, force the soloed one on).
    const int solo = std::clamp(static_cast<int>(std::lround(get(Solo))), 0, kBands) - 1;
    if (solo >= 0)
        for (int b = 0; b < kBands; ++b) on[b] = (b == solo);
    // Freq / Gain / Q glide toward the knobs; a band that just came on starts at its values.
    for (int b = 0; b < kBands; ++b) {
        if (on[b] && (!primed_ || !wasOn_[b])) {
            glF_[b] = tF[b];
            glG_[b] = tG[b];
            glQ_[b] = tQ[b];
        }
        wasOn_[b] = on[b];
        if (on[b]) moving |= glMoving[b] = glF_[b] != tF[b] || glG_[b] != tG[b] || glQ_[b] != tQ[b];
        f0[b] = fast_math::exp2f((float)glF_[b]);
        gStat[b] = glG_[b];
        q[b] = fast_math::exp2f((float)glQ_[b]);
        det[b] = dsp::BiquadCascade::bandpass(sr, f0[b], std::max(q[b], 0.5));
        detSect_[b].c = det[b];
    }
    primed_ = true;
    const double glideC = 1.0 - std::exp(-(double)kCtrl / (kGlideS * sr)); // per control step
    for (int b = 0; b < kBands; ++b) {
        dyn[b] = dynMaster && on[b] && hasGain[b] && mode[b] != DynOff;
        if (!dyn[b]) dynDb_[b] = 0.0; // parked bands read flat
    }
    const double detC = 1.0 - std::exp(-1.0 / (0.005 * sr)); // ~5 ms detector envelope

    // Apply coefficients — recomputed at control rate (dynamic gain moves them).
    dsp::BiquadCoeffs cf[kBands];
    for (int b = 0; b < kBands; ++b)
        if (on[b]) {
            cf[b] = buildOne(type[b], sr, f0[b], gStat[b] + dynDb_[b], q[b]);
            bands_[b].setStages(1);
            bands_[b].setSection(0, cf[b]);
        }

    double lvlMax[kBands];
    for (int b = 0; b < kBands; ++b) lvlMax[b] = -120.0;
    float inPk = 0.0f, outPk = 0.0f;
    uint32_t w = w0;
    const bool ringOk = ringOk0;

    int ctrl = 0;
    bool glStepped[kBands] = {};
    for (int32_t i = 0; i < frames; ++i) {
        const float l = buf[i * 2], r = buf[i * 2 + 1];
        inPk = std::max(inPk, std::max(std::fabs(l), std::fabs(r)));

        // --- gliding bands: step Freq / Gain / Q (refreshed with the dynamic gain below) ---
        const bool step = ctrl <= 1;
        if (step && moving) {
            moving = false;
            for (int b = 0; b < kBands; ++b) {
                if (!on[b] || !glMoving[b]) continue;
                glF_[b] = settle(glF_[b] + (tF[b] - glF_[b]) * glideC, tF[b], 1e-5);
                glG_[b] = settle(glG_[b] + (tG[b] - glG_[b]) * glideC, tG[b], 1e-4);
                glQ_[b] = settle(glQ_[b] + (tQ[b] - glQ_[b]) * glideC, tQ[b], 1e-5);
                f0[b] = fast_math::exp2f((float)glF_[b]);
                gStat[b] = glG_[b];
                q[b] = fast_math::exp2f((float)glQ_[b]);
                det[b] = dsp::BiquadCascade::bandpass(sr, f0[b], std::max(q[b], 0.5));
                detSect_[b].c = det[b];
                moving |= glMoving[b] = glF_[b] != tF[b] || glG_[b] != tG[b] || glQ_[b] != tQ[b];
                glStepped[b] = true;
            }
        }

        // --- detection + per-band dynamic gain ---
        const double self = 0.5 * (l + r);
        const double key = sc ? 0.5 * (sc[i * 2] + sc[i * 2 + 1]) * scGain : 0.0;
        for (int b = 0; b < kBands; ++b) {
            if (!on[b]) continue;
            const double d = detSect_[b].run(ext[b] ? key : self);
            detEnv_[b] += (std::fabs(d) - detEnv_[b]) * detC;
            const double lvl = fast_math::linToDb((float)detEnv_[b]);
            if (lvl > lvlMax[b]) lvlMax[b] = lvl;
            if (!dyn[b]) continue;
            const double over = (mode[b] == DynAbove) ? (lvl - thr[b]) : (thr[b] - lvl);
            const double engage = clampd(over / kKneeDb, 0.0, 1.0);
            const double target = range[b] * engage;
            const double c = (std::fabs(target) > std::fabs(dynDb_[b])) ? atkC[b] : relC[b];
            dynDb_[b] += (target - dynDb_[b]) * c;
        }

        // --- refresh apply coeffs for dynamic bands at control rate ---
        if (--ctrl <= 0) {
            ctrl = kCtrl;
            for (int b = 0; b < kBands; ++b)
                if (dyn[b] || glStepped[b]) {
                    cf[b] = buildOne(type[b], sr, f0[b], gStat[b] + dynDb_[b], q[b]);
                    bands_[b].setStages(1);
                    bands_[b].setSection(0, cf[b]);
                    glStepped[b] = false;
                }
        }

        // --- apply cascade, both channels (shared DF2T sections) ---
        for (int ch = 0; ch < 2; ++ch) {
            double x = buf[i * 2 + ch];
            for (int b = 0; b < kBands; ++b)
                if (on[b]) x = ch == 0 ? bands_[b].processSampleL(x) : bands_[b].processSampleR(x);
            buf[i * 2 + ch] = static_cast<float>(x * outGain);
        }
        const float ol = buf[i * 2], orr = buf[i * 2 + 1];
        outPk = std::max(outPk, std::max(std::fabs(ol), std::fabs(orr)));
        if (ringOk) ring_[(w++) & ringMask_] = 0.5f * (ol + orr);
    }
    ringW_.store(w, std::memory_order_release);
    scBuf_ = nullptr;
    scFrames_ = 0;
    keyLive_.store(sc ? 1.0f : 0.0f, std::memory_order_relaxed);

    if (inSilent && outPk <= 1.0e-5f) quietSamples_ += (uint64_t)frames;
    else
        quietSamples_ = 0;
    idle_ = quietSamples_ > kIdleSamples;

    // Publish: the momentary dynamic gain (signed dB) and a peak-held detector level that
    // falls 40 dB/s, so the threshold marker reads steadily at the UI's 60 Hz.
    const double blockS = frames > 0 ? frames / sr : 0.0;
    for (int b = 0; b < kBands; ++b) {
        gr_[b].store(static_cast<float>(dynDb_[b]), std::memory_order_relaxed);
        if (!on[b]) {
            lvlDisp_[b] = -120.0;
            detEnv_[b] = 0.0;
        } else
            lvlDisp_[b] = std::max(std::max(lvlMax[b], -120.0), lvlDisp_[b] - 40.0 * blockS);
        lvlA_[b].store(static_cast<float>(lvlDisp_[b]), std::memory_order_relaxed);
    }
    const float fall = (float)std::exp(-blockS / 0.3);
    inPk_ = std::max(inPk, inPk_ * fall);
    outPk_ = std::max(outPk, outPk_ * fall);
    inPkA_.store(db(inPk_), std::memory_order_relaxed);
    outPkA_.store(db(outPk_), std::memory_order_relaxed);
    if (frames > 0) {
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        cpuS_ = cpuS_ * 0.9 + (el / blockS) * 0.1;
        cpuA_.store((float)cpuS_, std::memory_order_relaxed);
    }
}

const char* DynamicEq::paramName(int32_t i) const {
    if (i == Output) return "Output";
    if (i == Sidechain) return "Sidechain";
    if (i == Solo) return "Solo";
    if (i == Dynamics) return "Dynamics";
    static thread_local char buf[16];
    if (i >= KeyBase && i < kNumParams) {
        std::snprintf(buf, sizeof(buf), "%d Key", i - KeyBase + 1);
        return buf;
    }
    if (i < 0 || i >= kBandParams) return "";
    const int b = i / kPerBand, f = i % kPerBand;
    const char* fld = f == On       ? "On"
                      : f == Type   ? "Type"
                      : f == Freq   ? "Freq"
                      : f == Gain   ? "Gain"
                      : f == Q      ? "Q"
                      : f == Mode   ? "Mode"
                      : f == Thresh ? "Thr"
                      : f == Range  ? "Rng"
                      : f == Attack ? "Atk"
                                    : "Rel";
    std::snprintf(buf, sizeof(buf), "%d %s", b + 1, fld);
    return buf;
}

float DynamicEq::paramMin(int32_t i) const {
    if (i == Output) return -18.0f;
    if (i < 0 || i >= kBandParams) return 0.0f; // Sidechain, Solo, Dynamics, Key
    switch (i % kPerBand) {
    case Freq: return 20.0f;
    case Gain: return -18.0f;
    case Q: return 0.1f;
    case Thresh: return -60.0f;
    case Range: return -18.0f;
    case Attack: return 0.1f;
    case Release: return 5.0f;
    default: return 0.0f; // On, Type, Mode
    }
}

float DynamicEq::paramMax(int32_t i) const {
    if (i == Output) return 18.0f;
    if (i == Solo) return kBands;
    if (i < 0 || i >= kBandParams) return 1.0f; // Sidechain, Dynamics, Key
    switch (i % kPerBand) {
    case On: return 1.0f;
    case Type: return kNumTypes - 1;
    case Freq: return 20000.0f;
    case Gain: return 18.0f;
    case Q: return 18.0f;
    case Mode: return 2.0f;
    case Thresh: return 6.0f;
    case Range: return 18.0f;
    case Attack: return 300.0f;
    default: return 2000.0f; // Release
    }
}

float DynamicEq::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void DynamicEq::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams && std::isfinite(v))
        p_[i].store(std::clamp(v, paramMin(i), paramMax(i)), std::memory_order_relaxed);
    wake();
}

int32_t DynamicEq::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    float t[kTele] = {};
    for (int b = 0; b < kBands; ++b) {
        t[S_Gain + b] = gr_[b].load(std::memory_order_relaxed);
        t[S_Level + b] = lvlA_[b].load(std::memory_order_relaxed);
    }
    t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    t[S_InPeak] = inPkA_.load(std::memory_order_relaxed);
    t[S_OutPeak] = outPkA_.load(std::memory_order_relaxed);
    t[S_KeyLive] = keyLive_.load(std::memory_order_relaxed);
    t[S_Bands] = (float)kSpec;
    t[S_Latency] = 0.0f;
    int32_t n = 0;
    if (maxSamples > kTele) {
        std::lock_guard<std::mutex> lock(mx_);
        t[S_Analysed] = analyse() ? 1.0f : 0.0f;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        for (int b = 0; b < kSpec && n < maxSamples; ++b) out[n++] = spec_[b];
        return n;
    }
    for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
    return n;
}

float DynamicEq::gainReductionDb() const {
    float g = 0.0f;
    for (int b = 0; b < kBands; ++b) {
        float v = gr_[b].load(std::memory_order_relaxed);
        if (v < g) g = v;
    }
    return g;
}

std::string DynamicEq::deviceText(int32_t id) const {
    static const char* types[kNumTypes] = {"Low cut", "Low shelf", "Bell", "Notch", "High shelf", "High cut"};
    static const char* modes[3] = {"static", "duck", "lift"};
    char b[320];
    if (id == 0) {
        int nOn = 0, nDyn = 0;
        for (int k = 0; k < kBands; ++k) {
            if (bandOn(k)) ++nOn;
            if (bandDyn(k)) ++nDyn;
        }
        std::snprintf(b, sizeof b, "%d bands on, %d dynamic - dynamics %s", nOn, nDyn,
            get(Dynamics) >= 0.5f ? "on" : "off (all static)");
        std::string s = b;
        for (int k = 0; k < kBands; ++k) {
            if (!bandOn(k)) continue;
            const int ty = typeOf(k);
            const bool g = ty == LowShelf || ty == Bell || ty == HighShelf;
            std::snprintf(b, sizeof b, " | B%d %s %.0f Hz", k + 1, types[ty], bp(k, Freq));
            s += b;
            if (g) {
                std::snprintf(b, sizeof b, " %+.1f dB", bp(k, Gain));
                s += b;
            }
            std::snprintf(b, sizeof b, " Q %.2f", bp(k, Q));
            s += b;
            if (g && modeOf(k) != DynOff) {
                std::snprintf(b, sizeof b, " %s thr %.0f dB range %+.1f dB atk %.1f ms rel %.0f ms key %s",
                    modes[modeOf(k)], bp(k, Thresh), bp(k, Range), bp(k, Attack), bp(k, Release),
                    keyExt(k) ? "ext" : "self");
                s += b;
            }
        }
        const int solo = std::clamp((int)std::lround(get(Solo)), 0, kBands);
        if (solo > 0) {
            std::snprintf(b, sizeof b, " | solo B%d", solo);
            s += b;
        }
        std::snprintf(b, sizeof b, " | output %+.1f dB", get(Output));
        s += b;
        if (scTrackId_.load(std::memory_order_relaxed) >= 0) s += " - sidechain routed";
        return s;
    }
    if (id == 1) {
        float sc[kTele];
        scopeRead(sc, kTele);
        auto lv = [](float v) {
            char t[24];
            if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf");
            else
                std::snprintf(t, sizeof t, "%.1f", v);
            return std::string(t);
        };
        std::snprintf(b, sizeof b, "in %s dBFS - out %s dBFS - key %s", lv(sc[S_InPeak]).c_str(),
            lv(sc[S_OutPeak]).c_str(), sc[S_KeyLive] > 0.5f ? "live" : "none");
        std::string s = b;
        for (int k = 0; k < kBands; ++k) {
            if (!bandOn(k)) continue;
            std::snprintf(b, sizeof b, " | B%d level %s dBFS", k + 1, lv(sc[S_Level + k]).c_str());
            s += b;
            if (bandDyn(k)) {
                std::snprintf(
                    b, sizeof b, " (thr %.0f) gain now %+.1f of %+.1f dB", bp(k, Thresh), sc[S_Gain + k], bp(k, Range));
                s += b;
            }
        }
        std::snprintf(b, sizeof b, " | CPU %.2f %%", sc[S_Cpu] * 100.0f);
        s += b;
        return s;
    }
    if (id == 2) {
        return "Raw units. Band n (1..8) params are named \"n <field>\" at index (n-1)*10 + field: On (0/1), Type "
               "(0 Low cut, 1 Low shelf, 2 Bell, 3 Notch, 4 High shelf, 5 High cut), Freq 20..20000 Hz, Gain -18..+18 "
               "dB "
               "(shelves and bells only), Q 0.1..18 (the resonance on the cuts), Mode (0 Static, 1 Duck - engages as "
               "the "
               "band's level rises above Thr, 2 Lift - engages as it falls below Thr), Thr -60..+6 dBFS, Rng -18..+18 "
               "dB "
               "(the extra gain at full engagement, 6 dB past the threshold: negative cuts, positive boosts; Duck "
               "normally "
               "cuts, Lift boosts), Atk 0.1..300 ms, Rel 5..2000 ms. Only shelves and bells are dynamic. 80 Output "
               "-18..+18 "
               "dB. 81 Sidechain 0/1 keys every band from the sidechain source (legacy). 82 Solo 0 = none, n = hear "
               "band n "
               "alone. 83 Dynamics 0/1 master switch (0 = every band static). 84..91 \"n Key\" 0 = the band hears this "
               "track, 1 = the sidechain source track (set with set_device_sidechain).";
    }
    return {};
}

bool DynamicEq::analyse() const {
    if (ring_.empty() || fftN_ <= 0) return false;
    const double now = nowSeconds();
    const uint32_t w = ringW_.load(std::memory_order_acquire);
    if (lastAnaS_ >= 0 && (now - lastAnaS_ < 1.0 / 30.0 || w == lastAnaW_)) return anaValid_;
    if (w < (uint32_t)fftN_) return anaValid_;
    const int n = fftN_;
    const uint32_t start = w - (uint32_t)n;
    for (int i = 0; i < n; ++i) tBuf_[(size_t)i] = ring_[(start + (uint32_t)i) & ringMask_] * win_[(size_t)i];
    fft_.fft(tBuf_.data(), fBuf_.data());
    const int half = n / 2;
    const double binHz = sr_ / n;
    const double ref = 0.25 * (double)n * n * 0.25; // Hann: a full-scale sine's bin power
    const bool stale = lastAnaS_ < 0 || now - lastAnaS_ > 1.2;
    const double a = stale ? 1.0 : 1.0 - std::exp(-(now - lastAnaS_) / 0.2);
    double tot = 0.0;
    for (int b = 0; b < kSpec; ++b) {
        const double lo = kSpecLo * std::pow(kSpecHi / kSpecLo, (double)b / kSpec);
        const double hi = kSpecLo * std::pow(kSpecHi / kSpecLo, (double)(b + 1) / kSpec);
        int k0 = std::max(1, (int)std::ceil(lo / binHz)), k1 = std::min(half - 1, (int)std::ceil(hi / binHz) - 1);
        if (k1 < k0) k0 = k1 = std::clamp((int)std::lround(std::sqrt(lo * hi) / binHz), 1, half - 1);
        double s = 0.0;
        for (int k = k0; k <= k1; ++k) s += std::norm(fBuf_[(size_t)k]);
        tot += s;
        aSpec_[b] += a * (s - aSpec_[b]);
        spec_[b] = aSpec_[b] > ref * 1e-12 ? (float)std::max(-120.0, 10.0 * std::log10(aSpec_[b] / ref)) : -120.0f;
    }
    lastAnaS_ = now;
    lastAnaW_ = w;
    anaValid_ = tot > ref * 1e-10;
    return anaValid_;
}

} // namespace nota
