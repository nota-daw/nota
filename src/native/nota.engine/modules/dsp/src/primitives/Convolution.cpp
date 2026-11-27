// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/primitives/Convolution.h"

#include "nota/math/BufferOps.h"
#include "nota/math/Fft.h"

#include <algorithm>
#include <cstddef>
#include <vector>
#include <memory>
#include <cstdint>

namespace nota::dsp {
namespace {

constexpr int32_t kFirTaps = 64;
constexpr int32_t kScratchFrames = 256;
constexpr int32_t kMaxStages = 2;

struct FirHead {
    int32_t f = 0;
    int32_t nIn = 0;
    int32_t nK = 0;
    int32_t pos = 0;
    int32_t quiet = 1 << 30;
    std::vector<float> taps;
    std::vector<float> hist;

    void init(int32_t f_, int32_t nIn_, int32_t nK_) {
        f = f_;
        nIn = nIn_;
        nK = nK_;
        pos = 0;
        quiet = 1 << 30;
        taps.assign(static_cast<std::size_t>(nK) * static_cast<std::size_t>(f), 0.0f);
        hist.assign(static_cast<std::size_t>(nIn) * 2u * static_cast<std::size_t>(f), 0.0f);
    }

    void setKernel(int32_t k, const float* h, int32_t len) {
        const std::size_t base = static_cast<std::size_t>(k) * static_cast<std::size_t>(f);
        for (int32_t t = 0; t < len; ++t) { taps[base + static_cast<std::size_t>(f - 1 - t)] = h[t]; }
    }

    void clear() {
        std::fill(hist.begin(), hist.end(), 0.0f);
        pos = 0;
        quiet = 1 << 30;
    }

    void run(const float* a, const float* b, float* oL, float* oR, int32_t n, const ConvKernel::Path* paths,
        int32_t nPaths) {
        const std::size_t stride = 2u * static_cast<std::size_t>(f);
        for (int32_t i = 0; i < n; ++i) {
            const float x0 = a[i];
            const float x1 = b[i];
            const bool nz = x0 != 0.0f || (nIn > 1 && x1 != 0.0f);
            if (nz) quiet = 0;
            else if (quiet < (1 << 30))
                ++quiet;

            const std::size_t pIdx = static_cast<std::size_t>(pos);
            const std::size_t fIdx = static_cast<std::size_t>(f);
            hist[pIdx] = x0;
            hist[pIdx + fIdx] = x0;
            if (nIn > 1) {
                hist[stride + pIdx] = x1;
                hist[stride + pIdx + fIdx] = x1;
            }
            if (quiet <= f) {
                for (int32_t p = 0; p < nPaths; ++p) {
                    const auto& ph = paths[p];
                    const float* win = &hist[static_cast<std::size_t>(ph.in) * stride + pIdx + 1u];
                    const float* tp = &taps[static_cast<std::size_t>(ph.k) * fIdx];
                    (ph.out == 0 ? oL : oR)[i] += math::dot(tp, win, f);
                }
            }
            if (++pos >= f) pos = 0;
        }
    }
};

struct PartitionStage {
    int32_t B = 0;
    int32_t P = 0;
    int32_t nIn = 0;
    int32_t nK = 0;
    math::RealFft fft;
    std::vector<float> Hr, Hi;
    std::vector<float> Xr, Xi;
    std::vector<uint8_t> live;
    std::vector<float> in, out, ovl;
    std::vector<float> time, ar, ai;
    int32_t pos = 0;
    int32_t fill = 0;
    int32_t liveSlots = 0;
    int32_t quietBlocks = 2;

    void init(int32_t b, int32_t p, int32_t nIn_, int32_t nK_, const std::vector<std::vector<float>>& h, int32_t offset,
        int32_t len) {
        B = b;
        P = std::max(1, p);
        nIn = nIn_;
        nK = nK_;
        fft.resize(2 * B);
        const std::size_t spec = static_cast<std::size_t>(P) * static_cast<std::size_t>(B);
        Hr.assign(spec * static_cast<std::size_t>(nK), 0.0f);
        Hi.assign(spec * static_cast<std::size_t>(nK), 0.0f);
        Xr.assign(spec * static_cast<std::size_t>(nIn), 0.0f);
        Xi.assign(spec * static_cast<std::size_t>(nIn), 0.0f);
        live.assign(static_cast<std::size_t>(P) * static_cast<std::size_t>(nIn), 0u);
        in.assign(static_cast<std::size_t>(nIn) * static_cast<std::size_t>(B), 0.0f);
        out.assign(2u * static_cast<std::size_t>(B), 0.0f);
        ovl.assign(2u * static_cast<std::size_t>(B), 0.0f);
        time.assign(2u * static_cast<std::size_t>(B), 0.0f);
        ar.assign(2u * static_cast<std::size_t>(B), 0.0f);
        ai.assign(2u * static_cast<std::size_t>(B), 0.0f);

        const float sc = 1.0f / (2.0f * static_cast<float>(B));
        for (int32_t k = 0; k < nK; ++k) {
            const float* src = h[static_cast<std::size_t>(k)].data() + offset;
            for (int32_t j = 0; j < P; ++j) {
                std::fill(time.begin(), time.end(), 0.0f);
                const int32_t a = j * B;
                const int32_t m = std::min(B, len - a);
                if (m > 0) std::copy(src + a, src + a + m, time.begin());
                const std::size_t idx =
                    (static_cast<std::size_t>(k) * static_cast<std::size_t>(P) + static_cast<std::size_t>(j)) *
                    static_cast<std::size_t>(B);
                float* hr = &Hr[idx];
                float* hi = &Hi[idx];
                fft.forward(time.data(), hr, hi);
                for (int32_t i = 0; i < B; ++i) {
                    hr[i] *= sc;
                    hi[i] *= sc;
                }
            }
        }
    }

    void clear() {
        std::fill(live.begin(), live.end(), 0u);
        liveSlots = 0;
        std::fill(in.begin(), in.end(), 0.0f);
        std::fill(out.begin(), out.end(), 0.0f);
        std::fill(ovl.begin(), ovl.end(), 0.0f);
        pos = 0;
        fill = 0;
        quietBlocks = 2;
    }

    bool silent() const noexcept { return liveSlots == 0 && quietBlocks >= 2; }

    void transplant(const PartitionStage& o) {
        fill = o.fill;
        std::copy(o.in.begin(), o.in.end(), in.begin());
        std::fill(out.begin(), out.end(), 0.0f);
        std::fill(ovl.begin(), ovl.end(), 0.0f);
        std::fill(live.begin(), live.end(), 0u);
        pos = 0;
        liveSlots = 0;
        quietBlocks = 0;
        const int32_t n = std::min(P, o.P);
        const std::size_t bu = static_cast<std::size_t>(B);
        for (int32_t c = 0; c < nIn; ++c) {
            for (int32_t j = 0; j < n; ++j) {
                const int32_t so = ((o.pos - j) % o.P + o.P) % o.P;
                const int32_t sn = ((pos - j) % P + P) % P;
                const uint8_t lv =
                    o.live[static_cast<std::size_t>(c) * static_cast<std::size_t>(o.P) + static_cast<std::size_t>(so)];
                live[static_cast<std::size_t>(c) * static_cast<std::size_t>(P) + static_cast<std::size_t>(sn)] = lv;
                if (lv == 0u) continue;
                ++liveSlots;
                const std::ptrdiff_t xo = static_cast<std::ptrdiff_t>(
                    (static_cast<std::size_t>(c) * static_cast<std::size_t>(o.P) + static_cast<std::size_t>(so)) * bu);
                const std::ptrdiff_t xn = static_cast<std::ptrdiff_t>(
                    (static_cast<std::size_t>(c) * static_cast<std::size_t>(P) + static_cast<std::size_t>(sn)) * bu);
                std::copy(o.Xr.begin() + xo, o.Xr.begin() + xo + B, Xr.begin() + xn);
                std::copy(o.Xi.begin() + xo, o.Xi.begin() + xo + B, Xi.begin() + xn);
            }
        }
    }

    void compute(const ConvKernel::Path* paths, int32_t nPaths, const bool* used) {
        pos = pos + 1 >= P ? 0 : pos + 1;
        const std::size_t bu = static_cast<std::size_t>(B);
        const std::size_t pu = static_cast<std::size_t>(P);
        for (int32_t c = 0; c < nIn; ++c) {
            const float* ic = &in[static_cast<std::size_t>(c) * bu];
            bool nz = false;
            for (int32_t k = 0; k < B; ++k) {
                if (ic[k] != 0.0f) {
                    nz = true;
                    break;
                }
            }
            uint8_t& lv = live[static_cast<std::size_t>(c) * pu + static_cast<std::size_t>(pos)];
            if (lv != 0u) --liveSlots;
            lv = nz ? 1u : 0u;
            if (!nz) continue;
            ++liveSlots;
            std::copy(ic, ic + B, time.begin());
            std::fill(time.begin() + B, time.end(), 0.0f);
            const std::size_t o = (static_cast<std::size_t>(c) * pu + static_cast<std::size_t>(pos)) * bu;
            fft.forward(time.data(), &Xr[o], &Xi[o]);
        }

        bool any[2] = {false, false};
        if (liveSlots > 0) {
            for (int32_t o = 0; o < 2; ++o) {
                if (used[o]) {
                    const std::ptrdiff_t off = static_cast<std::ptrdiff_t>(o) * static_cast<std::ptrdiff_t>(B);
                    std::fill(ar.begin() + off, ar.begin() + off + B, 0.0f);
                    std::fill(ai.begin() + off, ai.begin() + off + B, 0.0f);
                }
            }
            for (int32_t p = 0; p < nPaths; ++p) {
                const auto& ph = paths[p];
                float* accR = &ar[static_cast<std::size_t>(ph.out) * bu];
                float* accI = &ai[static_cast<std::size_t>(ph.out) * bu];
                for (int32_t j = 0; j < P; ++j) {
                    int32_t s = pos - j;
                    if (s < 0) s += P;
                    if (live[static_cast<std::size_t>(ph.in) * pu + static_cast<std::size_t>(s)] == 0u) continue;
                    const std::size_t xo = (static_cast<std::size_t>(ph.in) * pu + static_cast<std::size_t>(s)) * bu;
                    const std::size_t ho = (static_cast<std::size_t>(ph.k) * pu + static_cast<std::size_t>(j)) * bu;
                    accumulatePackedSpectrum(accR, accI, &Xr[xo], &Xi[xo], &Hr[ho], &Hi[ho], B);
                    any[ph.out] = true;
                }
            }
        }

        for (int32_t o = 0; o < 2; ++o) {
            if (!used[o]) continue;
            float* ob = &out[static_cast<std::size_t>(o) * bu];
            float* ov = &ovl[static_cast<std::size_t>(o) * bu];
            if (any[o]) {
                const std::size_t off = static_cast<std::size_t>(o) * bu;
                fft.inverse(&ar[off], &ai[off], time.data());
                for (int32_t k = 0; k < B; ++k) {
                    ob[k] = time[static_cast<std::size_t>(k)] + ov[k];
                    ov[k] = time[bu + static_cast<std::size_t>(k)];
                }
            } else {
                for (int32_t k = 0; k < B; ++k) {
                    ob[k] = ov[k];
                    ov[k] = 0.0f;
                }
            }
        }
        if (any[0] || any[1]) quietBlocks = 0;
        else if (quietBlocks < 2)
            ++quietBlocks;
    }

    void run(const float* a, const float* b, float* oL, float* oR, int32_t n, const ConvKernel::Path* paths,
        int32_t nPaths, const bool* used) {
        int32_t i = 0;
        while (i < n) {
            const int32_t m = std::min(n - i, B - fill);
            std::copy(a + i, a + i + m, &in[static_cast<std::size_t>(fill)]);
            if (nIn > 1) std::copy(b + i, b + i + m, &in[static_cast<std::size_t>(B + fill)]);
            if (used[0]) {
                const float* s = &out[static_cast<std::size_t>(fill)];
                for (int32_t k = 0; k < m; ++k) oL[i + k] += s[k];
            }
            if (used[1]) {
                const float* s = &out[static_cast<std::size_t>(B + fill)];
                for (int32_t k = 0; k < m; ++k) oR[i + k] += s[k];
            }
            fill += m;
            i += m;
            if (fill == B) {
                compute(paths, nPaths, used);
                fill = 0;
            }
        }
    }
};

} // namespace

struct ConvKernel::Impl {
    int32_t nIn = 2;
    int32_t nPaths = 0;
    int32_t nStages = 0;
    bool monoIn = false;
    bool used[2] = {false, false};
    Path paths[4] = {};
    FirHead fir;
    PartitionStage stages[kMaxStages];
    std::vector<float> scratch;
};

ConvKernel::ConvKernel() : impl_(std::make_unique<Impl>()) {}
ConvKernel::~ConvKernel() = default;
ConvKernel::ConvKernel(ConvKernel&&) noexcept = default;
ConvKernel& ConvKernel::operator=(ConvKernel&&) noexcept = default;

std::unique_ptr<ConvKernel> ConvKernel::build(const std::vector<std::vector<float>>& h, const std::vector<Path>& paths,
    int32_t nIn, bool monoIn, bool zeroLatency, int32_t bigB, double sr) {
    auto k = std::make_unique<ConvKernel>();
    auto& d = *k->impl_;
    k->sampleRate = sr;
    k->block = bigB;
    d.nIn = nIn;
    d.monoIn = monoIn;
    d.nPaths = std::min<int32_t>(static_cast<int32_t>(paths.size()), 4);
    for (int32_t p = 0; p < d.nPaths; ++p) {
        d.paths[p] = paths[static_cast<std::size_t>(p)];
        d.used[d.paths[p].out] = true;
    }
    const int32_t nK = static_cast<int32_t>(h.size());
    const int32_t len = nK > 0 ? static_cast<int32_t>(h[0].size()) : 0;
    k->seconds = sr > 0.0 ? static_cast<float>(static_cast<double>(len) / sr) : 0.0f;
    d.scratch.assign(static_cast<std::size_t>(kScratchFrames) * 2u, 0.0f);
    if (len == 0) return k;
    if (zeroLatency) {
        const int32_t f = std::min(kFirTaps, len);
        d.fir.init(f, nIn, nK);
        for (int32_t c = 0; c < nK; ++c) d.fir.setKernel(c, h[static_cast<std::size_t>(c)].data(), f);
        if (len > kFirTaps) {
            const int32_t segLen = std::min(len, bigB) - kFirTaps;
            d.stages[d.nStages++].init(kFirTaps, (segLen + kFirTaps - 1) / kFirTaps, nIn, nK, h, kFirTaps, segLen);
        }
        if (len > bigB) {
            d.stages[d.nStages++].init(bigB, (len - bigB + bigB - 1) / bigB, nIn, nK, h, bigB, len - bigB);
        }
        k->latency = 0;
    } else {
        d.stages[d.nStages++].init(bigB, (len + bigB - 1) / bigB, nIn, nK, h, 0, len);
        k->latency = bigB;
    }
    return k;
}

void ConvKernel::process(const float* inL, const float* inR, float* outL, float* outR, int32_t n) {
    if (!impl_ || n <= 0) return;
    auto& d = *impl_;
    for (int32_t off = 0; off < n; off += kScratchFrames) {
        const int32_t m = std::min(kScratchFrames, n - off);
        const float* a = nullptr;
        const float* b = nullptr;
        if (!inL) {
            std::fill(d.scratch.begin(), d.scratch.begin() + m, 0.0f);
            a = b = d.scratch.data();
        } else if (d.monoIn) {
            float* s = d.scratch.data();
            for (int32_t i = 0; i < m; ++i) s[i] = (inL[off + i] + inR[off + i]) * 0.70710678f;
            a = b = s;
        } else {
            a = inL + off;
            b = inR + off;
        }
        if (d.fir.f > 0) d.fir.run(a, b, outL + off, outR + off, m, d.paths, d.nPaths);
        for (int32_t s = 0; s < d.nStages; ++s) {
            d.stages[s].run(a, b, outL + off, outR + off, m, d.paths, d.nPaths, d.used);
        }
    }
}

void ConvKernel::clear() {
    if (!impl_) return;
    impl_->fir.clear();
    for (int32_t s = 0; s < impl_->nStages; ++s) impl_->stages[s].clear();
}

bool ConvKernel::compatible(const ConvKernel& o) const {
    if (!impl_ || !o.impl_) return false;
    const auto& a = *impl_;
    const auto& b = *o.impl_;
    if (a.nIn != b.nIn || a.monoIn != b.monoIn || a.fir.f != b.fir.f || a.nStages != b.nStages) return false;
    for (int32_t s = 0; s < a.nStages; ++s) {
        if (a.stages[s].B != b.stages[s].B) return false;
    }
    return true;
}

void ConvKernel::transplant(const ConvKernel& o) {
    if (!impl_ || !o.impl_) return;
    auto& a = *impl_;
    const auto& b = *o.impl_;
    if (a.fir.f > 0) {
        a.fir.hist = b.fir.hist;
        a.fir.pos = b.fir.pos;
        a.fir.quiet = b.fir.quiet;
    }
    for (int32_t s = 0; s < a.nStages; ++s) a.stages[s].transplant(b.stages[s]);
}

int32_t ConvKernel::warmup() const {
    if (!impl_) return 0;
    int32_t w = 0;
    for (int32_t s = 0; s < impl_->nStages; ++s) w = std::max(w, 2 * impl_->stages[s].B);
    return w;
}

bool ConvKernel::silent() const {
    if (!impl_) return true;
    const auto& d = *impl_;
    if (d.fir.f > 0 && d.fir.quiet < d.fir.f + 1) return false;
    for (int32_t s = 0; s < d.nStages; ++s) {
        if (!d.stages[s].silent()) return false;
    }
    return true;
}

} // namespace nota::dsp
