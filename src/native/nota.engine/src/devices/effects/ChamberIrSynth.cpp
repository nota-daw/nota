// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Chamber IR synthesis implementation. The only substitution is the wet-net softClip knee
// (std::tanh → tanhPrecise — downstream safety net, inaudible). loopLimit
// stays std::-exact (recirculating-loop guard).

#include "ChamberIrSynth.h"

#include "dsp/math/FastMath.h"

#include <algorithm>
#include <cmath>
#include <mutex>

namespace nota {
namespace chamber {

double expMap(double v, double lo, double hi) {
    return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0));
}
float onePole(double hz, double sr) {
    return static_cast<float>(1.0 - std::exp(-2.0 * kPi * std::clamp(hz, 1.0, sr * 0.49) / sr));
}
float flush(float x) {
    return std::fabs(x) < 1e-20f ? 0.0f : x;
}
// Transparent below |1|, a tanh knee above (the wet safety net).
float softClip(float x) {
    const float a = std::fabs(x);
    if (a <= 1.0f) return x;
    const float y = 1.0f + 0.6f * fast_math::tanhPrecise((a - 1.0f) / 0.6f);
    return x < 0.0f ? -y : y;
}
// Loop guard: identity in the normal range, bounded when shimmer / freeze-in overdrives it.
float loopLimit(float x) {
    const float a = std::fabs(x);
    if (a <= 2.0f) return x;
    const float y = 2.0f + std::tanh(a - 2.0f);
    return x < 0.0f ? -y : y;
}

const IrSpec& irSpec(int i) {
    static const IrSpec specs[kNumBuiltinIrs] = {
        {"Concert Hall · Wide", "HALL", 2.6f, 2.9f, 2.4f, 1.3f, 90.f, 18, 70.f, 12000.f, 0.55f, 0.f, IrRoom, 11},
        {"Stone Vault", "HALL", 4.4f, 4.8f, 3.8f, 1.5f, 120.f, 14, 110.f, 7000.f, 0.60f, 0.f, IrRoom, 23},
        {"Cathedral", "HALL", 7.5f, 7.8f, 6.4f, 2.6f, 170.f, 12, 170.f, 9000.f, 0.65f, 0.f, IrRoom, 37},
        {"Scoring Stage", "HALL", 2.3f, 2.1f, 1.8f, 1.1f, 70.f, 20, 50.f, 14000.f, 0.50f, 0.f, IrRoom, 41},
        {"Wood Chamber", "ROOM", 1.6f, 1.5f, 1.2f, 0.7f, 40.f, 16, 25.f, 11000.f, 0.45f, 0.f, IrRoom, 53},
        {"Live Room", "ROOM", 1.0f, 0.8f, 0.7f, 0.45f, 30.f, 14, 18.f, 13000.f, 0.40f, 0.f, IrRoom, 61},
        {"Drum Room", "ROOM", 0.8f, 0.6f, 0.5f, 0.35f, 22.f, 18, 10.f, 15000.f, 0.40f, 0.f, IrRoom, 67},
        {"Tiled Bathroom", "ROOM", 1.3f, 1.0f, 1.1f, 0.9f, 12.f, 20, 6.f, 16000.f, 0.35f, 7.f, IrRoom, 71},
        {"Vocal Plate", "PLATE", 2.8f, 2.2f, 2.4f, 1.8f, 0.f, 0, 3.f, 16000.f, 0.70f, 0.f, IrPlate, 79},
        {"Bright Plate", "PLATE", 2.0f, 1.3f, 1.6f, 1.5f, 0.f, 0, 2.f, 18000.f, 0.70f, 0.f, IrPlate, 83},
        {"Spring Tank", "SPRING", 2.6f, 2.0f, 2.2f, 1.2f, 0.f, 0, 0.f, 5200.f, 0.30f, 0.f, IrSpring, 89},
        {"Car Park", "SPACE", 3.6f, 3.2f, 2.9f, 1.5f, 80.f, 10, 60.f, 9000.f, 0.50f, 21.f, IrRoom, 97},
        {"Stairwell", "SPACE", 3.2f, 2.6f, 2.7f, 1.9f, 60.f, 24, 40.f, 12000.f, 0.45f, 13.f, IrRoom, 101},
        {"Forest Clearing", "OUTDOOR", 2.0f, 1.0f, 1.2f, 0.5f, 400.f, 14, 200.f, 8000.f, 0.50f, 0.f, IrOutdoor, 103},
        {"Gated Room", "FX", 0.7f, 0.35f, 0.35f, 0.3f, 20.f, 12, 5.f, 14000.f, 0.50f, 0.f, IrGated, 107},
        {"Metal Tank", "FX", 3.4f, 2.2f, 3.0f, 2.0f, 30.f, 8, 15.f, 12000.f, 0.50f, 0.f, IrMetal, 109},
    };
    return specs[std::clamp(i, 0, kNumBuiltinIrs - 1)];
}

// Rooms / halls / plates / gated / outdoor: sparse-to-dense noise (echo density growing with
// t²) split into four bands that each decay at their own RT60, plus discrete early taps.
void synthRoom(std::vector<float>& out, const IrSpec& s, double sr, Rng& rng) {
    const int64_t n = static_cast<int64_t>(out.size());
    const float rt[4] = {s.rtLow, s.rtMid, std::sqrt(s.rtMid * s.rtHigh), s.rtHigh};
    float k[4], env[4] = {1.0f, 1.0f, 1.0f, 1.0f};
    for (int b = 0; b < 4; ++b) k[b] = static_cast<float>(std::exp(-6.907755 / (std::max(0.05f, rt[b]) * sr)));
    const float c1 = onePole(300.0, sr), c2 = onePole(2000.0, sr), c3 = onePole(6000.0, sr);
    float l1 = 0.0f, l2 = 0.0f, l3 = 0.0f;
    const double build = std::max(0.5, static_cast<double>(s.buildup)) * 0.001;
    const double tStart = s.type == IrPlate ? 0.0 : s.erSpan * 0.001 * 0.3;
    const double pMax = s.type == IrOutdoor ? 0.035 : 1.0;
    const double gateT = s.type == IrGated ? s.length * 0.72 : 1e9;
    std::vector<float> fl;
    int flLen = 0, flPos = 0;
    if (s.flutter > 0.0f) {
        flLen = std::max(1, static_cast<int>(s.flutter * 0.001 * sr));
        fl.assign(static_cast<size_t>(flLen), 0.0f);
    }
    for (int64_t i = 0; i < n; ++i) {
        const double t = static_cast<double>(i) / sr;
        float x = 0.0f;
        if (t >= tStart) {
            const double tt = t - tStart;
            const double p = std::min(pMax, 0.004 + (tt / build) * (tt / build));
            if (rng.uni() < p) x = rng.bi() * 1.7320508f / static_cast<float>(std::sqrt(p));
            x *= static_cast<float>(1.0 - std::exp(-tt / (build * 0.5 + 1e-4)));
        }
        if (flLen > 0) { // parallel walls: a feedback comb over the noise
            const float y = x + 0.55f * fl[static_cast<size_t>(flPos)];
            fl[static_cast<size_t>(flPos)] = y;
            if (++flPos >= flLen) flPos = 0;
            x = y * 0.6f;
        }
        float y;
        if (s.type == IrGated) {
            const double body = std::exp(-t / (s.rtMid * 2.0));
            y = x * static_cast<float>(
                        t < gateT ? body : std::exp(-gateT / (s.rtMid * 2.0)) * std::exp(-(t - gateT) / 0.012));
        } else {
            l1 += c1 * (x - l1);
            l2 += c2 * (x - l2);
            l3 += c3 * (x - l3);
            y = l1 * env[0] + (l2 - l1) * env[1] + (l3 - l2) * env[2] + (x - l3) * env[3];
            for (int b = 0; b < 4; ++b) env[b] *= k[b];
        }
        out[static_cast<size_t>(i)] = y;
    }
    for (int e = 0; e < s.erCount; ++e) { // early reflections: soft (air-absorbed) taps
        const float frac = (static_cast<float>(e) + rng.uni() * 0.9f) / static_cast<float>(s.erCount);
        const double tk = 0.0015 + s.erSpan * 0.001 * std::pow(static_cast<double>(frac), 1.25);
        const int64_t pos = static_cast<int64_t>(tk * sr);
        if (pos + 2 >= n) continue;
        const float amp =
            9.0f * (1.0f - 0.55f * frac) * (0.55f + 0.45f * rng.uni()) * (rng.uni() < 0.5f ? -1.0f : 1.0f);
        out[static_cast<size_t>(pos)] += amp * 0.5f;
        out[static_cast<size_t>(pos + 1)] += amp * 0.3f;
        out[static_cast<size_t>(pos + 2)] += amp * 0.2f;
    }
}

// A spring tank: one recirculating delay per channel with a long first-order allpass chain
// in the loop (the dispersive "boing" chirp), band-limited like the real thing.
void synthSpring(std::vector<float>& out, const IrSpec& s, double sr, int c, Rng& rng) {
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
        for (int k = 0; k < kAp; ++k) {
            const float y = flush(-a * v + apX[k] + a * apY[k]);
            apX[k] = v;
            apY[k] = y;
            v = y;
        }
        lp += lpC * (v - lp);
        hp = flush(hpC * (hp + lp - hpPrev));
        hpPrev = lp;
        line[static_cast<size_t>(w)] = x + g * hp;
        if (++w >= d) w = 0;
        out[static_cast<size_t>(i)] = dv;
    }
}

// A metal tank: inharmonic decaying modes (recursive phasors) — resonant and clangy.
void synthMetal(std::vector<float>& out, const IrSpec& s, double sr, Rng& rng) {
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
            const double nr = zr * cr - zi * ci;
            zi = zr * ci + zi * cr;
            zr = nr;
            out[static_cast<size_t>(i)] += static_cast<float>(zi * (i < attack ? i / attack : 1.0));
            if (std::fabs(zr) + std::fabs(zi) < 1e-7) break;
        }
    }
}

// The 4-channel (LL, LR, RL, RR) true-stereo response of a built-in IR at 48 kHz.
std::shared_ptr<SourceIr> synthesizeIr(int idx) {
    const IrSpec& s = irSpec(idx);
    const double sr = 48000.0;
    const int64_t n = static_cast<int64_t>(s.length * sr);
    auto ir = std::make_shared<SourceIr>();
    ir->channels = 4;
    ir->frames = n;
    ir->sampleRate = sr;
    ir->name = s.name;
    ir->category = s.category;
    ir->data.assign(static_cast<size_t>(n) * 4, 0.0f);
    std::vector<float> ch(static_cast<size_t>(n));
    const float hc = onePole(s.bright, sr);
    for (int c = 0; c < 4; ++c) {
        std::fill(ch.begin(), ch.end(), 0.0f);
        Rng rng(s.seed * 7919u + static_cast<uint32_t>(c) * 104729u + 1u);
        if (s.type == IrSpring) synthSpring(ch, s, sr, c, rng);
        else if (s.type == IrMetal)
            synthMetal(ch, s, sr, rng);
        else
            synthRoom(ch, s, sr, rng);
        float a = 0.0f, b = 0.0f;
        const float g = (c == 0 || c == 3) ? 1.0f : s.cross;
        for (int64_t i = 0; i < n; ++i) {
            a += hc * (ch[static_cast<size_t>(i)] - a);
            b += hc * (a - b);
            ir->data[static_cast<size_t>(i) * 4 + c] = b * g;
        }
    }
    return ir;
}

// Built-in IRs are synthesised once per process and shared by every Chamber instance.
std::shared_ptr<const SourceIr> builtinIr(int idx) {
    static std::mutex mu;
    static std::shared_ptr<const SourceIr> cache[kNumBuiltinIrs];
    idx = std::clamp(idx, 0, kNumBuiltinIrs - 1);
    std::lock_guard<std::mutex> lk(mu);
    if (!cache[idx]) cache[idx] = synthesizeIr(idx);
    return cache[idx];
}

// Trim [start, end] of the source, time-stretch by Size while resampling to the engine rate
// (cubic Hermite), fade the truncated end, optionally reverse, then fade in over Attack.
// Loudness is normalised on the whole source so trims read as trims, not as level jumps.
std::vector<std::vector<float>> shapeIr(
    const SourceIr& src, const std::vector<int>& chans, const Shape& sh, double sr, double maxSec) {
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
    double e = 0.0;
    int mains = 0;
    for (int c = 0; c < nc; ++c) {
        if (nc == 4 && (c == 1 || c == 2)) continue;
        double ec = 0.0;
        for (int64_t i = 0; i < F; ++i) {
            const double v = src.data[static_cast<size_t>(i * nc + c)];
            ec += v * v;
        }
        e += ec;
        ++mains;
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
            const float c1 = 0.5f * (x1 - xm1), c2 = xm1 - 2.5f * x0 + 2.0f * x1 - 0.5f * x2,
                        c3 = 0.5f * (x2 - xm1) + 1.5f * (x0 - x1);
            v[static_cast<size_t>(i)] = (((c3 * f + c2) * f + c1) * f + x0) * norm;
        }
        for (int64_t i = 0; i < fadeOut; ++i) {
            const float g =
                0.5f - 0.5f * std::cos(static_cast<float>(kPi) * static_cast<float>(i) / static_cast<float>(fadeOut));
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

} // namespace chamber
} // namespace nota
