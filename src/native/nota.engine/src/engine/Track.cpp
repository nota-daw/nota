// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "Track.h"

namespace nota {

float ClipAdsr::gainAt(double t, double len) const {
    double g;
    if (t < attack) g = t / attack;
    else if (t < attack + decay)
        g = 1.0 - (1.0 - sustain) * ((t - attack) / decay);
    else
        g = sustain;
    if (release > 0.0 && t > len - release) g *= std::max(0.0, (len - t) / release);
    return static_cast<float>(std::clamp(g, 0.0, 1.0));
}

int64_t AudioClip::effectiveLength() const {
    if (!sample) return 0;
    int64_t maxLen = sample->frames - static_cast<int64_t>(sourceOffsetFrames);
    if (lengthFrames <= 0) return maxLen;
    return lengthFrames < maxLen ? lengthFrames : maxLen;
}

void Track::updateMidiCv(const MidiEv* evs, int n) {
    int held = midiHeld_.load(std::memory_order_relaxed);
    float vel = midiVel_.load(std::memory_order_relaxed);
    float note = midiNote_.load(std::memory_order_relaxed);
    for (int i = 0; i < n; ++i) {
        if (evs[i].on) {
            vel = evs[i].vel;
            note = evs[i].pitch / 127.0f;
            ++held;
        } else if (held > 0)
            --held;
    }
    midiHeld_.store(held < 0 ? 0 : held, std::memory_order_relaxed);
    midiVel_.store(vel, std::memory_order_relaxed);
    midiNote_.store(note, std::memory_order_relaxed);
}

void Track::setMeter(float pkL, float pkR, float rmsL, float rmsR) {
    mPeakL_.store(pkL, std::memory_order_relaxed);
    mPeakR_.store(pkR, std::memory_order_relaxed);
    mRmsL_.store(rmsL, std::memory_order_relaxed);
    mRmsR_.store(rmsR, std::memory_order_relaxed);
}

void Track::setStereo(float l, float r, float lr) {
    stereoL_.store(l, std::memory_order_relaxed);
    stereoR_.store(r, std::memory_order_relaxed);
    stereoLR_.store(lr, std::memory_order_relaxed);
}

void Track::stereoFold(double sqL, double sqR, double sLR, int32_t frames, float a) {
    const double n = frames > 0 ? frames : 1;
    auto fold = [a](std::atomic<float>& e, double mean) {
        const float cur = e.load(std::memory_order_relaxed);
        e.store(cur + a * (static_cast<float>(mean) - cur), std::memory_order_relaxed);
    };
    fold(stereoL_, sqL / n);
    fold(stereoR_, sqR / n);
    fold(stereoLR_, sLR / n);
}

const AudioClip* audioClipRef(const Track& t, int32_t clipIndex) {
    if (t.type() != TrackType::Audio) return nullptr;
    if (clipIndex >= 0) return clipIndex < static_cast<int32_t>(t.clips.size()) ? &t.clips[clipIndex] : nullptr;
    const int32_t scene = sessionSceneOfClip(clipIndex);
    if (scene < 0 || scene >= static_cast<int32_t>(t.sessionSlots.size())) return nullptr;
    const SessionSlot& s = t.sessionSlots[scene];
    return s.hasClip && s.audio.sample ? &s.audio : nullptr;
}

} // namespace nota
