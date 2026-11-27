// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/PluginContracts.h"
#include "nota/dsp/capabilities/IMosaicInstrument.h"
#include "nota/dsp/capabilities/IRack.h"
#include "nota/dsp/capabilities/IRhythmInstrument.h"
#include "nota/dsp/capabilities/ISampleInstrument.h"
#include "nota/dsp/capabilities/ISlotAnnotationDevice.h"
#include "nota/dsp/capabilities/IWavetableInstrument.h"

namespace nota::dsp {

// Anchor vtables and RTTI typeinfo cleanly inside libnota_dsp across shared library boundaries.
IPluginCatalog::~IPluginCatalog() = default;
IPluginScanner::~IPluginScanner() = default;
IPluginHost::~IPluginHost() = default;

namespace capabilities {

ISlotAnnotationDevice::~ISlotAnnotationDevice() = default;
ISampleInstrument::~ISampleInstrument() = default;
IMosaicInstrument::~IMosaicInstrument() = default;
IWavetableInstrument::~IWavetableInstrument() = default;
IRhythmInstrument::~IRhythmInstrument() = default;
IRack::~IRack() = default;

} // namespace capabilities
} // namespace nota::dsp
