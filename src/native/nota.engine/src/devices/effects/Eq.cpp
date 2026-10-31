// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota EQ-8 implementation . Audio path: shared
// dsp::BiquadCascade sections run sample-major (all bands per frame, in double),
// FastMath pitch/dB mapping, auto-sleep on silent stretches. Everything else
// (params, analyzer, blob-irrelevant text) is verbatim.

#include "Eq.h"

#include "dsp/math/FastMath.h"
#include "dsp/math/SimdAudio.h"

#include <cstring>

namespace nota {
namespace {

constexpr double kPi = 3.14159265358979323846;

} // namespace

Eq::Eq() {
    // Four musically-spread bands enabled and flat; the rest are ready but off.
    setBand(0, 1, LowShelf, 100.0f, 0.0f, 0.70f);
    setBand(1, 1, Bell, 300.0f, 0.0f, 0.70f);
    setBand(2, 1, Bell, 2000.0f, 0.0f, 0.70f);
    setBand(3, 1, HighShelf, 8000.0f, 0.0f, 0.70f);
    setBand(4, 0, Bell, 60.0f, 0.0f, 0.70f);
    setBand(5, 0, Bell, 800.0f, 0.0f, 0.70f);
    setBand(6, 0, Bell, 5000.0f, 0.0f, 0.70f);
    setBand(7, 0, HighCut, 16000.0f, 0.0f, 0.70f);
    for (int b = 0; b < kBands; ++b) {
        p_[SlopeBase + b].store(0.0f);
        p_[ChannelBase + b].store(0.0f);
    }
    p_[Scale].store(100.0f);
    p_[Output].store(0.0f);
    p_[AutoGain].store(0.0f);
    p_[Analyzer].store((float)AnaPost);
    p_[AnaRange].store(1.0f);
    p_[AnaPeak].store(0.0f);
    p_[Labels].store(1.0f);
    setSampleRate(44100.0, 0);
}

void Eq::setSampleRate(double sr, int32_t /*maxBlock*/) {
    std::lock_guard<std::mutex> lock(mx_);
    sr_ = sr > 0 ? sr : 44100.0;
    srA_.store((float)sr_, std::memory_order_relaxed);
    for (auto& bd : bands_) bd.reset();
    for (auto& c : cfgKey_) c = -1;
    autoSr_ = -1.0;
    autoDb_ = 0.0;
    autoCur_ = 1.0;
    outCur_ = 1.0;
    primed_ = false;
    inPk_ = outPk_ = 0.0f;
    quietSamples_ = 0;
    idle_ = false;
    const int fftN = dsp::SpectrumAnalyzer::fftSizeFor(sr_);
    size_t ring = 1;
    while (ring < (size_t)fftN * 2) ring <<= 1;
    for (auto& r : ring_) r.assign(ring, 0.0f);
    ringMask_ = (uint32_t)(ring - 1);
    ringW_.store(0, std::memory_order_relaxed);
    for (auto& a : ana_) a.prepare(sr_);
    lastAnaS_ = -1.0;
    anaValid_ = false;
}

void Eq::wake() {
    idle_ = false;
    quietSamples_ = 0;
}

dsp::BiquadChannel Eq::routing(int ch) {
    switch (ch) {
    case ChLeft: return dsp::BiquadChannel::Left;
    case ChRight: return dsp::BiquadChannel::Right;
    case ChMid: return dsp::BiquadChannel::Mid;
    case ChSide: return dsp::BiquadChannel::Side;
    default: return dsp::BiquadChannel::Stereo;
    }
}

int Eq::buildBand(dsp::BiquadCoeffs* out, int type, int slope, double sr, double f0, double gainDb, double q) {
    if ((type == LowCut || type == HighCut) && slope > 0) {
        double bq[4];
        const int n = dsp::BiquadCascade::butterworthQs(bq, slope);
        const double res = q / 0.70710678;
        for (int k = 0; k < n; ++k) {
            const double qk = k == n - 1 ? clampd(bq[k] * res, 0.1, 40.0) : bq[k];
            out[k] =
                type == LowCut ? dsp::BiquadCascade::highpass(sr, f0, qk) : dsp::BiquadCascade::lowpass(sr, f0, qk);
        }
        return n;
    }
    out[0] = buildOne(type, sr, f0, gainDb, q);
    return 1;
}

dsp::BiquadCoeffs Eq::buildOne(int type, double sr, double f0, double gainDb, double q) {
    // NOTE: Eq type ids intentionally mapped explicitly — dsp::BiquadType uses
    // a different ordering, so no static_cast here.
    switch (type) {
    case LowCut: return dsp::BiquadCascade::highpass(sr, f0, q);
    case LowShelf: return dsp::BiquadCascade::lowShelf(sr, f0, gainDb);
    case Notch: return dsp::BiquadCascade::notch(sr, f0, q);
    case HighShelf: return dsp::BiquadCascade::highShelf(sr, f0, gainDb);
    case HighCut: return dsp::BiquadCascade::lowpass(sr, f0, q);
    default: return dsp::BiquadCascade::bell(sr, f0, gainDb, q);
    }
}

double Eq::magDb(const dsp::BiquadCoeffs& k, double w) {
    const double cw = std::cos(w), sw = std::sin(w), c2 = std::cos(2 * w), s2 = std::sin(2 * w);
    const double nr = k.b0 + k.b1 * cw + k.b2 * c2, ni = -(k.b1 * sw + k.b2 * s2);
    const double dr = 1 + k.a1 * cw + k.a2 * c2, di = -(k.a1 * sw + k.a2 * s2);
    return 10.0 * std::log10(std::max(1e-12, (nr * nr + ni * ni) / std::max(1e-12, dr * dr + di * di)));
}

double Eq::averageDb(const bool* on, const int* ty, const int* ch, const dsp::BiquadCoeffs (*c)[kMaxSec], double sr) {
    constexpr int N = 64;
    double sum = 0.0;
    for (int i = 0; i < N; ++i) {
        const double f = 20.0 * std::pow(1000.0, (i + 0.5) / N);
        if (f >= sr * 0.49) continue;
        const double w = 2.0 * kPi * f / sr;
        double m = 0.0;
        for (int b = 0; b < kBands; ++b) {
            if (!on[b] || ch[b] == ChSide || !hasGain(ty[b])) continue;
            const double v = magDb(c[b][0], w);
            m += (ch[b] == ChLeft || ch[b] == ChRight) ? 0.5 * v : v;
        }
        sum += m;
    }
    return sum / N;
}

void Eq::process(float* buf, int32_t frames) {
    const auto t0 = std::chrono::steady_clock::now();
    if (frames <= 0) return;
    const double sr = sr_;
    const double scale = clampd(get(Scale), 0.0, 200.0) / 100.0;

    // Idle skip: silent input on a settled device leaves the buffer (already
    // silent) untouched; analyzer rings still get zeros so spectra decay.
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    uint32_t w0 = ringW_.load(std::memory_order_relaxed);
    const bool ringOk0 = !ring_[0].empty();
    if (inSilent && idle_) {
        if (ringOk0) {
            for (int32_t i = 0; i < frames; ++i) {
                ring_[0][w0 & ringMask_] = 0.0f;
                ring_[1][w0 & ringMask_] = 0.0f;
                ++w0;
            }
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

    // Per-band config + coefficient cascades, once per block (FastMath pitch/dB).
    bool on[kBands];
    int ty[kBands], ch[kBands], sl[kBands], nSec[kBands];
    double tF[kBands], tG[kBands], tQ[kBands]; // glide targets: log2 Hz, dB, log2 Q
    bool moving = false;
    dsp::BiquadCoeffs c[kBands][kMaxSec];
    for (int b = 0; b < kBands; ++b) {
        on[b] = bandOn(b);
        if (!on[b]) {
            cfgKey_[b] = -1;
            continue;
        }
        ty[b] = typeOf(b);
        ch[b] = channelOf(b);
        sl[b] = slopeOf(b);
        tF[b] = fast_math::log2f((float)clampd(bp(b, Freq), 20.0, sr * 0.49));
        tG[b] = clampd(bp(b, Gain), -18.0, 18.0) * scale;
        tQ[b] = fast_math::log2f((float)clampd(bp(b, Q), 0.1, 18.0));
        if (!primed_ || cfgKey_[b] < 0) {
            glF_[b] = tF[b];
            glG_[b] = tG[b];
            glQ_[b] = tQ[b];
        } // fresh: no glide in
        moving |= glMoving_[b] = glF_[b] != tF[b] || glG_[b] != tG[b] || glQ_[b] != tQ[b];
        nSec[b] = buildBand(
            c[b], ty[b], sl[b], sr, fast_math::exp2f((float)glF_[b]), glG_[b], fast_math::exp2f((float)glQ_[b]));
        // A band whose cascade length or channel changes starts clean (its old state means
        // something else); a type or frequency move keeps it, as the original EQ-8 did.
        const int key = nSec[b] * 10 + ch[b];
        if (key != cfgKey_[b]) {
            bands_[b].reset();
            cfgKey_[b] = key;
        }
        bands_[b].setStages(nSec[b]);
        for (int k = 0; k < nSec[b]; ++k) bands_[b].setSection(k, c[b][k]);
    }

    // Auto gain: the opposite of the average curve level, re-measured only when a param moves.
    const bool autoOn = get(AutoGain) >= 0.5f;
    if (autoOn) {
        bool moved = autoSr_ != sr;
        for (int i = 0; i < kAutoKeys && !moved; ++i) moved = autoKey_[i] != get(i);
        if (moved) {
            for (int i = 0; i < kAutoKeys; ++i) autoKey_[i] = get(i);
            autoSr_ = sr;
            dsp::BiquadCoeffs ct[kBands][kMaxSec]; // measured at the targets, not mid-glide
            for (int b = 0; b < kBands; ++b)
                if (on[b])
                    buildBand(
                        ct[b], ty[b], sl[b], sr, fast_math::exp2f((float)tF[b]), tG[b], fast_math::exp2f((float)tQ[b]));
            autoDb_ = clampd(-averageDb(on, ty, ch, ct, sr), -12.0, 12.0);
        }
    }
    const double autoTarget = autoOn ? fast_math::dbToLin((float)autoDb_) : 1.0;
    const double outTarget = fast_math::dbToLin((float)clampd(get(Output), -12.0, 12.0));
    if (!primed_) {
        autoCur_ = autoTarget;
        outCur_ = outTarget;
        primed_ = true;
    }
    const double glide = 1.0 - std::exp(-1.0 / (0.02 * sr));            // ~20 ms, click-free
    const double glideC = 1.0 - std::exp(-(double)kCtrl / (0.02 * sr)); // the same, per control step

    // Pre-pass: input peak + pre ring (the filter pass overwrites the buffer).
    float inPk = 0.0f;
    uint32_t w = w0;
    const bool ringOk = ringOk0;
    for (int32_t i = 0; i < frames; ++i) {
        const float l = buf[i * 2], r = buf[i * 2 + 1];
        const float a = std::fabs(l) > std::fabs(r) ? std::fabs(l) : std::fabs(r);
        if (a > inPk) inPk = a;
        if (ringOk) ring_[0][w & ringMask_] = 0.5f * (l + r);
        ++w;
    }

    // Bands in series, sample-major: every frame runs through all active bands before
    // the next, so the out-of-order core overlaps the bands' recurrences across frames
    // (band-major passes are each bound by one biquad's latency). Gliding bands rebuild
    // their coefficients every kCtrl frames.
    int act[kBands], nAct = 0;
    dsp::BiquadChannel rt[kBands];
    for (int b = 0; b < kBands; ++b)
        if (on[b]) {
            rt[b] = routing(ch[b]);
            act[nAct++] = b;
        }
    bool flat = !moving;
    for (int a = 0; a < nAct && flat; ++a) flat = rt[act[a]] == dsp::BiquadChannel::Stereo;
    if (flat) {
        // Common case — every active band Stereo and none gliding: the bands in series are
        // one chain of sections. Run it from a contiguous copy, then store the state back.
        dsp::BiquadSection chL[kBands * kMaxSec], chR[kBands * kMaxSec];
        int n = 0;
        for (int a = 0; a < nAct; ++a) {
            dsp::BiquadCascade& bc = bands_[act[a]];
            for (int k = 0; k < bc.stages(); ++k, ++n) {
                chL[n] = bc.bank(0)[k];
                chR[n] = bc.bank(1)[k];
            }
        }
        for (int32_t i = 0; i < frames; ++i) {
            double l = buf[i * 2], r = buf[i * 2 + 1];
            for (int k = 0; k < n; ++k) {
                l = chL[k].run(l);
                r = chR[k].run(r);
            }
            buf[i * 2] = static_cast<float>(l);
            buf[i * 2 + 1] = static_cast<float>(r);
        }
        n = 0;
        for (int a = 0; a < nAct; ++a) {
            dsp::BiquadCascade& bc = bands_[act[a]];
            for (int k = 0; k < bc.stages(); ++k, ++n) {
                bc.bank(0)[k] = chL[n];
                bc.bank(1)[k] = chR[n];
            }
        }
    }
    for (int32_t off = 0; off < frames && !flat; off += kCtrl) {
        const int32_t m = off + kCtrl <= frames ? kCtrl : frames - off;
        for (int a = 0; a < nAct; ++a) {
            const int b = act[a];
            if (!glMoving_[b]) continue;
            glF_[b] = settle(glF_[b] + (tF[b] - glF_[b]) * glideC, tF[b], 1e-5);
            glG_[b] = settle(glG_[b] + (tG[b] - glG_[b]) * glideC, tG[b], 1e-4);
            glQ_[b] = settle(glQ_[b] + (tQ[b] - glQ_[b]) * glideC, tQ[b], 1e-5);
            dsp::BiquadCoeffs cb[kMaxSec];
            const int n = buildBand(
                cb, ty[b], sl[b], sr, fast_math::exp2f((float)glF_[b]), glG_[b], fast_math::exp2f((float)glQ_[b]));
            bands_[b].setStages(n);
            for (int k = 0; k < n; ++k) bands_[b].setSection(k, cb[k]);
            glMoving_[b] = glF_[b] != tF[b] || glG_[b] != tG[b] || glQ_[b] != tQ[b];
            moving = moving || glMoving_[b];
        }
        float* p = buf + off * 2;
        for (int32_t i = 0; i < m; ++i) {
            double l = p[i * 2], r = p[i * 2 + 1];
            for (int a = 0; a < nAct; ++a) bands_[act[a]].processFrame(l, r, rt[act[a]]);
            p[i * 2] = static_cast<float>(l);
            p[i * 2 + 1] = static_cast<float>(r);
        }
    }
    (void)moving;

    // Gain stage: smoothed auto/output gains, output peak, post ring. The post ring is written
    // at the same indices as the pre ring, so the two stay aligned and the ring advances one
    // sample per frame (it used to run on from the pre pass, leaving every other block stale).
    float outPk = 0.0f;
    w = w0;
    for (int32_t i = 0; i < frames; ++i) {
        autoCur_ += (autoTarget - autoCur_) * glide;
        outCur_ += (outTarget - outCur_) * glide;
        const double g = autoCur_ * outCur_;
        const float ol = (float)(buf[i * 2] * g), orr = (float)(buf[i * 2 + 1] * g);
        buf[i * 2] = ol;
        buf[i * 2 + 1] = orr;
        const float a = std::fabs(ol) > std::fabs(orr) ? std::fabs(ol) : std::fabs(orr);
        if (a > outPk) outPk = a;
        if (ringOk) ring_[1][w & ringMask_] = 0.5f * (ol + orr);
        ++w;
    }
    ringW_.store(w, std::memory_order_release);

    // Sleep tracking: silent in + silent out accumulates the hangover.
    if (inSilent && outPk <= 1.0e-5f) quietSamples_ += (uint64_t)frames;
    else
        quietSamples_ = 0;
    idle_ = quietSamples_ > kIdleSamples;

    const double blockS = frames > 0 ? frames / sr : 0.0;
    if (resetPk_.exchange(false, std::memory_order_acquire)) inPk_ = outPk_ = 0.0f;
    const float fall = (float)std::exp(-blockS / 0.3);
    inPk_ = std::max(inPk, inPk_ * fall);
    outPk_ = std::max(outPk, outPk_ * fall);
    inPkA_.store(db(inPk_), std::memory_order_relaxed);
    outPkA_.store(db(outPk_), std::memory_order_relaxed);
    autoA_.store(autoOn ? (float)autoDb_ : 0.0f, std::memory_order_relaxed);
    if (frames > 0) {
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        cpuS_ = cpuS_ * 0.9 + (el / blockS) * 0.1;
        cpuA_.store((float)cpuS_, std::memory_order_relaxed);
    }
}

void Eq::setBand(int b, float on, int type, float freq, float gain, float q) {
    p_[b * kPerBand + On].store(on);
    p_[b * kPerBand + Type].store(static_cast<float>(type));
    p_[b * kPerBand + Freq].store(freq);
    p_[b * kPerBand + Gain].store(gain);
    p_[b * kPerBand + Q].store(q);
}

const char* Eq::paramName(int32_t i) const {
    if (i == Scale) return "Scale";
    if (i == Output) return "Output";
    if (i == AutoGain) return "Auto Gain";
    if (i == Analyzer) return "Analyzer";
    if (i == AnaRange) return "Analyzer Range";
    if (i == AnaPeak) return "Analyzer Peak";
    if (i == Labels) return "Labels";
    static thread_local char buf[16];
    if (i >= SlopeBase && i < ChannelBase) {
        std::snprintf(buf, sizeof(buf), "%d Slope", i - SlopeBase + 1);
        return buf;
    }
    if (i >= ChannelBase && i < Scale) {
        std::snprintf(buf, sizeof(buf), "%d Channel", i - ChannelBase + 1);
        return buf;
    }
    if (i < 0 || i >= kBandParams) return "";
    const int b = i / kPerBand, f = i % kPerBand;
    const char* fld = f == On ? "On" : f == Type ? "Type" : f == Freq ? "Freq" : f == Gain ? "Gain" : "Q";
    std::snprintf(buf, sizeof(buf), "%d %s", b + 1, fld);
    return buf;
}

float Eq::paramMin(int32_t i) const {
    if (i == Output) return -12.0f;
    if (i < 0 || i >= kBandParams) return 0.0f; // Slope, Channel, Scale, Auto Gain, Analyzer…
    switch (i % kPerBand) {
    case On: return 0.0f;
    case Type: return 0.0f;
    case Freq: return 20.0f;
    case Gain: return -18.0f;
    default: return 0.1f;
    }
}

float Eq::paramMax(int32_t i) const {
    if (i >= SlopeBase && i < ChannelBase) return 2.0f;
    if (i >= ChannelBase && i < Scale) return kNumChannels - 1;
    if (i == Scale) return 200.0f;
    if (i == Output) return 12.0f;
    if (i == AutoGain) return 1.0f;
    if (i == Analyzer) return 2.0f;
    if (i == AnaRange) return 2.0f;
    if (i == AnaPeak) return 1.0f;
    if (i == Labels) return 1.0f;
    switch (i % kPerBand) {
    case On: return 1.0f;
    case Type: return kNumTypes - 1;
    case Freq: return 20000.0f;
    case Gain: return 18.0f;
    default: return 18.0f;
    }
}

float Eq::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Eq::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams && std::isfinite(v))
        p_[i].store(std::clamp(v, paramMin(i), paramMax(i)), std::memory_order_relaxed);
    wake();
}

int32_t Eq::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    float t[kTele] = {};
    t[S_InPeak] = inPkA_.load(std::memory_order_relaxed);
    t[S_OutPeak] = outPkA_.load(std::memory_order_relaxed);
    t[S_AutoGain] = autoA_.load(std::memory_order_relaxed);
    t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    t[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
    t[S_Latency] = 0.0f;
    t[S_Bands] = (float)kSpec;
    int32_t n = 0;
    if (maxSamples > kTele) {
        std::lock_guard<std::mutex> lock(mx_);
        t[S_Analysed] = analyse() ? 1.0f : 0.0f;
        for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
        for (int s = 0; s < 2; ++s)
            for (int b = 0; b < kSpec && n < maxSamples; ++b) out[n++] = ana_[s].curve()[b];
        const float* pk = ana_[analyzerMode() == AnaPre ? 0 : 1].peak();
        for (int b = 0; b < kSpec && n < maxSamples; ++b) out[n++] = pk[b];
        return n;
    }
    for (int k = 0; k < kTele && n < maxSamples; ++k) out[n++] = t[k];
    return n;
}

void Eq::deviceAction(int32_t id, int32_t /*iarg*/, float /*farg*/) {
    if (id != 0) return;
    std::lock_guard<std::mutex> lock(mx_);
    resetPk_.store(true, std::memory_order_release); // the audio thread drops its holds
    inPkA_.store(-120.0f, std::memory_order_relaxed);
    outPkA_.store(-120.0f, std::memory_order_relaxed);
    lastAnaS_ = -1.0;
    anaValid_ = false;
    for (auto& a : ana_) a.reset();
}

std::string Eq::deviceText(int32_t id) const {
    static const char* types[kNumTypes] = {"Low cut", "Low shelf", "Bell", "Notch", "High shelf", "High cut"};
    static const char* chans[kNumChannels] = {"stereo", "mid", "side", "left", "right"};
    static const char* anas[3] = {"pre", "post", "off"};
    char b[256];
    if (id == 0) {
        int nOn = 0;
        for (int k = 0; k < kBands; ++k)
            if (bandOn(k)) ++nOn;
        std::snprintf(b, sizeof b, "%d bands on - scale %.0f %%", nOn, get(Scale));
        std::string s = b;
        for (int k = 0; k < kBands; ++k) {
            if (!bandOn(k)) continue;
            const int ty = typeOf(k);
            std::snprintf(b, sizeof b, " | B%d %s %.0f Hz", k + 1, types[ty], bp(k, Freq));
            s += b;
            if (hasGain(ty)) {
                std::snprintf(b, sizeof b, " %+.1f dB", bp(k, Gain));
                s += b;
            }
            std::snprintf(b, sizeof b, " Q %.2f", bp(k, Q));
            s += b;
            if (ty == LowCut || ty == HighCut) {
                std::snprintf(b, sizeof b, " %d dB/oct", 12 << slopeOf(k));
                s += b;
            }
            if (channelOf(k) != ChStereo) {
                std::snprintf(b, sizeof b, " %s", chans[channelOf(k)]);
                s += b;
            }
        }
        std::snprintf(b, sizeof b, " | output %+.1f dB%s | analyzer %s", get(Output),
            get(AutoGain) >= 0.5f ? " + auto gain" : "", anas[std::clamp((int)std::lround(get(Analyzer)), 0, 2)]);
        s += b;
        // The user's band labels and notes (specs/049): what each band is for.
        for (int k = 0; k < kBands; ++k) {
            if (label_[k].empty() && note_[k].empty()) continue;
            std::snprintf(b, sizeof b, " | B%d", k + 1);
            s += b;
            if (!label_[k].empty()) s += " label \"" + label_[k] + "\"";
            if (!note_[k].empty()) s += " note \"" + note_[k] + "\"";
        }
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
        std::snprintf(b, sizeof b, "in %s dBFS - out %s dBFS - auto gain %+.1f dB%s - CPU %.2f %%",
            lv(sc[S_InPeak]).c_str(), lv(sc[S_OutPeak]).c_str(), sc[S_AutoGain], get(AutoGain) >= 0.5f ? "" : " (off)",
            sc[S_Cpu] * 100.0f);
        return b;
    }
    if (id == 2) {
        return "Raw units. Band n (1..8) params are named \"n <field>\": at index (n-1)*5 + field On (0/1), Type (0 "
               "Low cut, "
               "1 Low shelf, 2 Bell, 3 Notch, 4 High shelf, 5 High cut), Freq 20..20000 Hz, Gain -18..+18 dB (shelves "
               "and bells "
               "only), Q 0.1..18 (the resonance on the cuts, 0.71 = flat); 40..47 \"n Slope\" 0 = 12, 1 = 24, 2 = 48 "
               "dB/oct "
               "(cuts only); 48..55 \"n Channel\" 0 Stereo, 1 Mid, 2 Side, 3 Left, 4 Right. 56 Scale 0..200 % "
               "multiplies every "
               "shelf and bell gain. 57 Output -12..+12 dB. 58 Auto Gain 0/1 adds the opposite of the average level of "
               "the "
               "shelves and bells (cuts and notches do not count; max +-12 dB). 59 Analyzer 0 Pre, 1 Post, 2 Off "
               "(display only). 60 Analyzer Range 0 = 60, 1 = 90, 2 = 120 dB (display only). 61 Analyzer Peak "
               "0/1 peak hold (display only). 62 Labels 0/1 shows the band labels in the graph (display only). "
               "Band labels and notes are text, not params: set them with the band-text tool.";
    }
    return {};
}

namespace {

// Keep at most `max` UTF-8 code points, drop control characters (line breaks too unless
// `multiline`) and trim a label's surrounding spaces (a blank note is empty). Invalid bytes are dropped.
std::string cleanText(const std::string& in, size_t max, bool multiline) {
    std::string out;
    size_t cps = 0;
    for (size_t i = 0; i < in.size() && cps < max;) {
        const auto c = static_cast<unsigned char>(in[i]);
        const size_t len = c < 0x80 ? 1 : (c >> 5) == 0x6 ? 2 : (c >> 4) == 0xE ? 3 : (c >> 3) == 0x1E ? 4 : 0;
        if (len == 0 || i + len > in.size()) {
            ++i;
            continue;
        }
        bool ok = true;
        for (size_t k = 1; k < len; ++k)
            if ((static_cast<unsigned char>(in[i + k]) & 0xC0) != 0x80) ok = false;
        if (!ok) {
            ++i;
            continue;
        }
        if (len == 1 && (c < 0x20 || c == 0x7F)) { // control characters: only a note keeps line breaks
            i += 1;
            if (c == '\t' || c == '\n') { // a tab, or a line break in a label, reads as a space
                out += multiline && c == '\n' ? '\n' : ' ';
                ++cps;
            }
            continue;
        }
        out.append(in, i, len);
        i += len;
        ++cps;
    }
    const auto first = out.find_first_not_of(" \n");
    if (first == std::string::npos) return {};
    if (multiline) return out; // a note is kept exactly
    const auto last = out.find_last_not_of(" \n");
    return out.substr(first, last - first + 1);
}

} // namespace

void Eq::setBandText(int b, const std::string& label, const std::string& note) {
    if (b < 0 || b >= kBands) return;
    label_[b] = cleanText(label, kLabelMax, false);
    note_[b] = cleanText(note, kNoteMax, true);
}

// "NEQ1", then per band: u16 label length, label bytes, u16 note length, note bytes (UTF-8).
std::vector<uint8_t> Eq::getState() const {
    bool any = false;
    for (int b = 0; b < kBands; ++b) any |= !label_[b].empty() || !note_[b].empty();
    if (!any) return {};
    std::vector<uint8_t> v{'N', 'E', 'Q', '1'};
    auto put = [&v](const std::string& s) {
        const auto n = static_cast<uint16_t>(std::min<size_t>(s.size(), 0xFFFF));
        v.push_back(static_cast<uint8_t>(n & 0xFF));
        v.push_back(static_cast<uint8_t>(n >> 8));
        v.insert(v.end(), s.begin(), s.begin() + n);
    };
    for (int b = 0; b < kBands; ++b) {
        put(label_[b]);
        put(note_[b]);
    }
    return v;
}

void Eq::setState(const uint8_t* data, int32_t size) {
    std::string lab[kBands], nt[kBands];
    if (data && size >= 4 && std::memcmp(data, "NEQ1", 4) == 0) {
        size_t at = 4;
        const auto n = static_cast<size_t>(size);
        auto get = [&](std::string& out) {
            if (at + 2 > n) return false;
            const size_t len = data[at] | (static_cast<size_t>(data[at + 1]) << 8);
            at += 2;
            if (at + len > n) return false;
            out.assign(reinterpret_cast<const char*>(data + at), len);
            at += len;
            return true;
        };
        for (int b = 0; b < kBands; ++b)
            if (!get(lab[b]) || !get(nt[b])) {
                lab[b].clear();
                nt[b].clear();
                break;
            }
    }
    // Anything else (an empty blob, an unknown format) reads as no annotations.
    for (int b = 0; b < kBands; ++b) setBandText(b, lab[b], nt[b]);
}

bool Eq::analyse() const {
    const int mode = analyzerMode();
    if (ring_[0].empty() || mode == AnaOff) return false;
    const double now = nowSeconds();
    if (lastAnaS_ >= 0 && now - lastAnaS_ < 1.0 / 30.0) return anaValid_;
    const double dt = lastAnaS_ < 0 ? 0.0 : now - lastAnaS_;
    const uint32_t w = ringW_.load(std::memory_order_acquire);
    const bool peak = get(AnaPeak) >= 0.5f;
    // Pre shows only the input; Post both, with the peak on the output.
    ana_[0].analyse(ring_[0].data(), ringMask_, w, dt, peak && mode == AnaPre);
    if (mode == AnaPost) ana_[1].analyse(ring_[1].data(), ringMask_, w, dt, peak);
    else ana_[1].reset();
    lastAnaS_ = now;
    anaValid_ = ana_[0].valid() || (mode == AnaPost && ana_[1].valid());
    return anaValid_;
}

} // namespace nota
