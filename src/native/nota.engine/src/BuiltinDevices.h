// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The built-in effects a nested chain can hold — a rack's chains (RackCore) and a Nota Rhythm
// voice's insert chain — by their builtinKind. One factory for both, so a chain device means
// the same thing wherever it lives and a kit's FX recipe (Drum Rack pad or Rhythm voice)
// builds the same devices. Kind 5 (the Audio Effect Rack itself), the Engine-only devices
// (Lens / Flanger / Phaser / Chorus) and hosted plugins are not built here.

#pragma once

#include "Device.h"

#include "Eq.h"
#include "Compressor.h"
#include "Reverb.h"
#include "Delay.h"
#include "Utility.h"
#include "Amp.h"
#include "AutoFilter.h"
#include "Vintage.h"
#include "AutoPan.h"
#include "AutoShift.h"
#include "BeatRepeat.h"
#include "Crush.h"
#include "DynamicEq.h"
#include "Ceiling.h"
#include "Strata.h"
#include "Eq3.h"
#include "Forge.h"
#include "AutoGain.h"
#include "Shutter.h"
#include "Chamber.h"
#include "Prism.h"

#include <cstdint>
#include <memory>

namespace nota {

inline std::shared_ptr<Device> makeChainDevice(int32_t kind) {
    switch (kind) {
        case 0:  return std::make_shared<Eq>();
        case 1:  return std::make_shared<Compressor>();
        case 2:  return std::make_shared<Reverb>();
        case 3:  return std::make_shared<Delay>();
        case 4:  return std::make_shared<Utility>();
        case 6:  return std::make_shared<Amp>();
        case 7:  return std::make_shared<AutoFilter>();
        case 8:  return std::make_shared<Vintage>();
        case 9:  return std::make_shared<AutoPan>();
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
        default: return nullptr;   // 5 = Effect Rack (nesting) / plugin: not via this factory
    }
}

} // namespace nota
