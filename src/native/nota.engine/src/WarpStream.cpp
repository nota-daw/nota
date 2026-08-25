// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "WarpStream.h"

#include "SampleBuffer.h"
#include "signalsmith-stretch/signalsmith-stretch.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>

namespace nota {

namespace {
constexpr int    kMaxBlock = 16384;      // matches Engine::kMaxBlock (offline chunk cap)
constexpr double kMaxRatio = 4.0;        // source frames per output frame (clamped; sizes input scratch)
constexpr double kNoPos = -1e18;         // "needs (re)seek" sentinel

// Per-mode analysis window/hop (seconds), mirrored from Warp.cpp::configureForMode.
void configureForMode(signalsmith::stretch::SignalsmithStretch<float>& st,
                      WarpMode mode, int channels, double sr) {
    const int ch = std::max(1, channels);
    switch (mode) {
        case WarpMode::Beats:
            st.configure(ch, static_cast<int>(sr * 0.06), static_cast<int>(sr * 0.012)); break;
        case WarpMode::Tones:
            st.configure(ch, static_cast<int>(sr * 0.10), static_cast<int>(sr * 0.025)); break;
        case WarpMode::Texture:
            st.configure(ch, static_cast<int>(sr * 0.22), static_cast<int>(sr * 0.055)); break;
        case WarpMode::ComplexPro:
        case WarpMode::Complex:
        default:
            // Same core algorithm; ComplexPro adds formant preservation, applied in
            // configure() once srSR/devSR is known (see the setFormantFactor note there).
            st.presetDefault(ch, static_cast<float>(sr)); break;
    }
}
} // namespace

struct ClipWarpStream::Impl {
    signalsmith::stretch::SignalsmithStretch<float> st;
    std::array<std::vector<float>, 2> in, out;   // planar scratch (audio thread)
    std::array<float*, 2> inPtr{}, outPtr{};
    int      channels = 0;
    WarpMode mode = WarpMode::Complex;
    float    pitch = 0.0f;
    double   devSR = 0.0;
    bool     rePitch = false;
    std::atomic<bool> ready{false};

    // audio-thread playback state
    double  expectedOutPos = kNoPos;  // device frames from warp origin (last block end)
    int64_t fedInt = 0;               // absolute source frame the vocoder has been fed up to
    double  lead = 0.0;               // source frames the input leads the current output by (from priming)

    int inputCap() const { return static_cast<int>(kMaxBlock * kMaxRatio) + 8192; }

    // Absolute source frame for an output position `o` (device frames from origin).
    static double srcAtOutput(double o, const std::vector<WarpMarker>& m, double warpBeats, double spb) {
        if (m.size() < 2 || spb <= 0.0) return 0.0;
        double beat = o / spb;
        if (beat <= m.front().beat) return m.front().srcFrame;
        if (beat >= m.back().beat)  return m.back().srcFrame;
        for (size_t i = 0; i + 1 < m.size(); ++i) {
            if (beat < m[i + 1].beat) {
                const double span = m[i + 1].beat - m[i].beat;
                if (span <= 0.0) return m[i].srcFrame;
                const double t = (beat - m[i].beat) / span;
                return m[i].srcFrame + (m[i + 1].srcFrame - m[i].srcFrame) * t;
            }
        }
        return m.back().srcFrame;
    }

    // Local source-frames-per-output-frame ratio at output position `o`.
    static double ratioAt(double o, const std::vector<WarpMarker>& m, double warpBeats, double spb) {
        const double a = srcAtOutput(o, m, warpBeats, spb);
        const double b = srcAtOutput(o + spb, m, warpBeats, spb);   // over one beat, robust
        double r = (b - a) / spb;
        if (!(r > 0.0)) r = 1.0;
        return std::clamp(r, 1.0 / kMaxRatio, kMaxRatio);
    }

    // Read `n` downmixed source frames from absolute frame `start` into in[] (zero-pad OOR).
    void readSource(const SampleBuffer& src, int64_t start, int n) {
        const int srcCh = src.channels;
        for (int i = 0; i < n; ++i) {
            const int64_t f = start + i;
            in[0][static_cast<size_t>(i)] = src.at(f, 0);
            if (channels == 2)
                in[1][static_cast<size_t>(i)] = src.at(f, srcCh >= 2 ? 1 : 0);
        }
    }
};

ClipWarpStream::ClipWarpStream() : d_(std::make_unique<Impl>()) {}
ClipWarpStream::~ClipWarpStream() = default;

bool ClipWarpStream::ready() const { return d_->ready.load(std::memory_order_acquire); }

void ClipWarpStream::configure(int channels, WarpMode mode, float pitchSemitones, double srcSR, double devSR) {
    Impl& d = *d_;
    d.ready.store(false, std::memory_order_release);
    d.channels = std::clamp(channels, 1, 2);
    d.mode = mode;
    d.pitch = pitchSemitones;
    d.devSR = devSR > 0.0 ? devSR : 44100.0;
    const double srSR = srcSR > 0.0 ? srcSR : d.devSR;
    d.rePitch = (mode == WarpMode::RePitch);
    d.expectedOutPos = kNoPos;
    d.fedInt = 0;
    d.lead = 0.0;

    const int cap = d.inputCap();
    for (int c = 0; c < 2; ++c) {
        d.in[c].assign(static_cast<size_t>(cap), 0.0f);
        d.out[c].assign(static_cast<size_t>(kMaxBlock), 0.0f);
        d.inPtr[c] = d.in[c].data();
        d.outPtr[c] = d.out[c].data();
    }

    if (!d.rePitch) {
        configureForMode(d.st, mode, d.channels, d.devSR);
        // Cancel the src≠device sample-rate pitch error (devSR/srcSR) and fold in the
        // user's transpose. RePitch resamples via the marker map, so it's already correct.
        const double factor = std::pow(2.0, static_cast<double>(pitchSemitones) / 12.0) * (srSR / d.devSR);
        d.st.setTransposeFactor(static_cast<float>(factor));
        if (mode == WarpMode::ComplexPro) {
            // Formant preservation: hold the spectral envelope at the source's position
            // while the musical pitch shift moves only the fine structure. With
            // compensatePitch the net formant map is input_envelope(f / formantMultiplier),
            // independent of the transpose. The transpose folds in the src≠device SR
            // correction (srSR/devSR), so the envelope Complex leaves at input(f·devSR/srSR)
            // is matched by setting formantMultiplier = srSR/devSR. That cancels the SR term
            // (SR-independent result, == Complex at pitch 0) and preserves only the musical
            // shift for pitch ≠ 0.
            d.st.setFormantFactor(static_cast<float>(srSR / d.devSR), /*compensatePitch=*/true);
        }
        d.st.reset();
        // Warm-up so the audio thread never grows Signalsmith's temp buffers: a
        // full-size process + a seek + an outputSeek reach their max capacity
        // (process/seek grow tmpProcessBuffer; outputSeek grows tmpPreRollBuffer).
        const int warmIn = std::min(cap, kMaxBlock);
        d.st.process(d.inPtr.data(), warmIn, d.outPtr.data(), kMaxBlock);
        d.st.seek(d.inPtr.data(), d.st.seekLength(), 1.0);
        const int warmSeek = std::min(cap, d.st.inputLatency() + d.st.outputLatency() + 1);
        d.st.outputSeek(d.inPtr.data(), warmSeek);
        d.st.reset();
    }
    d.ready.store(true, std::memory_order_release);
}

void ClipWarpStream::render(float* out, int frames, double outStartSamples,
                            const std::vector<WarpMarker>& markers, double warpBeats,
                            const SampleBuffer& src, double spb, double devSR, float gain) {
    Impl& d = *d_;
    if (frames <= 0 || markers.size() < 2 || spb <= 0.0) return;
    if (!d.ready.load(std::memory_order_acquire)) return;

    // RePitch: stateless varispeed resample straight from the source (pitch rides
    // speed). No stretcher, no seek state — inherently seamless across loops.
    if (d.rePitch) {
        const int srcCh = src.channels;
        for (int i = 0; i < frames; ++i) {
            const double sp = Impl::srcAtOutput(outStartSamples + i, markers, warpBeats, spb);
            const int64_t i0 = static_cast<int64_t>(sp);
            const double frac = sp - i0;
            const float l0 = src.at(i0, 0), l1 = src.at(i0 + 1, 0);
            const float r0 = src.at(i0, srcCh >= 2 ? 1 : 0), r1 = src.at(i0 + 1, srcCh >= 2 ? 1 : 0);
            out[i * 2]     += static_cast<float>(l0 + (l1 - l0) * frac) * gain;
            out[i * 2 + 1] += static_cast<float>(r0 + (r1 - r0) * frac) * gain;
        }
        return;
    }

    const double ratio = Impl::ratioAt(outStartSamples, markers, warpBeats, spb);

    // Discontinuity (start / loop wrap / transport seek) → outputSeek primes the
    // vocoder so the very first process() output is aligned to the source position
    // (no silent pre-roll gap). It consumes `inLen` source frames forward from the
    // target; `lead` records how far the fed input then runs ahead of the output's
    // source position, so the per-block feed below stays anchored (drift-free).
    if (d.expectedOutPos == kNoPos || std::fabs(outStartSamples - d.expectedOutPos) > 0.5) {
        const double srcTarget = Impl::srcAtOutput(outStartSamples, markers, warpBeats, spb);
        int inLen = d.st.inputLatency() + static_cast<int>(std::ceil(ratio * d.st.outputLatency()));
        inLen = std::clamp(inLen, 1, d.inputCap());
        const int64_t start = static_cast<int64_t>(std::llround(srcTarget));
        d.readSource(src, start, inLen);
        d.st.outputSeek(d.inPtr.data(), inLen);
        d.fedInt = start + inLen;
        d.lead = static_cast<double>(d.fedInt) - srcTarget;   // input leads the output's source by this
        d.expectedOutPos = outStartSamples;
    }

    // Feed exactly enough source to keep the vocoder input anchored to the true
    // output→source map (srcAtOutput) plus the fixed priming lead — no rounding
    // drift, so the result is independent of the render block size.
    const double srcTargetAbs = Impl::srcAtOutput(outStartSamples + frames, markers, warpBeats, spb) + d.lead;
    int srcLen = std::clamp(static_cast<int>(std::llround(srcTargetAbs) - d.fedInt), 0, d.inputCap());
    d.readSource(src, d.fedInt, srcLen);
    d.st.process(d.inPtr.data(), srcLen, d.outPtr.data(), frames);
    d.fedInt += srcLen;

    const float* o0 = d.out[0].data();
    const float* o1 = d.channels == 2 ? d.out[1].data() : d.out[0].data();
    for (int i = 0; i < frames; ++i) {
        out[i * 2]     += o0[i] * gain;
        out[i * 2 + 1] += o1[i] * gain;
    }

    d.expectedOutPos = outStartSamples + frames;
}

} // namespace nota
