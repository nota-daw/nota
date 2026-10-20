// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Instrument non-trivial defaults — constructor + controller routing moved out
// of the header . The remaining one-line virtual defaults stay
// inline as interface documentation.

#include "Instrument.h"

#include <limits>

#include "engine/MidiController.h"
#include "engine/NoteExpression.h"

namespace nota {

Instrument::Instrument() {
    for (auto& v : deliveredCtl) v.store(std::numeric_limits<float>::quiet_NaN(), std::memory_order_relaxed);
}

void Instrument::controller(int32_t ctl, float value) {
    switch (ctl) {
    case kCtlPitchWheel: noteExpression(-1, ExprBend, value); break;
    case kCtlChannelPressure: noteExpression(-1, ExprPressure, value); break;
    case 74: noteExpression(-1, ExprSlide, value); break;
    case kCtlSustain: noteExpression(-1, ExprSustain, value); break;
    default: break;
    }
}

} // namespace nota
