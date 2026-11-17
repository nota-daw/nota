// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#include "devices/effects/Chroma.h"
#include <cmath>
#include <algorithm>
namespace nota {
namespace {
constexpr double pi = 3.14159265358979323846, tau = 2 * pi;
double wrap(double v) {
    return std::remainder(v, tau);
}
float clean(float v) {
    return std::isfinite(v) && std::abs(v) > 1e-20f ? std::clamp(v, -64.f, 64.f) : 0.f;
}
} // namespace
double Chroma::expMap(double v, double lo, double hi) {
    return lo * std::pow(hi / lo, std::clamp(v, 0., 1.));
}
Chroma::Chroma() {
    const float defaults[] = {0, .25f, 1, .25f, float(std::log(120. / 20) / std::log(50.)),
        float(std::log(150. / 40) / std::log(50.)), float(std::log(8000. / 1000) / std::log(20.)), 1, 2.f / 3};
    for (int i = 0; i < NoteC; ++i) p_[i].store(defaults[i]);
    for (int i = 0; i < 12; ++i) p_[NoteC + i].store((0x5ad >> i) & 1); // C natural minor
    p_[GateThreshold].store(.25f);
    setSampleRate(44100, 1024);
}
const char* Chroma::paramName(int32_t i) const {
    static constexpr const char* names[] = {"Root", "Scale", "Amount", "Resonance", "Decay", "Low", "High", "Mix",
        "Output", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B", "Morph", "Gate", "Gate threshold"};
    return i >= 0 && i < kNumParams ? names[i] : "";
}
float Chroma::getParam(int32_t i) const {
    return i >= 0 && i < kNumParams ? p_[i].load(std::memory_order_relaxed) : 0;
}
void Chroma::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams && std::isfinite(v)) p_[i].store(std::clamp(v, 0.f, 1.f), std::memory_order_relaxed);
}
int Chroma::noteMask() const {
    int scale = int(std::lround(getParam(Scale) * 4));
    if (scale == 4) {
        int mask = 0;
        for (int i = 0; i < 12; ++i)
            if (getParam(NoteC + i) >= .5f) mask |= 1 << i;
        return mask;
    }
    constexpr int masks[] = {0xab5, 0x5ad, 0x295, 0x4a9};
    int root = int(std::lround(getParam(Root) * 11)), m = masks[scale];
    return ((m << root) | (m >> (12 - root))) & 4095;
}
void Chroma::setSampleRate(double sr, int32_t) {
    sr_ = std::isfinite(sr) && sr >= 8000 ? std::min(sr, 768000.) : 44100.;
    n_ = 2048;
    while (n_ < 32768 && sr_ / n_ > 48000. / 2048) n_ *= 2;
    hop_ = n_ / 8;
    bins_ = n_ / 2;
    for (int i = 0; i <= kSpectrumBins; ++i) viewEdges_[i] = 40.0 * std::pow(500.0, double(i) / kSpectrumBins);
    fft_.resize(n_);
    window_.resize(n_);
    time_.resize(n_);
    energy_.resize(bins_);
    previousEnergy_.assign(bins_, 0);
    gateGain_.assign(bins_, 1);
    for (int i = 0; i < n_; ++i) window_[i] = float(.5 - .5 * std::cos(tau * i / n_));
    for (int c = 0; c < 2; ++c) {
        input_[c].assign(n_, 0);
        ola_[c].assign(2 * n_, 0);
        previousPhase_[c].assign(bins_, 0);
        spectrum_[c].assign(bins_, {});
        wet_[c].assign(bins_, {});
        tail_[c].assign(bins_, {});
        tunedViewBins_[c].assign(bins_, {});
        tunedTail_[c].assign(bins_, {});
    }
    rotation_.assign(bins_, 0);
    previousRotation_ = rotation_;
    shift_ = rotation_;
    previousShift_ = rotation_;
    peakHz_ = previousPeakHz_ = rotation_;
    peaks_.assign(bins_, 0);
    owner_.assign(bins_, 0);
    reference_ = previousReference_ = owner_;
    activePeaks_ = previousPeaks_ = matched_ = owner_;
    count_ = quiet_ = 0;
    idle_ = false;
    snap_ = firstFrame_ = true;
    morphS_ = getParam(Morph);
    gateS_ = getParam(Gate) >= .5f ? 1 : 0;
    thresholdS_ = -80 + 80 * getParam(GateThreshold);
    amountS_ = getParam(Amount);
    resS_ = getParam(Resonance);
    lowS_ = expMap(getParam(Low), 40, 2000);
    highS_ = expMap(getParam(High), 1000, 20000);
    for (auto& value : spectrumView_) value.store(0.f, std::memory_order_relaxed);
    for (auto& value : tunedView_) value.store(0.f, std::memory_order_relaxed);
    srView_.store(float(sr_));
    latencyView_.store(float(n_));
}
double Chroma::targetHz(double hz, int mask) const {
    double midi = 69 + 12 * std::log2(std::max(hz, 1.) / 440.), best = 0, dist = 1e9;
    int center = int(std::floor(midi));
    for (int note = center - 12; note <= center + 12; ++note)
        if (mask & (1 << ((note % 12 + 12) % 12))) {
            double d = std::abs(note - midi);
            if (d < dist) {
                dist = d;
                best = note;
            }
        }
    return 440 * std::exp2((best - 69) / 12);
}
void Chroma::frame() {
    const int start = int(count_ % n_);
    for (int c = 0; c < 2; ++c) {
        for (int i = 0; i < n_; ++i) time_[i] = input_[c][(start + i) % n_] * window_[i];
        fft_.fft(time_.data(), spectrum_[c].data());
        std::fill(wet_[c].begin(), wet_[c].end(), Complex{});
        std::fill(tunedViewBins_[c].begin(), tunedViewBins_[c].end(), Complex{});
        wet_[c][0] = spectrum_[c][0]; // Packed DC and Nyquist are never retuned.
    }
    double control = 1 - std::exp(-double(hop_) / (.02 * sr_));
    morphS_ += control * (getParam(Morph) - morphS_);
    gateS_ += control * ((getParam(Gate) >= .5f ? 1 : 0) - gateS_);
    thresholdS_ += control * (-80 + 80 * getParam(GateThreshold) - thresholdS_);
    amountS_ += control * (getParam(Amount) - amountS_);
    resS_ += control * (getParam(Resonance) - resS_);
    lowS_ += control * (expMap(getParam(Low), 40, 2000) - lowS_);
    highS_ += control * (expMap(getParam(High), 1000, 20000) - highS_);
    double low = std::min(lowS_, highS_), high = std::min(std::max(lowS_, highS_), sr_ * .45), binHz = sr_ / n_;
    int mask = noteMask();
    for (int k = 1; k < bins_; ++k) energy_[k] = std::norm(spectrum_[0][k]) + std::norm(spectrum_[1][k]);
    energy_[0] = 0;
    peakCount_ = 0;
    for (int k = 1; k < bins_ - 1; ++k)
        if (energy_[k] > 1e-12f && energy_[k] >= energy_[k - 1] && energy_[k] > energy_[k + 1])
            peaks_[peakCount_++] = k;
    if (!peakCount_) peaks_[peakCount_++] = 1;
    int at = 0;
    for (int k = 1; k < bins_; ++k) {
        while (at + 1 < peakCount_ && 2 * k > peaks_[at] + peaks_[at + 1]) ++at;
        owner_[k] = peaks_[at];
    }
    std::fill(activePeaks_.begin(), activePeaks_.end(), 0);
    std::fill(matched_.begin(), matched_.end(), 0);
    for (int j = 0; j < peakCount_; ++j) {
        int k = peaks_[j];
        int ref = std::norm(spectrum_[1][k]) > std::norm(spectrum_[0][k]) ? 1 : 0;
        auto frequency = [&](int channel) {
            double hz = (k + wrap(std::arg(spectrum_[channel][k]) - previousPhase_[channel][k] - tau * k * hop_ / n_) *
                                 n_ / (tau * hop_)) *
                        binHz;
            return firstFrame_ || hz <= 0 ? k * binHz : hz;
        };
        double hz = frequency(ref), distance = 1e30;
        int prev = -1;
        for (int candidate = std::max(1, k - 4); candidate <= std::min(bins_ - 1, k + 4); ++candidate) {
            if (!previousPeaks_[candidate] || matched_[candidate]) continue;
            double d = std::abs(previousPeakHz_[candidate] - hz);
            if (d < distance || (d == distance && prev >= 0 && previousEnergy_[candidate] > previousEnergy_[prev])) {
                distance = d;
                prev = candidate;
            }
        }
        if (prev >= 0) {
            matched_[prev] = 1;
            ref = previousReference_[prev];
            if (std::norm(spectrum_[1 - ref][k]) > 2 * std::norm(spectrum_[ref][k])) ref = 1 - ref;
            hz = frequency(ref);
        }
        reference_[k] = ref;
        peakHz_[k] = hz;
        activePeaks_[k] = energy_[k] > 1e-12f;
        double weight = std::clamp((hz - low) / std::max(binHz * 2, low * .15), 0., 1.) *
                        std::clamp((high - hz) / std::max(binHz * 2, high * .1), 0., 1.);
        double desired = mask ? (targetHz(hz, mask) - hz) * amountS_ * weight : 0;
        shift_[k] = prev < 0 ? desired : previousShift_[prev] + control * (desired - previousShift_[prev]);
        // Advance the tracked output phase, then relate it to this frame's source phase.
        // Using the same reference channel on both frames preserves the L/R phase offset
        // even when the dominant channel or the FFT peak index changes.
        rotation_[k] = prev < 0 ? 0
                                : wrap(previousPhase_[ref][prev] + previousRotation_[prev] +
                                       tau * (hz + shift_[k]) * hop_ / sr_ - std::arg(spectrum_[ref][k]));
    }
    for (int k = 1; k < bins_; ++k) {
        int peak = owner_[k];
        double hz = k * binHz;
        bool inBand = hz > low && hz < high;
        double delta = inBand ? shift_[peak] : 0;
        int dest = k + int(std::lround(delta / binHz));
        if (dest < 1 || dest >= bins_) {
            dest = k;
            delta = 0;
        }
        Complex rot = delta != 0 ? std::polar(1.f, float(rotation_[peak])) : Complex(1, 0);
        for (int c = 0; c < 2; ++c) {
            auto v = spectrum_[c][k] * rot;
            wet_[c][dest] += v;
            if (std::abs(delta) > hz * 0.0005777895) tunedViewBins_[c][dest] += v;
        }
    }
    // A bounded resonant memory follows each output bin. Its pole radius defines decay;
    // an input gate shortens tails after silence, while the feed coefficient prevents runaway gain.
    double seconds = expMap(getParam(Decay), .02, 1.);
    if (quiet_ > uint64_t(sr_ * .06)) seconds = std::min(seconds, .08);
    float pole = float(std::exp(-double(hop_) / (seconds * sr_)));
    for (int k = 1; k < bins_; ++k) {
        double hz = k * binHz;
        if (mask && hz > low && hz < high) {
            double tuned = targetHz(hz, mask);
            Complex turn = std::polar(pole, float(tau * tuned * hop_ / sr_));
            for (int c = 0; c < 2; ++c) {
                auto& t = tail_[c][k];
                t = t * turn + wet_[c][k] * (1 - pole);
                if (std::norm(t) < 1e-20f) t = {};
                wet_[c][k] += t * float(2 * resS_ * amountS_);
                auto& vt = tunedTail_[c][k];
                vt = vt * turn + tunedViewBins_[c][k] * (1 - pole);
                if (std::norm(vt) < 1e-20f) vt = {};
                tunedViewBins_[c][k] += vt * float(2 * resS_ * amountS_);
            }
        } else
            for (int c = 0; c < 2; ++c) {
                tail_[c][k] = {};
                tunedTail_[c][k] = {};
            }
    }
    // Morph and Gate use shared stereo gains so neither changes relative channel phase.
    const float open = float(1 - std::exp(-double(hop_) / (.005 * sr_)));
    const float close = float(1 - std::exp(-double(hop_) / (.05 * sr_)));
    if (morphS_ > 1e-7 || gateS_ > 1e-7) {
        for (int k = 1; k < bins_; ++k) {
            double hz = k * binHz;
            if (hz <= low || hz >= high || !mask || getParam(Amount) <= 0) {
                gateGain_[k] = 1;
                continue;
            }
            float gain = 1;
            double dryMag = std::sqrt(energy_[k]);
            double wetMag = std::sqrt(std::norm(wet_[0][k]) + std::norm(wet_[1][k]));
            if (morphS_ > 1e-7 && wetMag > 1e-10) gain = float((1 - morphS_) + morphS_ * dryMag / wetMag);
            if (gateS_ > 1e-7) {
                double db = 20 * std::log10(std::max(1e-15, dryMag / (n_ * .35355339)));
                double knee = std::clamp((db - thresholdS_ + 3) / 6, 0., 1.);
                float target = float(knee * knee * (3 - 2 * knee));
                gateGain_[k] += (target > gateGain_[k] ? open : close) * (target - gateGain_[k]);
                gain *= float(1 + gateS_ * (gateGain_[k] - 1));
            } else
                gateGain_[k] = 1;
            for (int c = 0; c < 2; ++c) {
                wet_[c][k] *= gain;
                tunedViewBins_[c][k] *= gain;
            }
        }
    } else
        std::fill(gateGain_.begin(), gateGain_.end(), 1.f);
    // Publish independent dry and shifted spectra; all storage is prepared off-thread.
    float mixVis = mask && getParam(Amount) > 0 ? mixS_ : 0.f;
    auto publish = [&](int i, bool tuned) {
        double hz0 = viewEdges_[i];
        double hz1 = viewEdges_[i + 1];
        if (hz0 >= sr_ * .5) return 0.f;
        auto energy = [&](int k) {
            return tuned ? (std::norm(tunedViewBins_[0][k]) + std::norm(tunedViewBins_[1][k])) * mixVis * mixVis
                         : energy_[k];
        };
        double fb = std::clamp((hz0 + hz1) * .5 / binHz, 1.0, double(bins_ - 1));
        int k = std::min(int(fb), bins_ - 2);
        float f = float(fb - k), e = (1 - f) * energy(k) + f * energy(k + 1);
        int k0 = std::clamp(int(std::ceil(hz0 / binHz)), 1, bins_ - 1);
        int k1 = std::clamp(int(std::floor(hz1 / binHz)), 1, bins_ - 1);
        for (int j = k0; j <= k1; ++j) e = std::max(e, energy(j));
        float amp = std::sqrt(e) / float(n_ * .35355339);
        return amp > 1e-7f ? std::clamp((20.f * std::log10(amp) + 80.f) / 80.f, 0.f, 1.f) : 0.f;
    };
    for (int i = 0; i < kSpectrumBins; ++i) {
        spectrumView_[i].store(publish(i, false), std::memory_order_relaxed);
        tunedView_[i].store(publish(i, true), std::memory_order_relaxed);
    }
    for (int c = 0; c < 2; ++c) {
        for (int k = 1; k < bins_; ++k) previousPhase_[c][k] = std::arg(spectrum_[c][k]);
        fft_.ifft(wet_[c].data(), time_.data());
        for (int i = 0; i < n_; ++i) ola_[c][(count_ + i) % (2 * n_)] += time_[i] * window_[i] / float(3 * n_);
    }
    previousPeaks_.swap(activePeaks_);
    previousPeakHz_.swap(peakHz_);
    previousReference_.swap(reference_);
    std::copy(energy_.begin(), energy_.end(), previousEnergy_.begin());
    previousRotation_.swap(rotation_);
    previousShift_.swap(shift_);
    firstFrame_ = false;
}
void Chroma::process(float* buf, int32_t frames) {
    if (!buf || frames <= 0 || bypassed()) return;
    const float coeff = float(1 - std::exp(-1 / (.01 * sr_)));
    float mixTarget = noteMask() && getParam(Amount) > 0 ? getParam(Mix) : 0;
    float gainTarget = float(std::pow(10., (-24 + 36 * getParam(Output)) / 20));
    if (snap_) {
        mixS_ = mixTarget;
        gainS_ = gainTarget;
        snap_ = false;
    }
    for (int i = 0; i < frames; ++i) {
        int in = int(count_ % n_), out = int(count_ % (2 * n_));
        float l = clean(buf[2 * i]), r = clean(buf[2 * i + 1]);
        quiet_ = std::max(std::abs(l), std::abs(r)) < 1e-8f ? quiet_ + 1 : 0;
        mixS_ += coeff * (mixTarget - mixS_);
        gainS_ += coeff * (gainTarget - gainS_);
        for (int c = 0; c < 2; ++c) {
            float dry = input_[c][in], wet = ola_[c][out];
            ola_[c][out] = 0;
            input_[c][in] = c ? r : l;
            buf[2 * i + c] = clean((dry + mixS_ * (wet - dry)) * gainS_);
        }
        ++count_;
        if (count_ % hop_ == 0) frame();
    }
    // Delay and gated tails have drained before the engine may stop calling this device.
    idle_ = quiet_ > uint64_t(sr_ * 3) + 2 * uint64_t(n_);
}
int32_t Chroma::scopeRead(float* out, int32_t max) const {
    if (!out || max < 2) return 0;
    out[0] = srView_.load();
    out[1] = latencyView_.load();
    int count = std::min(max - 2, 2 * kSpectrumBins);
    for (int i = 0; i < count; ++i)
        out[i + 2] = i < kSpectrumBins ? spectrumView_[i].load(std::memory_order_relaxed)
                                       : tunedView_[i - kSpectrumBins].load(std::memory_order_relaxed);
    return 2 + count;
}
} // namespace nota
