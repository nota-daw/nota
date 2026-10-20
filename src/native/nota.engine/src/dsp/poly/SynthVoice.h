// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SynthVoice — base polyphonic voice state . Fixed-capacity,
// allocation-free: age counters drive voice stealing, sustain holds releases,
// and per-note MPE expression (bend/pressure/slide) rides alongside.
// Concrete instruments extend this struct with their DSP state and let
// PolyVoiceManager<T> own the allocation policy.

#pragma once

#include <cstdint>

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

struct SynthVoice {
    bool active = false;    // sounding (attack/decay/sustain/release)
    bool sustained = false; // held by the sustain pedal (not by a key)
    bool released = false;  // in release (note off received)
    int32_t pitch = -1;
    int32_t noteId = -1; // monotonically increasing allocation stamp
    float velocity = 0.0f;
    uint32_t age = 0;       // blocks since allocation (stealing: largest = oldest)
    float bendSemis = 0.0f; // MPE per-note pitch bend, semitones
    float pressure = 0.0f;  // MPE per-note pressure 0..1
    float slide = 0.5f;     // MPE per-note slide 0..1 (0.5 = neutral)

    void noteOn(int32_t p, float vel, int32_t id) NOTA_RT_SAFE;
    void noteOff() NOTA_RT_SAFE;
    void sustain(bool down) NOTA_RT_SAFE;
    void kill() noexcept;
    void tickAge() NOTA_RT_SAFE;
    bool audible() const noexcept { return active && !released; }
};

} // namespace dsp
} // namespace nota
