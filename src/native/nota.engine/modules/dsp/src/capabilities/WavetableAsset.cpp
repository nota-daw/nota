// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/capabilities/IWavetableInstrument.h"

#include "nota/media/AudioFile.h"

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cmath>
#include <complex>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <memory>
#include <utility>
#include <vector>

namespace nota::dsp {
namespace {

using Cx = std::complex<double>;
constexpr double kTwoPi = 6.283185307179586;
constexpr double kSilentPeak = 0.001;
constexpr int32_t kSeam = 32;

bool isPow2(int64_t n) noexcept {
    return n > 0 && (n & (n - 1)) == 0;
}

std::string extLower(const std::string& path) {
    const auto dot = path.find_last_of('.');
    if (dot == std::string::npos) return {};
    std::string e = path.substr(dot + 1);
    for (auto& c : e) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return e;
}

int32_t clmFrameSize(const std::string& path) {
    std::FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return 0;
    int32_t found = 0;
    unsigned char hdr[12]{};
    if (std::fread(hdr, 1, 12, f) == 12 && std::memcmp(hdr, "RIFF", 4) == 0 && std::memcmp(hdr + 8, "WAVE", 4) == 0) {
        for (int32_t guard = 0; guard < 64; ++guard) {
            unsigned char ch[8]{};
            if (std::fread(ch, 1, 8, f) != 8) break;
            const uint32_t sz = static_cast<uint32_t>(ch[4]) | (static_cast<uint32_t>(ch[5]) << 8u) |
                                (static_cast<uint32_t>(ch[6]) << 16u) | (static_cast<uint32_t>(ch[7]) << 24u);
            if (std::memcmp(ch, "clm ", 4) == 0) {
                char buf[64]{};
                const std::size_t want = std::min<std::size_t>(sz, sizeof(buf) - 1u);
                if (std::fread(buf, 1, want, f) == want) {
                    if (const char* p = std::strstr(buf, "<!>")) found = std::atoi(p + 3);
                }
                break;
            }
            const long step = static_cast<long>(sz + (sz & 1u));
            if (std::fseek(f, step, SEEK_CUR) != 0) break;
        }
    }
    std::fclose(f);
    return found;
}

// Unnormalized in-place radix-2 Cooley-Tukey FFT/IFFT matching Pow2FFT convention.
void fftPow2(std::vector<Cx>& a, bool inverse) {
    const std::size_t n = a.size();
    for (std::size_t i = 1, j = 0; i < n; ++i) {
        std::size_t bit = n >> 1u;
        for (; (j & bit) != 0u; bit >>= 1u) j ^= bit;
        j ^= bit;
        if (i < j) std::swap(a[i], a[j]);
    }
    for (std::size_t len = 2; len <= n; len <<= 1u) {
        const double ang = (inverse ? kTwoPi : -kTwoPi) / static_cast<double>(len);
        const Cx wlen(std::cos(ang), std::sin(ang));
        const std::size_t half = len >> 1u;
        for (std::size_t i = 0; i < n; i += len) {
            Cx w(1.0, 0.0);
            for (std::size_t j = 0; j < half; ++j) {
                const Cx u = a[i + j];
                const Cx v = a[i + j + half] * w;
                a[i + j] = u + v;
                a[i + j + half] = u - v;
                w *= wlen;
            }
        }
    }
}

void spectrum(const float* x, int32_t n, int32_t k, std::vector<Cx>& out) {
    out.assign(static_cast<std::size_t>(k), Cx{});
    if (isPow2(n)) {
        std::vector<Cx> fr(static_cast<std::size_t>(n));
        for (int32_t i = 0; i < n; ++i) fr[static_cast<std::size_t>(i)] = Cx(static_cast<double>(x[i]), 0.0);
        fftPow2(fr, false);
        for (int32_t i = 0; i < k; ++i) out[static_cast<std::size_t>(i)] = fr[static_cast<std::size_t>(i)];
        return;
    }
    for (int32_t b = 0; b < k; ++b) {
        Cx acc{};
        for (int32_t i = 0; i < n; ++i) {
            const double ph = -kTwoPi * static_cast<double>(b) * static_cast<double>(i) / static_cast<double>(n);
            acc += static_cast<double>(x[i]) * Cx(std::cos(ph), std::sin(ph));
        }
        out[static_cast<std::size_t>(b)] = acc;
    }
}

void synth(const std::vector<Cx>& bins, int32_t k, int32_t n, int32_t len, float* out) {
    std::vector<Cx> fr(static_cast<std::size_t>(len), Cx{});
    const int32_t kk = std::min(k, len / 2);
    fr[0] = Cx(bins[0].real(), 0.0);
    for (int32_t b = 1; b < kk; ++b) {
        fr[static_cast<std::size_t>(b)] = bins[static_cast<std::size_t>(b)];
        fr[static_cast<std::size_t>(len - b)] = std::conj(bins[static_cast<std::size_t>(b)]);
    }
    fftPow2(fr, true);
    const double invN = 1.0 / static_cast<double>(n);
    for (int32_t i = 0; i < len; ++i) { out[i] = static_cast<float>(fr[static_cast<std::size_t>(i)].real() * invN); }
}

void resampleCycle(const float* x, int32_t n, float* out) {
    if (n == WavetableAsset::kSize) {
        std::copy(x, x + n, out);
        return;
    }
    std::vector<Cx> bins;
    const int32_t k = std::min((n + 1) / 2, WavetableAsset::kSize / 2);
    spectrum(x, n, k, bins);
    synth(bins, k, n, WavetableAsset::kSize, out);
}

void buildMips(WavetableAsset& t) {
    constexpr int32_t N = WavetableAsset::kSize;
    for (int32_t k = 1; k < WavetableAsset::kLevels; ++k) {
        t.level[k].assign(static_cast<std::size_t>(t.frames) * static_cast<std::size_t>(WavetableAsset::len(k)), 0.0f);
    }
    std::vector<Cx> bins;
    for (int32_t f = 0; f < t.frames; ++f) {
        spectrum(t.frame(0, f), N, N / 2, bins);
        for (int32_t k = 1; k < WavetableAsset::kLevels; ++k) {
            const int32_t L = WavetableAsset::len(k);
            synth(bins, WavetableAsset::harmonics(k), N, L,
                t.level[k].data() + static_cast<std::size_t>(f) * static_cast<std::size_t>(L));
        }
    }
}

void hashBase(WavetableAsset& t) {
    uint64_t a = 1469598103934665603ull;
    uint64_t b = 1099511628211ull ^ 0x9e3779b97f4a7c15ull;
    const auto* p = reinterpret_cast<const unsigned char*>(t.level[0].data());
    const std::size_t n = t.level[0].size() * sizeof(float);
    for (std::size_t i = 0; i < n; ++i) {
        a = (a ^ p[i]) * 1099511628211ull;
        b = (b ^ p[i]) * 0x100000001b3ull + 0x2545F4914F6CDD1Dull;
    }
    for (int32_t i = 0; i < 8; ++i) {
        t.hash[i] = static_cast<uint8_t>(a >> (8 * i));
        t.hash[8 + i] = static_cast<uint8_t>(b >> (8 * i));
    }
}

uint32_t nextSerial() noexcept {
    static std::atomic<uint32_t> s{0};
    return ++s;
}

std::string baseName(const std::string& path) {
    const auto sl = path.find_last_of("/\\");
    return sl == std::string::npos ? path : path.substr(sl + 1);
}

} // namespace

int32_t WavetableAsset::buildFromMono(const float* mono, int64_t n, int32_t declaredFrame, std::string name,
    bool truncatedLength, std::shared_ptr<const WavetableAsset>& out) {
    if (!mono || n <= 0) return ErrEmpty;
    float peakIn = 0.0f;
    for (int64_t i = 0; i < n; ++i) peakIn = std::max(peakIn, std::fabs(mono[i]));
    if (!(peakIn >= kSilentPeak)) return ErrSilent;

    auto t = std::make_shared<WavetableAsset>();
    t->name = std::move(name);
    t->truncatedLength = truncatedLength;
    std::vector<float>& base = t->level[0];

    const bool declared = declaredFrame >= 256 && declaredFrame <= 4096 && isPow2(declaredFrame) && n >= declaredFrame;
    if (declared || (n % kSize == 0 && n >= 2 * kSize && n <= static_cast<int64_t>(kMaxFrames) * kSize)) {
        const int32_t fs = declared ? declaredFrame : kSize;
        int64_t count = n / fs;
        if (count > kMaxFrames) {
            count = kMaxFrames;
            t->truncatedFrames = true;
        }
        t->origin = WavetableFile;
        t->frames = static_cast<int32_t>(count);
        base.assign(static_cast<std::size_t>(count) * static_cast<std::size_t>(kSize), 0.0f);
        for (int64_t f = 0; f < count; ++f) {
            resampleCycle(mono + f * fs, fs, base.data() + static_cast<std::size_t>(f) * kSize);
        }
    } else if (n <= 2 * kSize) {
        t->origin = SingleCycle;
        t->frames = 1;
        base.assign(kSize, 0.0f);
        resampleCycle(mono, static_cast<int32_t>(n), base.data());
    } else {
        t->origin = Recording;
        t->frames = kRecordingFrames;
        base.assign(static_cast<std::size_t>(kRecordingFrames) * static_cast<std::size_t>(kSize), 0.0f);
        const int64_t span = std::max<int64_t>(0, n - kSize);
        for (int32_t f = 0; f < kRecordingFrames; ++f) {
            const int64_t s0 = static_cast<int64_t>(std::llround(
                static_cast<double>(f) * static_cast<double>(span) / static_cast<double>(kRecordingFrames - 1)));
            int64_t best = s0;
            int64_t bestD = -1;
            const int64_t lo = std::max<int64_t>(1, s0 - kSize / 2);
            const int64_t hi = std::min<int64_t>(span, s0 + kSize / 2);
            for (int64_t j = lo; j <= hi; ++j) {
                if (mono[j - 1] < 0.0f && mono[j] >= 0.0f) {
                    const int64_t d = std::llabs(j - s0);
                    if (bestD < 0 || d < bestD) {
                        bestD = d;
                        best = j;
                    }
                }
            }
            float* dst = base.data() + static_cast<std::size_t>(f) * kSize;
            for (int32_t i = 0; i < kSize; ++i) dst[i] = mono[std::min<int64_t>(best + i, n - 1)];
            for (int32_t i = 0; i < kSeam; ++i) {
                const int64_t src = best + kSize + i;
                if (src >= n) break;
                const double w = (static_cast<double>(i) + 0.5) / static_cast<double>(kSeam) * (kTwoPi / 4.0);
                dst[i] = static_cast<float>(
                    static_cast<double>(dst[i]) * std::sin(w) + static_cast<double>(mono[src]) * std::cos(w));
            }
        }
    }

    float peak = 0.0f;
    for (int32_t f = 0; f < t->frames; ++f) {
        float* p = base.data() + static_cast<std::size_t>(f) * kSize;
        double mean = 0.0;
        for (int32_t i = 0; i < kSize; ++i) mean += static_cast<double>(p[i]);
        mean /= static_cast<double>(kSize);
        for (int32_t i = 0; i < kSize; ++i) {
            p[i] = static_cast<float>(static_cast<double>(p[i]) - mean);
            peak = std::max(peak, std::fabs(p[i]));
        }
    }
    if (!(peak > 1e-9f)) return ErrSilent;
    const float g = 1.0f / peak;
    for (auto& v : base) v *= g;

    buildMips(*t);
    hashBase(*t);
    t->serial = nextSerial();
    out = std::move(t);
    return Ok;
}

int32_t WavetableAsset::buildFromFile(const std::string& path, std::shared_ptr<const WavetableAsset>& out) {
    const std::string ext = extLower(path);
    if (ext != "wav" && ext != "flac" && ext != "mp3") return ErrFormat;
    const auto buf = media::decodeAudioFile(path);
    if (!buf) return ErrDecode;
    if (buf->empty()) return ErrEmpty;
    const double sr = buf->sourceSampleRate > 0.0 ? buf->sourceSampleRate : 44100.0;
    const int64_t cap = static_cast<int64_t>(sr * kMaxSeconds);
    const bool truncated = buf->frames > cap;
    const int64_t n = std::min<int64_t>(buf->frames, cap);
    std::vector<float> mono(static_cast<std::size_t>(n), 0.0f);
    const int32_t ch = buf->channels;
    for (int64_t i = 0; i < n; ++i) {
        float acc = 0.0f;
        for (int32_t c = 0; c < ch; ++c) acc += buf->samples[static_cast<std::size_t>(i * ch + c)];
        mono[static_cast<std::size_t>(i)] = acc / static_cast<float>(ch);
    }
    const int32_t declared = ext == "wav" ? clmFrameSize(path) : 0;
    return buildFromMono(mono.data(), n, declared, baseName(path), truncated, out);
}

int32_t WavetableAsset::buildFromFrames(
    const float* frames, int32_t count, std::string name, int32_t origin, std::shared_ptr<const WavetableAsset>& out) {
    if (!frames || count <= 0 || count > kMaxFrames) return ErrArgs;
    auto t = std::make_shared<WavetableAsset>();
    t->name = std::move(name);
    t->origin = std::clamp(origin, 0, 2);
    t->frames = count;
    t->level[0].assign(frames, frames + static_cast<std::size_t>(count) * kSize);
    buildMips(*t);
    hashBase(*t);
    t->serial = nextSerial();
    out = std::move(t);
    return Ok;
}

} // namespace nota::dsp
