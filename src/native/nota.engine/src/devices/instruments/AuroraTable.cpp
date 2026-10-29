// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AuroraTable builders (see AuroraTable.h). Everything here runs on the caller's thread
// — a worker for an import, the message thread for a reload — and allocates freely; the
// audio thread only ever sees the finished, immutable table.

#include "devices/instruments/AuroraTable.h"

#include "media/AudioFile.h"

#include <signalsmith-linear/fft.h>

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cmath>
#include <complex>
#include <cstdio>
#include <cstring>

namespace nota {
namespace {

using Cx = std::complex<double>;
constexpr double kTwoPi = 6.283185307179586;
constexpr double kSilentPeak = 0.001; // −60 dBFS
constexpr int kSeam = 32;

bool isPow2(int64_t n) { return n > 0 && (n & (n - 1)) == 0; }

std::string extLower(const std::string& path) {
    auto dot = path.find_last_of('.');
    if (dot == std::string::npos) return {};
    std::string e = path.substr(dot + 1);
    for (auto& c : e) c = (char)std::tolower((unsigned char)c);
    return e;
}

// The frame size a wavetable WAV declares in its `clm ` chunk ("<!>2048 …"), or 0.
int32_t clmFrameSize(const std::string& path) {
    std::FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return 0;
    int32_t found = 0;
    unsigned char hdr[12];
    if (std::fread(hdr, 1, 12, f) == 12 && std::memcmp(hdr, "RIFF", 4) == 0 && std::memcmp(hdr + 8, "WAVE", 4) == 0) {
        for (int guard = 0; guard < 64; ++guard) {
            unsigned char ch[8];
            if (std::fread(ch, 1, 8, f) != 8) break;
            const uint32_t sz = (uint32_t)ch[4] | ((uint32_t)ch[5] << 8) | ((uint32_t)ch[6] << 16) | ((uint32_t)ch[7] << 24);
            if (std::memcmp(ch, "clm ", 4) == 0) {
                char buf[64] = {};
                const size_t want = std::min<size_t>(sz, sizeof(buf) - 1);
                if (std::fread(buf, 1, want, f) == want) {
                    const char* p = std::strstr(buf, "<!>");
                    if (p) found = std::atoi(p + 3);
                }
                break;
            }
            if (std::fseek(f, (long)(sz + (sz & 1u)), SEEK_CUR) != 0) break;
        }
    }
    std::fclose(f);
    return found;
}

// Complex spectrum of one real period x[0..n), bins 0..k-1 (k ≤ n/2), unscaled.
void spectrum(const float* x, int n, int k, std::vector<Cx>& out) {
    out.assign((size_t)k, Cx{});
    if (isPow2(n)) {
        signalsmith::linear::Pow2FFT<double> fft((size_t)n);
        std::vector<Cx> t((size_t)n), fr((size_t)n);
        for (int i = 0; i < n; ++i) t[(size_t)i] = Cx(x[i], 0.0);
        fft.fft(t.data(), fr.data());
        for (int i = 0; i < k; ++i) out[(size_t)i] = fr[(size_t)i];
        return;
    }
    for (int b = 0; b < k; ++b) { // naive DFT: only for short single cycles of odd length
        Cx acc{};
        for (int i = 0; i < n; ++i) {
            const double ph = -kTwoPi * (double)b * i / n;
            acc += (double)x[i] * Cx(std::cos(ph), std::sin(ph));
        }
        out[(size_t)b] = acc;
    }
}

// One period of `len` samples from bins 0..k-1 of an n-sample period's spectrum.
void synth(const std::vector<Cx>& bins, int k, int n, int len, float* out) {
    signalsmith::linear::Pow2FFT<double> fft((size_t)len);
    std::vector<Cx> fr((size_t)len, Cx{}), t((size_t)len);
    const int kk = std::min(k, len / 2);
    fr[0] = Cx(bins[0].real(), 0.0);
    for (int b = 1; b < kk; ++b) {
        fr[(size_t)b] = bins[(size_t)b];
        fr[(size_t)(len - b)] = std::conj(bins[(size_t)b]);
    }
    fft.ifft(fr.data(), t.data());
    for (int i = 0; i < len; ++i) out[i] = (float)(t[(size_t)i].real() / n);
}

// Resample one period of n samples to exactly 2048 (band-limited, no interpolation).
void resampleCycle(const float* x, int n, float* out) {
    if (n == AuroraTable::kSize) {
        std::copy(x, x + n, out);
        return;
    }
    std::vector<Cx> bins;
    const int k = std::min((n + 1) / 2, AuroraTable::kSize / 2);
    spectrum(x, n, k, bins);
    synth(bins, k, n, AuroraTable::kSize, out);
}

void buildMips(AuroraTable& t) {
    const int N = AuroraTable::kSize;
    for (int k = 1; k < AuroraTable::kLevels; ++k) t.level[k].assign((size_t)t.frames * (size_t)AuroraTable::len(k), 0.0f);
    std::vector<Cx> bins;
    for (int f = 0; f < t.frames; ++f) {
        spectrum(t.frame(0, f), N, N / 2, bins);
        for (int k = 1; k < AuroraTable::kLevels; ++k) {
            const int L = AuroraTable::len(k);
            synth(bins, AuroraTable::harmonics(k), N, L, t.level[k].data() + (size_t)f * (size_t)L);
        }
    }
}

void hashBase(AuroraTable& t) {
    uint64_t a = 1469598103934665603ull, b = 1099511628211ull ^ 0x9e3779b97f4a7c15ull;
    const auto* p = reinterpret_cast<const unsigned char*>(t.level[0].data());
    const size_t n = t.level[0].size() * sizeof(float);
    for (size_t i = 0; i < n; ++i) {
        a = (a ^ p[i]) * 1099511628211ull;
        b = (b ^ p[i]) * 0x100000001b3ull + 0x2545F4914F6CDD1Dull;
    }
    for (int i = 0; i < 8; ++i) {
        t.hash[i] = (uint8_t)(a >> (8 * i));
        t.hash[8 + i] = (uint8_t)(b >> (8 * i));
    }
}

uint32_t nextSerial() {
    static std::atomic<uint32_t> s{0};
    return ++s;
}

std::string baseName(const std::string& path) {
    auto sl = path.find_last_of("/\\");
    return sl == std::string::npos ? path : path.substr(sl + 1);
}

} // namespace

int32_t AuroraTable::buildFromMono(const float* mono, int64_t n, int32_t declaredFrame, std::string name,
    bool truncatedLength, std::shared_ptr<const AuroraTable>& out) {
    if (!mono || n <= 0) return ErrEmpty;
    float peakIn = 0.0f;
    for (int64_t i = 0; i < n; ++i) peakIn = std::max(peakIn, std::fabs(mono[i]));
    if (!(peakIn >= kSilentPeak)) return ErrSilent;

    auto t = std::make_shared<AuroraTable>();
    t->name = std::move(name);
    t->truncatedLength = truncatedLength;
    std::vector<float>& base = t->level[0];

    const bool declared = declaredFrame >= 256 && declaredFrame <= 4096 && isPow2(declaredFrame) && n >= declaredFrame;
    if (declared || (n % kSize == 0 && n >= 2 * kSize)) {
        // (a) A standard wavetable file.
        const int fs = declared ? declaredFrame : kSize;
        int64_t count = n / fs;
        if (count > kMaxFrames) {
            count = kMaxFrames;
            t->truncatedFrames = true;
        }
        t->origin = WavetableFile;
        t->frames = (int32_t)count;
        base.assign((size_t)count * kSize, 0.0f);
        for (int64_t f = 0; f < count; ++f) resampleCycle(mono + f * fs, fs, base.data() + f * kSize);
    } else if (n <= 2 * kSize) {
        // (b) One cycle.
        t->origin = SingleCycle;
        t->frames = 1;
        base.assign(kSize, 0.0f);
        resampleCycle(mono, (int)n, base.data());
    } else {
        // (c) A recording: 16 windows across it, each starting on an upward zero crossing
        // near its even position, and its last samples blended into its first so it loops.
        t->origin = Recording;
        t->frames = kRecordingFrames;
        base.assign((size_t)kRecordingFrames * kSize, 0.0f);
        const int64_t span = std::max<int64_t>(0, n - kSize);
        for (int f = 0; f < kRecordingFrames; ++f) {
            int64_t s0 = (int64_t)std::llround((double)f * (double)span / (kRecordingFrames - 1));
            int64_t best = s0, bestD = -1;
            const int64_t lo = std::max<int64_t>(1, s0 - kSize / 2), hi = std::min<int64_t>(span, s0 + kSize / 2);
            for (int64_t j = lo; j <= hi; ++j)
                if (mono[j - 1] < 0.0f && mono[j] >= 0.0f) {
                    const int64_t d = std::llabs(j - s0);
                    if (bestD < 0 || d < bestD) {
                        bestD = d;
                        best = j;
                    }
                }
            float* dst = base.data() + (size_t)f * kSize;
            for (int i = 0; i < kSize; ++i) dst[i] = mono[std::min<int64_t>(best + i, n - 1)];
            for (int i = 0; i < kSeam; ++i) {
                const int64_t src = best + kSize + i;
                if (src >= n) break;
                const double w = (i + 0.5) / kSeam * (kTwoPi / 4.0);
                dst[i] = (float)(dst[i] * std::sin(w) + mono[src] * std::cos(w));
            }
        }
    }

    // DC out of every frame, then the whole table to the built-in tables' peak (1.0),
    // the frames keeping their relative levels.
    float peak = 0.0f;
    for (int f = 0; f < t->frames; ++f) {
        float* p = base.data() + (size_t)f * kSize;
        double mean = 0.0;
        for (int i = 0; i < kSize; ++i) mean += p[i];
        mean /= kSize;
        for (int i = 0; i < kSize; ++i) {
            p[i] = (float)(p[i] - mean);
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

int32_t AuroraTable::buildFromFile(const std::string& path, std::shared_ptr<const AuroraTable>& out) {
    const std::string ext = extLower(path);
    if (ext != "wav" && ext != "flac" && ext != "mp3") return ErrFormat;
    auto buf = decodeAudioFile(path);
    if (!buf) return ErrDecode;
    if (buf->empty()) return ErrEmpty;
    const double sr = buf->sourceSampleRate > 0 ? buf->sourceSampleRate : 44100.0;
    const int64_t cap = (int64_t)(sr * kMaxSeconds);
    const bool truncated = buf->frames > cap;
    const int64_t n = std::min<int64_t>(buf->frames, cap);
    std::vector<float> mono((size_t)n, 0.0f);
    const int ch = buf->channels;
    for (int64_t i = 0; i < n; ++i) {
        float acc = 0.0f;
        for (int c = 0; c < ch; ++c) acc += buf->samples[(size_t)(i * ch + c)];
        mono[(size_t)i] = acc / (float)ch;
    }
    const int32_t declared = ext == "wav" ? clmFrameSize(path) : 0;
    return buildFromMono(mono.data(), n, declared, baseName(path), truncated, out);
}

int32_t AuroraTable::buildFromFrames(const float* frames, int32_t count, std::string name, int32_t origin,
    std::shared_ptr<const AuroraTable>& out) {
    if (!frames || count <= 0 || count > kMaxFrames) return ErrArgs;
    auto t = std::make_shared<AuroraTable>();
    t->name = std::move(name);
    t->origin = std::clamp(origin, 0, 2);
    t->frames = count;
    t->level[0].assign(frames, frames + (size_t)count * kSize);
    buildMips(*t);
    hashBase(*t);
    t->serial = nextSerial();
    out = std::move(t);
    return Ok;
}

} // namespace nota
