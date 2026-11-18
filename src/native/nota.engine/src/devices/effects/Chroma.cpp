// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#include "devices/effects/Chroma.h"
#include <cmath>
#include <algorithm>
namespace nota {
namespace {
constexpr double pi = 3.14159265358979323846, tau = 2 * pi;
constexpr double identityHzFraction = .00014442; // About a quarter-cent in relative Hz.
constexpr float quietOutputThreshold = 3.16227766e-5f; // -90 dBFS peak.
constexpr double maxOutputGain = 3.98107171; // +12 dB
double wrap(double v) {
    return std::remainder(v, tau);
}
double noteHz(int note) {
    return 440 * std::exp2((note - 69) / 12.);
}
bool containsNote(int mask, int note) {
    return (mask & (1 << ((note % 12 + 12) % 12))) != 0;
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
        "Output", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B", "Morph", "Gate", "Gate threshold", "Mode"};
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
    velocity_ = previousVelocity_ = rotation_;
    placement_.assign(bins_, {});
    previousPlacement_ = placement_;
    rotationFactor_.assign(bins_, {1, 0});
    tunedWeight_.assign(bins_, 0);
    previousTunedWeight_ = attackKeep_ = tunedWeight_;
    resonanceNote_.assign(bins_, -1);
    binNote_ = resonanceNote_;
    resonanceTurn_.assign(bins_, {1, 0});
    binTurn_ = resonanceTurn_;
    peaks_.assign(bins_, 0);
    owner_.assign(bins_, 0);
    reference_ = previousReference_ = owner_;
    peakAge_ = previousPeakAge_ = matched_ = owner_;
    matchOrder_ = owner_;
    peakMatch_ = owner_;
    selectedNote_.assign(bins_, -1);
    previousNote_ = selectedNote_;
    partialEvidence_.assign(bins_, 0);
    partialShift_.assign(bins_, 0);
    for (int i = 0; i < kVoiceNotes; ++i) voicePitch_[i] = noteHz(kFirstCandidate + i);
    resetMode(getParam(Mode) >= .5f);
    modeWet_ = 1;
    modeWarmup_ = 0;
    count_ = quiet_ = quietOutput_ = quietTail_ = 0;
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
int Chroma::targetNote(double hz, int mask, int previous) const {
    double midi = 69 + 12 * std::log2(std::max(hz, 1.) / 440.), dist = 1e9;
    int best = -1;
    int center = int(std::floor(midi));
    for (int note = center - 12; note <= center + 12; ++note)
        if (containsNote(mask, note)) {
            double d = std::abs(note - midi);
            // Float FFT estimates fluctuate around an exact tie; always prefer its lower note.
            if (d < dist - .002) {
                dist = d;
                best = note;
            }
        }
    // The distance difference changes twice as fast as motion past the midpoint:
    // 0.30 semitone therefore extends the held note's boundary by 15 cents.
    if (previous >= 0 && containsNote(mask, previous) &&
        std::abs(previous - midi) <= dist + .30) return previous;
    return best;
}
void Chroma::resetMode(bool polyphonic) {
    polyphonic_ = polyphonic;
    voices_ = {};
    chord_.fill(-1);
    chordMask_ = 0;
    previousMask_ = 0;
    std::fill(previousPeakAge_.begin(), previousPeakAge_.end(), 0);
    std::fill(previousNote_.begin(), previousNote_.end(), -1);
    std::fill(gateGain_.begin(), gateGain_.end(), 1);
    std::fill(resonanceNote_.begin(), resonanceNote_.end(), -1);
    std::fill(binNote_.begin(), binNote_.end(), -1);
    for (int c = 0; c < 2; ++c) {
        std::fill(ola_[c].begin(), ola_[c].end(), 0.f);
        std::fill(tail_[c].begin(), tail_[c].end(), Complex{});
        std::fill(tunedTail_[c].begin(), tunedTail_[c].end(), Complex{});
    }
}
void Chroma::analyseChord(int mask, double binHz) {
    constexpr int harmonics = 12, confirmFrames = 4, releaseFrames = 6;
    struct Candidate {
        std::array<int, harmonics> peaks{};
    };
    std::array<Candidate, kVoiceNotes> candidates{};
    std::array<bool, kVoiceNotes> picked{};
    std::fill(partialEvidence_.begin(), partialEvidence_.end(), 0);
    std::fill(partialShift_.begin(), partialShift_.end(), 0);
    if (quiet_ >= uint64_t(n_)) {
        voices_ = {};
        chord_.fill(-1);
        return;
    }
    float frameMax = 0;
    for (int j = 0; j < peakCount_; ++j) {
        const int k = peaks_[j];
        if (peakAge_[k] < 2) continue;
        partialEvidence_[k] = std::sqrt(energy_[k]);
        frameMax = std::max(frameMax, partialEvidence_[k]);
    }
    for (int p = 0; p < kVoiceNotes; ++p) {
        // A separate pitch slot keeps held notes stable when their loudest
        // partial moves between FFT bins or the chord's balance changes.
        double fundamental = voicePitch_[p];
        for (int h = 1; h <= harmonics; ++h) {
            const double expected = fundamental * h;
            const int bin = int(std::lround(expected / binHz));
            if (bin >= bins_ - 1) break;
            // A broad fundamental window mistakes the blended peak of two
            // nearby notes for a single flat note. Upper partials may stretch.
            double distance = expected * (.014 + .0009 * (h - 1));
            for (int b = std::max(1, bin - 1); b <= bin + 1; ++b) {
                const int k = owner_[b];
                const double error = std::abs(peakHz_[k] - expected);
                if (partialEvidence_[k] > frameMax * .018f && error < distance) {
                    distance = error;
                    candidates[p].peaks[h - 1] = k;
                }
            }
            if (h == 1) {
                if (candidates[p].peaks[0] == 0) break;
                fundamental = peakHz_[candidates[p].peaks[0]];
            }
        }
    }
    double strongest = 0;
    for (int pick = 0; pick < int(chord_.size()); ++pick) {
        int best = -1;
        double bestScore = 0;
        for (int p = 0; p < kVoiceNotes; ++p) {
            if (picked[p]) continue;
            const auto& peaks = candidates[p].peaks;
            const double fundamental = partialEvidence_[peaks[0]];
            if (fundamental <= frameMax * .04) continue;
            double score = fundamental;
            int independent = 1;
            for (int h = 2; h <= harmonics; ++h) {
                const double evidence = partialEvidence_[peaks[h - 1]];
                if (evidence <= frameMax * .018) continue;
                score += evidence * (2. / (h + 1));
                ++independent;
            }
            if (independent < 2) continue;
            if (voices_[p].seen >= confirmFrames) score *= 1.12;
            if (score > bestScore) { best = p; bestScore = score; }
        }
        if (best < 0 || bestScore < strongest * .065) break;
        if (pick == 0) strongest = bestScore;
        picked[best] = true;
        const auto& ps = candidates[best].peaks;
        const double measured = peakHz_[ps[0]];
        Voice& voice = voices_[best];
        voice.hz = voice.seen == 0 ? measured : voice.hz + .27 * (measured - voice.hz);
        voice.seen = std::min(voice.seen + 1, confirmFrames);
        voice.missed = 0;
        // The same evidence cannot form another voice. A coincident partial
        // will be considered against all confirmed notes again for synthesis.
        for (int k : ps) partialEvidence_[k] = 0;
    }
    std::array<int, 4> active{ {-1, -1, -1, -1} };
    int count = 0;
    for (int p = 0; p < kVoiceNotes; ++p) {
        auto& voice = voices_[p];
        if (!picked[p] && (++voice.missed >= releaseFrames || voice.seen < confirmFrames)) voice = {};
        // Confirmed voices that still have evidence take precedence over
        // recently missed ones when a new chord replaces a decaying chord.
        if (picked[p] && voice.seen == confirmFrames && count < int(active.size())) active[count++] = p;
    }
    for (int p = 0; p < kVoiceNotes && count < int(active.size()); ++p)
        if (!picked[p] && voices_[p].seen == confirmFrames) active[count++] = p;
    std::sort(active.begin(), active.begin() + count);
    if (active != chord_ || mask != chordMask_) {
        std::array<int, 4> selected{};
        int bestCost = 1000;
        // Four voices with at most five options each. Enumerate in ascending
        // note order so equal-cost assignments prefer lower notes.
        auto assign = [&](auto&& self, int i, int lower, int cost) -> void {
            if (cost >= bestCost) return;
            if (i == count) {
                bestCost = cost;
                for (int v = 0; v < count; ++v) voices_[active[v]].target = selected[v];
                return;
            }
            const int source = kFirstCandidate + active[i];
            const bool fixed = source < kFirstVoice || containsNote(mask, source);
            for (int target = source - 2; target <= source + 2; ++target) {
                if (target <= lower || (fixed && target != source)) continue;
                const bool allowed = containsNote(mask, target);
                if (!allowed && target != source) continue;
                selected[i] = target;
                self(self, i + 1, target, cost + (allowed ? std::abs(target - source) : 16));
            }
        };
        assign(assign, 0, 0, 0);
        chord_ = active;
        chordMask_ = mask;
    }
    std::array<double, 4> ratio{};
    for (int i = 0; i < count; ++i) {
        const auto& voice = voices_[active[i]];
        ratio[i] = voice.target == kFirstCandidate + active[i] ? 1 :
            std::pow(noteHz(voice.target) / voice.hz, amountS_);
    }
    for (int j = 0; j < peakCount_; ++j) {
        const int k = peaks_[j];
        if (peakAge_[k] < 2 || energy_[k] < frameMax * frameMax * .000016f) continue;
        const double hz = peakHz_[k];
        int owner = -1;
        bool ambiguous = false;
        for (int i = 0; i < count; ++i) {
            const auto& voice = voices_[active[i]];
            const int h = int(std::lround(hz / voice.hz));
            if (h < 1 || h > 24) continue;
            const double expected = voice.hz * h;
            const double error = hz - expected;
            const double below = std::min(voice.hz * .12, expected * .013);
            const double above = std::min(voice.hz * .18, expected * (.013 + .0009 * (h - 1)));
            if (error < -below || error > above) continue;
            if (owner < 0) owner = i;
            else if (std::abs(ratio[owner] - ratio[i]) > .0005778) ambiguous = true;
        }
        if (owner >= 0 && !ambiguous) partialShift_[k] = hz * (ratio[owner] - 1);
    }
}
void Chroma::applySpectralColor(int mask, float amount, double low, double high, double binHz) {
    // Let active resonance retain Decay after input ends. If the band or scale
    // leaves a bin's note behind, release its old phase before retuning the bin.
    const double seconds = expMap(getParam(Decay), .02, 1.);
    const float pole = float(std::exp(-double(hop_) / (seconds * sr_)));
    const float obsoletePole = float(std::exp(-double(hop_) / (.015 * sr_)));
    const float resonance = float(2 * resS_ * amountS_);
    const bool checkTail = quiet_ >= uint64_t(2 * n_);
    double remaining = 0;
    for (int k = 1; k < bins_; ++k) {
        const double hz = k * binHz;
        if (mask != previousMask_) {
            binNote_[k] = targetNote(hz, mask);
            const double tuned = 440 * std::exp2((binNote_[k] - 69) / 12.);
            binTurn_[k] = std::polar(1.f, float(tau * tuned * hop_ / sr_));
        }
        const int wanted = hz > low && hz < high ? binNote_[k] : -1;
        const float memory = std::norm(tail_[0][k]) + std::norm(tail_[1][k]) +
                             std::norm(tunedTail_[0][k]) + std::norm(tunedTail_[1][k]);
        constexpr float replaceBelow = 1e-8f;
        if (resonanceNote_[k] != wanted && memory < replaceBelow) {
            resonanceNote_[k] = wanted;
            resonanceTurn_[k] = binTurn_[k];
        }
        if (resonanceNote_[k] < 0 && memory < 1e-20f) continue;
        const bool active = wanted >= 0 && wanted == resonanceNote_[k];
        const Complex turn = resonanceTurn_[k] * (active ? pole : obsoletePole);
        const float feed = active ? 1 - pole : 0;
        for (int c = 0; c < 2; ++c) {
            auto& t = tail_[c][k];
            t = t * turn + wet_[c][k] * feed;
            if (std::norm(t) < 1e-20f) t = {};
            wet_[c][k] += t * resonance;
            auto& vt = tunedTail_[c][k];
            vt = vt * turn + tunedViewBins_[c][k] * feed;
            if (std::norm(vt) < 1e-20f) vt = {};
            tunedViewBins_[c][k] += vt * resonance;
            if (checkTail) remaining += std::abs(t);
        }
    }
    // A bin contributes at most twice its magnitude to the real IFFT. The
    // overlapping Hann windows sum to 4, and synthesis scales by 1/(3*N).
    // Bound the remaining tail at maximum resonance and output, even if
    // automation changes gains. Wait for earlier OLA samples to drain, too.
    const double tailBound = remaining * (16 * maxOutputGain / (3 * n_));
    quietTail_ = checkTail && tailBound < quietOutputThreshold ? quietTail_ + hop_ : 0;
    // Morph and Gate use shared stereo gains so neither changes relative channel phase.
    const float open = float(1 - std::exp(-double(hop_) / (.005 * sr_)));
    const float close = float(1 - std::exp(-double(hop_) / (.05 * sr_)));
    if (morphS_ > 1e-7 || gateS_ > 1e-7) {
        for (int k = 1; k < bins_; ++k) {
            double hz = k * binHz;
            if (hz <= low || hz >= high || !mask || amount <= 0) {
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
    const int mask = noteMask();
    const float amount = getParam(Amount);
    for (int k = 1; k < bins_; ++k) energy_[k] = std::norm(spectrum_[0][k]) + std::norm(spectrum_[1][k]);
    energy_[0] = 0;
    const float peakFloor = std::max(1e-12f, *std::max_element(energy_.begin(), energy_.end()) * 1e-8f);
    peakCount_ = 0;
    for (int k = 1; k < bins_ - 1; ++k)
        if (energy_[k] > peakFloor && energy_[k] >= energy_[k - 1] && energy_[k] > energy_[k + 1])
            peaks_[peakCount_++] = k;
    if (!peakCount_) peaks_[peakCount_++] = 1;
    int at = 0;
    for (int k = 1; k < bins_; ++k) {
        while (at + 1 < peakCount_ && 2 * k > peaks_[at] + peaks_[at + 1]) ++at;
        owner_[k] = peaks_[at];
    }
    std::fill(peakAge_.begin(), peakAge_.end(), 0);
    std::fill(matched_.begin(), matched_.end(), 0);
    std::copy_n(peaks_.begin(), peakCount_, matchOrder_.begin());
    std::sort(matchOrder_.begin(), matchOrder_.begin() + peakCount_, [&](int a, int b) {
        return energy_[a] == energy_[b] ? a < b : energy_[a] > energy_[b];
    });
    for (int j = 0; j < peakCount_; ++j) {
        int k = matchOrder_[j];
        int ref = std::norm(spectrum_[1][k]) > std::norm(spectrum_[0][k]) ? 1 : 0;
        double left = std::log(std::max(1e-30f, energy_[k - 1]));
        double middle = std::log(std::max(1e-30f, energy_[k]));
        double right = std::log(std::max(1e-30f, energy_[std::min(k + 1, bins_ - 1)]));
        double denominator = left - 2 * middle + right;
        double offset = denominator < -1e-12 ? std::clamp(.5 * (left - right) / denominator, -.5, .5) : 0;
        double magnitudeHz = (k + offset) * binHz;
        auto frequency = [&](int channel) {
            double hz = (k + wrap(std::arg(spectrum_[channel][k]) - previousPhase_[channel][k] - tau * k * hop_ / n_) *
                                 n_ / (tau * hop_)) *
                        binHz;
            return firstFrame_ || hz <= 0 || previousEnergy_[k] < peakFloor ||
                           std::abs(hz - magnitudeHz) > 1.5 * binHz
                       ? magnitudeHz : hz;
        };
        double hz = frequency(ref), distance = 1e30;
        int prev = -1;
        for (int candidate = std::max(1, k - 4); candidate <= std::min(bins_ - 1, k + 4); ++candidate) {
            if (!previousPeakAge_[candidate] || matched_[candidate]) continue;
            double ratio = energy_[k] / std::max(peakFloor, previousEnergy_[candidate]);
            double prediction = previousPeakHz_[candidate] + previousVelocity_[candidate];
            double frequencyDistance = std::abs(prediction - hz);
            if (ratio < .01 || ratio > 100 || frequencyDistance > 2 * binHz) continue;
            double d = frequencyDistance + .1 * binHz * std::abs(std::log(ratio));
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
        peakMatch_[k] = prev;
        reference_[k] = ref;
        peakHz_[k] = hz;
        velocity_[k] = prev < 0 ? 0 : .5 * previousVelocity_[prev] +
            .5 * std::clamp(hz - previousPeakHz_[prev], -binHz, binHz);
        const int stableAge = n_ / hop_ + 1;
        if (energy_[k] > peakFloor)
            peakAge_[k] = prev < 0 ? 1 : std::min(stableAge, previousPeakAge_[prev] + 1);
    }
    if (polyphonic_) analyseChord(mask, binHz);
    for (int j = 0; j < peakCount_; ++j) {
        const int k = matchOrder_[j], prev = peakMatch_[k], ref = reference_[k];
        const double hz = peakHz_[k];
        double desired;
        if (polyphonic_) {
            desired = partialShift_[k];
        } else {
            int held = prev >= 0 && mask == previousMask_ ? previousNote_[prev] : -1;
            int note = targetNote(hz, mask, held);
            // A new partial (including after silence) needs a window of history
            // before its frequency estimate can lock a note at the boundary.
            selectedNote_[k] = peakAge_[k] >= n_ / hop_ + 1 ? note : -1;
            double weight = std::clamp((hz - low) / std::max(binHz * 2, low * .15), 0., 1.) *
                            std::clamp((high - hz) / std::max(binHz * 2, high * .1), 0., 1.);
            double tuned = noteHz(note);
            desired = mask && std::abs(tuned - hz) > hz * identityHzFraction ?
                                 (tuned - hz) * amountS_ * weight : 0;
        }
        shift_[k] = prev < 0 ? desired : previousShift_[prev] + control * (desired - previousShift_[prev]);
        const bool tunedLane = std::abs(shift_[k]) > hz * identityHzFraction;
        const float oldWeight = prev < 0 ? 0 : previousTunedWeight_[prev];
        // Fade back to the unrotated source before parking the synthesis phase.
        tunedWeight_[k] = tunedLane ? 1.f : std::max(0.f, oldWeight - .5f);
        if (!tunedLane) shift_[k] = 0;
        const int offsetBins = int(std::lround(shift_[k] / binHz));
        auto placement = prev < 0 ? Placement{offsetBins, offsetBins, 0} : previousPlacement_[prev];
        if (placement.hops > 0 && --placement.hops == 0) placement.from = placement.to;
        if (placement.hops == 0 && placement.to != offsetBins) {
            placement.to = offsetBins;
            placement.hops = 2;
        }
        placement_[k] = placement;
        // Store phase at the Hann centre so both placements agree there, including
        // when a matched peak's strongest bin moves by an odd number of bins.
        rotation_[k] = prev < 0 || oldWeight == 0 || tunedWeight_[k] == 0 ? 0 :
            wrap(previousPhase_[ref][prev] + previousRotation_[prev] +
                 pi * (prev - k) + tau * (hz + shift_[k]) * hop_ / sr_ - std::arg(spectrum_[ref][k]));
        rotationFactor_[k] = tunedWeight_[k] > 0 ? std::polar(1.f, float(rotation_[k])) : Complex(1, 0);
    }
    const float attackRelease = float(hop_ / (.020 * sr_));
    const float attackFloor = float(n_ * n_) * 2e-8f;
    for (int k = 1; k < bins_; ++k) {
        int peak = owner_[k];
        double hz = k * binHz;
        bool inBand = polyphonic_ ? std::abs(k - peak) <= 2 : hz > low && hz < high;
        const int next = std::min(k + 1, bins_ - 1);
        const float local = energy_[k - 1] + 2 * energy_[k] + energy_[next];
        const float history = previousEnergy_[k - 1] + 2 * previousEnergy_[k] + previousEnergy_[next];
        const float flux = std::clamp((local - 2.5f * history) / (local + attackFloor), 0.f, 1.f);
        attackKeep_[k] = std::max(.8f * flux, attackKeep_[k] - attackRelease);
        const float tuned = inBand ? tunedWeight_[peak] * (1 - attackKeep_[k]) : 0.f;
        const auto placement = placement_[peak];
        const float blend = placement.hops > 0 ? (3 - placement.hops) / 3.f : 1.f;
        const bool showTuned = std::abs(shift_[peak]) > hz * .0005777895;
        auto place = [&](int offset, float weight) {
            if (weight == 0) return;
            const int dest = k + offset;
            const bool valid = dest > 0 && dest < bins_;
            const Complex factor = valid ? rotationFactor_[peak] * ((offset % 2) ? -weight : weight) :
                                           Complex(weight, 0);
            for (int c = 0; c < 2; ++c) {
                const auto v = spectrum_[c][k] * factor;
                wet_[c][valid ? dest : k] += v;
                if (valid && showTuned) tunedViewBins_[c][dest] += v;
            }
        };
        for (int c = 0; c < 2; ++c) {
            wet_[c][k] += spectrum_[c][k] * (1 - tuned);
        }
        place(placement.from, tuned * (1 - blend));
        place(placement.to, tuned * blend);
    }
    if (polyphonic_) {
        quietTail_ = quiet_ >= uint64_t(2 * n_) ? quietTail_ + hop_ : 0;
    } else {
        applySpectralColor(mask, amount, low, high, binHz);
    }
    // Publish independent dry and shifted spectra; all storage is prepared off-thread.
    float mixVis = mask && amount > 0 ? mixS_ * modeWet_ : 0.f;
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
    previousPeakAge_.swap(peakAge_);
    previousPeakHz_.swap(peakHz_);
    previousVelocity_.swap(velocity_);
    previousNote_.swap(selectedNote_);
    previousMask_ = mask;
    previousReference_.swap(reference_);
    std::copy(energy_.begin(), energy_.end(), previousEnergy_.begin());
    previousRotation_.swap(rotation_);
    previousShift_.swap(shift_);
    previousPlacement_.swap(placement_);
    previousTunedWeight_.swap(tunedWeight_);
    firstFrame_ = false;
}
void Chroma::process(float* buf, int32_t frames) {
    if (!buf || frames <= 0 || bypassed()) return;
    const float coeff = float(1 - std::exp(-1 / (.01 * sr_)));
    const float modeStep = float(1 / (.010 * sr_));
    const bool requestedMode = getParam(Mode) >= .5f;
    float mixTarget = noteMask() && getParam(Amount) > 0 ? getParam(Mix) : 0;
    float gainTarget = float(std::pow(10., (-24 + 36 * getParam(Output)) / 20));
    if (snap_) {
        mixS_ = mixTarget;
        gainS_ = gainTarget;
        if (requestedMode != polyphonic_) resetMode(requestedMode);
        snap_ = false;
    }
    for (int i = 0; i < frames; ++i) {
        int in = int(count_ % n_), out = int(count_ % (2 * n_));
        float l = clean(buf[2 * i]), r = clean(buf[2 * i + 1]);
        quiet_ = std::max(std::abs(l), std::abs(r)) < 1e-8f ? quiet_ + 1 : 0;
        mixS_ += coeff * (mixTarget - mixS_);
        gainS_ += coeff * (gainTarget - gainS_);
        if (requestedMode != polyphonic_)
            modeWet_ = std::max(0.f, modeWet_ - modeStep);
        else if (modeWarmup_ > 0)
            --modeWarmup_;
        else
            modeWet_ = std::min(1.f, modeWet_ + modeStep);
        float peak = 0;
        for (int c = 0; c < 2; ++c) {
            float dry = input_[c][in], wet = ola_[c][out];
            ola_[c][out] = 0;
            input_[c][in] = c ? r : l;
            buf[2 * i + c] = clean((dry + mixS_ * modeWet_ * (wet - dry)) * gainS_);
            peak = std::max(peak, std::abs(buf[2 * i + c]));
        }
        quietOutput_ = peak < quietOutputThreshold ? quietOutput_ + 1 : 0;
        ++count_;
        if (count_ % hop_ == 0) {
            if (requestedMode != polyphonic_ && modeWet_ == 0) {
                resetMode(requestedMode);
                modeWarmup_ = n_;
            }
            frame();
        }
    }
    idle_ = quiet_ >= uint64_t(2 * n_) && quietOutput_ >= uint64_t(n_) && quietTail_ >= uint64_t(n_);
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
