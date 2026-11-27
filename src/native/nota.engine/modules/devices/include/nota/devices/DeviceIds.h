// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DeviceIds — canonical numeric kinds and stable string slugs for built-in devices.

#pragma once

#include "nota/devices/Export.h"

#include <cstdint>
#include <string_view>

namespace nota::devices {

enum class AudioEffectKind : int32_t {
    Eq = 0,
    Compressor = 1,
    Reverb = 2,
    Delay = 3,
    Utility = 4,
    EffectRack = 5,
    Amp = 6,
    AutoFilter = 7,
    Vintage = 8,
    AutoPan = 9,
    AutoShift = 10,
    BeatRepeat = 11,
    Crush = 12,
    DynamicEq = 13,
    Ceiling = 14,
    Strata = 15,
    Eq3 = 16,
    Forge = 17,
    AutoGain = 18,
    Shutter = 19,
    Chamber = 20,
    Prism = 21,
    Lens = 22,
    Flanger = 23,
    Phaser = 24,
    Chorus = 25,
    Vocoder = 26,
    Color = 27,
};

enum class InstrumentKind : int32_t {
    Synth = 0,
    Sampler = 1,
    PhysicalSynth = 2,
    InstrumentRack = 3,
    DrumRack = 4,
    WavetableSynth = 5,
    VoltSynth = 6,
    BassSynth = 7,
    PendulumSynth = 8,
    OperatorSynth = 9,
    GrainSynth = 10,
    FluxSynth = 11,
    RhythmMachine = 12,
    Monolith = 13,
    Pentad = 14,
    Consort = 15,
    Keys = 16,
    Mosaic = 17,
    Twin = 18,
};

enum class MidiEffectKind : int32_t {
    Arpeggiator = 0,
    MidiChord = 1,
    MidiScale = 2,
    MidiNoteLength = 3,
    MidiVelocity = 4,
    MidiRandom = 5,
    MidiRack = 6,
};

NOTA_DEVICES_API std::string_view audioEffectSlug(int32_t kind) noexcept;
NOTA_DEVICES_API std::string_view instrumentSlug(int32_t kind) noexcept;
NOTA_DEVICES_API std::string_view midiEffectSlug(int32_t kind) noexcept;

NOTA_DEVICES_API int32_t audioEffectKindFromSlug(std::string_view slug) noexcept;
NOTA_DEVICES_API int32_t instrumentKindFromSlug(std::string_view slug) noexcept;
NOTA_DEVICES_API int32_t midiEffectKindFromSlug(std::string_view slug) noexcept;

} // namespace nota::devices
