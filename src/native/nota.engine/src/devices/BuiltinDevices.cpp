// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Chain-device factory — the only TU including all concrete effect headers,
// so RackCore.h and other consumers stay light.

#include "BuiltinDevices.h"

#include "Device.h"

#include "devices/effects/Eq.h"
#include "devices/effects/Compressor.h"
#include "devices/effects/Reverb.h"
#include "devices/effects/Delay.h"
#include "devices/effects/Utility.h"
#include "devices/effects/Amp.h"
#include "devices/effects/AutoFilter.h"
#include "devices/effects/Vintage.h"
#include "devices/effects/AutoPan.h"
#include "devices/effects/AutoShift.h"
#include "devices/effects/BeatRepeat.h"
#include "devices/effects/Crush.h"
#include "devices/effects/DynamicEq.h"
#include "devices/effects/Ceiling.h"
#include "devices/effects/Strata.h"
#include "devices/effects/Eq3.h"
#include "devices/effects/Forge.h"
#include "devices/effects/AutoGain.h"
#include "devices/effects/Shutter.h"
#include "devices/effects/Chamber.h"
#include "devices/effects/Prism.h"

namespace nota {

std::shared_ptr<Device> makeChainDevice(int32_t kind) {
    switch (kind) {
    case 0: return std::make_shared<Eq>();
    case 1: return std::make_shared<Compressor>();
    case 2: return std::make_shared<Reverb>();
    case 3: return std::make_shared<Delay>();
    case 4: return std::make_shared<Utility>();
    case 6: return std::make_shared<Amp>();
    case 7: return std::make_shared<AutoFilter>();
    case 8: return std::make_shared<Vintage>();
    case 9: return std::make_shared<AutoPan>();
    case 10: return std::make_shared<AutoShift>();
    case 11: return std::make_shared<BeatRepeat>();
    case 12: return std::make_shared<Crush>();
    case 13: return std::make_shared<DynamicEq>();
    case 14: return std::make_shared<Ceiling>();
    case 15: return std::make_shared<Strata>();
    case 16: return std::make_shared<Eq3>();
    case 17: return std::make_shared<Forge>();
    case 18: return std::make_shared<AutoGain>();
    case 19: return std::make_shared<Shutter>();
    case 20: return std::make_shared<Chamber>();
    case 21: return std::make_shared<Prism>();
    default: return nullptr; // 5 = Effect Rack (nesting) / plugin: not via this factory
    }
}

} // namespace nota
