// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Oscillator — band-limited polyBLEP waveforms and tempo-synced LFOs
// . polyBLEP residual evaluation matches the standard
// Stilson/Smith form; LFOs use fast_math::sin for audio-rate FM safety.
// Shared by all synth and modulation devices. AR-6 safe (no tables).

#pragma once

#include <cstdint>

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

// Band-limited oscillator with phase in [0, 1).
class BleOscillator {
  public:
    BleOscillator() = default;

    void setSampleRate(double sr) noexcept;
    // freq in Hz; wave: 0 = saw, 1 = square, 2 = triangle, 3 = sine.
    void setParams(float freq, int wave, float pulseWidth = 0.5f) NOTA_RT_SAFE;
    void reset(float phase = 0.0f) noexcept;

    float tick() NOTA_RT_SAFE;
    float phase() const noexcept { return phase_; }

  private:
    static float polyBlep(float t, float dt) NOTA_RT_SAFE;

    double sr_ = 44100.0;
    float freq_ = 440.0f;
    int wave_ = 0;
    float pw_ = 0.5f;
    float phase_ = 0.0f;
};

enum class LfoWave : uint8_t { Sine = 0, Triangle = 1, Saw = 2, Square = 3, SampleHold = 4 };

// Tempo-synced multi-waveform LFO. Rate is either free (Hz) or locked to the
// transport (bpm + beats per cycle); the engine pushes tempo per block.
class LfoGenerator {
  public:
    LfoGenerator() = default;

    void setSampleRate(double sr) noexcept;
    void setWave(LfoWave w) noexcept { wave_ = w; }
    void setRateHz(float hz) NOTA_RT_SAFE;
    // beatsPerCycle = cycle length in beats (e.g. 4 = one bar of 4/4).
    void setTempoSync(float bpm, float beatsPerCycle) NOTA_RT_SAFE;
    void reset(float phase = 0.0f) noexcept;

    // Next bipolar [-1, 1] value. Audio thread.
    float tick() NOTA_RT_SAFE;

  private:
    double sr_ = 44100.0;
    LfoWave wave_ = LfoWave::Sine;
    float phase_ = 0.0f;
    float inc_ = 0.01f;
    uint32_t holdSeed_ = 0x9E3779B9u;
    float holdValue_ = 0.0f;
};

} // namespace dsp
} // namespace nota
