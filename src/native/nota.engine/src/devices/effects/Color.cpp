// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#include "devices/effects/Color.h"
#include "dsp/math/Simd.h"

#include <algorithm>
#include <bit>
#include <cmath>
#include <numbers>
#include <utility>

namespace nota {
namespace {
constexpr double tau = 2 * std::numbers::pi;
constexpr double butterworthQ = std::numbers::sqrt2 / 2;
constexpr double inputNotchQ = 16, rejectNotchQ = 12.5;
constexpr float silence = 3.16227766e-5f;               // -90 dBFS peak.
constexpr double resonanceCurve = 1.736965594166206;    // log(.3) / log(.5).
constexpr double compensationCurve = .6611980865482415; // log(21.5 / 34) / log(.5).

float clean(float value) {
    return std::isfinite(value) && std::abs(value) > 1e-20f ? value : 0.f;
}

float dbToGain(double db) {
    return float(std::pow(10., db / 20));
}

float approach(float value, float target, float step) {
    return value + std::clamp(target - value, -step, step);
}

double wrapPhase(double phase) {
#if defined(NOTA_SIMD_NEON) || defined(NOTA_SIMD_FMA)
    // The configured hops and ratios keep both phase increments below this bound.
    if (std::abs(phase) < 8192.) {
        const double revolutions = phase * (1 / tau);
#if defined(NOTA_SIMD_NEON)
        const double turns = vget_lane_f64(vrndn_f64(vdup_n_f64(revolutions)), 0);
        const double wrapped = std::fma(-turns, tau, phase);
#else
        const auto turns = _mm_round_sd(_mm_setzero_pd(), _mm_set_sd(revolutions), _MM_FROUND_TO_NEAREST_INT);
        const double wrapped = _mm_cvtsd_f64(_mm_fnmadd_sd(turns, _mm_set_sd(tau), _mm_set_sd(phase)));
#endif
        // Resolve quotient rounding near a half-turn with the exact remainder operation.
        if (std::abs(wrapped) < std::numbers::pi - 1e-11)
            return wrapped == 0. ? std::copysign(0., phase) : wrapped;
    }
#endif
    return std::remainder(phase, tau);
}

float cubic(float a, float b, float c, float d, float frac) {
    const float slope = .5f * (c - a);
    const float curvature = b - c + slope;
    const float cubicTerm = .5f * (d - b) + curvature + b - c;
    return ((cubicTerm * frac - (cubicTerm + curvature)) * frac + slope) * frac + b;
}

template <size_t N>
float spectrumPower(const std::array<float, N>& power, double first, double last) {
    constexpr int bins = int(N) - 1; // The last entry is a zero sentinel, outside the plotted FFT bins.
    if (first >= bins) return 0;
    const double center = std::min((first + last) * .5, double(bins - 1));
    const int mid = int(center);
    float peak = std::lerp(power[mid], power[mid + 1], float(center - mid));
    for (int k = std::max(1, int(std::ceil(first))); k < bins && k <= last; ++k)
        peak = std::max(peak, power[k]);
    return peak;
}
} // namespace

struct Color::PreShifter {
    struct Frame {
        Bands bands{};
        int mask = 0;
    };
    struct Overlap {
        Stereo sum{};
        float weight = 0;
    };

    void prepare(double sampleRate) {
        // Keep enough frequency resolution for the bottom of Color's band.
        const int size = std::clamp(std::bit_ceil(unsigned(std::lround(.036 * sampleRate))), 512u, 8192u);
        latency.store(size, std::memory_order_relaxed);
        fft.resize(size);
        delay.resize(size);
        window.resize(size);
        time.resize(size);
        spectrum.resize(size / 2);
        overlap.resize(2 * size);
        for (auto& channel : analysisPhase) channel.resize(size / 2);
        for (auto& channel : synthesisPhase) channel.resize(size / 2);
        for (int i = 0; i < size; ++i)
            window[i] = float(std::sqrt(.5 - .5 * std::cos(tau * i / size)));
        pole = float(std::exp(-1 / (.015 * sampleRate)));
        reset();
    }

    void reset() {
        std::fill(delay.begin(), delay.end(), Frame{});
        std::fill(overlap.begin(), overlap.end(), Overlap{});
        position = 0;
        cents = blend = 0;
        snap = first = true;
    }

    int delayedMask() const { return delay[position & (delay.size() - 1)].mask; }

    Bands process(Frame frame, float target, PreOutput& pre) {
        const size_t size = delay.size(), hop = size / 4;
        auto delayed = std::exchange(delay[position & (size - 1)], frame);
        auto rendered = std::exchange(overlap[position & (overlap.size() - 1)], {});
        cents = snap || std::abs(cents - target) < .001f ? target : target + pole * (cents - target);
        const float weight = std::min(1.f, 2 * std::abs(cents));
        blend = snap || std::abs(blend - weight) < 1e-5f ? weight : weight + pole * (blend - weight);
        snap = false;
        pre.mix = blend;
        for (int ch = 0; ch < 2; ++ch) {
            const float shifted = rendered.weight > .001f ? clean(rendered.sum[ch] / rendered.weight) : 0;
            pre.tuned[ch] = blend * shifted;
            pre.delta[ch] = pre.tuned[ch] - blend * delayed.bands.mid[ch];
            delayed.bands.mid[ch] += pre.delta[ch];
        }
        if ((++position & (hop - 1)) == 0) synthesize(std::exp2(double(cents) / 1200));
        return delayed.bands;
    }

    void synthesize(double ratio) {
        const int size = int(delay.size()), bins = size / 2;
        const float normalization = 1.f / size;
        // Re-sample each synthesis frame at the exact ratio. Re-weight the
        // overlap because the resampled frames have different lengths.
        for (int ch = 0; ch < 2; ++ch) {
            for (int i = 0; i < size; ++i)
                time[i] = delay[(position + i) & (size - 1)].bands.mid[ch] * window[i];
            fft.fft(time.data(), spectrum.data());
            spectrum[0] = {}; // DC and Nyquist are outside the processed band.
            for (int k = 1; k < bins; ++k) {
                const double phase = std::arg(spectrum[k]);
                if (first) synthesisPhase[ch][k] = phase;
                else {
                    const double advance = (tau / 4) * k;
                    const double delta = wrapPhase(phase - analysisPhase[ch][k] - advance);
                    synthesisPhase[ch][k] = wrapPhase(synthesisPhase[ch][k] + ratio * (advance + delta));
                }
                analysisPhase[ch][k] = phase;
                spectrum[k] = k * ratio < bins ? std::polar(std::abs(spectrum[k]), float(synthesisPhase[ch][k])) : Complex{};
            }
            fft.ifft(spectrum.data(), time.data());
            for (int i = 0; i < size; ++i) time[i] *= window[i];
            for (int i = 0, length = int(std::ceil(size / ratio)); i < length; ++i) {
                const double read = i * ratio;
                const int j = std::min(int(read), size - 1), next = std::min(j + 1, size - 1);
                const float fraction = float(read - j);
                auto& out = overlap[(position + i) & (overlap.size() - 1)];
                out.sum[ch] += normalization * cubic(time[std::max(0, j - 1)], time[j], time[next],
                    time[std::min(j + 2, size - 1)], fraction);
                if (ch == 0) out.weight += std::lerp(window[j] * window[j], window[next] * window[next], fraction);
            }
        }
        first = false;
    }

    FFT fft;
    std::atomic<int> latency{0};
    std::vector<Frame> delay;
    std::vector<Overlap> overlap;
    std::vector<float> window, time;
    std::vector<Complex> spectrum;
    std::array<std::vector<double>, 2> analysisPhase, synthesisPhase;
    uint64_t position = 0;
    float pole = 0, cents = 0, blend = 0;
    bool snap = true, first = true;
};

double Color::expMap(double v, double lo, double hi) {
    return lo * std::pow(hi / lo, std::clamp(v, 0., 1.));
}

Color::Color() : pre_(std::make_unique<PreShifter>()) {
    const float defaults[] = {0, .25f, 1, .25f, float(std::log(120. / 20) / std::log(50.)),
        float(std::log(150. / 40) / std::log(50.)), float(std::log(8000. / 1000) / std::log(20.)), 1, 2.f / 3};
    for (int i = 0; i < NoteC; ++i) p_[i].store(defaults[i]);
    for (int i = 0; i < kPitchClasses; ++i) p_[NoteC + i].store((0x5ad >> i) & 1);
    p_[GateThreshold].store(.25f);
    p_[FftSize].store(.5f);
    p_[Pre].store(.5f);
    p_[Post].store(.5f);
    setSampleRate(44100, 1024);
}

Color::~Color() = default;

int32_t Color::latencySamples() const {
    return kFftSizes[requestedFft()] * (requestedGate() ? 2 : 1) +
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
    return i >= 0 && i < kNumParams ? p_[i].load(std::memory_order_relaxed) : 0;
}

void Color::setParam(int32_t i, float v) {
    if (i < 0 || i >= kNumParams || i == RetiredMode || !std::isfinite(v)) return;
    v = std::clamp(v, 0.f, 1.f);
    p_[i].store(i == FftSize ? .5f * std::round(2 * v) : v, std::memory_order_relaxed);
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
    if (midiSource_.exchange(id, std::memory_order_relaxed) != id)
        midiMaskView_.store(0, std::memory_order_relaxed);
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
        window[i] = float(.5 - .5 * std::cos(tau * i / windowSize));
        windowSum += window[i];
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
        const double center = (edges[i] + edges[i + 1]) * .5;
        const float t = float(std::clamp((center - 250) / (400 - 250), 0., 1.));
        shortWeights[i] = t * t * (3 - 2 * t);
        if (shortWeights[i] < 1) viewBins = i + 1;
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
    dry.history[write] = dryFiltered;
    tuned.history[write] = tunedFiltered;
    write = (write + 1) % kFftSize;
    untilNext = stride;
}

void Color::BassSpectrum::analyse(Line& line, const ViewEdges& edges) {
    power.fill(0);
    const int start = write + kFftSize - windowSize;
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < windowSize; ++i)
            time[i] = line.history[(start + i) % kFftSize][ch] * window[i];
        fft.fft(time.data(), bins.data());
        for (int k = 1; k < kFftBins; ++k) power[k] += std::norm(bins[k]);
    }
    for (int i = 0; i < viewBins; ++i)
        line.viewPower[i] = powerScale * spectrumPower(power, edges[i] * binsPerHz, edges[i + 1] * binsPerHz);
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

void Color::Bank::tune(const NoteTunings& tunings, double sr, int mask, double low, double high, Shape shape,
    double q, double gain) {
    const double first = low / 1.8, last = high * 1.8;
    int candidates = 0;
    count = 0;
    for (int i = 0; i <= kLastMidi - kFirstMidi; ++i) {
        const int bit = 1 << ((i + kFirstMidi) % kPitchClasses);
        const auto& tuning = tunings[i];
        const double frequency = tuning.frequency;
        const bool selected = (mask & bit) != 0;
        if (selected) ++candidates;
        const bool enabled =
            selected && candidates <= kBankCapacity && frequency >= first && frequency <= last && frequency < .49 * sr;
        auto& note = notes[i];
        if (enabled) {
            if (shape == Shape::Bell) {
                const double edge =
                    std::min({1., (frequency - first) / (low - first), (last - frequency) / (last - high)});
                note.coeffs = tuning.bell(q, 1 + edge * (gain - 1));
            } else if (!note.active || note.frequency != frequency) {
                note.coeffs = shape == Shape::InputNotch ? tuning.inputNotch : tuning.rejectNotch;
            }
            note.frequency = frequency;
            active[count++] = i;
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
        auto& note = notes[active[i]];
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
    rmsSum += double(power) - rmsHistory[rmsPosition];
    rmsHistory[rmsPosition] = power;
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

    const uint64_t start = end + kMaxWindow - config.size;
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < config.size; ++i) effect.time_[i] = input[(start + i) % kMaxWindow][ch] * config.window[i];
        auto& bins = spectrum[ch];
        config.fft.fft(effect.time_.data(), bins.data());
        bins[0] = {bins[0].real(), 0}; // Packed Nyquist is outside the correction band.
        for (int k = 1; k < config.bins; ++k) {
            const double phase = std::arg(bins[k]);
            if (onset || firstFrame) {
                synthesisPhase[ch][k] = phase;
            } else {
                const double advance = config.phaseAdvance[k];
                const double delta = wrapPhase(phase - analysisPhase[ch][k] - advance);
                synthesisPhase[ch][k] = wrapPhase(synthesisPhase[ch][k] + ratio * (advance + delta));
                bins[k] = std::polar(std::abs(bins[k]), float(synthesisPhase[ch][k]));
            }
            analysisPhase[ch][k] = phase;
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
        const float value = std::norm(spectrum[0][k]) + std::norm(spectrum[1][k]);
        power[bin] += (1 - frac) * value;
        power[bin + 1] += frac * value;
    }
}

void Color::Shifter::synthesize(Color& effect, uint64_t end, const Magnitudes* gain) {
    auto& config = effect.processingFft();
    const int length = int(std::lround(config.size / ratio));
    const float normalization = 2.f * config.hop / (config.size * config.size);
    for (int ch = 0; ch < 2; ++ch) {
        auto& bins = spectrum[ch];
        if (gain) {
            for (int k = 1; k < config.bins; ++k) {
                const double destination = k * ratio;
                const int bin = int(destination);
                if (bin >= config.bins) {
                    bins[k] = {};
                    continue;
                }
                bins[k] *= std::lerp((*gain)[bin], (*gain)[bin + 1], float(destination - bin));
            }
        }
        config.fft.ifft(bins.data(), effect.time_.data());
        for (int i = 0; i < config.size; ++i) effect.time_[i] *= config.window[i];
        for (int i = 0; i < length; ++i) {
            const double read = i * (double(config.size) / length);
            const int j = int(read);
            const auto& x = effect.time_;
            const float value = cubic(x[std::max(0, j - 1)], x[j], x[std::min(j + 1, config.size - 1)],
                x[std::min(j + 2, config.size - 1)], float(read - j));
            output[(end + i) % kRing][ch] += value * normalization;
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
        const double sourceLow = shared.count ? tunings[shared.active[0]].frequency : low;
        const double sourceHigh = shared.count ? tunings[shared.active[shared.count - 1]].frequency : high;
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
            const float wet = std::sqrt(power[k]);
            const float target = (*dry)[k];
            gains[k] = wet > 1e-8f ? std::lerp(1.f, std::min(target / wet, 16.f), effect.morph_) : 1.f;
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
            for (int ch = 0; ch < 2; ++ch) correction[ch] += raised[ch];
        }
        for (int ch = 0; ch < 2; ++ch) correction[ch] *= 1.6f * dynamics[ch];
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
        for (int ch = 0; ch < 2; ++ch) wet[ch] = std::lerp(wet[ch], resonated[ch] * resonanceGain, resonanceBlend);
    }
    for (int ch = 0; ch < 2; ++ch) {
        tuned[ch] *= effect.amount_ * (1 - transient);
        wet[ch] = std::lerp(dry[ch], wet[ch], effect.amount_ * (1 - transient));
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
    auto& slot = input[sample & (window - 1)];
    auto delayed = slot;
    slot = frame;
    auto& gated = output[sample % kRing];
    for (int ch = 0; ch < 2; ++ch)
        delayed.wet[ch] = std::lerp(delayed.wet[ch], gated[ch], delayed.gatedFraction);
    gated = {};
    return delayed;
}

void Color::SpectralGate::analyse(Color& effect, uint64_t end) {
    auto& config = effect.processingFft();
    auto& time = effect.time_;
    auto& bins = effect.fftBins_;
    const auto windowMask = uint64_t(config.size - 1);
    // A full ring ending at end - 1 starts at the next write position.
    const auto sampleAt = [&](int i) -> const OutputFrame& { return input[(end + i) & windowMask]; };

    Magnitudes power{};
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < config.size; ++i) time[i] = sampleAt(i).dry[ch] * config.window[i];
        config.fft.fft(time.data(), bins.data());
        // RealFFT packs DC and Nyquist together. Neither has a negative-frequency partner.
        power[0] += .25f * bins[0].real() * bins[0].real();
        power[config.bins] += .25f * bins[0].imag() * bins[0].imag();
        for (int k = 1; k < config.bins; ++k) power[k] += std::norm(bins[k]);
    }
    const float threshold = .450158f * config.size * dbToGain(effect.gateThreshold_);
    const float thresholdPower = threshold * threshold;
    for (int k = 0; k <= config.bins; ++k) {
        const float target = power[k] >= thresholdPower ? 1.f : 0.f;
        if (firstFrame) gain[k] = target;
        else gain[k] += (target - gain[k]) * (target > gain[k] ? config.gateOpen : config.gateClose);
    }
    firstFrame = false;

    // This sqrt-Hann pair has a constant sum of squared windows at a quarter hop.
    const float normalization = 2.f * config.hop / (config.size * config.size);
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < config.size; ++i) time[i] = sampleAt(i).wet[ch] * config.window[i];
        config.fft.fft(time.data(), bins.data());
        bins[0] = {bins[0].real() * gain[0], bins[0].imag() * gain[config.bins]};
        for (int k = 1; k < config.bins; ++k) bins[k] *= gain[k];
        config.fft.ifft(bins.data(), time.data());
        for (int i = 0; i < config.size; ++i)
            output[(end + i) % kRing][ch] += time[i] * config.window[i] * normalization;
    }
}

float Color::SpectralGate::gainAt(double bin) const {
    const double bounded = std::clamp(bin, 0., double(kMaxBins));
    const int low = int(bounded);
    return std::lerp(gain[low], gain[std::min(low + 1, kMaxBins)], float(bounded - low));
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
        auto& tuning = tunings[i];
        tuning.frequency = cents == 0 ? 440 * std::exp2((i + kFirstMidi - 69) / 12.)
                                      : noteTunings_[i].frequency * ratio;
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

void Color::setSampleRate(double sr, int32_t) {
    sr_ = std::isfinite(sr) && sr >= 8000 ? std::min(sr, 768000.) : 44100.;
    const auto prepareFft = [&](int index) {
        auto& config = fftConfigs_[index];
        config.size = kFftSizes[index];
        config.hop = config.size / 4;
        config.bins = config.size / 2;
        config.fft.resize(config.size);
        for (int i = 0; i < config.size; ++i)
            config.window[i] = float(std::sqrt(.5 - .5 * std::cos(tau * i / config.size)));
        for (int k = 0; k < config.bins; ++k) config.phaseAdvance[k] = tau * k * config.hop / config.size;
        config.onsetPole = std::pow(pole(10), config.hop);
        config.gateOpen = float(1 - std::exp(-config.hop / (.005 * sr_)));
        config.gateClose = float(1 - std::exp(-config.hop / (.050 * sr_)));
    };
    prepareFft(kDefaultFft);
    prepareNoteTunings(noteTunings_, 0);
    pre_->prepare(sr_);
    for (int i = 0; i <= kSpectrumBins; ++i) viewEdges_[i] = 40 * std::pow(500., i / double(kSpectrumBins));
    bassSpectrum_.prepare(sr_, viewEdges_);
    for (auto& path : paths_) {
        for (auto& branch : path.shift) branch.rmsHistory.assign(size_t(std::lround(.008 * sr_)), 0);
        path.shift[0].ratio = 242. / 256;
        path.shift[1].ratio = 271. / 256;
    }
    // Preserve the default path's FFT setup order before preparing the alternatives.
    prepareFft(0);
    prepareFft(2);

    fadeLength_ = int(std::lround(.15 * sr_));
    controlInterval_ = std::max(1, int(std::lround(.00018140589 * sr_)));
    guardHigh_ = {dsp::BiquadCascade::highpass(sr_, 100, butterworthQ)};
    guardLow_ = {dsp::BiquadCascade::lowpass(sr_, 3000, butterworthQ)};
    guardLength_ = int(.0025 * sr_);
    fastRelease_ = pole(32);
    slowAttack_ = pole(100);
    slowRelease_ = pole(400);
    noteRelease_ = pole(8);
    guardAttack_ = pole(75);
    guardSlowRelease_ = pole(300);
    guardFastRelease_ = pole(88);
    bandPole_ = float(std::exp(-controlInterval_ / (.02 * sr_)));
    resonantPole_ = pole(200);
    makeupPole_ = float(std::exp(-1 / (.010 * sr_)));
    controlPole_ = makeupPole_;
    amountRampLength_ = int(std::lround(.001 * sr_));
    muteStep_ = 1.f / float(std::lround(.01 * sr_));
    blendStep_ = float(10 / sr_);
    srView_.store(float(sr_), std::memory_order_relaxed);
    resetProcessing(requestedFft(), requestedGate(), requestedPre());
}

void Color::resetProcessing(int fftIndex, bool gateEnabled, bool preEnabled) {
    if (activeGate_ || gateEnabled) spectralGate_.reset();
    if (activePre_ || preEnabled) {
        pre_->reset();
        preOutputDelay_.fill({});
    }
    activeFft_ = fftIndex;
    activeGate_ = gateEnabled;
    activePre_ = preEnabled;
    sample_ = quiet_ = 0;
    fadeRemaining_ = currentPath_ = amountRemaining_ = guardPosition_ = 0;
    fadeWait_ = 0;
    for (auto& path : paths_) path.reset(0);
    bandDelay_.fill({});
    dryView_.fill({});
    tunedViewHistory_.fill({});
    bassSpectrum_.reset();
    low1_ = low2_ = high1_ = high2_ = midHigh_ = midLow_ = finalHigh_ = finalLow_ = {};
    guardHigh_.reset();
    guardLow_.reset();
    fast_ = slow_ = noteMaskEnvelope_ = {};
    guardEnvelope_ = guardRelease_ = {};
    guardRamp_ = 0;
    postCents_ = postSmooth_ = 0;
    clearSpectrum();
    idle_ = true;
    snap_ = true;
}

void Color::wake() {
    if (!idle_) return;
    if (!snap_) {
        // Drop sub-audible history while retaining the FFT and scope cadence.
        const auto clock = sample_;
        resetProcessing(activeFft_, activeGate_, activePre_);
        sample_ = clock;
    }
    idle_ = false;
}

void Color::updateBand() {
    const auto highpass = dsp::BiquadCascade::highpass(sr_, low_, butterworthQ);
    const auto lowpass = dsp::BiquadCascade::lowpass(sr_, high_, butterworthQ);
    const auto lowSide = dsp::BiquadCascade::lowpass(sr_, low_, butterworthQ);
    const auto highSide = dsp::BiquadCascade::highpass(sr_, high_, butterworthQ);
    low1_.tune(lowSide);
    low2_.tune(lowSide);
    high1_.tune(highSide);
    high2_.tune(highSide);
    midHigh_.tune(highpass);
    finalHigh_.tune(highpass);
    midLow_.tune(lowpass);
    finalLow_.tune(lowpass);
}

void Color::updateControls(int mask, bool midi) {
    requestedMask_ = mask;
    updatePost();
    targetLow_ = std::min(.245 * sr_, expMap(getParam(Low), 40, 2000));
    targetHigh_ = std::min(.49 * sr_, std::max(2 * targetLow_, expMap(getParam(High), 1000, 20000)));

    resonanceTarget_ = std::pow(getParam(Resonance), resonanceCurve);
    const float amountTarget = getParam(Amount);
    if (amountTarget != amountTarget_) {
        amountTarget_ = amountTarget;
        amountRemaining_ = amountRampLength_;
        amountDelta_ = (amountTarget_ - amount_) / amountRemaining_;
    }
    mixTarget_ = getParam(Mix);
    muteLowTarget_ = getParam(MuteLow) >= .5f ? 1.f : 0.f;
    muteHighTarget_ = getParam(MuteHigh) >= .5f ? 1.f : 0.f;
    const float output = getParam(Output);
    gainTarget_ = output == 2.f / 3 ? 1.f : dbToGain(-24 + 36 * output);
    morphTarget_ = getParam(Morph);
    gateThreshold_ = -80 + 80 * getParam(GateThreshold);
    const auto smoothBand = [&](double current, double target) {
        return snap_ || std::abs(current - target) < .01 ? target : target + bandPole_ * (current - target);
    };
    const double low = smoothBand(low_, targetLow_), high = smoothBand(high_, targetHigh_);
    const double decayTarget = expMap(getParam(Decay), .02, 1);
    decay_ =
        snap_ || std::abs(decayTarget - decay_) < 1e-5 ? decayTarget : decayTarget + bandPole_ * (decay_ - decayTarget);
    if (snap_ || low != low_ || high != high_) {
        low_ = low;
        high_ = high;
        updateBand();
    }
    if (snap_) {
        paths_[currentPath_].reset(requestedMask_);
        amount_ = amountTarget_;
        amountRemaining_ = 0;
        mix_ = mixTarget_;
        muteLow_ = muteLowTarget_;
        muteHigh_ = muteHighTarget_;
        gain_ = gainTarget_;
        resonance_ = resonanceTarget_;
        morph_ = morphTarget_;
        snap_ = false;
    } else if (fadeRemaining_ == 0 && requestedMask_ != paths_[currentPath_].mask) {
        paths_[1 - currentPath_].reset(requestedMask_);
        // Start preparing the new chord at the MIDI input offset so its first
        // crossfade meets the corresponding delayed audio.
        fadeWait_ = midi ? processingFft().size : 0;
        activeFadeLength_ = midi ? processingFft().hop : fadeLength_;
        fadeRemaining_ = activeFadeLength_;
    }
    paths_[currentPath_].tune(*this);
    if (fadeRemaining_) paths_[1 - currentPath_].tune(*this);
}

float Color::transientMask(Stereo dry) {
    const double detected = guardLow_.run(guardHigh_.run(.5 * (dry[0] + dry[1])));
    if (detected > .001 && detected >= 10 * guardEnvelope_.value) {
        if (guardPosition_ == 0) guardPosition_ = 1;
        guardEnvelope_.value = .25 * detected;
    } else {
        guardEnvelope_.run(std::abs(detected), guardAttack_, guardSlowRelease_);
    }
    if (guardPosition_ != 0) {
        guardRamp_ = std::sqrt(guardPosition_ / double(guardLength_));
        if (++guardPosition_ > guardLength_) guardPosition_ = 0;
    } else {
        guardRamp_ = 0;
    }
    return float(guardRelease_.run(guardRamp_, 0, guardFastRelease_));
}

Color::Stereo Color::correctionMask(Stereo mid) {
    Stereo result{};
    for (int ch = 0; ch < 2; ++ch) {
        const double magnitude = std::abs(mid[ch]);
        const double quick = 4.2 * fast_[ch].run(magnitude, 0, fastRelease_);
        const double slow = slow_[ch].run(magnitude, slowAttack_, slowRelease_);
        result[ch] = float(noteMaskEnvelope_[ch].run(quick >= slow && quick > .001 ? 1 : 0, 0, noteRelease_));
    }
    return result;
}

void Color::analyseDry(uint64_t end) {
    auto& config = processingFft();
    dryMagnitude_.fill(0);
    const uint64_t start = end + kMaxWindow - config.size;
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < config.size; ++i) time_[i] = bandDelay_[(start + i) % kMaxWindow].mid[ch] * config.window[i];
        config.fft.fft(time_.data(), fftBins_.data());
        for (int k = 1; k < config.bins; ++k) dryMagnitude_[k] += std::norm(fftBins_[k]);
    }
    for (auto& value : dryMagnitude_) value = std::sqrt(value);
}

void Color::publishLine(const ScopeHistory& history, const BassSpectrum::Line& bass, ScopeLevels& destination, bool gated) {
    auto& config = fftConfigs_[kDefaultFft];
    std::array<float, kScopeWindow / 2 + 1> power{};
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < kScopeWindow; ++i) time_[i] = history[(sample_ + i) % kScopeWindow][ch] * config.window[i];
        config.fft.fft(time_.data(), fftBins_.data());
        for (int k = 1; k < kScopeWindow / 2; ++k) power[k] += std::norm(fftBins_[k]);
    }
    const double binsPerHz = kScopeWindow / sr_;
    const double gateBinsPerHz = gated ? processingFft().size / sr_ : 0;
    for (int i = 0; i < kSpectrumBins; ++i) {
        const float weight = bassSpectrum_.shortWeights[i];
        float peak = bass.viewPower[i];
        if (weight > 0) {
            const float fullBand = spectrumPower(power, viewEdges_[i] * binsPerHz, viewEdges_[i + 1] * binsPerHz);
            peak = std::lerp(peak, fullBand, weight);
        }
        const float gain = gated ? spectralGate_.gainAt((viewEdges_[i] + viewEdges_[i + 1]) * .5 * gateBinsPerHz) : 1.f;
        const float amplitude = std::sqrt(peak) * gain / kScopeNormalizer;
        const float level = amplitude > 1e-4f ? std::clamp((20 * std::log10(amplitude) + 80) / 80, 0.f, 1.f) : 0.f;
        destination[i].store(level, std::memory_order_relaxed);
    }
}

void Color::clearSpectrum() {
    for (auto& value : spectrumView_) value.store(0.f, std::memory_order_relaxed);
    for (auto& value : tunedView_) value.store(0.f, std::memory_order_relaxed);
}

void Color::publishSpectrum() {
    if (bassSpectrum_.refresh) {
        bassSpectrum_.analyse(bassSpectrum_.dry, viewEdges_);
        bassSpectrum_.analyse(bassSpectrum_.tuned, viewEdges_);
    }
    bassSpectrum_.refresh = !bassSpectrum_.refresh;
    publishLine(dryView_, bassSpectrum_.dry, spectrumView_);
    publishLine(tunedViewHistory_, bassSpectrum_.tuned, tunedView_, activeGate_);
}

void Color::process(float* buf, int32_t frames) {
    if (!buf || frames <= 0 || bypassed()) return;
    const int requested = requestedFft();
    const bool gate = requestedGate();
    const bool pre = requestedPre();
    if (requested != activeFft_ || gate != activeGate_ || pre != activePre_) resetProcessing(requested, gate, pre);
    const auto preLatency = pre ? pre_->delay.size() : 0;
    const float preTarget = (getParam(Pre) - .5f) * 200;
    const auto& config = processingFft();
    const uint64_t scopeInterval = kScopeHop * uint64_t(std::ceil(sr_ / (50 * kScopeHop)));
    const uint64_t settleSamples = std::max(uint64_t(latencySamples() + config.size), uint64_t(.4 * sr_));
    const unsigned hopMask = config.hop - 1; // Every supported hop is a power of two.
    const int32_t source = sidechainSourceTrackId();
    const bool midi = source >= 0;
    const int32_t blockSource = std::exchange(midiBlockSource_, -1);
    const bool received = midi && blockSource == source;
    MidiKeyNotes notes = received ? midiStart_ : MidiKeyNotes{};
    const int eventCount = received ? midiEventCount_ : 0;
    int eventIndex = 0;
    const auto takeMidiThrough = [&](int sample) {
        while (eventIndex < eventCount && midiEvents_[eventIndex].off <= sample)
            notes.apply(midiEvents_[eventIndex++]);
    };
    for (int i = 0; i < frames; ++i) {
        if (idle_) {
            float* const begin = buf + 2 * i;
            float* const signal = std::find_if(begin, buf + 2 * frames, [](float value) { return clean(value) != 0; });
            const int next = int((signal - buf) / 2);
            // Keep the whole stereo frame when either channel receives input.
            std::fill(begin, buf + 2 * next, 0.f);
            sample_ += next - i;
            i = next;
            if (i == frames) return;
            wake();
        }
        if (eventIndex < eventCount) takeMidiThrough(i);
        const int mask = pre && midi ? pre_->delayedMask() : notes.mask;
        if (snap_ || sample_ % controlInterval_ == 0 ||
            (midi && fadeRemaining_ == 0 && mask != paths_[currentPath_].mask))
            updateControls(midi ? mask : noteMask(), midi);
        if (amountRemaining_ > 0) amount_ = --amountRemaining_ > 0 ? amount_ + amountDelta_ : amountTarget_;
        mix_ = mixTarget_ + controlPole_ * (mix_ - mixTarget_);
        muteLow_ = approach(muteLow_, muteLowTarget_, muteStep_);
        muteHigh_ = approach(muteHigh_, muteHighTarget_, muteStep_);
        gain_ = gainTarget_ + controlPole_ * (gain_ - gainTarget_);
        morph_ = morphTarget_ + controlPole_ * (morph_ - morphTarget_);
        resonance_ = std::abs(resonance_ - resonanceTarget_) < 1e-4
                         ? resonanceTarget_
                         : resonanceTarget_ + resonantPole_ * (resonance_ - resonanceTarget_);

        const Stereo in{std::clamp(clean(buf[2 * i]), -64.f, 64.f), std::clamp(clean(buf[2 * i + 1]), -64.f, 64.f)};
        const size_t slot = sample_ % kMaxWindow;
        const size_t read = (sample_ + kMaxWindow - config.size) % kMaxWindow;
        const size_t scopeSlot = sample_ % kScopeWindow;
        const Bands delayed = bandDelay_[read];
        Bands input{in, midLow_.run(midHigh_.run(in)), low2_.run(low1_.run(in)), high2_.run(high1_.run(in))};
        // Start downstream analysis on the first frame delivered by Pre's delay.
        const bool preReady = !pre || pre_->position >= preLatency;
        PreOutput delayedPre{};
        if (pre) {
            PreOutput transformed{};
            input = pre_->process({input, notes.mask}, preTarget, transformed);
            delayedPre = preOutputDelay_[read];
            preOutputDelay_[slot] = transformed;
        }
        bandDelay_[slot] = input;
        const auto& dryMid = delayed.mid;
        const auto& mid = input.mid;

        const Stereo guardInput{dryMid[0] - delayedPre.delta[0], dryMid[1] - delayedPre.delta[1]};
        const float transient = transientMask(guardInput);
        const Stereo dynamics = correctionMask(mid);
        auto& current = paths_[currentPath_];
        float active = current.active() ? 1.f : 0.f;
        Stereo tuned{};
        Stereo wet = current.process(*this, mid, dryMid, transient, dynamics, sample_, tuned);
        if (fadeRemaining_) {
            auto& next = paths_[1 - currentPath_];
            const float fade = fadeWait_ > 0 ? 0.f : 1.f - float(fadeRemaining_) / activeFadeLength_;
            Stereo nextTuned{};
            const Stereo nextWet = next.process(*this, mid, dryMid, transient, dynamics, sample_, nextTuned);
            active = std::lerp(active, next.active() ? 1.f : 0.f, fade);
            for (int ch = 0; ch < 2; ++ch) {
                if (current.active() && next.active()) wet[ch] = std::lerp(wet[ch], nextWet[ch], fade);
                else if (next.active())
                    wet[ch] = nextWet[ch];
                tuned[ch] = std::lerp(tuned[ch], nextTuned[ch], fade);
            }
        }
        // With an inactive mask, use the crossover when Pre or a sideband mute is audible.
        const float crossoverDry = (1 - active) * std::max({muteLow_, muteHigh_, delayedPre.mix});
        const float effectWeight = active + crossoverDry;
        if (crossoverDry > 0)
            for (int ch = 0; ch < 2; ++ch) wet[ch] = std::lerp(wet[ch], dryMid[ch], crossoverDry / effectWeight);
        wet = finalLow_.run(finalHigh_.run(wet));
        OutputFrame output;
        output.dry = delayed.full;
        output.wet = wet;
        output.tuned = tuned;
        output.effectWeight = effectWeight;
        for (int ch = 0; ch < 2; ++ch) {
            output.sides[ch] = delayed.low[ch] + delayed.high[ch];
            output.muted[ch] = muteLow_ * delayed.low[ch] + muteHigh_ * delayed.high[ch];
            if (pre) output.tuned[ch] += delayedPre.tuned[ch];
        }
        if (activeGate_) {
            // A neutral note mask bypasses Gate while retaining Pre and the sideband mutes.
            output.gatedFraction = effectWeight > 0 ? active / effectWeight : 0;
            output = spectralGate_.push(output, sample_, config.size);
        }
        dryView_[scopeSlot] = output.dry;
        for (int ch = 0; ch < 2; ++ch) {
            const float mixed = std::lerp(output.dry[ch], output.wet[ch] + output.sides[ch], mix_ * output.effectWeight)
                - mix_ * output.muted[ch];
            buf[2 * i + ch] = clean(gain_ * mixed);
            tunedViewHistory_[scopeSlot][ch] = output.tuned[ch] * mix_ * gain_;
        }
        if (preReady) bassSpectrum_.push(output.dry, tunedViewHistory_[scopeSlot]);
        const float outputPeak = std::max(std::abs(buf[2 * i]), std::abs(buf[2 * i + 1]));
        quiet_ = in[0] == 0.f && in[1] == 0.f && outputPeak < silence ? quiet_ + 1 : 0;

        ++sample_;
        if (fadeWait_ > 0) --fadeWait_;
        else if (fadeRemaining_ && --fadeRemaining_ == 0) currentPath_ = 1 - currentPath_;
        if (quiet_ >= settleSamples && fadeRemaining_ == 0) {
            idle_ = true;
            clearSpectrum();
            continue;
        }
        if (preReady && (sample_ & hopMask) == 0) {
            const bool shape = morph_ > 1e-6f &&
                               (paths_[currentPath_].down || (fadeRemaining_ && paths_[1 - currentPath_].down));
            if (shape) analyseDry(sample_);
            paths_[currentPath_].frame(*this, sample_, shape ? &dryMagnitude_ : nullptr);
            if (fadeRemaining_) paths_[1 - currentPath_].frame(*this, sample_, shape ? &dryMagnitude_ : nullptr);
            if (activeGate_) spectralGate_.analyse(*this, sample_);
        }
        if (preReady && (sample_ - preLatency) % scopeInterval == 0) publishSpectrum();
    }
}

int32_t Color::scopeRead(float* out, int32_t max) const {
    if (!out || max < 2) return 0;
    out[0] = srView_.load(std::memory_order_relaxed);
    out[1] = float(latencySamples());
    const int count = std::min(max - 2, 2 * kSpectrumBins);
    for (int i = 0; i < count; ++i)
        out[i + 2] = i < kSpectrumBins ? spectrumView_[i].load(std::memory_order_relaxed)
                                       : tunedView_[i - kSpectrumBins].load(std::memory_order_relaxed);
    if (max > 2 + 2 * kSpectrumBins) {
        out[2 + 2 * kSpectrumBins] = sidechainSourceTrackId() >= 0 ? float(midiMaskView_.load(std::memory_order_relaxed)) : 0.f;
        return 3 + 2 * kSpectrumBins;
    }
    return 2 + count;
}

} // namespace nota
