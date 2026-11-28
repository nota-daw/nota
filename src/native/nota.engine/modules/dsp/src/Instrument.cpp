// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/Instrument.h"
#include "nota/dsp/rt/ExpressionTypes.h"
#include "nota/dsp/rt/MidiControllerTypes.h"
#include "nota/dsp/rt/SilenceGate.h"

#include <atomic>
#include <cstdint>
#include <limits>

namespace nota::dsp {
namespace {

std::atomic<uint64_t> gNextInstrumentUid{1};

} // namespace

uint64_t nextInstrumentUid() noexcept {
    return gNextInstrumentUid.fetch_add(1u, std::memory_order_relaxed);
}

Instrument::Instrument() {
    for (auto& v : deliveredCtl) { v.store(std::numeric_limits<float>::quiet_NaN(), std::memory_order_relaxed); }
}

Instrument::~Instrument() {
    if (countedNeverSleep_.exchange(false, std::memory_order_acq_rel)) { adjustNeverSleepDevices(-1); }
}

void Instrument::setSleepMode(PluginSleepMode mode) {
    sleepMode_.store(mode, std::memory_order_release);
    const bool never = (mode == PluginSleepMode::Never);
    const bool prev = countedNeverSleep_.exchange(never, std::memory_order_acq_rel);
    if (prev != never) adjustNeverSleepDevices(never ? 1 : -1);
    wake();
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

} // namespace nota::dsp
