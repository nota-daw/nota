// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "ColorHelpers.h"
#include <memory>
#include <iterator>
#include <cstdlib>
#include <cstdint>
#include <cstddef>
#include <complex>
#include <cmath>
#include <bit>
#include <atomic>
#include <array>
#include <algorithm>
#include "nota/dsp/primitives/BiquadCascade.h"
#include "effects/color/Color.h"
#include "common/DeviceMath.h"

namespace nota {
using namespace color_detail;

double Color::expMap(double v, double lo, double hi) {
    return lo * std::pow(hi / lo, std::clamp(v, 0., 1.));
}

Color::Color() : pre_(std::make_unique<PreShifter>()) {
    const float defaults[] = {0, .25f, 1, .25f, float(std::log(120. / 20) / std::log(50.)),
        float(std::log(150. / 40) / std::log(50.)), float(std::log(8000. / 1000) / std::log(20.)), 1, 2.f / 3};
    for (int i = 0; i < NoteC; ++i) p_[static_cast<size_t>(i)].store(defaults[i]);
    for (int i = 0; i < kPitchClasses; ++i) p_[static_cast<size_t>(NoteC + i)].store((0x5ad >> i) & 1);
    p_[GateThreshold].store(.25f);
    p_[FftSize].store(.5f);
    p_[Pre].store(.5f);
    p_[Post].store(.5f);
    setSampleRate(44100, 1024);
}

Color::~Color() = default;

int32_t Color::latencySamples() const {
    return kFftSizes[static_cast<size_t>(requestedFft())] * (requestedGate() ? 2 : 1) +
           (requestedPre() ? pre_->latency.load(std::memory_order_relaxed) : 0);
}

const char* Color::paramName(int32_t i) const {
    static constexpr const char* names[] = {"Root", "Scale", "Amount", "Resonance", "Decay", "Low", "High", "Mix",
        "Output", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B", "Morph", "Gate", "Gate threshold",
        "", "Mute low", "Mute high", "View", "FFT size", "Pre", "Post", "Pre enabled"};
    static_assert(std::size(names) == kNumParams);
    return i >= 0 && i < kNumParams ? names[i] : "";
}

float Color::getParam(int32_t i) const {
    return i >= 0 && i < kNumParams ? p_[static_cast<size_t>(i)].load(std::memory_order_relaxed) : 0;
}

void Color::setParam(int32_t i, float v) {
    if (i < 0 || i >= kNumParams || i == RetiredMode || !std::isfinite(v)) return;
    v = std::clamp(v, 0.f, 1.f);
    p_[static_cast<size_t>(i)].store(i == FftSize ? .5f * std::round(2 * v) : v, std::memory_order_relaxed);
}

int Color::requestedFft() const {
    return int(2 * getParam(FftSize));
}

int Color::noteMask() const {
    const int scale = int(std::lround(getParam(Scale) * 4));
    if (scale == 4) {
        int mask = 0;
        for (int i = 0; i < kPitchClasses; ++i)
            if (getParam(NoteC + i) >= .5f) mask |= 1 << i;
        return mask;
    }
    constexpr int masks[] = {0xab5, 0x5ad, 0x295, 0x4a9};
    const int root = int(std::lround(getParam(Root) * 11));
    return ((masks[scale] << root) | (masks[scale] >> (kPitchClasses - root))) & kAllNotes;
}

void Color::setSidechainSourceTrackId(int32_t id) {
    if (midiSource_.exchange(id, std::memory_order_relaxed) != id) midiMaskView_.store(0, std::memory_order_relaxed);
}

void Color::setMidiKeyBlock(const MidiKeyBlock& block) {
    midiBlockSource_ = block.sourceTrack;
    midiStart_ = block.start ? *block.start : MidiKeyNotes{};
    midiEventCount_ = block.start ? std::min(block.count, int32_t(midiEvents_.size())) : 0;
    if (midiEventCount_ > 0) std::copy_n(block.events, midiEventCount_, midiEvents_.data());
    midiMaskView_.store(block.endMask, std::memory_order_relaxed);
}

void Color::StereoFilter::tune(dsp::BiquadCoeffs coeff) {
    for (auto& section : channel) section.c = coeff;
}

Color::Stereo Color::StereoFilter::run(Stereo in) {
    return {float(channel[0].run(in[0])), float(channel[1].run(in[1]))};
}

Color::Stereo Color::BassSpectrum::Line::filter(Stereo sample) {
    for (auto& section : lowpass) sample = section.run(sample);
    return sample;
}

void Color::BassSpectrum::prepare(double sr, const ViewEdges& edges) {
    // Keep the decimated rate low enough to fit 180 ms in the FFT and high enough
    // to leave a wide anti-alias transition above the 400 Hz display band.
    stride = int(std::ceil(sr / 5600));
    const double rate = sr / stride;
    binsPerHz = kFftSize / rate;
    windowSize = int(std::lround(.180 * rate));
    fft.resize(kFftSize);
    double windowSum = 0;
    for (int i = 0; i < windowSize; ++i) {
        window[static_cast<size_t>(i)] = float(.5 - .5 * std::cos(tau * i / windowSize));
        windowSum += window[static_cast<size_t>(i)];
    }
    // Match the short analysis's coherent gain before blending the two powers.
    const double scale = kScopeNormalizer / (windowSum * butterworthQ);
    powerScale = float(scale * scale);
    time.fill(0);

    constexpr std::array qs{.517638090205, butterworthQ, 1.931851652578}; // Sixth-order Butterworth.
    for (size_t i = 0; i < qs.size(); ++i) {
        const auto coeffs = dsp::BiquadCascade::lowpass(sr, 650, qs[i]);
        dry.lowpass[i].tune(coeffs);
        tuned.lowpass[i].tune(coeffs);
    }
    viewBins = 0;
    for (int i = 0; i < kSpectrumBins; ++i) {
        const double center = (edges[static_cast<size_t>(i)] + edges[static_cast<size_t>(i + 1)]) * .5;
        const float t = float(std::clamp((center - 250) / (400 - 250), 0., 1.));
        shortWeights[static_cast<size_t>(i)] = t * t * (3 - 2 * t);
        if (shortWeights[static_cast<size_t>(i)] < 1) viewBins = i + 1;
    }
}

void Color::BassSpectrum::reset() {
    for (auto* line : {&dry, &tuned}) {
        for (auto& section : line->lowpass)
            for (auto& channel : section.channel) channel.reset();
        line->history.fill({});
        line->viewPower.fill(0);
    }
    write = 0;
    untilNext = stride;
    refresh = true;
}

void Color::BassSpectrum::push(Stereo drySample, Stereo tunedSample) {
    const Stereo dryFiltered = dry.filter(drySample);
    const Stereo tunedFiltered = tuned.filter(tunedSample);
    if (--untilNext != 0) return;
    dry.history[static_cast<size_t>(write)] = dryFiltered;
    tuned.history[static_cast<size_t>(write)] = tunedFiltered;
    write = (write + 1) % kFftSize;
    untilNext = stride;
}

void Color::BassSpectrum::analyse(Line& line, const ViewEdges& edges) {
    power.fill(0);
    const int start = write + kFftSize - windowSize;
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < windowSize; ++i)
            time[static_cast<size_t>(i)] =
                line.history[static_cast<size_t>((start + i) % kFftSize)][static_cast<size_t>(ch)] *
                window[static_cast<size_t>(i)];
        fft.fft(time.data(), bins.data());
        for (int k = 1; k < kFftBins; ++k) power[static_cast<size_t>(k)] += std::norm(bins[static_cast<size_t>(k)]);
    }
    for (int i = 0; i < viewBins; ++i)
        line.viewPower[static_cast<size_t>(i)] =
            powerScale * spectrumPower(power, edges[static_cast<size_t>(i)] * binsPerHz,
                             edges[static_cast<size_t>(i + 1)] * binsPerHz);
}

dsp::BiquadCoeffs Color::NoteTuning::bell(double q, double gain) const {
    // The RBJ bell in BiquadCascade with cached angular terms and linear gain.
    const double a = std::sqrt(gain);
    const double alpha = sinOmega / (2 * q);
    const double numerator = alpha * a, denominator = alpha / a;
    const double a0 = 1 + denominator;
    const double b1 = -2 * cosOmega / a0;
    return {(1 + numerator) / a0, b1, (1 - numerator) / a0, b1, (1 - denominator) / a0};
}

void Color::Bank::tune(
    const NoteTunings& tunings, double sr, int mask, double low, double high, Shape shape, double q, double gain) {
    const double first = low / 1.8, last = high * 1.8;
    int candidates = 0;
    count = 0;
    for (int i = 0; i <= kLastMidi - kFirstMidi; ++i) {
        const int bit = 1 << ((i + kFirstMidi) % kPitchClasses);
        const auto& tuning = tunings[static_cast<size_t>(i)];
        const double frequency = tuning.frequency;
        const bool selected = (mask & bit) != 0;
        if (selected) ++candidates;
        const bool enabled =
            selected && candidates <= kBankCapacity && frequency >= first && frequency <= last && frequency < .49 * sr;
        auto& note = notes[static_cast<size_t>(i)];
        if (enabled) {
            if (shape == Shape::Bell) {
                const double edge =
                    std::min({1., (frequency - first) / (low - first), (last - frequency) / (last - high)});
                note.coeffs = tuning.bell(q, 1 + edge * (gain - 1));
            } else if (!note.active || note.frequency != frequency) {
                note.coeffs = shape == Shape::InputNotch ? tuning.inputNotch : tuning.rejectNotch;
            }
            note.frequency = frequency;
            active[static_cast<size_t>(count++)] = i;
        } else if (note.active) {
            note.sections = {};
        }
        note.active = enabled;
    }
}

Color::Stereo Color::Bank::run(Stereo in) {
#if defined(NOTA_SIMD_NEON) || defined(NOTA_SIMD_SSE)
#if defined(NOTA_SIMD_NEON)
    auto x = vcvt_f64_f32(vld1_f32(in.data()));
#else
    auto x = _mm_setr_pd(in[0], in[1]);
#endif
    // Each note's two DF2T sections share coefficients and keep independent L/R histories.
    for (int i = 0; i < count; ++i) {
        auto& note = notes[static_cast<size_t>(active[static_cast<size_t>(i)])];
        const auto& c = note.coeffs;
        for (auto& stage : note.sections) {
#if defined(NOTA_SIMD_NEON)
            const auto y = vfmaq_n_f64(vld1q_f64(stage.z1.data()), x, c.b0);
            const auto z1 = vfmaq_n_f64(vmulq_n_f64(y, -c.a1), x, c.b1);
            const auto z2 = vfmaq_n_f64(vmulq_n_f64(y, -c.a2), x, c.b2);
            vst1q_f64(stage.z1.data(), vaddq_f64(z1, vld1q_f64(stage.z2.data())));
            vst1q_f64(stage.z2.data(), z2);
            x = y;
#else
            const auto y = _mm_add_pd(_mm_mul_pd(_mm_set1_pd(c.b0), x), _mm_load_pd(stage.z1.data()));
            const auto z1 = _mm_sub_pd(_mm_mul_pd(_mm_set1_pd(c.b1), x), _mm_mul_pd(_mm_set1_pd(c.a1), y));
            const auto z2 = _mm_sub_pd(_mm_mul_pd(_mm_set1_pd(c.b2), x), _mm_mul_pd(_mm_set1_pd(c.a2), y));
            _mm_store_pd(stage.z1.data(), _mm_add_pd(z1, _mm_load_pd(stage.z2.data())));
            _mm_store_pd(stage.z2.data(), z2);
            x = y;
#endif
        }
    }
#if defined(NOTA_SIMD_NEON)
    vst1_f32(in.data(), vcvt_f32_f64(x));
    return in;
#else
    const auto result = _mm_cvtpd_ps(x);
    return {_mm_cvtss_f32(result), _mm_cvtss_f32(_mm_shuffle_ps(result, result, _MM_SHUFFLE(1, 1, 1, 1)))};
#endif
#else
    const auto process = [](const dsp::BiquadCoeffs& c, Section& state, int ch, double x) {
        dsp::BiquadSection section{c, state.z1[ch], state.z2[ch]};
        const double y = section.run(x);
        state.z1[ch] = section.z1;
        state.z2[ch] = section.z2;
        return y;
    };
    double left = in[0], right = in[1];
    for (int i = 0; i < count; ++i) {
        auto& note = notes[active[i]];
        left = process(note.coeffs, note.sections[1], 0, process(note.coeffs, note.sections[0], 0, left));
        right = process(note.coeffs, note.sections[1], 1, process(note.coeffs, note.sections[0], 1, right));
    }
    return {float(left), float(right)};
#endif
}

void Color::Shifter::reset() {
    input.fill({});
    output.fill({});
    std::fill(rmsHistory.begin(), rmsHistory.end(), 0.f);
    rmsSum = lastRms = onsetEnvelope = 0;
    rmsPosition = refractory = 0;
    firstFrame = true;
}

void Color::Shifter::push(Stereo in, uint64_t sample) {
    input[sample % kMaxWindow] = in;
    const float power = .5f * (in[0] * in[0] + in[1] * in[1]);
    rmsSum += double(power) - rmsHistory[static_cast<size_t>(rmsPosition)];
    rmsHistory[static_cast<size_t>(rmsPosition)] = power;
    rmsPosition = (rmsPosition + 1) % int(rmsHistory.size());
}

void Color::Shifter::analyse(Color& effect, uint64_t end) {
    auto& config = effect.processingFft();
    const double rms = std::sqrt(std::max(0., rmsSum) / double(rmsHistory.size()));
    const double rise = std::max(0., rms - lastRms);
    lastRms = rms;
    const bool onset = refractory == 0 && rise > .001 && rise > 8 * onsetEnvelope;
    onsetEnvelope = rise + config.onsetPole * (onsetEnvelope - rise);
    if (onset) refractory = int(std::lround(.08 * effect.sr_)) / config.hop;
    else if (refractory > 0)
        --refractory;

    const uint64_t start = end + kMaxWindow - static_cast<uint64_t>(config.size);
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < config.size; ++i)
            effect.time_[static_cast<size_t>(i)] =
                input[(start + static_cast<uint64_t>(i)) % kMaxWindow][static_cast<size_t>(ch)] *
                config.window[static_cast<size_t>(i)];
        auto& bins = spectrum[static_cast<size_t>(ch)];
        config.fft.fft(effect.time_.data(), bins.data());
        bins[0] = {bins[0].real(), 0}; // Packed Nyquist is outside the correction band.
        for (int k = 1; k < config.bins; ++k) {
            const double phase = std::arg(bins[static_cast<size_t>(k)]);
            if (onset || firstFrame) {
                synthesisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)] = phase;
            } else {
                const double advance = config.phaseAdvance[static_cast<size_t>(k)];
                const double delta =
                    wrapPhase(phase - analysisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)] - advance);
                synthesisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)] = wrapPhase(
                    synthesisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)] + ratio * (advance + delta));
                bins[static_cast<size_t>(k)] = std::polar(std::abs(bins[static_cast<size_t>(k)]),
                    float(synthesisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)]));
            }
            analysisPhase[static_cast<size_t>(ch)][static_cast<size_t>(k)] = phase;
        }
    }
    firstFrame = false;
}

void Color::Shifter::accumulate(Magnitudes& power, int bins) const {
    for (int k = 1; k < bins; ++k) {
        const double destination = k * ratio;
        const int bin = int(destination);
        if (bin >= bins) break;
        const float frac = float(destination - bin);
        const float value =
            std::norm(spectrum[0][static_cast<size_t>(k)]) + std::norm(spectrum[1][static_cast<size_t>(k)]);
        power[static_cast<size_t>(bin)] += (1 - frac) * value;
        power[static_cast<size_t>(bin + 1)] += frac * value;
    }
}

void Color::Shifter::synthesize(Color& effect, uint64_t end, const Magnitudes* gain) {
    auto& config = effect.processingFft();
    const int length = int(std::lround(config.size / ratio));
    const float normalization = 2.f * static_cast<float>(config.hop) / (static_cast<float>(config.size * config.size));
    for (int ch = 0; ch < 2; ++ch) {
        auto& bins = spectrum[static_cast<size_t>(ch)];
        if (gain) {
            for (int k = 1; k < config.bins; ++k) {
                const double destination = k * ratio;
                const int bin = int(destination);
                if (bin >= config.bins) {
                    bins[static_cast<size_t>(k)] = {};
                    continue;
                }
                bins[static_cast<size_t>(k)] *= std::lerp(
                    (*gain)[static_cast<size_t>(bin)], (*gain)[static_cast<size_t>(bin + 1)], float(destination - bin));
            }
        }
        config.fft.ifft(bins.data(), effect.time_.data());
        for (int i = 0; i < config.size; ++i)
            effect.time_[static_cast<size_t>(i)] *= config.window[static_cast<size_t>(i)];
        for (int i = 0; i < length; ++i) {
            const double read = i * (double(config.size) / length);
            const int j = int(read);
            const auto& x = effect.time_;
            const float value = cubic(x[static_cast<size_t>(std::max(0, j - 1))], x[static_cast<size_t>(j)],
                x[static_cast<size_t>(std::min(j + 1, config.size - 1))],
                x[static_cast<size_t>(std::min(j + 2, config.size - 1))], float(read - j));
            output[(end + static_cast<uint64_t>(i)) % kRing][static_cast<size_t>(ch)] += value * normalization;
        }
    }
}

Color::Stereo Color::Shifter::pop(uint64_t sample) {
    auto& slot = output[sample % kRing];
    const Stereo value = slot;
    slot = {};
    return value;
}

void Color::Path::reset(int newMask) {
    mask = newMask;
    down = up = common = 0;
    if (active()) {
        const int outside = kAllNotes ^ mask;
        for (int note = 0; note < kPitchClasses; ++note) {
            const int bit = 1 << note;
            if (!(outside & bit)) continue;
            if (mask & (1 << ((note + 11) % kPitchClasses))) down |= bit;
            else if (mask & (1 << ((note + 1) % kPitchClasses)))
                up |= bit;
        }
        common = kAllNotes ^ (down | up);
    }
    shared = {};
    downInput = {};
    upInput = {};
    reject = {};
    resonator = {};
    correctionHigh = {};
    correctionLow = {};
    for (auto& branch : shift) branch.reset();
    notchGain = dbToGain(1 + 1.9 * (std::popcount(unsigned(kAllNotes ^ mask)) - 1));
    resonanceGain = resonanceGainTarget = 1;
    resonanceBlend = 0;
    dirty = true;
}

void Color::Path::tune(const Color& effect) {
    if (!active()) return;
    const double sr = effect.sr_, low = effect.low_, high = effect.high_;
    const auto& tunings = effect.postCents_ == 0 ? effect.noteTunings_ : effect.shiftedNoteTunings_;
    const bool bandChanged = dirty || low != tunedLow || high != tunedHigh || tunedPost != effect.postCents_;
    if (bandChanged) {
        shared.tune(tunings, sr, common, low, high, Bank::Shape::InputNotch);
        downInput.tune(tunings, sr, up, low, high, Bank::Shape::InputNotch);
        upInput.tune(tunings, sr, down, low, high, Bank::Shape::InputNotch);
        reject.tune(tunings, sr, kAllNotes ^ mask, low, high, Bank::Shape::RejectNotch);
        const double sourceLow = shared.count ? tunings[static_cast<size_t>(shared.active[0])].frequency : low;
        const double sourceHigh =
            shared.count ? tunings[static_cast<size_t>(shared.active[static_cast<size_t>(shared.count - 1)])].frequency
                         : high;
        correctionHigh.tune(dsp::BiquadCascade::highpass(sr, std::min(sourceLow * 1.5, .49 * sr), butterworthQ));
        correctionLow.tune(dsp::BiquadCascade::lowpass(sr, sourceHigh / 1.5, butterworthQ));
        tunedLow = low;
        tunedHigh = high;
        tunedPost = effect.postCents_;
    }
    const double r = effect.resonance_;
    const double q = (18 + r) * effect.decay_ / .12;
    const double gain = 1 + 5 * r;
    if (r > 0 && (bandChanged || q != tunedQ || gain != tunedGain)) {
        resonator.tune(tunings, sr, mask, low, high, Bank::Shape::Bell, q, gain);
        const double density = (std::popcount(unsigned(mask)) - 1) / 11.;
        resonanceGainTarget = dbToGain(-34 * std::pow(r, compensationCurve) * .7 * (1 + density));
        tunedQ = q;
        tunedGain = gain;
    } else if (r == 0) {
        resonanceGainTarget = 1;
        if (resonanceBlend == 0 && resonator.count) {
            resonator = {};
            tunedGain = -1;
        }
    }
    dirty = false;
}

void Color::Path::frame(Color& effect, uint64_t end, const Magnitudes* dry) {
    if (!down) return;
    shift[0].analyse(effect, end);
    if (up) shift[1].analyse(effect, end);

    Magnitudes gains{};
    if (dry) {
        const auto& config = effect.processingFft();
        Magnitudes power{};
        shift[0].accumulate(power, config.bins);
        if (up) shift[1].accumulate(power, config.bins);
        for (int k = 0; k <= config.bins; ++k) {
            const float wet = std::sqrt(power[static_cast<size_t>(k)]);
            const float target = (*dry)[static_cast<size_t>(k)];
            gains[static_cast<size_t>(k)] =
                wet > 1e-8f ? std::lerp(1.f, std::min(target / wet, 16.f), effect.morph_) : 1.f;
        }
    }
    shift[0].synthesize(effect, end, dry ? &gains : nullptr);
    if (up) shift[1].synthesize(effect, end, dry ? &gains : nullptr);
}

Color::Stereo Color::Path::process(
    Color& effect, Stereo mid, Stereo dry, float transient, Stereo dynamics, uint64_t sample, Stereo& tuned) {
    tuned = {};
    if (!active()) return dry;
    if (down) {
        const Stereo source = shared.run(mid);
        shift[0].push(downInput.run(source), sample);
        Stereo correction = shift[0].pop(sample);
        if (up) {
            shift[1].push(upInput.run(source), sample);
            const Stereo raised = shift[1].pop(sample);
            for (int ch = 0; ch < 2; ++ch) correction[static_cast<size_t>(ch)] += raised[static_cast<size_t>(ch)];
        }
        for (int ch = 0; ch < 2; ++ch) correction[static_cast<size_t>(ch)] *= 1.6f * dynamics[static_cast<size_t>(ch)];
        tuned = correctionLow.run(correctionHigh.run(correction));
    }
    Stereo input{dry[0] + tuned[0], dry[1] + tuned[1]};
    Stereo wet = reject.run(input);
    for (auto& value : wet) value *= notchGain;
    const double r = effect.resonance_;
    resonanceBlend = approach(resonanceBlend, r > 0 ? 1.f : 0.f, effect.blendStep_);
    if (resonator.count && resonanceBlend > 0) {
        resonanceGain = resonanceGainTarget + effect.makeupPole_ * (resonanceGain - resonanceGainTarget);
        const Stereo resonated = resonator.run(wet);
        for (int ch = 0; ch < 2; ++ch)
            wet[static_cast<size_t>(ch)] = std::lerp(
                wet[static_cast<size_t>(ch)], resonated[static_cast<size_t>(ch)] * resonanceGain, resonanceBlend);
    }
    for (int ch = 0; ch < 2; ++ch) {
        tuned[static_cast<size_t>(ch)] *= effect.amount_ * (1 - transient);
        wet[static_cast<size_t>(ch)] =
            std::lerp(dry[static_cast<size_t>(ch)], wet[static_cast<size_t>(ch)], effect.amount_ * (1 - transient));
    }
    return wet;
}

void Color::SpectralGate::reset() {
    input.fill({});
    output.fill({});
    gain.fill(0);
    firstFrame = true;
}

Color::OutputFrame Color::SpectralGate::push(const OutputFrame& frame, uint64_t sample, int window) {
    auto& slot = input[sample & (static_cast<uint64_t>(window - 1))];
    auto delayed = slot;
    slot = frame;
    auto& gated = output[sample % kRing];
    for (int ch = 0; ch < 2; ++ch)
        delayed.wet[static_cast<size_t>(ch)] =
            std::lerp(delayed.wet[static_cast<size_t>(ch)], gated[static_cast<size_t>(ch)], delayed.gatedFraction);
    gated = {};
    return delayed;
}

void Color::SpectralGate::analyse(Color& effect, uint64_t end) {
    auto& config = effect.processingFft();
    auto& time = effect.time_;
    auto& bins = effect.fftBins_;
    const auto windowMask = uint64_t(config.size - 1);
    // A full ring ending at end - 1 starts at the next write position.
    const auto sampleAt = [&](int i) -> const OutputFrame& {
        return input[(end + static_cast<uint64_t>(i)) & windowMask];
    };

    Magnitudes power{};
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < config.size; ++i)
            time[static_cast<size_t>(i)] =
                sampleAt(i).dry[static_cast<size_t>(ch)] * config.window[static_cast<size_t>(i)];
        config.fft.fft(time.data(), bins.data());
        // RealFFT packs DC and Nyquist together. Neither has a negative-frequency partner.
        power[0] += .25f * bins[0].real() * bins[0].real();
        power[static_cast<size_t>(config.bins)] += .25f * bins[0].imag() * bins[0].imag();
        for (int k = 1; k < config.bins; ++k) power[static_cast<size_t>(k)] += std::norm(bins[static_cast<size_t>(k)]);
    }
    const float threshold = .450158f * static_cast<float>(config.size) * dbToGain(effect.gateThreshold_);
    const float thresholdPower = threshold * threshold;
    for (int k = 0; k <= config.bins; ++k) {
        const float target = power[static_cast<size_t>(k)] >= thresholdPower ? 1.f : 0.f;
        if (firstFrame) gain[static_cast<size_t>(k)] = target;
        else
            gain[static_cast<size_t>(k)] +=
                (target - gain[static_cast<size_t>(k)]) *
                (target > gain[static_cast<size_t>(k)] ? config.gateOpen : config.gateClose);
    }
    firstFrame = false;

    // This sqrt-Hann pair has a constant sum of squared windows at a quarter hop.
    const float normalization = 2.f * static_cast<float>(config.hop) / (static_cast<float>(config.size * config.size));
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < config.size; ++i)
            time[static_cast<size_t>(i)] =
                sampleAt(i).wet[static_cast<size_t>(ch)] * config.window[static_cast<size_t>(i)];
        config.fft.fft(time.data(), bins.data());
        bins[0] = {bins[0].real() * gain[0], bins[0].imag() * gain[static_cast<size_t>(config.bins)]};
        for (int k = 1; k < config.bins; ++k) bins[static_cast<size_t>(k)] *= gain[static_cast<size_t>(k)];
        config.fft.ifft(bins.data(), time.data());
        for (int i = 0; i < config.size; ++i)
            output[(end + static_cast<uint64_t>(i)) % kRing][static_cast<size_t>(ch)] +=
                time[static_cast<size_t>(i)] * config.window[static_cast<size_t>(i)] * normalization;
    }
}

float Color::SpectralGate::gainAt(double bin) const {
    const double bounded = std::clamp(bin, 0., double(kMaxBins));
    const int low = int(bounded);
    return std::lerp(
        gain[static_cast<size_t>(low)], gain[static_cast<size_t>(std::min(low + 1, kMaxBins))], float(bounded - low));
}

double Color::Envelope::run(double input, double attack, double release) {
    value = input + (input > value ? attack : release) * (value - input);
    return value;
}

float Color::pole(double ms) const {
    return float(std::exp(-tau * 1000 / (sr_ * ms)));
}

void Color::prepareNoteTunings(NoteTunings& tunings, double cents) {
    const double ratio = std::exp2(cents / 1200.);
    for (int i = 0; i <= kLastMidi - kFirstMidi; ++i) {
        auto& tuning = tunings[static_cast<size_t>(i)];
        tuning.frequency = cents == 0 ? 440 * std::exp2((i + kFirstMidi - 69) / 12.)
                                      : noteTunings_[static_cast<size_t>(i)].frequency * ratio;
        if (tuning.frequency >= .49 * sr_) continue;
        // Match BiquadCascade's angular convention for its RBJ coefficients.
        const double omega = 2.0 * 3.14159265358979 * tuning.frequency / sr_;
        tuning.sinOmega = std::sin(omega);
        tuning.cosOmega = std::cos(omega);
        tuning.inputNotch = dsp::BiquadCascade::notch(sr_, tuning.frequency, inputNotchQ);
        tuning.rejectNotch = dsp::BiquadCascade::notch(sr_, tuning.frequency, rejectNotchQ);
    }
}

void Color::updatePost() {
    const double target = (double(getParam(Post)) - .5) * 200;
    postSmooth_ = snap_ || std::abs(postSmooth_ - target) < .01 ? target : target + bandPole_ * (postSmooth_ - target);
    const double cents = std::round(postSmooth_ * 20.) / 20.;
    if (cents == postCents_) return;
    postCents_ = cents;
    if (cents != 0) prepareNoteTunings(shiftedNoteTunings_, cents);
}

} // namespace nota
