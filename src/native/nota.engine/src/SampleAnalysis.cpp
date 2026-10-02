// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "SampleAnalysis.h"
#include "TempoDetect.h"

#include "signalsmith-linear/fft.h"

#include <algorithm>
#include <cmath>
#include <complex>
#include <vector>

namespace nota {

namespace {

// Krumhansl–Kessler probe-tone profiles (1982), tonic first.
constexpr double kMajorProfile[12] = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
constexpr double kMinorProfile[12] = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

constexpr double kPi = 3.14159265358979323846;

constexpr double kKeyLoHz = 50.0;      // chroma band: low enough for a bass line…
constexpr double kKeyHiHz = 2500.0;    // …high enough for the melody's fundamentals
constexpr double kPeakOverLocal = 4.0; // a spectral peak counts as a partial at 4× its neighbourhood
constexpr int    kLocalHalf = 16;      // neighbourhood half-width in bins
constexpr double kMinTonalFrac = 0.04; // partials' share of the band's magnitude; drums sit below it
constexpr double kMinChromaCrest = 1.5;
constexpr double kMinKeyR = 0.5;
constexpr double kModeMargin = 0.04;   // major vs minor on one tonic closer than this = mode unclear

constexpr int    kTimbreFft = 2048;
constexpr double kTimbreSeconds = 10.0;
constexpr int    kBands = 12;
constexpr double kBandLoHz = 40.0, kBandHiHz = 16000.0;

std::vector<float> monoOf(const SampleBuffer& src, int64_t offset, int64_t len) {
    std::vector<float> m(static_cast<size_t>(std::max<int64_t>(0, len)));
    const int ch = std::max(1, src.channels);
    for (int64_t i = 0; i < len; ++i) {
        const float* p = &src.samples[static_cast<size_t>((offset + i) * src.channels)];
        float s = 0.0f;
        for (int c = 0; c < src.channels; ++c) s += p[c];
        m[static_cast<size_t>(i)] = s / static_cast<float>(ch);
    }
    return m;
}

std::vector<float> hann(int n) {
    std::vector<float> w(static_cast<size_t>(n));
    for (int i = 0; i < n; ++i) w[static_cast<size_t>(i)] = 0.5f - 0.5f * static_cast<float>(std::cos(2.0 * kPi * i / n));
    return w;
}

double pearson(const double* a, const double* b, int n) {
    double ma = 0, mb = 0;
    for (int i = 0; i < n; ++i) { ma += a[i]; mb += b[i]; }
    ma /= n; mb /= n;
    double cov = 0, va = 0, vb = 0;
    for (int i = 0; i < n; ++i) {
        const double da = a[i] - ma, db = b[i] - mb;
        cov += da * db; va += da * da; vb += db * db;
    }
    return (va <= 0 || vb <= 0) ? 0.0 : cov / std::sqrt(va * vb);
}

void keyFromMono(const std::vector<float>& mono, double sr, int32_t& tonic, int32_t& mode, float& confidence) {
    tonic = -1; mode = -1; confidence = 0.0f;
    if (mono.empty() || sr <= 0) return;
    const int n = sr > 32000.0 ? 16384 : 8192;   // ~2.7 Hz bins: semitones resolve down to the bass
    const int hop = n / 2;
    const int bins = n / 2;
    const double binHz = sr / n;
    const int kLo = std::max(2, static_cast<int>(std::ceil(kKeyLoHz / binHz)));
    const int kHi = std::min(bins - 2, static_cast<int>(kKeyHiHz / binHz));
    if (kHi <= kLo + 2) return;

    signalsmith::linear::RealFFT<float> fft(static_cast<size_t>(n));
    const auto win = hann(n);
    std::vector<float> time(static_cast<size_t>(n));
    std::vector<std::complex<float>> freq(static_cast<size_t>(bins));
    std::vector<double> mag(static_cast<size_t>(bins)), prefix(static_cast<size_t>(bins) + 1);

    double chroma[12] = {};
    double partials = 0.0, band = 0.0;
    const int64_t len = static_cast<int64_t>(mono.size());
    for (int64_t start = 0; start == 0 || start + n / 4 < len; start += hop) {
        for (int i = 0; i < n; ++i) {
            const int64_t at = start + i;
            time[static_cast<size_t>(i)] = at < len ? mono[static_cast<size_t>(at)] * win[static_cast<size_t>(i)] : 0.0f;
        }
        fft.fft(time.data(), freq.data());
        prefix[0] = 0.0;
        for (int k = 0; k < bins; ++k) {
            mag[static_cast<size_t>(k)] = std::abs(freq[static_cast<size_t>(k)]);
            prefix[static_cast<size_t>(k) + 1] = prefix[static_cast<size_t>(k)] + mag[static_cast<size_t>(k)];
        }
        for (int k = kLo; k <= kHi; ++k) {
            const double m = mag[static_cast<size_t>(k)];
            band += m;
            if (!(m > mag[static_cast<size_t>(k) - 1] && m >= mag[static_cast<size_t>(k) + 1])) continue;
            const int a = std::max(0, k - kLocalHalf), b = std::min(bins - 1, k + kLocalHalf);
            const double local = (prefix[static_cast<size_t>(b) + 1] - prefix[static_cast<size_t>(a)]) / (b - a + 1);
            if (m < kPeakOverLocal * local || m <= 1e-9) continue;
            // Parabolic interpolation on log magnitude for the partial's true frequency.
            const double la = std::log(mag[static_cast<size_t>(k) - 1] + 1e-12), lb = std::log(m + 1e-12),
                         lc = std::log(mag[static_cast<size_t>(k) + 1] + 1e-12);
            const double den = la - 2.0 * lb + lc;
            const double p = std::fabs(den) > 1e-12 ? std::clamp(0.5 * (la - lc) / den, -0.5, 0.5) : 0.0;
            const double hz = (k + p) * binHz;
            const double midi = 69.0 + 12.0 * std::log2(hz / 440.0);
            const int pc = ((static_cast<int>(std::lround(midi)) % 12) + 12) % 12;
            chroma[pc] += m;
            partials += m;
        }
        if (start + n >= len) break;
    }
    if (band <= 1e-9 || partials / band < kMinTonalFrac) return;   // no partials worth naming

    double cmax = 0, cmean = 0;
    for (double c : chroma) { cmax = std::max(cmax, c); cmean += c; }
    cmean /= 12.0;
    if (cmean <= 0 || cmax / cmean < kMinChromaCrest) return;   // spread evenly: noise, clusters

    double rMaj[12], rMin[12], rot[12];
    double best = -2.0; int bestT = 0, bestM = 0;
    for (int t = 0; t < 12; ++t) {
        for (int i = 0; i < 12; ++i) rot[i] = kMajorProfile[(i - t + 12) % 12];
        rMaj[t] = pearson(chroma, rot, 12);
        for (int i = 0; i < 12; ++i) rot[i] = kMinorProfile[(i - t + 12) % 12];
        rMin[t] = pearson(chroma, rot, 12);
        if (rMaj[t] > best) { best = rMaj[t]; bestT = t; bestM = 0; }
        if (rMin[t] > best) { best = rMin[t]; bestT = t; bestM = 1; }
    }
    if (best < kMinKeyR) return;
    tonic = bestT;
    mode = std::fabs(rMaj[bestT] - rMin[bestT]) < kModeMargin ? -1 : bestM;
    confidence = static_cast<float>(std::clamp(best, 0.0, 1.0));
}

// Timbre fingerprint over the first ~10 s (layout of SampleFeatures::timbre):
//   0..11  energy share of 12 log-spaced bands, 40 Hz–16 kHz, in dB / 30
//   12     spectral centroid, log2(Hz / 1000)
//   13     spectral flatness (0 tonal … 1 noise), energy-weighted
//   14     attack: log10(seconds to 90 % of the envelope peak + 1 ms)
//   15     length: log2(seconds analysed + 0.05)
void timbreOf(const std::vector<float>& mono, double sr, SampleFeatures& f) {
    const int64_t len = std::min<int64_t>(static_cast<int64_t>(mono.size()), static_cast<int64_t>(kTimbreSeconds * sr));
    const int n = kTimbreFft, bins = n / 2, hop = n / 2;
    const double binHz = sr / n;
    int bandOf[kTimbreFft / 2];
    const double ratio = std::pow(kBandHiHz / kBandLoHz, 1.0 / kBands);
    for (int k = 0; k < bins; ++k) {
        const double hz = k * binHz;
        bandOf[k] = (hz < kBandLoHz || hz >= kBandHiHz) ? -1
                  : std::min(kBands - 1, static_cast<int>(std::log(hz / kBandLoHz) / std::log(ratio)));
    }

    signalsmith::linear::RealFFT<float> fft(static_cast<size_t>(n));
    const auto win = hann(n);
    std::vector<float> time(static_cast<size_t>(n));
    std::vector<std::complex<float>> freq(static_cast<size_t>(bins));
    double bandPow[kBands] = {};
    double total = 0, centroidNum = 0, flatNum = 0, flatDen = 0;
    for (int64_t start = 0; start == 0 || start + n / 4 < len; start += hop) {
        for (int i = 0; i < n; ++i) {
            const int64_t at = start + i;
            time[static_cast<size_t>(i)] = at < len ? mono[static_cast<size_t>(at)] * win[static_cast<size_t>(i)] : 0.0f;
        }
        fft.fft(time.data(), freq.data());
        double frame = 0, logSum = 0; int counted = 0;
        for (int k = 1; k < bins; ++k) {
            if (bandOf[k] < 0) continue;
            const double p = std::norm(freq[static_cast<size_t>(k)]);
            bandPow[bandOf[k]] += p;
            frame += p;
            centroidNum += p * k * binHz;
            logSum += std::log(p + 1e-20);
            ++counted;
        }
        total += frame;
        if (counted > 0 && frame > 1e-12) {
            const double flat = std::exp(logSum / counted) / (frame / counted);
            flatNum += flat * frame; flatDen += frame;
        }
        if (start + n >= len) break;
    }
    for (int b = 0; b < kBands; ++b)
        f.timbre[static_cast<size_t>(b)] = total > 0 ? static_cast<float>(10.0 * std::log10(bandPow[b] / total + 1e-9) / 30.0) : -3.0f;
    f.timbre[12] = total > 0 ? static_cast<float>(std::log2(std::max(20.0, centroidNum / total) / 1000.0)) : 0.0f;
    f.timbre[13] = flatDen > 0 ? static_cast<float>(flatNum / flatDen) : 1.0f;

    // Envelope at 10 ms: attack, tail ratio, peak / RMS.
    const int64_t eh = std::max<int64_t>(1, static_cast<int64_t>(sr * 0.01));
    const int64_t all = static_cast<int64_t>(mono.size());
    double sumSq = 0, peak = 0, envMax = 0; int64_t envMaxAt = 0;
    std::vector<double> env;
    env.reserve(static_cast<size_t>(all / eh + 1));
    for (int64_t s = 0; s < all; s += eh) {
        double acc = 0;
        const int64_t e = std::min(all, s + eh);
        for (int64_t i = s; i < e; ++i) {
            const double v = mono[static_cast<size_t>(i)];
            acc += v * v; peak = std::max(peak, std::fabs(v));
        }
        sumSq += acc;
        const double rms = std::sqrt(acc / static_cast<double>(e - s));
        env.push_back(rms);
        if (rms > envMax) { envMax = rms; envMaxAt = static_cast<int64_t>(env.size()) - 1; }
    }
    int64_t attackAt = envMaxAt;
    for (int64_t i = 0; i <= envMaxAt; ++i)
        if (env[static_cast<size_t>(i)] >= 0.9 * envMax) { attackAt = i; break; }
    f.timbre[14] = static_cast<float>(std::log10(attackAt * 0.01 + 0.001));
    f.timbre[15] = static_cast<float>(std::log2(all / sr + 0.05));

    const double rmsAll = all > 0 ? std::sqrt(sumSq / static_cast<double>(all)) : 0.0;
    f.peakDb = static_cast<float>(20.0 * std::log10(peak + 1e-6));
    f.rmsDb = static_cast<float>(20.0 * std::log10(rmsAll + 1e-6));
    const int64_t tail = std::max<int64_t>(1, all / 10);
    double tailSq = 0;
    for (int64_t i = all - tail; i < all; ++i) tailSq += static_cast<double>(mono[static_cast<size_t>(i)]) * mono[static_cast<size_t>(i)];
    const double rmsTail = std::sqrt(tailSq / static_cast<double>(tail));
    f.tailRatio = rmsAll > 1e-9 ? static_cast<float>(std::min(4.0, rmsTail / rmsAll)) : 0.0f;
}

} // namespace

void detectKey(const SampleBuffer& src, int64_t offset, int64_t len, double sampleRate,
               int32_t& tonic, int32_t& mode, float& confidence) {
    tonic = -1; mode = -1; confidence = 0.0f;
    if (src.channels <= 0 || sampleRate <= 0) return;
    offset = std::clamp<int64_t>(offset, 0, src.frames);
    len = std::min<int64_t>(len, src.frames - offset);
    if (len <= 0) return;
    keyFromMono(monoOf(src, offset, len), sampleRate, tonic, mode, confidence);
}

SampleFeatures analyzeSample(const SampleBuffer& src, int64_t frames) {
    SampleFeatures f;
    frames = std::min<int64_t>(frames, src.frames);
    const double sr = src.sourceSampleRate;
    if (frames <= 0 || src.channels <= 0 || sr <= 0) return f;
    const auto mono = monoOf(src, 0, frames);
    f.bpm = detectTempo(src, 0, frames, sr);
    keyFromMono(mono, sr, f.keyTonic, f.keyMode, f.keyConfidence);
    timbreOf(mono, sr, f);
    return f;
}

} // namespace nota
