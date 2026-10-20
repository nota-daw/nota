// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SynthVoice implementation.

#include "dsp/poly/SynthVoice.h"

namespace nota {
namespace dsp {

void SynthVoice::noteOn(int32_t p, float vel, int32_t id) NOTA_RT_SAFE {
    active = true;
    sustained = false;
    released = false;
    pitch = p;
    velocity = vel < 0.0f ? 0.0f : (vel > 1.0f ? 1.0f : vel);
    noteId = id;
    age = 0;
    bendSemis = 0.0f;
    pressure = 0.0f;
    slide = 0.5f;
}

void SynthVoice::noteOff() NOTA_RT_SAFE {
    released = true;
}

void SynthVoice::sustain(bool down) NOTA_RT_SAFE {
    if (!down && sustained) {
        sustained = false;
        released = true;
    } else if (down && active && released) {
        sustained = true;
        released = false;
    }
}

void SynthVoice::kill() noexcept {
    active = false;
    sustained = false;
    released = false;
    pitch = -1;
    velocity = 0.0f;
}

void SynthVoice::tickAge() NOTA_RT_SAFE {
    if (active && age < 0xFFFFFFFEu) ++age;
}

} // namespace dsp
} // namespace nota
