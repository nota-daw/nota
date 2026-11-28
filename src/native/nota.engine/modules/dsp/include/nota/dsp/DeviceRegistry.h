// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DeviceRegistry — thread-safe single-process built-in device factory registry.

#pragma once

#include "nota/dsp/Export.h"

#include <cstdint>
#include <functional>
#include <memory>
#include <vector>

namespace nota::dsp {

class Device;
class Instrument;
class MidiDevice;

class NOTA_DSP_API DeviceRegistry {
  public:
    using AudioEffectFactory = std::function<std::shared_ptr<Device>()>;
    using InstrumentFactory = std::function<std::shared_ptr<Instrument>()>;
    using MidiEffectFactory = std::function<std::shared_ptr<MidiDevice>()>;

    static void registerAudioEffect(int32_t kind, AudioEffectFactory factory, bool allowInRackChain = true);
    static void registerChainEffect(int32_t kind, AudioEffectFactory factory);
    static void registerInstrument(int32_t kind, InstrumentFactory factory);
    static void registerMidiEffect(int32_t kind, MidiEffectFactory factory);

    [[nodiscard]] static std::shared_ptr<Device> createAudioEffect(int32_t kind);
    [[nodiscard]] static std::shared_ptr<Device> createChainEffect(int32_t kind);
    [[nodiscard]] static std::shared_ptr<Instrument> createInstrument(int32_t kind);
    [[nodiscard]] static std::shared_ptr<MidiDevice> createMidiEffect(int32_t kind);

    static bool hasAudioEffect(int32_t kind);
    static bool hasChainEffect(int32_t kind);
    static bool hasInstrument(int32_t kind);
    static bool hasMidiEffect(int32_t kind);

    static std::vector<int32_t> audioEffectKinds();
    static std::vector<int32_t> chainEffectKinds();
    static std::vector<int32_t> instrumentKinds();
    static std::vector<int32_t> midiEffectKinds();

    static void clearForTests();
};

} // namespace nota::dsp
