// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The built-in effects a nested chain can hold — a rack's chains (RackCore) and a Nota Rhythm
// voice's insert chain — by their builtinKind. One factory for both, so a chain device means
// the same thing wherever it lives and a kit's FX recipe (Drum Rack pad or Rhythm voice)
// builds the same devices. Kind 5 (the Audio Effect Rack itself), the Engine-only devices
// (Lens / Flanger / Phaser / Chorus) and hosted plugins are not built here.

#pragma once

#include <cstdint>
#include <memory>

namespace nota {

class Device;

std::shared_ptr<Device> makeChainDevice(int32_t kind);

} // namespace nota
