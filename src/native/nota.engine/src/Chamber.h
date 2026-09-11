// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Chamber (device kind 20, mockup "Nota Chamber") — a hybrid reverb: a convolution
// engine and an algorithmic engine side by side, crossfaded by Blend (in parallel, or with
// the convolution feeding the algorithm in Serial).
//
//  · Convolution — sixteen procedurally synthesised impulse responses (halls, rooms,
//    plates, a spring, spaces, FX) or a user file (WAV / FLAC / MP3; mono, stereo or 4-ch
//    true stereo), shaped by Start / Decay trims, an Attack fade-in, Size (time-stretch),
//    Reverse and True Stereo. The engine is zero-latency: a 64-tap direct FIR head, a
//    64-sample uniformly partitioned stage up to one big block, then big-block partitions
//    for the rest (overlap-add over a frequency-domain delay line). Shaping an IR rebuilds
//    its partitions on a worker thread; on the audio thread the new kernel inherits the
//    input history (which doesn't depend on the IR) and crossfades in, so the whole tail
//    morphs without a click. With Zero Latency off the head stages are skipped (less CPU)
//    and the big block's delay is absorbed into the convolution pre-delay.
//  · Algorithm — Dark Hall / Plate / Quartz / Shimmer: an 8-line feedback delay network
//    (Householder feedback) behind a 4-stage Hadamard diffuser, with per-line three-band
//    decay (low / mid / high RT60 around a 250 Hz and a Damping crossover), modulated
//    fractional delays, Freeze (a lossless loop, input muted unless Freeze In), a Vintage
//    colour and a two-grain pitch shifter (Shimmer — fed back into the loop or on top).
//  · Around them: separate pre-delays (free or tempo-synced), a 4-band EQ placed at the
//    input / on the tail / at the output, width + bass mono, input-keyed ducking of the
//    wet signal, Dry/Wet with a dry level, a wet-only (send) switch and output gain.
//
// Params are normalized 0..1 (denormalized in process()); persistence / automation / clone
// flow generically through the base Device, and a user IR's PCM travels in the state blob.
// process() never allocates or locks: kernels arrive through an atomic hand-off and go back
// to the worker thread through a lock-free ring to be freed there.

#pragma once

#include "AudioFile.h"
#include "Device.h"

#include "signalsmith-linear/fft.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace nota {
namespace chamber {

constexpr double kPi = 3.14159265358979323846;

struct Rng {
    uint32_t s;
    explicit Rng(uint32_t seed) : s(seed ? seed : 0x9E3779B9u) {}
    uint32_t next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
    float uni() { return static_cast<float>(next() >> 8) * (1.0f / 16777216.0f); }   // [0, 1)
    float bi() { return uni() * 2.0f - 1.0f; }                                         // [-1, 1)
};

inline double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
inline float onePole(double hz, double sr) { return static_cast<float>(1.0 - std::exp(-2.0 * kPi * std::clamp(hz, 1.0, sr * 0.49) / sr)); }
inline float flush(float x) { return std::fabs(x) < 1e-20f ? 0.0f : x; }
// Transparent below |1|, a tanh knee above (the wet safety net).
inline float softClip(float x) {
    const float a = std::fabs(x);
    if (a <= 1.0f) return x;
    const float y = 1.0f + 0.6f * std::tanh((a - 1.0f) / 0.6f);
    return x < 0.0f ? -y : y;
}
// Loop guard: identity in the normal range, bounded when shimmer / freeze-in overdrives it.
inline float loopLimit(float x) {
    const float a = std::fabs(x);
    if (a <= 2.0f) return x;
    const float y = 2.0f + std::tanh(a - 2.0f);
    return x < 0.0f ? -y : y;
}

// ============================================================================
// Impulse responses
// ============================================================================

struct SourceIr {
    std::vector<float> data;      // interleaved, `channels` per frame
    int channels = 0;
    int64_t frames = 0;
    double sampleRate = 48000.0;
    std::string name, category;
    float seconds() const { return sampleRate > 0.0 ? static_cast<float>(frames / sampleRate) : 0.0f; }
};

enum IrType { IrRoom = 0, IrPlate, IrSpring, IrGated, IrMetal, IrOutdoor };

struct IrSpec {
    const char* name; const char* category;
    float length;                 // generated length (s)
    float rtLow, rtMid, rtHigh;   // RT60 (s) around 125 Hz / 1 kHz / 8 kHz
    float erSpan; int erCount;    // early reflections: window (ms), count
    float buildup;                // echo-density build-up (ms)
    float bright;                 // overall high cut (Hz)
    float cross;                  // true-stereo cross-feed gain (LR / RL)
    float flutter;                // flutter-echo period (ms), 0 = none
    int type; uint32_t seed;
};

constexpr int kNumBuiltinIrs = 16;

inline const IrSpec& irSpec(int i) {
    static const IrSpec specs[kNumBuiltinIrs] = {
        { "Concert Hall · Wide", "HALL",    2.6f, 2.9f, 2.4f, 1.3f,  90.f, 18,  70.f, 12000.f, 0.55f,  0.f, IrRoom,    11 },
        { "Stone Vault",         "HALL",    4.4f, 4.8f, 3.8f, 1.5f, 120.f, 14, 110.f,  7000.f, 0.60f,  0.f, IrRoom,    23 },
        { "Cathedral",           "HALL",    7.5f, 7.8f, 6.4f, 2.6f, 170.f, 12, 170.f,  9000.f, 0.65f,  0.f, IrRoom,    37 },
        { "Scoring Stage",       "HALL",    2.3f, 2.1f, 1.8f, 1.1f,  70.f, 20,  50.f, 14000.f, 0.50f,  0.f, IrRoom,    41 },
        { "Wood Chamber",        "ROOM",    1.6f, 1.5f, 1.2f, 0.7f,  40.f, 16,  25.f, 11000.f, 0.45f,  0.f, IrRoom,    53 },
        { "Live Room",           "ROOM",    1.0f, 0.8f, 0.7f, 0.45f, 30.f, 14,  18.f, 13000.f, 0.40f,  0.f, IrRoom,    61 },
        { "Drum Room",           "ROOM",    0.8f, 0.6f, 0.5f, 0.35f, 22.f, 18,  10.f, 15000.f, 0.40f,  0.f, IrRoom,    67 },
        { "Tiled Bathroom",      "ROOM",    1.3f, 1.0f, 1.1f, 0.9f,  12.f, 20,   6.f, 16000.f, 0.35f,  7.f, IrRoom,    71 },
        { "Vocal Plate",         "PLATE",   2.8f, 2.2f, 2.4f, 1.8f,   0.f,  0,   3.f, 16000.f, 0.70f,  0.f, IrPlate,   79 },
        { "Bright Plate",        "PLATE",   2.0f, 1.3f, 1.6f, 1.5f,   0.f,  0,   2.f, 18000.f, 0.70f,  0.f, IrPlate,   83 },
        { "Spring Tank",         "SPRING",  2.6f, 2.0f, 2.2f, 1.2f,   0.f,  0,   0.f,  5200.f, 0.30f,  0.f, IrSpring,  89 },
        { "Car Park",            "SPACE",   3.6f, 3.2f, 2.9f, 1.5f,  80.f, 10,  60.f,  9000.f, 0.50f, 21.f, IrRoom,    97 },
        { "Stairwell",           "SPACE",   3.2f, 2.6f, 2.7f, 1.9f,  60.f, 24,  40.f, 12000.f, 0.45f, 13.f, IrRoom,   101 },
        { "Forest Clearing",     "OUTDOOR", 2.0f, 1.0f, 1.2f, 0.5f, 400.f, 14, 200.f,  8000.f, 0.50f,  0.f, IrOutdoor,103 },
        { "Gated Room",          "FX",      0.7f, 0.35f,0.35f,0.3f,  20.f, 12,   5.f, 14000.f, 0.50f,  0.f, IrGated,  107 },
        { "Metal Tank",          "FX",      3.4f, 2.2f, 3.0f, 2.0f,  30.f,  8,  15.f, 12000.f, 0.50f,  0.f, IrMetal,  109 },
    };
    return specs[std::clamp(i, 0, kNumBuiltinIrs - 1)];
}

// Rooms / halls / plates / gated / outdoor: sparse-to-dense noise (echo density growing with
// t²) split into four bands that each decay at their own RT60, plus discrete early taps.
inline void synthRoom(std::vector<float>& out, const IrSpec& s, double sr, Rng& rng) {
    const int64_t n = static_cast<int64_t>(out.size());
    const float rt[4] = { s.rtLow, s.rtMid, std::sqrt(s.rtMid * s.rtHigh), s.rtHigh };
    float k[4], env[4] = { 1.0f, 1.0f, 1.0f, 1.0f };
    for (int b = 0; b < 4; ++b) k[b] = static_cast<float>(std::exp(-6.907755 / (std::max(0.05f, rt[b]) * sr)));
    const float c1 = onePole(300.0, sr), c2 = onePole(2000.0, sr), c3 = onePole(6000.0, sr);
    float l1 = 0.0f, l2 = 0.0f, l3 = 0.0f;
    const double build = std::max(0.5, static_cast<double>(s.buildup)) * 0.001;
    const double tStart = s.type == IrPlate ? 0.0 : s.erSpan * 0.001 * 0.3;
    const double pMax = s.type == IrOutdoor ? 0.035 : 1.0;
    const double gateT = s.type == IrGated ? s.length * 0.72 : 1e9;
    std::vector<float> fl;
    int flLen = 0, flPos = 0;
    if (s.flutter > 0.0f) { flLen = std::max(1, static_cast<int>(s.flutter * 0.001 * sr)); fl.assign(static_cast<size_t>(flLen), 0.0f); }
    for (int64_t i = 0; i < n; ++i) {
        const double t = static_cast<double>(i) / sr;
        float x = 0.0f;
        if (t >= tStart) {
            const double tt = t - tStart;
            const double p = std::min(pMax, 0.004 + (tt / build) * (tt / build));
            if (rng.uni() < p) x = rng.bi() * 1.7320508f / static_cast<float>(std::sqrt(p));
            x *= static_cast<float>(1.0 - std::exp(-tt / (build * 0.5 + 1e-4)));
        }
        if (flLen > 0) {   // parallel walls: a feedback comb over the noise
            const float y = x + 0.55f * fl[static_cast<size_t>(flPos)];
            fl[static_cast<size_t>(flPos)] = y;
            if (++flPos >= flLen) flPos = 0;
            x = y * 0.6f;
        }
        float y;
        if (s.type == IrGated) {
            const double body = std::exp(-t / (s.rtMid * 2.0));
            y = x * static_cast<float>(t < gateT ? body : std::exp(-gateT / (s.rtMid * 2.0)) * std::exp(-(t - gateT) / 0.012));
        } else {
            l1 += c1 * (x - l1); l2 += c2 * (x - l2); l3 += c3 * (x - l3);
            y = l1 * env[0] + (l2 - l1) * env[1] + (l3 - l2) * env[2] + (x - l3) * env[3];
            for (int b = 0; b < 4; ++b) env[b] *= k[b];
        }
        out[static_cast<size_t>(i)] = y;
    }
    for (int e = 0; e < s.erCount; ++e) {   // early reflections: soft (air-absorbed) taps
        const float frac = (static_cast<float>(e) + rng.uni() * 0.9f) / static_cast<float>(s.erCount);
        const double tk = 0.0015 + s.erSpan * 0.001 * std::pow(static_cast<double>(frac), 1.25);
        const int64_t pos = static_cast<int64_t>(tk * sr);
        if (pos + 2 >= n) continue;
        const float amp = 9.0f * (1.0f - 0.55f * frac) * (0.55f + 0.45f * rng.uni()) * (rng.uni() < 0.5f ? -1.0f : 1.0f);
        out[static_cast<size_t>(pos)] += amp * 0.5f;
        out[static_cast<size_t>(pos + 1)] += amp * 0.3f;
        out[static_cast<size_t>(pos + 2)] += amp * 0.2f;
    }
}

// A spring tank: one recirculating delay per channel with a long first-order allpass chain
// in the loop (the dispersive "boing" chirp), band-limited like the real thing.
inline void synthSpring(std::vector<float>& out, const IrSpec& s, double sr, int c, Rng& rng) {
    const int64_t n = static_cast<int64_t>(out.size());
    const int d = std::max(8, static_cast<int>((31.0 + 4.3 * c) * 0.001 * sr));
    const float g = static_cast<float>(std::pow(10.0, -3.0 * (d / sr) / s.rtMid));
    std::vector<float> line(static_cast<size_t>(d), 0.0f);
    int w = 0;
    constexpr int kAp = 48;
    float apX[kAp] = {}, apY[kAp] = {};
    const float a = 0.72f;
    float lp = 0.0f, hp = 0.0f, hpPrev = 0.0f;
    const float lpC = onePole(4200.0, sr), hpC = static_cast<float>(std::exp(-2.0 * kPi * 140.0 / sr));
    const int64_t splash = static_cast<int64_t>(0.02 * sr);
    for (int64_t i = 0; i < n; ++i) {
        const float x = (i == 0 ? 1.0f : 0.0f) + (i < splash ? rng.bi() * 0.03f : 0.0f);
        const float dv = line[static_cast<size_t>(w)];
        float v = dv;
        for (int k = 0; k < kAp; ++k) { const float y = flush(-a * v + apX[k] + a * apY[k]); apX[k] = v; apY[k] = y; v = y; }
        lp += lpC * (v - lp);
        hp = flush(hpC * (hp + lp - hpPrev)); hpPrev = lp;
        line[static_cast<size_t>(w)] = x + g * hp;
        if (++w >= d) w = 0;
        out[static_cast<size_t>(i)] = dv;
    }
}

// A metal tank: inharmonic decaying modes (recursive phasors) — resonant and clangy.
inline void synthMetal(std::vector<float>& out, const IrSpec& s, double sr, Rng& rng) {
    const int64_t n = static_cast<int64_t>(out.size());
    constexpr int kModes = 56;
    const double attack = 0.002 * sr;
    for (int m = 0; m < kModes; ++m) {
        const double f = 160.0 * std::pow(45.0, static_cast<double>(rng.uni()));
        const double tn = std::clamp((std::log10(f) - 2.1) / 1.8, 0.0, 1.0);
        const double rtm = (s.rtLow + (s.rtHigh - s.rtLow) * tn) * (0.6 + 0.8 * rng.uni());
        const double k = std::exp(-6.907755 / (rtm * sr));
        const double amp = (0.3 + 0.7 * rng.uni()) * 0.27;
        const double ph = rng.uni() * 2.0 * kPi, w0 = 2.0 * kPi * f / sr;
        const double cr = std::cos(w0) * k, ci = std::sin(w0) * k;
        double zr = std::cos(ph) * amp, zi = std::sin(ph) * amp;
        for (int64_t i = 0; i < n; ++i) {
            const double nr = zr * cr - zi * ci; zi = zr * ci + zi * cr; zr = nr;
            out[static_cast<size_t>(i)] += static_cast<float>(zi * (i < attack ? i / attack : 1.0));
            if (std::fabs(zr) + std::fabs(zi) < 1e-7) break;
        }
    }
}

// The 4-channel (LL, LR, RL, RR) true-stereo response of a built-in IR at 48 kHz.
inline std::shared_ptr<SourceIr> synthesizeIr(int idx) {
    const IrSpec& s = irSpec(idx);
    const double sr = 48000.0;
    const int64_t n = static_cast<int64_t>(s.length * sr);
    auto ir = std::make_shared<SourceIr>();
    ir->channels = 4; ir->frames = n; ir->sampleRate = sr; ir->name = s.name; ir->category = s.category;
    ir->data.assign(static_cast<size_t>(n) * 4, 0.0f);
    std::vector<float> ch(static_cast<size_t>(n));
    const float hc = onePole(s.bright, sr);
    for (int c = 0; c < 4; ++c) {
        std::fill(ch.begin(), ch.end(), 0.0f);
        Rng rng(s.seed * 7919u + static_cast<uint32_t>(c) * 104729u + 1u);
        if (s.type == IrSpring) synthSpring(ch, s, sr, c, rng);
        else if (s.type == IrMetal) synthMetal(ch, s, sr, rng);
        else synthRoom(ch, s, sr, rng);
        float a = 0.0f, b = 0.0f;
        const float g = (c == 0 || c == 3) ? 1.0f : s.cross;
        for (int64_t i = 0; i < n; ++i) {
            a += hc * (ch[static_cast<size_t>(i)] - a); b += hc * (a - b);
            ir->data[static_cast<size_t>(i) * 4 + c] = b * g;
        }
    }
    return ir;
}

// Built-in IRs are synthesised once per process and shared by every Chamber instance.
inline std::shared_ptr<const SourceIr> builtinIr(int idx) {
    static std::mutex mu;
    static std::shared_ptr<const SourceIr> cache[kNumBuiltinIrs];
    idx = std::clamp(idx, 0, kNumBuiltinIrs - 1);
    std::lock_guard<std::mutex> lk(mu);
    if (!cache[idx]) cache[idx] = synthesizeIr(idx);
    return cache[idx];
}

// ============================================================================
// Zero-latency partitioned convolution
// ============================================================================

inline void cmac(float* __restrict ar, float* __restrict ai, const float* __restrict xr, const float* __restrict xi,
                 const float* __restrict hr, const float* __restrict hi, int b) {
    ar[0] += xr[0] * hr[0];   // bin 0 packs DC (re) and Nyquist (im): two real products
    ai[0] += xi[0] * hi[0];
    for (int i = 1; i < b; ++i) {
        ar[i] += xr[i] * hr[i] - xi[i] * hi[i];
        ai[i] += xr[i] * hi[i] + xi[i] * hr[i];
    }
}

class ConvKernel {
public:
    struct Path { int in, out, k; };

    double sampleRate = 0.0;
    int latency = 0;             // samples this kernel adds (0 = zero-latency head)
    int block = 0;               // big partition size
    float seconds = 0.0f;        // processed IR length

    // h: processed responses at the engine rate (equal lengths); paths route input → output.
    static std::unique_ptr<ConvKernel> build(const std::vector<std::vector<float>>& h, const std::vector<Path>& paths,
                                             int nIn, bool monoIn, bool zeroLatency, int bigB, double sr) {
        auto k = std::unique_ptr<ConvKernel>(new ConvKernel());
        k->sampleRate = sr; k->block = bigB; k->nIn_ = nIn; k->monoIn_ = monoIn;
        k->nPaths_ = std::min(static_cast<int>(paths.size()), 4);
        for (int p = 0; p < k->nPaths_; ++p) { k->paths_[p] = paths[static_cast<size_t>(p)]; k->used_[paths[static_cast<size_t>(p)].out] = true; }
        const int nK = static_cast<int>(h.size());
        const int len = nK > 0 ? static_cast<int>(h[0].size()) : 0;
        k->seconds = static_cast<float>(len / sr);
        k->scratch_.assign(static_cast<size_t>(kScratch) * 2, 0.0f);
        if (len == 0) return k;
        if (zeroLatency) {
            const int f = std::min(kFir, len);
            k->fir_.init(f, nIn, nK);
            for (int c = 0; c < nK; ++c) k->fir_.setKernel(c, h[static_cast<size_t>(c)].data(), f);
            if (len > kFir) {
                const int segLen = std::min(len, bigB) - kFir;
                k->stages_[k->nStages_++].init(kFir, (segLen + kFir - 1) / kFir, nIn, nK, h, kFir, segLen);
            }
            if (len > bigB)
                k->stages_[k->nStages_++].init(bigB, (len - bigB + bigB - 1) / bigB, nIn, nK, h, bigB, len - bigB);
            k->latency = 0;
        } else {
            k->stages_[k->nStages_++].init(bigB, (len + bigB - 1) / bigB, nIn, nK, h, 0, len);
            k->latency = bigB;
        }
        return k;
    }

    // Convolve n frames, ADDING into outL/outR. A null input means silence (a fading kernel).
    void process(const float* inL, const float* inR, float* outL, float* outR, int n) {
        for (int off = 0; off < n; off += kScratch) {
            const int m = std::min(kScratch, n - off);
            const float* a;
            const float* b;
            if (!inL) { std::fill(scratch_.begin(), scratch_.begin() + m, 0.0f); a = b = scratch_.data(); }
            else if (monoIn_) {
                float* s = scratch_.data();
                for (int i = 0; i < m; ++i) s[i] = (inL[off + i] + inR[off + i]) * 0.70710678f;
                a = b = s;
            } else { a = inL + off; b = inR + off; }
            if (fir_.f > 0) fir_.run(a, b, outL + off, outR + off, m, paths_, nPaths_);
            for (int s = 0; s < nStages_; ++s) stages_[s].run(a, b, outL + off, outR + off, m, paths_, nPaths_, used_);
        }
    }

    void clear() { fir_.clear(); for (int s = 0; s < nStages_; ++s) stages_[s].clear(); }

    // Same partition layout → the input history (FIR ring + frequency-domain delay lines,
    // which depend only on the input, never on the IR) can be handed from one kernel to the
    // next, so a re-shaped IR re-renders the whole tail instead of starting from silence.
    bool compatible(const ConvKernel& o) const {
        if (nIn_ != o.nIn_ || monoIn_ != o.monoIn_ || fir_.f != o.fir_.f || nStages_ != o.nStages_) return false;
        for (int s = 0; s < nStages_; ++s) if (stages_[s].B != o.stages_[s].B) return false;
        return true;
    }
    void transplant(const ConvKernel& o) {
        if (fir_.f > 0) { fir_.hist = o.fir_.hist; fir_.pos = o.fir_.pos; fir_.quiet = o.fir_.quiet; }
        for (int s = 0; s < nStages_; ++s) stages_[s].transplant(o.stages_[s]);
    }
    // Samples until every stage's output is exact after a transplant (partial block + one
    // block for the overlap to be rebuilt with the new IR).
    int warmup() const { int w = 0; for (int s = 0; s < nStages_; ++s) w = std::max(w, 2 * stages_[s].B); return w; }
    bool silent() const {
        if (fir_.f > 0 && fir_.quiet < fir_.f + 1) return false;
        for (int s = 0; s < nStages_; ++s) if (!stages_[s].silent()) return false;
        return true;
    }

private:
    static constexpr int kFir = 64, kScratch = 256;

    struct Fir {
        int f = 0, nIn = 0, nK = 0, pos = 0, quiet = 1 << 30;
        std::vector<float> taps;   // [nK][f], time-reversed
        std::vector<float> hist;   // [nIn][2f] mirrored ring
        void init(int f_, int nIn_, int nK_) {
            f = f_; nIn = nIn_; nK = nK_; pos = 0;
            taps.assign(static_cast<size_t>(nK) * f, 0.0f);
            hist.assign(static_cast<size_t>(nIn) * 2 * f, 0.0f);
        }
        void setKernel(int k, const float* h, int len) {
            for (int t = 0; t < len; ++t) taps[static_cast<size_t>(k) * f + (f - 1 - t)] = h[t];
        }
        void clear() { std::fill(hist.begin(), hist.end(), 0.0f); pos = 0; quiet = 1 << 30; }
        void run(const float* a, const float* b, float* oL, float* oR, int n, const Path* paths, int nPaths) {
            for (int i = 0; i < n; ++i) {
                const float x0 = a[i], x1 = b[i];
                const bool nz = x0 != 0.0f || (nIn > 1 && x1 != 0.0f);
                if (nz) quiet = 0; else if (quiet < (1 << 30)) ++quiet;
                float* h0 = &hist[0];
                h0[pos] = x0; h0[pos + f] = x0;
                if (nIn > 1) { float* h1 = &hist[static_cast<size_t>(2 * f)]; h1[pos] = x1; h1[pos + f] = x1; }
                if (quiet <= f) {
                    for (int p = 0; p < nPaths; ++p) {
                        const float* win = &hist[static_cast<size_t>(paths[p].in) * 2 * f + pos + 1];
                        const float* tp = &taps[static_cast<size_t>(paths[p].k) * f];
                        float acc = 0.0f;
                        for (int t = 0; t < f; ++t) acc += tp[t] * win[t];
                        (paths[p].out == 0 ? oL : oR)[i] += acc;
                    }
                }
                if (++pos >= f) pos = 0;
            }
        }
    };

    // One uniformly partitioned overlap-add stage over IR[offset, offset + P·B); its block
    // latency equals `offset` in the zero-latency layout, so the stages line up exactly.
    struct Stage {
        int B = 0, P = 0, nIn = 0, nK = 0;
        signalsmith::linear::RealFFT<float> fft;
        std::vector<float> Hr, Hi;         // [nK][P][B], pre-scaled by 1/2B
        std::vector<float> Xr, Xi;         // [nIn][P][B] frequency-domain delay line
        std::vector<uint8_t> live;         // [nIn][P] slot holds non-zero input
        std::vector<float> in, out, ovl;   // [nIn][B] · [2][B] · [2][B]
        std::vector<float> time, ar, ai;   // [2B] · [2][B] · [2][B]
        int pos = 0, fill = 0, liveSlots = 0, quietBlocks = 2;

        void init(int b, int p, int nIn_, int nK_, const std::vector<std::vector<float>>& h, int offset, int len) {
            B = b; P = std::max(1, p); nIn = nIn_; nK = nK_;
            fft.resize(static_cast<size_t>(2 * B));
            const size_t spec = static_cast<size_t>(P) * B;
            Hr.assign(spec * nK, 0.0f); Hi.assign(spec * nK, 0.0f);
            Xr.assign(spec * nIn, 0.0f); Xi.assign(spec * nIn, 0.0f);
            live.assign(static_cast<size_t>(P) * nIn, 0);
            in.assign(static_cast<size_t>(nIn) * B, 0.0f);
            out.assign(static_cast<size_t>(2) * B, 0.0f); ovl.assign(static_cast<size_t>(2) * B, 0.0f);
            time.assign(static_cast<size_t>(2) * B, 0.0f);
            ar.assign(static_cast<size_t>(2) * B, 0.0f); ai.assign(static_cast<size_t>(2) * B, 0.0f);
            const float sc = 1.0f / (2.0f * B);
            for (int k = 0; k < nK; ++k) {
                const float* src = h[static_cast<size_t>(k)].data() + offset;
                for (int j = 0; j < P; ++j) {
                    std::fill(time.begin(), time.end(), 0.0f);
                    const int a = j * B, m = std::min(B, len - a);
                    if (m > 0) std::copy(src + a, src + a + m, time.begin());
                    float* hr = &Hr[(static_cast<size_t>(k) * P + j) * B];
                    float* hi = &Hi[(static_cast<size_t>(k) * P + j) * B];
                    fft.fft(time.data(), hr, hi);
                    for (int i = 0; i < B; ++i) { hr[i] *= sc; hi[i] *= sc; }
                }
            }
        }
        void clear() {
            std::fill(live.begin(), live.end(), 0); liveSlots = 0;
            std::fill(in.begin(), in.end(), 0.0f); std::fill(out.begin(), out.end(), 0.0f); std::fill(ovl.begin(), ovl.end(), 0.0f);
            pos = 0; fill = 0; quietBlocks = 2;
        }
        bool silent() const { return liveSlots == 0 && quietBlocks >= 2; }
        void transplant(const Stage& o) {
            fill = o.fill;
            std::copy(o.in.begin(), o.in.end(), in.begin());
            std::fill(out.begin(), out.end(), 0.0f); std::fill(ovl.begin(), ovl.end(), 0.0f);
            std::fill(live.begin(), live.end(), 0);
            pos = 0; liveSlots = 0; quietBlocks = 0;
            const int n = std::min(P, o.P);
            for (int c = 0; c < nIn; ++c)
                for (int j = 0; j < n; ++j) {
                    const int so = ((o.pos - j) % o.P + o.P) % o.P, sn = ((pos - j) % P + P) % P;
                    const uint8_t lv = o.live[static_cast<size_t>(c) * o.P + so];
                    live[static_cast<size_t>(c) * P + sn] = lv;
                    if (!lv) continue;
                    ++liveSlots;
                    const size_t xo = (static_cast<size_t>(c) * o.P + so) * B, xn = (static_cast<size_t>(c) * P + sn) * B;
                    std::copy(o.Xr.begin() + xo, o.Xr.begin() + xo + B, Xr.begin() + xn);
                    std::copy(o.Xi.begin() + xo, o.Xi.begin() + xo + B, Xi.begin() + xn);
                }
        }

        void run(const float* a, const float* b, float* oL, float* oR, int n, const Path* paths, int nPaths, const bool* used) {
            int i = 0;
            while (i < n) {
                const int m = std::min(n - i, B - fill);
                std::copy(a + i, a + i + m, &in[static_cast<size_t>(fill)]);
                if (nIn > 1) std::copy(b + i, b + i + m, &in[static_cast<size_t>(B + fill)]);
                if (used[0]) { const float* s = &out[static_cast<size_t>(fill)]; for (int k = 0; k < m; ++k) oL[i + k] += s[k]; }
                if (used[1]) { const float* s = &out[static_cast<size_t>(B + fill)]; for (int k = 0; k < m; ++k) oR[i + k] += s[k]; }
                fill += m; i += m;
                if (fill == B) { compute(paths, nPaths, used); fill = 0; }
            }
        }

        void compute(const Path* paths, int nPaths, const bool* used) {
            pos = pos + 1 >= P ? 0 : pos + 1;
            for (int c = 0; c < nIn; ++c) {
                const float* ic = &in[static_cast<size_t>(c) * B];
                bool nz = false;
                for (int k = 0; k < B; ++k) if (ic[k] != 0.0f) { nz = true; break; }
                uint8_t& lv = live[static_cast<size_t>(c) * P + pos];
                if (lv) --liveSlots;
                lv = nz ? 1 : 0;
                if (!nz) continue;
                ++liveSlots;
                std::copy(ic, ic + B, time.begin());
                std::fill(time.begin() + B, time.end(), 0.0f);
                const size_t o = (static_cast<size_t>(c) * P + pos) * B;
                fft.fft(time.data(), &Xr[o], &Xi[o]);
            }
            bool any[2] = { false, false };
            if (liveSlots > 0) {
                for (int o = 0; o < 2; ++o) if (used[o]) {
                    std::fill(ar.begin() + o * B, ar.begin() + (o + 1) * B, 0.0f);
                    std::fill(ai.begin() + o * B, ai.begin() + (o + 1) * B, 0.0f);
                }
                for (int p = 0; p < nPaths; ++p) {
                    const Path& ph = paths[p];
                    float* accR = &ar[static_cast<size_t>(ph.out) * B];
                    float* accI = &ai[static_cast<size_t>(ph.out) * B];
                    for (int j = 0; j < P; ++j) {
                        int s = pos - j; if (s < 0) s += P;
                        if (!live[static_cast<size_t>(ph.in) * P + s]) continue;
                        const size_t xo = (static_cast<size_t>(ph.in) * P + s) * B;
                        const size_t ho = (static_cast<size_t>(ph.k) * P + j) * B;
                        cmac(accR, accI, &Xr[xo], &Xi[xo], &Hr[ho], &Hi[ho], B);
                        any[ph.out] = true;
                    }
                }
            }
            for (int o = 0; o < 2; ++o) {
                if (!used[o]) continue;
                float* ob = &out[static_cast<size_t>(o) * B];
                float* ov = &ovl[static_cast<size_t>(o) * B];
                if (any[o]) {
                    fft.ifft(&ar[static_cast<size_t>(o) * B], &ai[static_cast<size_t>(o) * B], time.data());
                    for (int k = 0; k < B; ++k) { ob[k] = time[static_cast<size_t>(k)] + ov[k]; ov[k] = time[static_cast<size_t>(B + k)]; }
                } else {
                    for (int k = 0; k < B; ++k) { ob[k] = ov[k]; ov[k] = 0.0f; }
                }
            }
            if (any[0] || any[1]) quietBlocks = 0; else if (quietBlocks < 2) ++quietBlocks;
        }
    };

    ConvKernel() = default;

    int nIn_ = 2, nPaths_ = 0, nStages_ = 0;
    bool monoIn_ = false;
    bool used_[2] = { false, false };
    Path paths_[4] = {};
    Fir fir_;
    Stage stages_[2];
    std::vector<float> scratch_;
};

struct Shape { float start = 0.0f, end = 1.0f, attackMs = 0.0f, size = 1.0f; bool reverse = false; };

// Trim [start, end] of the source, time-stretch by Size while resampling to the engine rate
// (cubic Hermite), fade the truncated end, optionally reverse, then fade in over Attack.
// Loudness is normalised on the whole source so trims read as trims, not as level jumps.
inline std::vector<std::vector<float>> shapeIr(const SourceIr& src, const std::vector<int>& chans, const Shape& sh,
                                               double sr, double maxSec) {
    std::vector<std::vector<float>> out;
    if (src.frames < 4 || src.channels <= 0) return out;
    const int64_t F = src.frames;
    const double s0 = std::clamp(static_cast<double>(sh.start), 0.0, 0.98) * F;
    const double s1 = std::max(s0 + 0.004 * src.sampleRate, std::clamp(static_cast<double>(sh.end), 0.0, 1.0) * F);
    const double step = (src.sampleRate / sr) / std::clamp(static_cast<double>(sh.size), 0.25, 4.0);
    int64_t m = static_cast<int64_t>((s1 - s0) / step);
    m = std::clamp<int64_t>(m, 1, static_cast<int64_t>(maxSec * sr));
    const bool capped = static_cast<double>(m) * step < (s1 - s0) - 1.0;

    // Normalisation: mean energy of the main (direct) channels over the whole source.
    const int nc = src.channels;
    double e = 0.0; int mains = 0;
    for (int c = 0; c < nc; ++c) {
        if (nc == 4 && (c == 1 || c == 2)) continue;
        double ec = 0.0;
        for (int64_t i = 0; i < F; ++i) { const double v = src.data[static_cast<size_t>(i * nc + c)]; ec += v * v; }
        e += ec; ++mains;
    }
    e /= std::max(1, mains);
    const float norm = e > 1e-12 ? static_cast<float>(0.5 / std::sqrt(e * (sr / src.sampleRate) * sh.size)) : 0.0f;

    auto at = [&](int c, int64_t f) { return (f >= 0 && f < F) ? src.data[static_cast<size_t>(f * nc + c)] : 0.0f; };
    const int64_t fadeOut = (sh.end < 0.999f || capped) ? std::min<int64_t>(m * 3 / 10, static_cast<int64_t>(0.35 * sr))
                                                        : std::min<int64_t>(m / 4, static_cast<int64_t>(0.01 * sr));
    const int64_t fadeIn = std::max<int64_t>(static_cast<int64_t>(sh.attackMs * 0.001 * sr),
                                             (sh.start > 0.0005f || sh.reverse) ? static_cast<int64_t>(0.002 * sr) : 0);
    for (int c : chans) {
        std::vector<float> v(static_cast<size_t>(m));
        for (int64_t i = 0; i < m; ++i) {
            const double pos = s0 + static_cast<double>(i) * step;
            const int64_t k = static_cast<int64_t>(pos);
            const float f = static_cast<float>(pos - static_cast<double>(k));
            const float xm1 = at(c, k - 1), x0 = at(c, k), x1 = at(c, k + 1), x2 = at(c, k + 2);
            const float c1 = 0.5f * (x1 - xm1), c2 = xm1 - 2.5f * x0 + 2.0f * x1 - 0.5f * x2, c3 = 0.5f * (x2 - xm1) + 1.5f * (x0 - x1);
            v[static_cast<size_t>(i)] = (((c3 * f + c2) * f + c1) * f + x0) * norm;
        }
        for (int64_t i = 0; i < fadeOut; ++i) {
            const float g = 0.5f - 0.5f * std::cos(static_cast<float>(kPi) * static_cast<float>(i) / static_cast<float>(fadeOut));
            v[static_cast<size_t>(m - 1 - i)] *= g;
        }
        if (sh.reverse) std::reverse(v.begin(), v.end());
        for (int64_t i = 0; i < fadeIn && i < m; ++i) {
            const float g = static_cast<float>(i) / static_cast<float>(fadeIn);
            v[static_cast<size_t>(i)] *= g * g;
        }
        out.push_back(std::move(v));
    }
    return out;
}

// ============================================================================
// Algorithmic engine
// ============================================================================

struct DLine {
    std::vector<float> b;
    int mask = 0, w = 0;
    void init(int maxLen) { int sz = 16; while (sz < maxLen + 8) sz <<= 1; b.assign(static_cast<size_t>(sz), 0.0f); mask = sz - 1; w = 0; }
    void clear() { std::fill(b.begin(), b.end(), 0.0f); w = 0; }
    void push(float x) { b[static_cast<size_t>(w)] = x; w = (w + 1) & mask; }
    float tap(int d) const { return b[static_cast<size_t>((w - 1 - d) & mask)]; }   // d = 0 → newest
    float lin(float d) const { const int i = static_cast<int>(d); const float f = d - static_cast<float>(i); const float a = tap(i); return a + f * (tap(i + 1) - a); }
    float cubic(float d) const {
        const int i = static_cast<int>(d); const float f = d - static_cast<float>(i);
        const float xm1 = tap(i - 1), x0 = tap(i), x1 = tap(i + 1), x2 = tap(i + 2);
        const float c1 = 0.5f * (x1 - xm1), c2 = xm1 - 2.5f * x0 + 2.0f * x1 - 0.5f * x2, c3 = 0.5f * (x2 - xm1) + 1.5f * (x0 - x1);
        return ((c3 * f + c2) * f + c1) * f + x0;
    }
};

inline void hadamard8(float* x) {
    for (int h = 1; h < 8; h <<= 1)
        for (int i = 0; i < 8; i += h * 2)
            for (int j = i; j < i + h; ++j) { const float a = x[j], b = x[j + h]; x[j] = a + b; x[j + h] = a - b; }
    for (int i = 0; i < 8; ++i) x[i] *= 0.35355339f;
}

// Two crossfaded read heads sweeping a 60 ms window (sin² windows sum to one).
struct PitchShift {
    DLine d; float phase = 0.0f; int W = 2048;
    void init(double sr) { W = std::max(64, static_cast<int>(0.06 * sr)); d.init(W + 8); phase = 0.0f; }
    void clear() { d.clear(); phase = 0.0f; }
    float process(float x, float ratio) {
        d.push(x);
        phase += (1.0f - ratio) / static_cast<float>(W);
        phase -= std::floor(phase);
        float p2 = phase + 0.5f; if (p2 >= 1.0f) p2 -= 1.0f;
        float w1 = std::sin(static_cast<float>(kPi) * phase); w1 *= w1;
        return d.lin(1.0f + phase * (W - 4)) * w1 + d.lin(1.0f + p2 * (W - 4)) * (1.0f - w1);
    }
};

class Algo {
public:
    static constexpr int N = 8;
    struct Params {
        int mode = 0;
        float decay = 3.0f, size = 1.0f, diffuse = 0.7f, damp = 4500.0f, lowMul = 1.0f, highMul = 0.5f;
        float rate = 0.4f, depth = 0.3f, shimAmt = 0.0f, shimRatio = 2.0f;
        bool freeze = false, vintage = false, cubic = false, shimFb = true;
    };

    void prepare(double sr) {
        sr_ = sr;
        for (auto& l : line_) l.init(static_cast<int>(0.5 * sr) + 64);
        for (int s = 0; s < 4; ++s) for (int i = 0; i < N; ++i) diff_[s][i].init(static_cast<int>(0.085 * sr) + 8);
        for (int i = 0; i < N; ++i) ap_[i].init(static_cast<int>(0.012 * sr) + 8);
        shL_.init(sr); shR_.init(sr);
        Rng r(0xC4A3B1u);
        for (int s = 0; s < 4; ++s)
            for (int i = 0; i < N; ++i) { frac_[s][i] = (static_cast<float>(i) + 0.15f + 0.7f * r.uni()) / static_cast<float>(N); flip_[s][i] = r.uni() < 0.5f ? -1.0f : 1.0f; }
        for (int i = 0; i < N; ++i) { const double ph = 2.0 * kPi * i / N; lfoC_[i] = static_cast<float>(std::cos(ph)); lfoS_[i] = static_cast<float>(std::sin(ph)); }
        primed_ = false;
        reset();
    }

    void reset() {
        for (auto& l : line_) l.clear();
        for (int s = 0; s < 4; ++s) for (auto& d : diff_[s]) d.clear();
        for (auto& a : ap_) a.clear();
        shL_.clear(); shR_.clear();
        for (int i = 0; i < N; ++i) { bl1_[i] = bl2_[i] = 0.0f; inj_[i] = 0.0f; }
        vinL_[0] = vinL_[1] = vinR_[0] = vinR_[1] = 0.0f; hcL_ = hcR_ = 0.0f;
        shHpL_ = shHpR_ = shLpL_ = shLpR_ = 0.0f;
    }

    // A clean start (delays re-primed at their targets) — for the offline preview render.
    void restart() { primed_ = false; lfoCos_ = 1.0f; lfoSin_ = 0.0f; reset(); }

    // Tail length the engine is set to (mid band, seconds) — for the UI readout.
    static float modeScale(int mode) { return mode == 1 ? 0.3f : mode == 2 ? 1.6f : mode == 3 ? 1.1f : 1.0f; }

    void process(const float* inL, const float* inR, float* outL, float* outR, int n, const Params& p) {
        const Mode& mc = mode(p.mode);
        const double sr = sr_;
        const float size = std::clamp(p.size, 0.3f, 2.6f);
        float dT[N], ddT[4][N];
        for (int i = 0; i < N; ++i) dT[i] = std::max(4.0f, static_cast<float>(mc.dly[i] * 0.001 * sr * size));
        static const float kStage[4] = { 5.3f, 10.9f, 21.7f, 41.3f };
        const float sScale = mc.diff * std::sqrt(size);
        for (int s = 0; s < 4; ++s)
            for (int i = 0; i < N; ++i) ddT[s][i] = static_cast<float>(kStage[s] * sScale * 0.001 * sr) * frac_[s][i];
        if (!primed_) {   // first block: start at the targets instead of gliding from zero
            for (int i = 0; i < N; ++i) dCur_[i] = dT[i];
            for (int s = 0; s < 4; ++s) for (int i = 0; i < N; ++i) ddCur_[s][i] = ddT[s][i];
            primed_ = true;
        }
        // Three-band loop gains from the RT60s (per line, at its current length).
        const float a1 = onePole(250.0, sr), a2 = onePole(std::max(600.0f, p.damp), sr);
        // RT60s, with an empirical allowance for the loop's interpolation / modulation loss
        // (without it long tails measure ~20 % short).
        auto comp = [](float rt) { return rt * (1.0f + 0.028f * std::min(rt, 20.0f)); };
        const float rtM = comp(std::max(0.1f, p.decay)), rtL = comp(std::max(0.1f, p.decay * p.lowMul)), rtH = comp(std::max(0.05f, p.decay * p.highMul));
        float gL[N], gM[N], gH[N];
        for (int i = 0; i < N; ++i) {
            const double d = (dCur_[i] + kApMs[i] * 0.001 * sr) / sr;
            gL[i] = p.freeze ? 1.0f : std::min(0.9999f, static_cast<float>(std::pow(10.0, -3.0 * d / rtL)));
            gM[i] = p.freeze ? 1.0f : std::min(0.9999f, static_cast<float>(std::pow(10.0, -3.0 * d / rtM)));
            gH[i] = p.freeze ? 1.0f : std::min(0.9999f, static_cast<float>(std::pow(10.0, -3.0 * d / rtH)));
        }
        const float apG = mc.ap * (0.35f + 0.65f * p.diffuse);
        const float dmix = std::clamp(p.diffuse * (p.mode == 2 ? 0.6f : 1.0f), 0.0f, 1.0f);
        const float glide = static_cast<float>(1.0 - std::exp(-1.0 / (0.12 * sr)));
        const float md = p.depth * static_cast<float>(0.0011 * sr) * (p.vintage ? 2.0f : 1.0f);
        const double w0 = 2.0 * kPi * std::clamp(static_cast<double>(p.rate), 0.01, 20.0) / sr;
        const float rc = static_cast<float>(std::cos(w0)), rs = static_cast<float>(std::sin(w0));
        const float hc = onePole(p.vintage ? std::min(mc.hiCut, 8500.0f) : mc.hiCut, sr);
        const float vin = onePole(6500.0, sr);
        const bool shim = p.mode == 3 && p.shimAmt > 0.001f;
        // Shimmer fed back into the loop: keep the pitched loop gain below one — longer tails
        // ring more, so the injection shrinks with √RT; a frozen (lossless) loop takes none.
        const bool shimFb = p.shimFb && !p.freeze;
        const float shimLoop = 0.5f * std::min(1.0f, std::sqrt(2.5f / std::max(0.1f, p.decay)));
        const float shHp = onePole(250.0, sr), shLp = onePole(7000.0, sr);
        static const float sL[N] = { 1, -1, 1, -1, 1, -1, 1, -1 }, sR[N] = { 1, 1, -1, -1, 1, 1, -1, -1 };
        static const float inSign[N] = { 1, 1, -1, 1, -1, -1, 1, -1 };
        const float inG = 0.42f, outG = 0.30f * mc.gain;   // per-mode gain: ~level-matched to the convolution
        const float erG = mc.er * 0.55f;
        int apLen[N];
        for (int i = 0; i < N; ++i) apLen[i] = std::max(1, static_cast<int>(kApMs[i] * 0.001 * sr));

        for (int t = 0; t < n; ++t) {
            float xL = inL[t], xR = inR[t];
            if (p.vintage) {
                vinL_[0] += vin * (xL - vinL_[0]); vinL_[1] += vin * (vinL_[0] - vinL_[1]); xL = vinL_[1];
                vinR_[0] += vin * (xR - vinR_[0]); vinR_[1] += vin * (vinR_[0] - vinR_[1]); xR = vinR_[1];
            }
            float ch[N], direct[N];
            for (int i = 0; i < N; ++i) { ch[i] = ((i & 1) ? xR : xL) * inSign[i]; direct[i] = ch[i]; }
            float eL = 0.0f, eR = 0.0f;
            for (int s = 0; s < 4; ++s) {
                for (int i = 0; i < N; ++i) {
                    ddCur_[s][i] += glide * (ddT[s][i] - ddCur_[s][i]);
                    diff_[s][i].push(ch[i]);
                    ch[i] = diff_[s][i].lin(ddCur_[s][i]);
                }
                hadamard8(ch);
                for (int i = 0; i < N; ++i) ch[i] *= flip_[s][i];
                if (s == 1) { eL = ch[0] + ch[2] + ch[4] + ch[6]; eR = ch[1] + ch[3] + ch[5] + ch[7]; }
            }
            // LFO: one quadrature oscillator, eight phase-offset taps.
            const float nc = lfoCos_ * rc - lfoSin_ * rs; lfoSin_ = lfoSin_ * rc + lfoCos_ * rs; lfoCos_ = nc;
            float y[N], sum = 0.0f;
            for (int i = 0; i < N; ++i) {
                dCur_[i] += glide * (dT[i] - dCur_[i]);
                const float dd = std::max(2.0f, dCur_[i] + md * (lfoSin_ * lfoC_[i] + lfoCos_ * lfoS_[i]));
                float v = p.cubic ? line_[i].cubic(dd - 1.0f) : line_[i].lin(dd - 1.0f);
                // in-loop allpass (g = 0 → a plain short delay, so switching is seamless)
                const float z = ap_[i].tap(apLen[i] - 1);
                const float wv = v + apG * z;
                ap_[i].push(flush(wv));
                v = z - apG * wv;
                // three-band decay
                bl1_[i] += a1 * (v - bl1_[i]); bl2_[i] += a2 * (v - bl2_[i]);
                v = gL[i] * bl1_[i] + gM[i] * (bl2_[i] - bl1_[i]) + gH[i] * (v - bl2_[i]);
                y[i] = v; sum += v;
            }
            sum *= 2.0f / N;
            for (int i = 0; i < N; ++i) {
                float v = y[i] - sum + (direct[i] + (ch[i] - direct[i]) * dmix) * inG + inj_[i];
                if (p.vintage) v = std::floor(v * 8192.0f + 0.5f) * (1.0f / 8192.0f);
                line_[i].push(flush(loopLimit(v)));
            }
            float oL = 0.0f, oR = 0.0f;
            for (int i = 0; i < N; ++i) { oL += y[i] * sL[i]; oR += y[i] * sR[i]; }
            oL *= outG; oR *= outG;
            if (shim) {
                float a = shL_.process(oL, p.shimRatio), b = shR_.process(oR, p.shimRatio);
                shLpL_ += shLp * (a - shLpL_); shHpL_ += shHp * (shLpL_ - shHpL_); a = shLpL_ - shHpL_;
                shLpR_ += shLp * (b - shLpR_); shHpR_ += shHp * (shLpR_ - shHpR_); b = shLpR_ - shHpR_;
                if (shimFb) {
                    const float g = p.shimAmt * shimLoop;
                    for (int i = 0; i < N; ++i) inj_[i] = ((i & 1) ? b : a) * g * inSign[i];
                    oL += a * p.shimAmt * 0.35f; oR += b * p.shimAmt * 0.35f;
                } else {
                    for (int i = 0; i < N; ++i) inj_[i] = 0.0f;
                    oL += a * p.shimAmt; oR += b * p.shimAmt;
                }
            } else {
                for (int i = 0; i < N; ++i) inj_[i] = 0.0f;
            }
            oL += eL * erG; oR += eR * erG;
            hcL_ += hc * (oL - hcL_); hcR_ += hc * (oR - hcR_);
            outL[t] = hcL_; outR[t] = hcR_;
        }
        const float mag = std::sqrt(lfoCos_ * lfoCos_ + lfoSin_ * lfoSin_);   // keep the phasor on the unit circle
        if (mag > 1e-6f) { lfoCos_ /= mag; lfoSin_ /= mag; } else { lfoCos_ = 1.0f; lfoSin_ = 0.0f; }
    }

private:
    struct Mode { float dly[N]; float diff, ap, er, hiCut, gain; };
    static const Mode& mode(int m) {
        static const Mode cfg[4] = {
            { { 43.1f, 49.7f, 55.3f, 62.9f, 69.4f, 77.9f, 85.1f, 93.7f }, 1.00f, 0.50f, 0.20f,  9000.0f, 2.40f },   // Dark Hall
            { {  9.7f, 12.1f, 14.9f, 17.3f, 20.3f, 23.9f, 27.1f, 31.3f }, 0.40f, 0.62f, 0.00f, 18000.0f, 1.60f },   // Plate
            { { 57.1f, 68.3f, 79.9f, 91.7f,107.3f,121.9f,139.1f,157.7f }, 0.55f, 0.00f, 0.50f, 15000.0f, 1.45f },   // Quartz
            { { 47.3f, 56.1f, 63.7f, 71.9f, 81.3f, 89.9f, 98.3f,109.1f }, 1.15f, 0.55f, 0.12f, 14000.0f, 2.20f },   // Shimmer
        };
        return cfg[std::clamp(m, 0, 3)];
    }
    static constexpr float kApMs[N] = { 3.1f, 4.7f, 6.3f, 7.9f, 3.9f, 5.5f, 7.1f, 8.7f };

    double sr_ = 48000.0;
    bool primed_ = false;
    DLine line_[N], diff_[4][N], ap_[N];
    PitchShift shL_, shR_;
    float dCur_[N] = {}, ddCur_[4][N] = {}, frac_[4][N] = {}, flip_[4][N] = {};
    float bl1_[N] = {}, bl2_[N] = {}, inj_[N] = {};
    float lfoC_[N] = {}, lfoS_[N] = {}, lfoCos_ = 1.0f, lfoSin_ = 0.0f;
    float vinL_[2] = {}, vinR_[2] = {}, hcL_ = 0.0f, hcR_ = 0.0f;
    float shHpL_ = 0.0f, shHpR_ = 0.0f, shLpL_ = 0.0f, shLpR_ = 0.0f;
};

// ============================================================================
// Small helpers: biquad EQ
// ============================================================================

struct Biquad {
    float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
    float z1[2] = {}, z2[2] = {};
    void clear() { z1[0] = z1[1] = z2[0] = z2[1] = 0.0f; }
    float run(int c, float x) {
        const float y = b0 * x + z1[c];
        z1[c] = flush(b1 * x - a1 * y + z2[c]);
        z2[c] = flush(b2 * x - a2 * y);
        return y;
    }
    void set(double B0, double B1, double B2, double A0, double A1, double A2) {
        b0 = static_cast<float>(B0 / A0); b1 = static_cast<float>(B1 / A0); b2 = static_cast<float>(B2 / A0);
        a1 = static_cast<float>(A1 / A0); a2 = static_cast<float>(A2 / A0);
    }
    void hp(double f, double sr) { const double w = 2 * kPi * f / sr, c = std::cos(w), al = std::sin(w) / (2 * 0.7071); set((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al); }
    void lp(double f, double sr) { const double w = 2 * kPi * f / sr, c = std::cos(w), al = std::sin(w) / (2 * 0.7071); set((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al); }
    void lowShelf(double f, double db, double sr) {
        const double A = std::pow(10.0, db / 40.0), w = 2 * kPi * f / sr, c = std::cos(w), al = std::sin(w) / 2 * std::sqrt(2.0), sa = 2 * std::sqrt(A) * al;
        set(A * ((A + 1) - (A - 1) * c + sa), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sa),
            (A + 1) + (A - 1) * c + sa, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sa);
    }
    void highShelf(double f, double db, double sr) {
        const double A = std::pow(10.0, db / 40.0), w = 2 * kPi * f / sr, c = std::cos(w), al = std::sin(w) / 2 * std::sqrt(2.0), sa = 2 * std::sqrt(A) * al;
        set(A * ((A + 1) + (A - 1) * c + sa), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sa),
            (A + 1) - (A - 1) * c + sa, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sa);
    }
};

} // namespace chamber

// ============================================================================
// The device
// ============================================================================

class Chamber final : public Device {
public:
    enum {
        Blend = 0, DryWet, ConvOn, AlgoOn, DryLevel, Routing,
        IrSelect, IrStart, IrDecay, IrAttack, IrSize, ConvPredelay, ConvSync, IrReverse, IrTrueStereo,
        AlgoMode, AlgoDecay, AlgoSize, AlgoDiffusion, AlgoDamping, AlgoPredelay, AlgoSync, AlgoLowDecay, AlgoHighDecay,
        Freeze, FreezeIn, AlgoVintage, ModRate, ModDepth, ShimmerAmount, ShimmerPitch, ShimmerFeedback,
        EqLowCut, EqLowGain, EqHighShelf, EqHighCut, EqPosition,
        DuckAmount, DuckRelease, Output, Width, BassMono, Quality, WetOnly, ZeroLatency,
        kNumParams
    };
    // Packed scope telemetry (lock-free, UI-polled).
    enum { S_WetL = 0, S_WetR, S_OutL, S_OutR, S_DuckDb, S_Cpu, S_Latency, S_SampleRate, S_Building,
           S_IrSeconds, S_IrRate, S_IrChannels, S_KernelSeconds, S_Bpm, S_Frozen, S_Block, S_UserIr, S_InPeak, S_Kernels,
           S_ResultSeconds, S_PreviewSeconds, S_PreviewGen, kScope };
    // deviceText ids.
    enum { T_IrName = 0, T_IrCategory = 1, T_UserName = 2, T_IrList = 10 };

    static constexpr int kIrCount = chamber::kNumBuiltinIrs + 1;   // the last slot = the user IR
    static constexpr int kSyncDivs = 13;

    Chamber() {
        static const float def[kNumParams] = {
            0.5f, 0.35f, 1.0f, 1.0f, 0.70710678f, 0.0f,            // blend · dry/wet · conv · algo · dry level · routing
            0.0f, 0.0f, 1.0f, 0.0f, 0.5f, 0.2f, 0.0f, 0.0f, 1.0f,  // IR · start · decay · attack · size · predelay · sync · reverse · true stereo
            0.0f, 0.588f, 0.5f, 0.7f, 0.613f, 0.2f, 0.0f, 0.6f, 0.25f,   // mode · decay · size · diffusion · damping · predelay · sync · low · high
            0.0f, 0.0f, 0.0f, 0.41f, 0.3f, 0.5f, 1.0f, 1.0f,       // freeze · freeze in · vintage · rate · depth · shimmer · pitch · feedback
            0.0f, 0.5f, 0.5f, 1.0f, 0.5f,                          // EQ low cut · low gain · high shelf · high cut · position
            0.0f, 0.548f, 0.6666667f, 0.5f, 0.0f, 0.5f, 0.0f, 1.0f // duck · release · output · width · bass mono · quality · wet only · zero latency
        };
        for (int i = 0; i < kNumParams; ++i) p_[i].store(def[i], std::memory_order_relaxed);
    }
    ~Chamber() override {
        stopWorker();
        delete ready_.exchange(nullptr, std::memory_order_acq_rel);
        delete cur_; delete fade_; delete ramp_; delete xfOld_;
        cur_ = fade_ = ramp_ = xfOld_ = nullptr;
        for (auto*& k : spill_) { delete k; k = nullptr; }
        drainRetired();
    }
    Chamber(const Chamber&) = delete;
    Chamber& operator=(const Chamber&) = delete;

    // ---- lifecycle ---------------------------------------------------------------
    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        const double nsr = sr > 0 ? sr : 44100.0;
        if (prepared_ && nsr == sr_) return;   // re-prepare at the same rate keeps tails + kernels
        sr_ = nsr; srA_.store(nsr, std::memory_order_relaxed);
        algo_.prepare(sr_);
        for (auto* d : { &convPre_[0], &convPre_[1], &algoPre_[0], &algoPre_[1] }) d->init(static_cast<int>(2.6 * sr_) + 8);
        convPreCur_ = algoPreCur_ = -1.0f;
        for (auto& b : eq_) b.clear();
        duckEnv_ = 0.0f; duckGain_ = 1.0f; msLp1_[0] = msLp1_[1] = 0.0f;
        // Not concurrent with process(): install a kernel for the current params directly.
        delete ready_.exchange(nullptr, std::memory_order_acq_rel);
        delete cur_; delete fade_; delete ramp_; delete xfOld_;
        cur_ = fade_ = ramp_ = xfOld_ = nullptr;
        prepared_ = true;
        rebuild(true);
        startWorker();
    }

    void setTransport(double /*beatStart*/, double spb, bool /*playing*/) override { if (spb > 1.0) spb_ = spb; }

    // ---- DSP ---------------------------------------------------------------------
    void process(float* buf, int32_t frames) override {
        if (!prepared_ || frames <= 0) return;
        const auto t0 = std::chrono::steady_clock::now();
        flushSpill();
        if (!xfOld_) if (auto* k = ready_.exchange(nullptr, std::memory_order_acq_rel)) install(k);

        const double sr = sr_;
        // ---- block parameters ----
        const float blend = get(Blend);
        const float mix = get(DryWet);
        const bool convOn = get(ConvOn) >= 0.5f, algoOn = get(AlgoOn) >= 0.5f;
        const float dryLvl = 2.0f * get(DryLevel) * get(DryLevel);
        const bool serial = get(Routing) >= 0.5f;
        const bool freeze = get(Freeze) >= 0.5f, freezeIn = get(FreezeIn) >= 0.5f;
        const bool wetOnly = get(WetOnly) >= 0.5f;
        const int eqPos = std::clamp(static_cast<int>(std::lround(get(EqPosition) * 2.0f)), 0, 2);
        const float outGain = std::pow(10.0f, (-24.0f + 36.0f * get(Output)) / 20.0f);
        const float width = get(Width) * 2.0f;
        const float monoV = get(BassMono);
        const float monoC = monoV > 0.001f ? chamber::onePole(chamber::expMap(monoV, 30.0, 500.0), sr) : 0.0f;
        const float duckDb = get(DuckAmount) * 24.0f;
        const float duckRel = static_cast<float>(std::exp(-1.0 / (chamber::expMap(get(DuckRelease), 20.0, 2000.0) * 0.001 * sr)));
        const float duckAtk = static_cast<float>(std::exp(-1.0 / (0.003 * sr)));
        const float gc = std::sqrt(1.0f - blend), ga = std::sqrt(blend);
        const float wetMix = wetOnly ? 1.0f : std::sin(mix * static_cast<float>(chamber::kPi) * 0.5f);
        const float dryMix = wetOnly ? 0.0f : std::cos(mix * static_cast<float>(chamber::kPi) * 0.5f) * dryLvl;
        const float sm = static_cast<float>(1.0 - std::exp(-1.0 / (0.015 * sr)));
        const float preGlide = static_cast<float>(1.0 - std::exp(-1.0 / (0.03 * sr)));
        const int lat = cur_ ? cur_->latency : 0;
        const float convPreT = std::max(0.0f, predelaySamples(ConvPredelay, ConvSync) - static_cast<float>(lat));
        const float algoPreT = predelaySamples(AlgoPredelay, AlgoSync);
        if (convPreCur_ < 0.0f) convPreCur_ = convPreT;
        if (algoPreCur_ < 0.0f) algoPreCur_ = algoPreT;
        updateEq();

        const chamber::Algo::Params ap = algoParams();

        // Engines on/off ramp their contribution; a fully-off engine stops and forgets its tail.
        if (convOn && !convActive_) { convActive_ = true; clearConv(); }
        if (algoOn && !algoActive_) { algoActive_ = true; algo_.reset(); }

        float wetPk = 0.0f, outPk[2] = { 0.0f, 0.0f }, inPk = 0.0f;
        for (int32_t off = 0; off < frames; off += kChunk) {
            const int n = std::min<int>(kChunk, frames - off);
            float* b = buf + static_cast<size_t>(off) * 2;
            // dry + ducking detector + the (freeze-gated) engine feed
            for (int i = 0; i < n; ++i) {
                const float l = b[i * 2], r = b[i * 2 + 1];
                dL_[i] = l; dR_[i] = r;
                const float a = std::max(std::fabs(l), std::fabs(r));
                inPk = std::max(inPk, a);
                duckEnv_ = a > duckEnv_ ? a + duckAtk * (duckEnv_ - a) : a + duckRel * (duckEnv_ - a);
                feedG_ += sm * (((freeze && !freezeIn) ? 0.0f : 1.0f) - feedG_);
                fL_[i] = l * feedG_; fR_[i] = r * feedG_;
            }
            if (eqPos == 0) runEq(fL_, fR_, n);

            // ---- convolution ----
            std::fill(cL_, cL_ + n, 0.0f); std::fill(cR_, cR_ + n, 0.0f);
            if (convActive_) {
                for (int i = 0; i < n; ++i) {
                    convPreCur_ += preGlide * (convPreT - convPreCur_);
                    convPre_[0].push(fL_[i]); convPre_[1].push(fR_[i]);
                    tL_[i] = convPre_[0].lin(convPreCur_); tR_[i] = convPre_[1].lin(convPreCur_);
                }
                if (xfOld_) {   // re-shaped IR: both kernels hear the input; hold the old until
                                // the new one's output is exact, then an equal-power crossfade
                    std::fill(xL_, xL_ + n, 0.0f); std::fill(xR_, xR_ + n, 0.0f);
                    xfOld_->process(tL_, tR_, xL_, xR_, n);
                    if (cur_) cur_->process(tL_, tR_, cL_, cR_, n);
                    for (int i = 0; i < n; ++i) {
                        const float a = std::clamp(static_cast<float>(xfPos_++ - xfWarm_) / kXfLen, 0.0f, 1.0f);
                        const float gn = std::sin(a * 1.5707963f), go = std::cos(a * 1.5707963f);
                        cL_[i] = cL_[i] * gn + xL_[i] * go; cR_[i] = cR_[i] * gn + xR_[i] * go;
                    }
                    if (xfPos_ >= xfWarm_ + kXfLen) { retire(xfOld_); xfOld_ = nullptr; }
                } else if (cur_) cur_->process(tL_, tR_, cL_, cR_, n);
                if (fade_) { fade_->process(nullptr, nullptr, cL_, cR_, n); if (fade_->silent()) { retire(fade_); fade_ = nullptr; } }
                if (ramp_) {
                    std::fill(tL_, tL_ + n, 0.0f); std::fill(tR_, tR_ + n, 0.0f);
                    ramp_->process(nullptr, nullptr, tL_, tR_, n);
                    for (int i = 0; i < n; ++i) {
                        const float g = std::max(0.0f, 1.0f - static_cast<float>(rampPos_++) / kRampLen);
                        cL_[i] += tL_[i] * g; cR_[i] += tR_[i] * g;
                    }
                    if (rampPos_ >= kRampLen) { retire(ramp_); ramp_ = nullptr; }
                }
            }

            // ---- algorithm ----
            std::fill(aL_, aL_ + n, 0.0f); std::fill(aR_, aR_ + n, 0.0f);
            if (algoActive_) {
                const bool fromConv = serial && convActive_;
                for (int i = 0; i < n; ++i) {
                    algoPreCur_ += preGlide * (algoPreT - algoPreCur_);
                    algoPre_[0].push(fromConv ? cL_[i] : fL_[i]); algoPre_[1].push(fromConv ? cR_[i] : fR_[i]);
                    tL_[i] = algoPre_[0].lin(algoPreCur_); tR_[i] = algoPre_[1].lin(algoPreCur_);
                }
                // Idle gate: silent input + a tail below −140 dB → skip the tank until sound returns.
                float inPkA = 0.0f;
                for (int i = 0; i < n; ++i) inPkA = std::max(inPkA, std::max(std::fabs(tL_[i]), std::fabs(tR_[i])));
                if (inPkA > 0.0f || freeze || algoQuiet_ < kQuietSamples) {
                    algo_.process(tL_, tR_, aL_, aR_, n, ap);
                    float outPkA = 0.0f;
                    for (int i = 0; i < n; ++i) outPkA = std::max(outPkA, std::max(std::fabs(aL_[i]), std::fabs(aR_[i])));
                    algoQuiet_ = (inPkA == 0.0f && outPkA < 1e-7f) ? algoQuiet_ + n : 0;
                    if (algoQuiet_ >= kQuietSamples) algo_.reset();   // drop sub-audible residue
                }
            }

            // ---- wet: blend → tail EQ → width / bass mono → ducking ----
            for (int i = 0; i < n; ++i) {
                convG_ += sm * ((convOn ? gc : 0.0f) - convG_);
                algoG_ += sm * ((algoOn ? ga : 0.0f) - algoG_);
                wL_[i] = cL_[i] * convG_ + aL_[i] * algoG_;
                wR_[i] = cR_[i] * convG_ + aR_[i] * algoG_;
            }
            if (eqPos == 1) runEq(wL_, wR_, n);
            for (int i = 0; i < n; ++i) {
                const float mid = 0.5f * (wL_[i] + wR_[i]);
                widthS_ += sm * (width - widthS_);
                float side = 0.5f * (wL_[i] - wR_[i]) * widthS_;
                if (monoC > 0.0f) { msLp1_[0] += monoC * (side - msLp1_[0]); msLp1_[1] += monoC * (msLp1_[0] - msLp1_[1]); side -= msLp1_[1]; }
                float target = 1.0f;
                if (duckDb > 0.01f) {   // wet dips by up to Duck Amount as the input rises −42 → −12 dBFS
                    const float envDb = 20.0f * std::log10(std::max(1e-6f, duckEnv_));
                    target = std::pow(10.0f, -duckDb * std::clamp((envDb + 42.0f) / 30.0f, 0.0f, 1.0f) / 20.0f);
                }
                duckGain_ += sm * (target - duckGain_);
                wetS_ += sm * (wetMix - wetS_);
                dryS_ += sm * (dryMix - dryS_);
                outS_ += sm * (outGain - outS_);
                const float wl = chamber::softClip((mid + side) * duckGain_) * wetS_;
                const float wr = chamber::softClip((mid - side) * duckGain_) * wetS_;
                wetPk = std::max(wetPk, std::max(std::fabs(wl), std::fabs(wr)));
                wL_[i] = wl + dL_[i] * dryS_;
                wR_[i] = wr + dR_[i] * dryS_;
            }
            if (eqPos == 2) runEq(wL_, wR_, n);
            for (int i = 0; i < n; ++i) {
                const float l = wL_[i] * outS_, r = wR_[i] * outS_;
                b[i * 2] = l; b[i * 2 + 1] = r;
                outPk[0] = std::max(outPk[0], std::fabs(l)); outPk[1] = std::max(outPk[1], std::fabs(r));
            }
        }

        // Engines that finished ramping out stop (and drop their tails).
        if (!convOn && convActive_ && convG_ < 1e-4f) { convActive_ = false; convG_ = 0.0f; clearConv(); }
        if (!algoOn && algoActive_ && algoG_ < 1e-4f) { algoActive_ = false; algoG_ = 0.0f; algo_.reset(); }

        // ---- meters + CPU ----
        const float dec = static_cast<float>(std::exp(-frames / (0.3 * sr)));
        auto hold = [&](std::atomic<float>& a, float v) { a.store(std::max(v, a.load(std::memory_order_relaxed) * dec), std::memory_order_relaxed); };
        hold(mWet_, wetPk); hold(mOutL_, outPk[0]); hold(mOutR_, outPk[1]); hold(mIn_, inPk);
        mDuck_.store(-20.0f * std::log10(std::max(1e-6f, duckGain_)), std::memory_order_relaxed);
        latA_.store(cur_ ? cur_->latency : 0, std::memory_order_relaxed);
        kernA_.store((cur_ ? 1 : 0) + (fade_ ? 1 : 0) + (ramp_ ? 1 : 0) + (xfOld_ ? 1 : 0), std::memory_order_relaxed);
        kernSecA_.store(cur_ ? cur_->seconds : 0.0f, std::memory_order_relaxed);
        bpmA_.store(static_cast<float>(60.0 * sr / spb_), std::memory_order_relaxed);
        const double el = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
        cpuS_ = cpuS_ * 0.9 + (el / (frames / sr)) * 0.1;
        cpuA_.store(static_cast<float>(cpuS_), std::memory_order_relaxed);
    }

    // ---- identity / params ---------------------------------------------------------
    const char* displayName() const override { return "Nota Chamber"; }
    int32_t     builtinKind() const override { return 20; }
    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        static const char* nm[kNumParams] = {
            "Blend", "Dry/Wet", "Conv On", "Algo On", "Dry Level", "Routing",
            "IR", "IR Start", "IR Decay", "IR Attack", "IR Size", "Conv Predelay", "Conv Sync", "IR Reverse", "IR True Stereo",
            "Algo Mode", "Algo Decay", "Algo Size", "Algo Diffusion", "Algo Damping", "Algo Predelay", "Algo Sync", "Algo Low Decay", "Algo High Decay",
            "Freeze", "Freeze In", "Algo Vintage", "Mod Rate", "Mod Depth", "Shimmer Amount", "Shimmer Pitch", "Shimmer Feedback",
            "EQ Low Cut", "EQ Low Gain", "EQ High Shelf", "EQ High Cut", "EQ Position",
            "Duck Amount", "Duck Release", "Output", "Width", "Bass Mono", "Quality", "Wet Only", "Zero Latency" };
        return (i >= 0 && i < kNumParams) ? nm[i] : "";
    }
    float paramMin(int32_t) const override { return 0.0f; }
    float paramMax(int32_t) const override { return 1.0f; }
    float getParam(int32_t i) const override { return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f; }
    void  setParam(int32_t i, float v) override {
        if (i < 0 || i >= kNumParams) return;
        p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

    // ---- telemetry ---------------------------------------------------------------
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples < kScope) return 0;
        std::shared_ptr<const chamber::SourceIr> src;
        { std::lock_guard<std::mutex> lk(srcMu_); src = shownSrc_; }
        out[S_WetL] = mWet_.load(std::memory_order_relaxed);
        out[S_WetR] = out[S_WetL];
        out[S_OutL] = mOutL_.load(std::memory_order_relaxed);
        out[S_OutR] = mOutR_.load(std::memory_order_relaxed);
        out[S_DuckDb] = mDuck_.load(std::memory_order_relaxed);
        out[S_Cpu] = cpuA_.load(std::memory_order_relaxed);
        out[S_Latency] = static_cast<float>(latA_.load(std::memory_order_relaxed));
        out[S_SampleRate] = static_cast<float>(srA_.load(std::memory_order_relaxed));
        out[S_Building] = (pending_.load(std::memory_order_relaxed) || ready_.load(std::memory_order_relaxed)) ? 1.0f : 0.0f;
        out[S_IrSeconds] = src ? src->seconds() : 0.0f;
        out[S_IrRate] = src ? static_cast<float>(src->sampleRate) : 0.0f;
        out[S_IrChannels] = src ? static_cast<float>(src->channels) : 0.0f;
        out[S_KernelSeconds] = kernSecA_.load(std::memory_order_relaxed);
        out[S_Bpm] = bpmA_.load(std::memory_order_relaxed);
        out[S_Frozen] = get(Freeze) >= 0.5f ? 1.0f : 0.0f;
        out[S_Block] = static_cast<float>(bigBlock(srA_.load(std::memory_order_relaxed)));
        { std::lock_guard<std::mutex> lk(srcMu_); out[S_UserIr] = userIr_ ? 1.0f : 0.0f; }
        out[S_InPeak] = mIn_.load(std::memory_order_relaxed);
        out[S_Kernels] = static_cast<float>(kernA_.load(std::memory_order_relaxed));
        { std::lock_guard<std::mutex> lk(srcMu_); out[S_ResultSeconds] = convEnvSec_; out[S_PreviewSeconds] = algoEnvSec_; }
        out[S_PreviewGen] = static_cast<float>(previewGen_.load(std::memory_order_relaxed) % 1000000u);
        return kScope;
    }

    // Peak envelopes for the card: layer 0 / 1 = the source IR left (LL) / right (RR) over its
    // full length; layer 2 = the processed IR as convolved (S_ResultSeconds long); layer 3 = the
    // algorithm's rendered impulse response (S_PreviewSeconds long). All normalised to 1.
    int32_t layerWave(int32_t layer, float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        if (layer == 2 || layer == 3) {
            std::lock_guard<std::mutex> lk(srcMu_);
            const auto& e = layer == 2 ? convEnv_ : algoEnv_;
            if (e.empty()) return 0;
            const size_t n = e.size();
            for (int32_t k = 0; k < maxSamples; ++k) {
                const size_t a = n * static_cast<size_t>(k) / static_cast<size_t>(maxSamples);
                const size_t z = std::max(a + 1, n * static_cast<size_t>(k + 1) / static_cast<size_t>(maxSamples));
                float pk = 0.0f;
                for (size_t i = a; i < z && i < n; ++i) pk = std::max(pk, e[i]);
                out[k] = pk;
            }
            return maxSamples;
        }
        std::shared_ptr<const chamber::SourceIr> src;
        { std::lock_guard<std::mutex> lk(srcMu_); src = shownSrc_; }
        if (!src || src->frames <= 0) return 0;
        const int nc = src->channels;
        const int c = layer <= 0 ? 0 : (nc >= 4 ? 3 : nc >= 2 ? 1 : 0);
        const int64_t F = src->frames;
        float peak = 1e-9f;
        for (int64_t i = 0; i < F; ++i) peak = std::max(peak, std::fabs(src->data[static_cast<size_t>(i * nc + c)]));
        for (int32_t k = 0; k < maxSamples; ++k) {
            const int64_t a = F * k / maxSamples, z = std::max(a + 1, F * (k + 1) / maxSamples);
            float pk = 0.0f;
            for (int64_t i = a; i < z && i < F; ++i) pk = std::max(pk, std::fabs(src->data[static_cast<size_t>(i * nc + c)]));
            out[k] = pk / peak;
        }
        return maxSamples;
    }

    std::string deviceText(int32_t id) const override {
        if (id == T_IrList) {
            std::string s;
            for (int i = 0; i < chamber::kNumBuiltinIrs; ++i) {
                const auto& sp = chamber::irSpec(i);
                char len[32]; std::snprintf(len, sizeof(len), "%.1f", static_cast<double>(sp.length));
                s += sp.name; s += '\t'; s += sp.category; s += '\t'; s += len; s += '\n';
            }
            return s;
        }
        std::lock_guard<std::mutex> lk(srcMu_);
        if (id == T_UserName) return userIr_ ? userIr_->name : std::string{};
        const int ir = irIndex();
        if (id == T_IrName) {
            if (ir < chamber::kNumBuiltinIrs) return chamber::irSpec(ir).name;
            return userIr_ ? userIr_->name : std::string("User IR — none loaded");
        }
        if (id == T_IrCategory) return ir < chamber::kNumBuiltinIrs ? chamber::irSpec(ir).category : "USER";
        return {};
    }

    // ---- user impulse response -------------------------------------------------------
    bool loadFile(const std::string& path) override {
        auto sb = decodeAudioFile(path);
        if (!sb || sb->empty()) return false;
        const int inCh = sb->channels;
        const int ch = inCh >= 4 ? 4 : inCh >= 2 ? 2 : 1;
        float peak = 0.0f;
        for (float v : sb->samples) peak = std::max(peak, std::fabs(v));
        if (peak <= 1e-9f) return false;
        int64_t first = 0;   // skip leading silence (below −60 dB of the peak)
        while (first < sb->frames) {
            bool hit = false;
            for (int c = 0; c < inCh; ++c) if (std::fabs(sb->samples[static_cast<size_t>(first * inCh + c)]) > peak * 1e-3f) { hit = true; break; }
            if (hit) break;
            ++first;
        }
        first = std::max<int64_t>(0, first - 8);
        const double srcSr = sb->sourceSampleRate > 0 ? sb->sourceSampleRate : 44100.0;
        const int64_t frames = std::min<int64_t>(sb->frames - first, static_cast<int64_t>(kMaxUserSec * srcSr));
        if (frames < 8) return false;
        auto ir = std::make_shared<chamber::SourceIr>();
        ir->channels = ch; ir->frames = frames; ir->sampleRate = srcSr; ir->category = "USER";
        std::string nm = path;
        if (auto sl = nm.find_last_of("/\\"); sl != std::string::npos) nm = nm.substr(sl + 1);
        if (auto dot = nm.find_last_of('.'); dot != std::string::npos && dot > 0) nm = nm.substr(0, dot);
        ir->name = nm;
        ir->data.resize(static_cast<size_t>(frames) * ch);
        for (int64_t i = 0; i < frames; ++i)
            for (int c = 0; c < ch; ++c) ir->data[static_cast<size_t>(i * ch + c)] = sb->samples[static_cast<size_t>((first + i) * inCh + c)];
        { std::lock_guard<std::mutex> lk(srcMu_); userIr_ = std::move(ir); }
        userGen_.fetch_add(1, std::memory_order_relaxed);
        p_[IrSelect].store(1.0f, std::memory_order_relaxed);
        if (prepared_) rebuild(false);
        return true;
    }

    // State blob: the user IR (if any). Always non-empty so a restore re-syncs the kernel.
    std::vector<uint8_t> getState() const override {
        std::shared_ptr<const chamber::SourceIr> u;
        { std::lock_guard<std::mutex> lk(srcMu_); u = userIr_; }
        std::vector<uint8_t> s;
        auto pU = [&](uint32_t v) { for (int b = 0; b < 4; ++b) s.push_back(static_cast<uint8_t>(v >> (8 * b))); };
        auto pF = [&](float f) { uint32_t v; std::memcpy(&v, &f, 4); pU(v); };
        pU(kMagic); pU(1u); pU(u ? 1u : 0u);
        if (u) {
            pU(static_cast<uint32_t>(u->name.size()));
            s.insert(s.end(), u->name.begin(), u->name.end());
            pF(static_cast<float>(u->sampleRate));
            pU(static_cast<uint32_t>(u->channels));
            pU(static_cast<uint32_t>(u->frames));
            s.reserve(s.size() + u->data.size() * 4);
            for (float v : u->data) pF(v);
        }
        return s;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data || size < 12) return;
        size_t off = 0;
        const size_t n = static_cast<size_t>(size);
        auto gU = [&](uint32_t& v) { if (off + 4 > n) return false; v = 0; for (int b = 0; b < 4; ++b) v |= static_cast<uint32_t>(data[off++]) << (8 * b); return true; };
        auto gF = [&](float& f) { uint32_t v; if (!gU(v)) return false; std::memcpy(&f, &v, 4); return true; };
        uint32_t magic = 0, ver = 0, has = 0;
        if (!gU(magic) || magic != kMagic || !gU(ver) || !gU(has)) return;
        std::shared_ptr<chamber::SourceIr> ir;
        if (has) {
            uint32_t nl = 0, ch = 0, frames = 0; float sr = 0.0f;
            if (!gU(nl) || off + nl > n) return;
            std::string name(reinterpret_cast<const char*>(data + off), nl); off += nl;
            if (!gF(sr) || !gU(ch) || !gU(frames)) return;
            if (ch < 1 || ch > 4 || sr <= 0.0f || frames == 0 || off + static_cast<size_t>(frames) * ch * 4 > n) return;
            ir = std::make_shared<chamber::SourceIr>();
            ir->name = name; ir->category = "USER"; ir->sampleRate = sr; ir->channels = static_cast<int>(ch); ir->frames = frames;
            ir->data.resize(static_cast<size_t>(frames) * ch);
            for (auto& v : ir->data) gF(v);
        }
        { std::lock_guard<std::mutex> lk(srcMu_); userIr_ = std::move(ir); }
        userGen_.fetch_add(1, std::memory_order_relaxed);
        if (prepared_) rebuild(false);   // synchronous: the restored params + IR are live on return
    }

private:
    static constexpr int kChunk = 64;
    static constexpr int kRampLen = 1024;
    static constexpr int kXfLen = 1536;
    static constexpr int kQuietSamples = 4096;
    static constexpr double kMaxUserSec = 20.0;
    static constexpr uint32_t kMagic = 0x424D4843u;   // "CHMB"

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    int irIndex() const { return std::clamp(static_cast<int>(std::lround(get(IrSelect) * (kIrCount - 1))), 0, kIrCount - 1); }
    int qualityIndex() const { return std::clamp(static_cast<int>(std::lround(get(Quality) * 2.0f)), 0, 2); }
    float shimRatio() const {
        static const float r[3] = { 0.5f, 1.4983071f, 2.0f };   // −12 · +7 · +12 semitones
        return r[std::clamp(static_cast<int>(std::lround(get(ShimmerPitch) * 2.0f)), 0, 2)];
    }
    static int bigBlock(double sr) { return sr <= 50000.0 ? 1024 : sr <= 100000.0 ? 2048 : 4096; }

    float predelaySamples(int timeParam, int syncParam) const {
        static const double beats[kSyncDivs] = { 1.0 / 16, 1.0 / 12, 1.0 / 8, 1.0 / 6, 1.0 / 4, 1.0 / 3, 3.0 / 8, 1.0 / 2, 2.0 / 3, 3.0 / 4, 1.0, 1.5, 2.0 };
        const float v = get(timeParam);
        double s;
        if (get(syncParam) >= 0.5f) s = beats[std::clamp(static_cast<int>(std::lround(v * (kSyncDivs - 1))), 0, kSyncDivs - 1)] * spb_;
        else s = 0.5 * v * v * sr_;   // 0..500 ms, quadratic
        return static_cast<float>(std::clamp(s, 0.0, 2.5 * sr_));
    }

    // ---- EQ ----
    void updateEq() {
        const float v[4] = { get(EqLowCut), get(EqLowGain), get(EqHighShelf), get(EqHighCut) };
        if (v[0] == eqV_[0] && v[1] == eqV_[1] && v[2] == eqV_[2] && v[3] == eqV_[3] && eqSr_ == sr_) return;
        const bool wasOn[4] = { eqOn_[0], eqOn_[1], eqOn_[2], eqOn_[3] };
        const double lc = chamber::expMap(v[0], 20.0, 2000.0), hc = chamber::expMap(v[3], 1000.0, 20000.0);
        const double lg = (v[1] - 0.5) * 36.0, hg = (v[2] - 0.5) * 36.0;
        eqOn_[0] = v[0] > 0.001f; eqOn_[1] = std::fabs(lg) > 0.05; eqOn_[2] = std::fabs(hg) > 0.05; eqOn_[3] = v[3] < 0.999f && hc < sr_ * 0.45;
        eq_[0].hp(lc, sr_);
        eq_[1].lowShelf(std::max(250.0, lc * 2.0), lg, sr_);
        eq_[2].highShelf(std::min(4000.0, hc * 0.5), hg, sr_);
        eq_[3].lp(std::min(hc, sr_ * 0.45), sr_);
        for (int i = 0; i < 4; ++i) if (eqOn_[i] && !wasOn[i]) eq_[i].clear();
        for (int i = 0; i < 4; ++i) eqV_[i] = v[i];
        eqSr_ = sr_;
    }
    void runEq(float* l, float* r, int n) {
        for (int s = 0; s < 4; ++s) {
            if (!eqOn_[s]) continue;
            auto& b = eq_[s];
            for (int i = 0; i < n; ++i) { l[i] = b.run(0, l[i]); r[i] = b.run(1, r[i]); }
        }
    }

    // ---- kernels (audio thread side) ----
    void install(chamber::ConvKernel* k) {
        if (k->sampleRate != sr_) { retire(k); return; }      // built for a rate we left
        if (!cur_ || !convActive_) {                          // nothing sounding: just swap
            retire(cur_); cur_ = k;
            retire(fade_); fade_ = nullptr; retire(ramp_); ramp_ = nullptr;
            return;
        }
        if (k->compatible(*cur_)) {                           // same layout: morph the whole tail
            k->transplant(*cur_);
            xfOld_ = cur_; cur_ = k; xfPos_ = 0; xfWarm_ = k->warmup();
            return;
        }
        // Structural change (zero latency / true stereo / length class): the old kernel rings
        // out what it already heard while the new one starts clean; a third ramps away.
        if (ramp_) retire(ramp_);
        ramp_ = fade_; rampPos_ = 0;
        fade_ = cur_;
        cur_ = k;
    }
    void clearConv() {
        if (cur_) cur_->clear();
        retire(fade_); fade_ = nullptr;
        retire(ramp_); ramp_ = nullptr;
        retire(xfOld_); xfOld_ = nullptr;
    }
    void retire(chamber::ConvKernel* k) {
        if (!k) return;
        const uint32_t h = rHead_.load(std::memory_order_relaxed);
        if (h - rTail_.load(std::memory_order_acquire) < kRing) {
            ring_[h % kRing] = k;
            rHead_.store(h + 1, std::memory_order_release);
            return;
        }
        for (auto*& s : spill_) if (!s) { s = k; return; }
        // Ring and spill both full: the worker hasn't run for 40 retirements. Unreachable in
        // practice (it drains every 10 ms); leaking one kernel beats freeing on the audio thread.
    }
    void flushSpill() {
        for (auto*& s : spill_) {
            if (!s) continue;
            const uint32_t h = rHead_.load(std::memory_order_relaxed);
            if (h - rTail_.load(std::memory_order_acquire) >= kRing) return;
            ring_[h % kRing] = s; s = nullptr;
            rHead_.store(h + 1, std::memory_order_release);
        }
    }

    // ---- kernels (worker / message thread side) ----
    struct Sig {
        int ir = -1, start = 0, end = 0, attack = 0, size = 0, rev = 0, ts = 0, zl = 0, q = 0, sr = 0;
        uint32_t gen = 0;
        bool operator==(const Sig& o) const {
            return ir == o.ir && start == o.start && end == o.end && attack == o.attack && size == o.size && rev == o.rev
                && ts == o.ts && zl == o.zl && q == o.q && sr == o.sr && gen == o.gen;
        }
        bool operator!=(const Sig& o) const { return !(*this == o); }
    };
    chamber::Algo::Params algoParams() const {
        chamber::Algo::Params ap;
        ap.mode = std::clamp(static_cast<int>(std::lround(get(AlgoMode) * 3.0f)), 0, 3);
        ap.decay = static_cast<float>(chamber::expMap(get(AlgoDecay), 0.2, 20.0));
        ap.size = static_cast<float>(chamber::expMap(get(AlgoSize), 0.4, 2.5));
        ap.diffuse = get(AlgoDiffusion);
        ap.damp = static_cast<float>(chamber::expMap(get(AlgoDamping), 500.0, 18000.0));
        ap.lowMul = static_cast<float>(chamber::expMap(get(AlgoLowDecay), 0.25, 4.0));
        ap.highMul = static_cast<float>(chamber::expMap(get(AlgoHighDecay), 0.25, 4.0));
        ap.rate = static_cast<float>(chamber::expMap(get(ModRate), 0.05, 8.0));
        ap.depth = get(ModDepth);
        ap.freeze = get(Freeze) >= 0.5f;
        ap.vintage = get(AlgoVintage) >= 0.5f;
        ap.cubic = qualityIndex() == 2;
        ap.shimAmt = get(ShimmerAmount);
        ap.shimRatio = shimRatio();
        ap.shimFb = get(ShimmerFeedback) >= 0.5f;
        return ap;
    }

    // ---- previews for the card (worker thread): what the engines actually sound like ----
    static constexpr int kEnvBins = 512;
    static constexpr double kPreviewSr = 16000.0;
    // Peak envelope of `chans` over kEnvBins, normalised to its maximum.
    static std::vector<float> envelope(const std::vector<std::vector<float>>& chans, const std::vector<int>& use) {
        std::vector<float> env(kEnvBins, 0.0f);
        if (chans.empty() || chans[0].empty()) return env;
        const size_t n = chans[0].size();
        float peak = 1e-12f;
        for (int k = 0; k < kEnvBins; ++k) {
            const size_t a = n * static_cast<size_t>(k) / kEnvBins, z = std::max(a + 1, n * static_cast<size_t>(k + 1) / kEnvBins);
            float pk = 0.0f;
            for (int c : use) { const auto& v = chans[static_cast<size_t>(c)]; for (size_t i = a; i < z && i < n; ++i) pk = std::max(pk, std::fabs(v[i])); }
            env[static_cast<size_t>(k)] = pk; peak = std::max(peak, pk);
        }
        for (auto& v : env) v /= peak;
        return env;
    }
    std::vector<int> previewSignature() const {
        static const int ids[] = { AlgoMode, AlgoDecay, AlgoSize, AlgoDiffusion, AlgoDamping, AlgoLowDecay, AlgoHighDecay, Freeze,
                                   AlgoVintage, ModRate, ModDepth, ShimmerAmount, ShimmerPitch, ShimmerFeedback };
        std::vector<int> v;
        for (int id : ids) v.push_back(static_cast<int>(std::lround(get(id) * 2000.0f)));
        return v;
    }
    // Render the algorithm's impulse response offline (a private tank at 16 kHz) and keep its
    // envelope — the card's decay graph draws the real echogram, not a model of it.
    void renderAlgoPreview() {
        const auto p = algoParams();
        if (!preview_) { preview_ = std::make_unique<chamber::Algo>(); preview_->prepare(kPreviewSr); }
        preview_->restart();
        const double rt = p.freeze ? 3.0 : p.decay * std::max(1.0f, std::max(p.lowMul, p.highMul));
        const double secs = std::clamp(rt * 1.15 + 0.1, 0.4, 12.0);
        const size_t frames = static_cast<size_t>(secs * kPreviewSr);
        std::vector<std::vector<float>> out(2, std::vector<float>(frames, 0.0f));
        float inL[256] = {}, inR[256] = {};
        for (size_t off = 0; off < frames; off += 256) {
            const int n = static_cast<int>(std::min<size_t>(256, frames - off));
            inL[0] = inR[0] = off == 0 ? 1.0f : 0.0f;
            preview_->process(inL, inR, &out[0][off], &out[1][off], n, p);
        }
        auto env = envelope(out, { 0, 1 });
        std::lock_guard<std::mutex> lk(srcMu_);
        algoEnv_ = std::move(env); algoEnvSec_ = static_cast<float>(secs);
        previewGen_.fetch_add(1, std::memory_order_relaxed);
    }

    Sig signature() const {
        auto q = [](float v) { return static_cast<int>(std::lround(v * 4000.0f)); };
        Sig s;
        s.ir = irIndex(); s.start = q(get(IrStart)); s.end = q(get(IrDecay)); s.attack = q(get(IrAttack)); s.size = q(get(IrSize));
        s.rev = get(IrReverse) >= 0.5f; s.ts = get(IrTrueStereo) >= 0.5f; s.zl = get(ZeroLatency) >= 0.5f; s.q = qualityIndex();
        s.sr = static_cast<int>(std::lround(srA_.load(std::memory_order_relaxed)));
        s.gen = userGen_.load(std::memory_order_relaxed);
        return s;
    }

    std::unique_ptr<chamber::ConvKernel> makeKernel(const Sig& s) {
        using chamber::ConvKernel;
        const double sr = static_cast<double>(s.sr);
        std::shared_ptr<const chamber::SourceIr> src;
        if (s.ir < chamber::kNumBuiltinIrs) src = chamber::builtinIr(s.ir);
        else { std::lock_guard<std::mutex> lk(srcMu_); src = userIr_; }
        { std::lock_guard<std::mutex> lk(srcMu_); shownSrc_ = src; }
        std::vector<int> chans;
        std::vector<ConvKernel::Path> paths;
        int nIn = 2; bool mono = false;
        const int nc = src ? src->channels : 0;
        if (nc == 1) { chans = { 0 }; paths = { { 0, 0, 0 }, { 1, 1, 0 } }; }
        else if (nc == 2) {
            chans = { 0, 1 };
            if (s.ts) { nIn = 1; mono = true; paths = { { 0, 0, 0 }, { 0, 1, 1 } }; }
            else paths = { { 0, 0, 0 }, { 1, 1, 1 } };
        } else if (nc >= 4) {
            if (s.ts) { chans = { 0, 1, 2, 3 }; paths = { { 0, 0, 0 }, { 0, 1, 1 }, { 1, 0, 2 }, { 1, 1, 3 } }; }
            else { chans = { 0, 3 }; paths = { { 0, 0, 0 }, { 1, 1, 1 } }; }
        }
        chamber::Shape sh;
        sh.start = s.start / 4000.0f; sh.end = s.end / 4000.0f;
        sh.attackMs = 500.0f * (s.attack / 4000.0f) * (s.attack / 4000.0f);
        sh.size = static_cast<float>(chamber::expMap(s.size / 4000.0, 0.5, 2.0));
        sh.reverse = s.rev != 0;
        static const double maxSec[3] = { 3.0, 6.0, 10.0 };
        std::vector<std::vector<float>> h;
        if (src && nc > 0) h = chamber::shapeIr(*src, chans, sh, sr, maxSec[std::clamp(s.q, 0, 2)]);
        {   // the processed response, as the card's RESULT lane (direct paths only)
            std::vector<int> use;
            if (!h.empty()) { use.push_back(0); if (h.size() > 1) use.push_back(h.size() == 4 ? 3 : 1); }
            auto env = envelope(h, use);
            std::lock_guard<std::mutex> lk(srcMu_);
            convEnv_ = h.empty() ? std::vector<float>{} : std::move(env);
            convEnvSec_ = h.empty() ? 0.0f : static_cast<float>(h[0].size() / sr);
            previewGen_.fetch_add(1, std::memory_order_relaxed);
        }
        return ConvKernel::build(h, paths, nIn, mono, s.zl != 0, bigBlock(sr), sr);
    }

    // Build a kernel for the current params when they changed. `direct` installs it as the
    // current kernel (only while process() can't run); otherwise it's handed to the audio thread.
    bool rebuild(bool direct) {
        std::lock_guard<std::mutex> lk(buildMu_);
        const Sig s = signature();
        if (!direct && built_ && s == builtSig_) { pending_.store(false, std::memory_order_relaxed); return false; }
        pending_.store(true, std::memory_order_relaxed);
        auto k = makeKernel(s);
        builtSig_ = s; built_ = true;
        if (direct) { delete cur_; cur_ = k.release(); }
        else delete ready_.exchange(k.release(), std::memory_order_acq_rel);   // an unclaimed older one is ours to free
        pending_.store(false, std::memory_order_relaxed);
        return true;
    }

    void startWorker() {
        if (worker_.joinable()) return;
        stop_.store(false);
        worker_ = std::thread([this] { workerLoop(); });
    }
    void stopWorker() {
        if (!worker_.joinable()) return;
        { std::lock_guard<std::mutex> lk(wMu_); stop_.store(true); }
        wCv_.notify_all();
        worker_.join();
    }
    void workerLoop() {
        using clock = std::chrono::steady_clock;
        auto last = clock::now() - std::chrono::seconds(1), lastPreview = last;
        while (!stop_.load()) {
            {
                std::unique_lock<std::mutex> lk(wMu_);
                wCv_.wait_for(lk, std::chrono::milliseconds(10), [this] { return stop_.load(); });
            }
            if (stop_.load()) break;
            drainRetired();
            if (clock::now() - lastPreview >= std::chrono::milliseconds(40)) {
                auto ps = previewSignature();
                if (ps != previewSig_) { previewSig_ = std::move(ps); renderAlgoPreview(); lastPreview = clock::now(); }
            }
            if (clock::now() - last < std::chrono::milliseconds(25)) continue;   // coalesce drags
            bool changed;
            { std::lock_guard<std::mutex> lk(buildMu_); changed = !built_ || signature() != builtSig_; }
            if (changed && rebuild(false)) last = clock::now();
        }
    }
    void drainRetired() {
        uint32_t t = rTail_.load(std::memory_order_relaxed);
        const uint32_t h = rHead_.load(std::memory_order_acquire);
        while (t != h) { delete ring_[t % kRing]; ring_[t % kRing] = nullptr; ++t; }
        rTail_.store(t, std::memory_order_release);
    }

    // ---- state ----
    std::atomic<float> p_[kNumParams] = {};
    double sr_ = 44100.0, spb_ = 22050.0;
    bool prepared_ = false;
    std::atomic<double> srA_{44100.0};

    chamber::Algo algo_;
    chamber::DLine convPre_[2], algoPre_[2];
    float convPreCur_ = -1.0f, algoPreCur_ = -1.0f;
    chamber::Biquad eq_[4];
    bool eqOn_[4] = {};
    float eqV_[4] = { -1.0f, -1.0f, -1.0f, -1.0f };
    double eqSr_ = 0.0;
    bool convActive_ = true, algoActive_ = true;
    float convG_ = 0.0f, algoG_ = 0.0f, feedG_ = 1.0f, wetS_ = 0.0f, dryS_ = 1.0f, outS_ = 1.0f, widthS_ = 1.0f;
    float duckEnv_ = 0.0f, duckGain_ = 1.0f, msLp1_[2] = {};
    double cpuS_ = 0.0;
    int algoQuiet_ = 0;
    float dL_[kChunk] = {}, dR_[kChunk] = {}, fL_[kChunk] = {}, fR_[kChunk] = {}, cL_[kChunk] = {}, cR_[kChunk] = {};
    float aL_[kChunk] = {}, aR_[kChunk] = {}, tL_[kChunk] = {}, tR_[kChunk] = {}, wL_[kChunk] = {}, wR_[kChunk] = {};
    float xL_[kChunk] = {}, xR_[kChunk] = {};

    // kernels: audio-thread owned (cur_ / fade_ / ramp_), handed over through ready_,
    // returned through the SPSC ring (audio → worker) for deletion.
    chamber::ConvKernel* cur_ = nullptr;
    chamber::ConvKernel* fade_ = nullptr;
    chamber::ConvKernel* ramp_ = nullptr;
    chamber::ConvKernel* xfOld_ = nullptr;
    int rampPos_ = 0, xfPos_ = 0, xfWarm_ = 0;
    std::atomic<chamber::ConvKernel*> ready_{nullptr};
    static constexpr uint32_t kRing = 32;
    chamber::ConvKernel* ring_[kRing] = {};
    std::atomic<uint32_t> rHead_{0}, rTail_{0};
    chamber::ConvKernel* spill_[8] = {};

    // worker
    std::thread worker_;
    std::atomic<bool> stop_{false};
    std::mutex wMu_;
    std::condition_variable wCv_;
    std::mutex buildMu_;
    Sig builtSig_;
    bool built_ = false;
    std::atomic<bool> pending_{false};

    // IR sources (message / worker threads; never touched by process()).
    mutable std::mutex srcMu_;
    std::shared_ptr<const chamber::SourceIr> userIr_, shownSrc_;
    std::vector<float> convEnv_, algoEnv_;          // RESULT lane + algorithm echogram (srcMu_)
    float convEnvSec_ = 0.0f, algoEnvSec_ = 0.0f;
    std::atomic<uint32_t> previewGen_{0};
    std::unique_ptr<chamber::Algo> preview_;        // worker-only offline tank
    std::vector<int> previewSig_;                   // worker-only
    std::atomic<uint32_t> userGen_{0};

    // meters
    std::atomic<float> mWet_{0.0f}, mOutL_{0.0f}, mOutR_{0.0f}, mIn_{0.0f}, mDuck_{0.0f}, cpuA_{0.0f}, kernSecA_{0.0f}, bpmA_{120.0f};
    std::atomic<int> latA_{0}, kernA_{0};
};

} // namespace nota
