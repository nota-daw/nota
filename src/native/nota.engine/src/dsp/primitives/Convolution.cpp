// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Convolution implementation.
// (chamber::ConvKernel → nota::dsp::ConvKernel).

#include "dsp/primitives/Convolution.h"

#include <algorithm>

namespace nota {
namespace dsp {
namespace {

// Complex multiply-accumulate over B bins (Chamber's cmac).
void cmac(float* __restrict ar, float* __restrict ai, const float* __restrict xr, const float* __restrict xi,
    const float* __restrict hr, const float* __restrict hi, int b) {
    ar[0] += xr[0] * hr[0]; // bin 0 packs DC (re) and Nyquist (im): two real products
    ai[0] += xi[0] * hi[0];
    for (int i = 1; i < b; ++i) {
        ar[i] += xr[i] * hr[i] - xi[i] * hi[i];
        ai[i] += xr[i] * hi[i] + xi[i] * hr[i];
    }
}

} // namespace

std::unique_ptr<ConvKernel> ConvKernel::build(const std::vector<std::vector<float>>& h, const std::vector<Path>& paths,
    int nIn, bool monoIn, bool zeroLatency, int bigB, double sr) {
    auto k = std::unique_ptr<ConvKernel>(new ConvKernel());
    k->sampleRate = sr;
    k->block = bigB;
    k->nIn_ = nIn;
    k->monoIn_ = monoIn;
    k->nPaths_ = std::min(static_cast<int>(paths.size()), 4);
    for (int p = 0; p < k->nPaths_; ++p) {
        k->paths_[p] = paths[static_cast<size_t>(p)];
        k->used_[paths[static_cast<size_t>(p)].out] = true;
    }
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

void ConvKernel::process(const float* inL, const float* inR, float* outL, float* outR, int n) {
    for (int off = 0; off < n; off += kScratch) {
        const int m = std::min(kScratch, n - off);
        const float* a;
        const float* b;
        if (!inL) {
            std::fill(scratch_.begin(), scratch_.begin() + m, 0.0f);
            a = b = scratch_.data();
        } else if (monoIn_) {
            float* s = scratch_.data();
            for (int i = 0; i < m; ++i) s[i] = (inL[off + i] + inR[off + i]) * 0.70710678f;
            a = b = s;
        } else {
            a = inL + off;
            b = inR + off;
        }
        if (fir_.f > 0) fir_.run(a, b, outL + off, outR + off, m, paths_, nPaths_);
        for (int s = 0; s < nStages_; ++s) stages_[s].run(a, b, outL + off, outR + off, m, paths_, nPaths_, used_);
    }
}

void ConvKernel::clear() {
    fir_.clear();
    for (int s = 0; s < nStages_; ++s) stages_[s].clear();
}

bool ConvKernel::compatible(const ConvKernel& o) const {
    if (nIn_ != o.nIn_ || monoIn_ != o.monoIn_ || fir_.f != o.fir_.f || nStages_ != o.nStages_) return false;
    for (int s = 0; s < nStages_; ++s)
        if (stages_[s].B != o.stages_[s].B) return false;
    return true;
}

void ConvKernel::transplant(const ConvKernel& o) {
    if (fir_.f > 0) {
        fir_.hist = o.fir_.hist;
        fir_.pos = o.fir_.pos;
        fir_.quiet = o.fir_.quiet;
    }
    for (int s = 0; s < nStages_; ++s) stages_[s].transplant(o.stages_[s]);
}

int ConvKernel::warmup() const {
    int w = 0;
    for (int s = 0; s < nStages_; ++s) w = std::max(w, 2 * stages_[s].B);
    return w;
}

bool ConvKernel::silent() const {
    if (fir_.f > 0 && fir_.quiet < fir_.f + 1) return false;
    for (int s = 0; s < nStages_; ++s)
        if (!stages_[s].silent()) return false;
    return true;
}

void ConvKernel::Fir::init(int f_, int nIn_, int nK_) {
    f = f_;
    nIn = nIn_;
    nK = nK_;
    pos = 0;
    taps.assign(static_cast<size_t>(nK) * f, 0.0f);
    hist.assign(static_cast<size_t>(nIn) * 2 * f, 0.0f);
}

void ConvKernel::Fir::setKernel(int k, const float* h, int len) {
    for (int t = 0; t < len; ++t) taps[static_cast<size_t>(k) * f + (f - 1 - t)] = h[t];
}

void ConvKernel::Fir::clear() {
    std::fill(hist.begin(), hist.end(), 0.0f);
    pos = 0;
    quiet = 1 << 30;
}

void ConvKernel::Fir::run(const float* a, const float* b, float* oL, float* oR, int n, const Path* paths, int nPaths) {
    for (int i = 0; i < n; ++i) {
        const float x0 = a[i], x1 = b[i];
        const bool nz = x0 != 0.0f || (nIn > 1 && x1 != 0.0f);
        if (nz) quiet = 0;
        else if (quiet < (1 << 30))
            ++quiet;
        float* h0 = &hist[0];
        h0[pos] = x0;
        h0[pos + f] = x0;
        if (nIn > 1) {
            float* h1 = &hist[static_cast<size_t>(2 * f)];
            h1[pos] = x1;
            h1[pos + f] = x1;
        }
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

void ConvKernel::Stage::init(
    int b, int p, int nIn_, int nK_, const std::vector<std::vector<float>>& h, int offset, int len) {
    B = b;
    P = std::max(1, p);
    nIn = nIn_;
    nK = nK_;
    fft.resize(static_cast<size_t>(2 * B));
    const size_t spec = static_cast<size_t>(P) * B;
    Hr.assign(spec * nK, 0.0f);
    Hi.assign(spec * nK, 0.0f);
    Xr.assign(spec * nIn, 0.0f);
    Xi.assign(spec * nIn, 0.0f);
    live.assign(static_cast<size_t>(P) * nIn, 0);
    in.assign(static_cast<size_t>(nIn) * B, 0.0f);
    out.assign(static_cast<size_t>(2) * B, 0.0f);
    ovl.assign(static_cast<size_t>(2) * B, 0.0f);
    time.assign(static_cast<size_t>(2) * B, 0.0f);
    ar.assign(static_cast<size_t>(2) * B, 0.0f);
    ai.assign(static_cast<size_t>(2) * B, 0.0f);
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
            for (int i = 0; i < B; ++i) {
                hr[i] *= sc;
                hi[i] *= sc;
            }
        }
    }
}

void ConvKernel::Stage::clear() {
    std::fill(live.begin(), live.end(), 0);
    liveSlots = 0;
    std::fill(in.begin(), in.end(), 0.0f);
    std::fill(out.begin(), out.end(), 0.0f);
    std::fill(ovl.begin(), ovl.end(), 0.0f);
    pos = 0;
    fill = 0;
    quietBlocks = 2;
}

bool ConvKernel::Stage::silent() const {
    return liveSlots == 0 && quietBlocks >= 2;
}

void ConvKernel::Stage::transplant(const Stage& o) {
    fill = o.fill;
    std::copy(o.in.begin(), o.in.end(), in.begin());
    std::fill(out.begin(), out.end(), 0.0f);
    std::fill(ovl.begin(), ovl.end(), 0.0f);
    std::fill(live.begin(), live.end(), 0);
    pos = 0;
    liveSlots = 0;
    quietBlocks = 0;
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

void ConvKernel::Stage::run(
    const float* a, const float* b, float* oL, float* oR, int n, const Path* paths, int nPaths, const bool* used) {
    int i = 0;
    while (i < n) {
        const int m = std::min(n - i, B - fill);
        std::copy(a + i, a + i + m, &in[static_cast<size_t>(fill)]);
        if (nIn > 1) std::copy(b + i, b + i + m, &in[static_cast<size_t>(B + fill)]);
        if (used[0]) {
            const float* s = &out[static_cast<size_t>(fill)];
            for (int k = 0; k < m; ++k) oL[i + k] += s[k];
        }
        if (used[1]) {
            const float* s = &out[static_cast<size_t>(B + fill)];
            for (int k = 0; k < m; ++k) oR[i + k] += s[k];
        }
        fill += m;
        i += m;
        if (fill == B) {
            compute(paths, nPaths, used);
            fill = 0;
        }
    }
}

void ConvKernel::Stage::compute(const Path* paths, int nPaths, const bool* used) {
    pos = pos + 1 >= P ? 0 : pos + 1;
    for (int c = 0; c < nIn; ++c) {
        const float* ic = &in[static_cast<size_t>(c) * B];
        bool nz = false;
        for (int k = 0; k < B; ++k)
            if (ic[k] != 0.0f) {
                nz = true;
                break;
            }
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
    bool any[2] = {false, false};
    if (liveSlots > 0) {
        for (int o = 0; o < 2; ++o)
            if (used[o]) {
                std::fill(ar.begin() + o * B, ar.begin() + (o + 1) * B, 0.0f);
                std::fill(ai.begin() + o * B, ai.begin() + (o + 1) * B, 0.0f);
            }
        for (int p = 0; p < nPaths; ++p) {
            const Path& ph = paths[p];
            float* accR = &ar[static_cast<size_t>(ph.out) * B];
            float* accI = &ai[static_cast<size_t>(ph.out) * B];
            for (int j = 0; j < P; ++j) {
                int s = pos - j;
                if (s < 0) s += P;
                if (!live[static_cast<size_t>(ph.in) * P + s]) continue;
                const size_t xo = (static_cast<size_t>(ph.in) * P + s) * B;
                const size_t ho = (static_cast<size_t>(ph.k) * P + j) * B;
                cmac(accR, accI, &Xr[xo], &Xi[xo], &Hr[ho], &Hi[ho], B);
            }
        }
    }
    for (int o = 0; o < 2; ++o) {
        if (!used[o]) continue;
        float* ob = &out[static_cast<size_t>(o) * B];
        float* ov = &ovl[static_cast<size_t>(o) * B];
        if (any[o]) {
            fft.ifft(&ar[static_cast<size_t>(o) * B], &ai[static_cast<size_t>(o) * B], time.data());
            for (int k = 0; k < B; ++k) {
                ob[k] = time[static_cast<size_t>(k)] + ov[k];
                ov[k] = time[static_cast<size_t>(B + k)];
            }
        } else {
            for (int k = 0; k < B; ++k) {
                ob[k] = ov[k];
                ov[k] = 0.0f;
            }
        }
    }
    if (any[0] || any[1]) quietBlocks = 0;
    else if (quietBlocks < 2)
        ++quietBlocks;
}

} // namespace dsp
} // namespace nota
