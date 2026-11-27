// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SynthVoice — base polyphonic voice state with age stealing, sustain, and MPE expression.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <cstdint>

namespace nota::dsp {

struct NOTA_DSP_API SynthVoice {
    bool active = false;
    bool sustained = false;
    bool released = false;
    int32_t pitch = -1;
    int32_t noteId = -1;
    float velocity = 0.0f;
    uint32_t age = 0;
    float bendSemis = 0.0f;
    float pressure = 0.0f;
    float slide = 0.5f;

    void noteOn(int32_t p, float vel, int32_t id) NOTA_RT_SAFE;
    void noteOff() NOTA_RT_SAFE;
    void sustain(bool down) NOTA_RT_SAFE;
    void kill() noexcept;
    void tickAge() NOTA_RT_SAFE;
    bool audible() const noexcept { return active && !released; }
};

} // namespace nota::dsp
