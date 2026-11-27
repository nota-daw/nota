// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "effects/color/Color.h"
#include "ColorHelpers.h"
#include <utility>
#include <cstdlib>
#include <cstdint>
#include <cstddef>
#include <complex>
#include <cmath>
#include <atomic>
#include <array>
#include <algorithm>
#include "nota/dsp/primitives/BiquadCascade.h"
#include "common/DeviceMath.h"

namespace nota {
using namespace color_detail;
void Color::setSampleRate(double sr, int32_t) {
    sr_ = std::isfinite(sr) && sr >= 8000 ? std::min(sr, 768000.) : 44100.;
    const auto prepareFft = [&](int index) {
        auto& config = fftConfigs_[static_cast<size_t>(index)];
        config.size = kFftSizes[static_cast<size_t>(index)];
        config.hop = config.size / 4;
        config.bins = config.size / 2;
        config.fft.resize(static_cast<size_t>(config.size));
        for (int i = 0; i < config.size; ++i)
            config.window[static_cast<size_t>(i)] = float(std::sqrt(.5 - .5 * std::cos(tau * i / config.size)));
        for (int k = 0; k < config.bins; ++k)
            config.phaseAdvance[static_cast<size_t>(k)] = tau * k * config.hop / config.size;
        config.onsetPole = static_cast<float>(std::pow(pole(10), config.hop));
        config.gateOpen = float(1 - std::exp(-config.hop / (.005 * sr_)));
        config.gateClose = float(1 - std::exp(-config.hop / (.050 * sr_)));
    };
    prepareFft(kDefaultFft);
    prepareNoteTunings(noteTunings_, 0);
    pre_->prepare(sr_);
    for (int i = 0; i <= kSpectrumBins; ++i)
        viewEdges_[static_cast<size_t>(i)] = 40 * std::pow(500., i / double(kSpectrumBins));
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
        amountDelta_ = (amountTarget_ - amount_) / static_cast<float>(amountRemaining_);
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
        paths_[static_cast<size_t>(currentPath_)].reset(requestedMask_);
        amount_ = amountTarget_;
        amountRemaining_ = 0;
        mix_ = mixTarget_;
        muteLow_ = muteLowTarget_;
        muteHigh_ = muteHighTarget_;
        gain_ = gainTarget_;
        resonance_ = resonanceTarget_;
        morph_ = morphTarget_;
        snap_ = false;
    } else if (fadeRemaining_ == 0 && requestedMask_ != paths_[static_cast<size_t>(currentPath_)].mask) {
        paths_[static_cast<size_t>(1 - currentPath_)].reset(requestedMask_);
        // Start preparing the new chord at the MIDI input offset so its first
        // crossfade meets the corresponding delayed audio.
        fadeWait_ = midi ? processingFft().size : 0;
        activeFadeLength_ = midi ? processingFft().hop : fadeLength_;
        fadeRemaining_ = activeFadeLength_;
    }
    paths_[static_cast<size_t>(currentPath_)].tune(*this);
    if (fadeRemaining_) paths_[static_cast<size_t>(1 - currentPath_)].tune(*this);
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
        const double magnitude = std::abs(mid[static_cast<size_t>(ch)]);
        const double quick = 4.2 * fast_[static_cast<size_t>(ch)].run(magnitude, 0, fastRelease_);
        const double slow = slow_[static_cast<size_t>(ch)].run(magnitude, slowAttack_, slowRelease_);
        result[static_cast<size_t>(ch)] = float(
            noteMaskEnvelope_[static_cast<size_t>(ch)].run(quick >= slow && quick > .001 ? 1 : 0, 0, noteRelease_));
    }
    return result;
}

void Color::analyseDry(uint64_t end) {
    auto& config = processingFft();
    dryMagnitude_.fill(0);
    const uint64_t start = end + kMaxWindow - static_cast<uint64_t>(config.size);
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < config.size; ++i)
            time_[static_cast<size_t>(i)] =
                bandDelay_[(start + static_cast<uint64_t>(i)) % kMaxWindow].mid[static_cast<size_t>(ch)] *
                config.window[static_cast<size_t>(i)];
        config.fft.fft(time_.data(), fftBins_.data());
        for (int k = 1; k < config.bins; ++k)
            dryMagnitude_[static_cast<size_t>(k)] += std::norm(fftBins_[static_cast<size_t>(k)]);
    }
    for (auto& value : dryMagnitude_) value = std::sqrt(value);
}

void Color::publishLine(
    const ScopeHistory& history, const BassSpectrum::Line& bass, ScopeLevels& destination, bool gated) {
    auto& config = fftConfigs_[kDefaultFft];
    std::array<float, kScopeWindow / 2 + 1> power{};
    for (int ch = 0; ch < 2; ++ch) {
        for (int i = 0; i < kScopeWindow; ++i)
            time_[static_cast<size_t>(i)] =
                history[(sample_ + static_cast<uint64_t>(i)) % kScopeWindow][static_cast<size_t>(ch)] *
                config.window[static_cast<size_t>(i)];
        config.fft.fft(time_.data(), fftBins_.data());
        for (int k = 1; k < kScopeWindow / 2; ++k)
            power[static_cast<size_t>(k)] += std::norm(fftBins_[static_cast<size_t>(k)]);
    }
    const double binsPerHz = kScopeWindow / sr_;
    const double gateBinsPerHz = gated ? processingFft().size / sr_ : 0;
    for (int i = 0; i < kSpectrumBins; ++i) {
        const float weight = bassSpectrum_.shortWeights[static_cast<size_t>(i)];
        float peak = bass.viewPower[static_cast<size_t>(i)];
        if (weight > 0) {
            const float fullBand = spectrumPower(power, viewEdges_[static_cast<size_t>(i)] * binsPerHz,
                viewEdges_[static_cast<size_t>(i + 1)] * binsPerHz);
            peak = std::lerp(peak, fullBand, weight);
        }
        const float gain =
            gated ? spectralGate_.gainAt((viewEdges_[static_cast<size_t>(i)] + viewEdges_[static_cast<size_t>(i + 1)]) *
                                         .5 * gateBinsPerHz)
                  : 1.f;
        const float amplitude = std::sqrt(peak) * gain / kScopeNormalizer;
        const float level = amplitude > 1e-4f ? std::clamp((20 * std::log10(amplitude) + 80) / 80, 0.f, 1.f) : 0.f;
        destination[static_cast<size_t>(i)].store(level, std::memory_order_relaxed);
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
    const unsigned hopMask = static_cast<unsigned>(config.hop - 1); // Every supported hop is a power of two.
    const int32_t source = sidechainSourceTrackId();
    const bool midi = source >= 0;
    const int32_t blockSource = std::exchange(midiBlockSource_, -1);
    const bool received = midi && blockSource == source;
    MidiKeyNotes notes = received ? midiStart_ : MidiKeyNotes{};
    const int eventCount = received ? midiEventCount_ : 0;
    int eventIndex = 0;
    const auto takeMidiThrough = [&](int sample) {
        while (eventIndex < eventCount && midiEvents_[static_cast<size_t>(eventIndex)].off <= sample)
            notes.apply(midiEvents_[static_cast<size_t>(eventIndex++)]);
    };
    for (int i = 0; i < frames; ++i) {
        if (idle_) {
            float* const begin = buf + 2 * i;
            float* const signal = std::find_if(begin, buf + 2 * frames, [](float value) { return clean(value) != 0; });
            const int next = int((signal - buf) / 2);
            // Keep the whole stereo frame when either channel receives input.
            std::fill(begin, buf + 2 * next, 0.f);
            sample_ += static_cast<uint64_t>(next - i);
            i = next;
            if (i == frames) return;
            wake();
        }
        if (eventIndex < eventCount) takeMidiThrough(i);
        const int mask = pre && midi ? pre_->delayedMask() : notes.mask;
        if (snap_ || sample_ % static_cast<uint64_t>(controlInterval_) == 0 ||
            (midi && fadeRemaining_ == 0 && mask != paths_[static_cast<size_t>(currentPath_)].mask))
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
        const size_t read = (sample_ + kMaxWindow - static_cast<uint64_t>(config.size)) % kMaxWindow;
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
        auto& current = paths_[static_cast<size_t>(currentPath_)];
        float active = current.active() ? 1.f : 0.f;
        Stereo tuned{};
        Stereo wet = current.process(*this, mid, dryMid, transient, dynamics, sample_, tuned);
        if (fadeRemaining_) {
            auto& next = paths_[static_cast<size_t>(1 - currentPath_)];
            const float fade =
                fadeWait_ > 0 ? 0.f : 1.f - float(fadeRemaining_) / static_cast<float>(activeFadeLength_);
            Stereo nextTuned{};
            const Stereo nextWet = next.process(*this, mid, dryMid, transient, dynamics, sample_, nextTuned);
            active = std::lerp(active, next.active() ? 1.f : 0.f, fade);
            for (int ch = 0; ch < 2; ++ch) {
                if (current.active() && next.active())
                    wet[static_cast<size_t>(ch)] =
                        std::lerp(wet[static_cast<size_t>(ch)], nextWet[static_cast<size_t>(ch)], fade);
                else if (next.active())
                    wet[static_cast<size_t>(ch)] = nextWet[static_cast<size_t>(ch)];
                tuned[static_cast<size_t>(ch)] =
                    std::lerp(tuned[static_cast<size_t>(ch)], nextTuned[static_cast<size_t>(ch)], fade);
            }
        }
        // With an inactive mask, use the crossover when Pre or a sideband mute is audible.
        const float crossoverDry = (1 - active) * std::max({muteLow_, muteHigh_, delayedPre.mix});
        const float effectWeight = active + crossoverDry;
        if (crossoverDry > 0)
            for (int ch = 0; ch < 2; ++ch)
                wet[static_cast<size_t>(ch)] = std::lerp(
                    wet[static_cast<size_t>(ch)], dryMid[static_cast<size_t>(ch)], crossoverDry / effectWeight);
        wet = finalLow_.run(finalHigh_.run(wet));
        OutputFrame output;
        output.dry = delayed.full;
        output.wet = wet;
        output.tuned = tuned;
        output.effectWeight = effectWeight;
        for (int ch = 0; ch < 2; ++ch) {
            output.sides[static_cast<size_t>(ch)] =
                delayed.low[static_cast<size_t>(ch)] + delayed.high[static_cast<size_t>(ch)];
            output.muted[static_cast<size_t>(ch)] =
                muteLow_ * delayed.low[static_cast<size_t>(ch)] + muteHigh_ * delayed.high[static_cast<size_t>(ch)];
            if (pre) output.tuned[static_cast<size_t>(ch)] += delayedPre.tuned[static_cast<size_t>(ch)];
        }
        if (activeGate_) {
            // A neutral note mask bypasses Gate while retaining Pre and the sideband mutes.
            output.gatedFraction = effectWeight > 0 ? active / effectWeight : 0;
            output = spectralGate_.push(output, sample_, config.size);
        }
        dryView_[scopeSlot] = output.dry;
        for (int ch = 0; ch < 2; ++ch) {
            const float mixed = std::lerp(output.dry[static_cast<size_t>(ch)],
                                    output.wet[static_cast<size_t>(ch)] + output.sides[static_cast<size_t>(ch)],
                                    mix_ * output.effectWeight) -
                                mix_ * output.muted[static_cast<size_t>(ch)];
            buf[2 * i + ch] = clean(gain_ * mixed);
            tunedViewHistory_[scopeSlot][static_cast<size_t>(ch)] =
                output.tuned[static_cast<size_t>(ch)] * mix_ * gain_;
        }
        if (preReady) bassSpectrum_.push(output.dry, tunedViewHistory_[scopeSlot]);
        const float outputPeak = std::max(std::abs(buf[2 * i]), std::abs(buf[2 * i + 1]));
        quiet_ = in[0] == 0.f && in[1] == 0.f && outputPeak < silence ? quiet_ + 1 : 0;

        ++sample_;
        if (fadeWait_ > 0) --fadeWait_;
        else if (fadeRemaining_ && --fadeRemaining_ == 0)
            currentPath_ = 1 - currentPath_;
        if (quiet_ >= settleSamples && fadeRemaining_ == 0) {
            idle_ = true;
            clearSpectrum();
            continue;
        }
        if (preReady && (sample_ & hopMask) == 0) {
            const bool shape =
                morph_ > 1e-6f && (paths_[static_cast<size_t>(currentPath_)].down ||
                                      (fadeRemaining_ && paths_[static_cast<size_t>(1 - currentPath_)].down));
            if (shape) analyseDry(sample_);
            paths_[static_cast<size_t>(currentPath_)].frame(*this, sample_, shape ? &dryMagnitude_ : nullptr);
            if (fadeRemaining_)
                paths_[static_cast<size_t>(1 - currentPath_)].frame(*this, sample_, shape ? &dryMagnitude_ : nullptr);
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
        out[i + 2] = i < kSpectrumBins
                         ? spectrumView_[static_cast<size_t>(i)].load(std::memory_order_relaxed)
                         : tunedView_[static_cast<size_t>(i - kSpectrumBins)].load(std::memory_order_relaxed);
    if (max > 2 + 2 * kSpectrumBins) {
        out[2 + 2 * kSpectrumBins] =
            sidechainSourceTrackId() >= 0 ? float(midiMaskView_.load(std::memory_order_relaxed)) : 0.f;
        return 3 + 2 * kSpectrumBins;
    }
    return 2 + count;
}

} // namespace nota
