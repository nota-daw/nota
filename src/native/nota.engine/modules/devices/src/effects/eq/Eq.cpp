// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota EQ-8 implementation . Audio path: shared
// dsp::BiquadCascade sections run sample-major (all bands per frame, in double),
// FastMath pitch/dB mapping, auto-sleep on silent stretches. Everything else
// (params, analyzer, blob-irrelevant text) is verbatim.

#include "Eq.h"

#include "nota/dsp/primitives/BiquadCascade.h"
#include "nota/dsp/primitives/SpectrumAnalyzer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <vector>

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
    p_[Analyzer].store(static_cast<float>(AnaPost));
    p_[AnaRange].store(1.0f);
    p_[AnaPeak].store(0.0f);
    p_[Labels].store(1.0f);
    p_[View].store(0.0f); // L
    setSampleRate(44100.0, 0);
}

void Eq::setSampleRate(double sr, int32_t /*maxBlock*/) {
    std::lock_guard<std::mutex> lock(mx_);
    sr_ = sr > 0 ? sr : 44100.0;
    srA_.store(static_cast<float>(sr_), std::memory_order_relaxed);
    for (auto& bd : bands_) bd.reset();
    for (auto& bc : bandCache_) bc = BandCache{};
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
    while (ring < static_cast<size_t>(fftN) * 2) ring <<= 1;
    for (auto& r : ring_) r.assign(ring, 0.0f);
    ringMask_ = static_cast<uint32_t>(ring - 1);
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

int32_t Eq::scopeRead(float* out, int32_t maxSamples) const {
    if (!out || maxSamples <= 0) return 0;
    float t[kTele] = {};
    t[S_InPeak] = inPkA_.load(std::memory_order_relaxed);
    t[S_OutPeak] = outPkA_.load(std::memory_order_relaxed);
    t[S_AutoGain] = autoA_.load(std::memory_order_relaxed);
    t[S_SampleRate] = srA_.load(std::memory_order_relaxed);
    t[S_Cpu] = readScopeCpuLoad();
    t[S_Latency] = 0.0f;
    t[S_Bands] = static_cast<float>(kSpec);
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
    else
        ana_[1].reset();
    lastAnaS_ = now;
    anaValid_ = ana_[0].valid() || (mode == AnaPost && ana_[1].valid());
    return anaValid_;
}

} // namespace nota
