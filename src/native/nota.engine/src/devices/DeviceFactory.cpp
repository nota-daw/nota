// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in factories — the only TU (besides BuiltinDevices.cpp) including all
// concrete device/instrument headers, so Engine_Devices.cpp stays light
// . Bodies moved verbatim from Engine_Devices.cpp.

#include "DeviceFactory.h"

#include "Device.h"
#include "Instrument.h"
#include "MidiDevice.h"

#include "devices/effects/Amp.h"
#include "devices/midi_fx/Arpeggiator.h"
#include "devices/effects/AutoFilter.h"
#include "devices/effects/AutoGain.h"
#include "devices/effects/AutoPan.h"
#include "devices/effects/AutoShift.h"
#include "devices/instruments/BassSynth.h"
#include "devices/effects/BeatRepeat.h"
#include "devices/effects/Ceiling.h"
#include "devices/effects/Chamber.h"
#include "devices/effects/Chorus.h"
#include "devices/effects/Compressor.h"
#include "devices/instruments/Consort.h"
#include "devices/effects/Crush.h"
#include "devices/effects/Delay.h"
#include "devices/effects/DynamicEq.h"
#include "devices/effects/Eq.h"
#include "devices/effects/Eq3.h"
#include "devices/effects/Flanger.h"
#include "devices/instruments/FluxSynth.h"
#include "devices/effects/Forge.h"
#include "devices/instruments/GrainSynth.h"
#include "devices/instruments/Keys.h"
#include "devices/effects/Lens.h"
#include "devices/midi_fx/MidiChord.h"
#include "devices/midi_fx/MidiNoteLength.h"
#include "devices/midi_fx/MidiRandom.h"
#include "devices/midi_fx/MidiScale.h"
#include "devices/midi_fx/MidiVelocity.h"
#include "devices/instruments/Monolith.h"
#include "devices/instruments/Mosaic.h"
#include "devices/instruments/OperatorSynth.h"
#include "devices/instruments/PendulumSynth.h"
#include "devices/instruments/Pentad.h"
#include "devices/effects/Phaser.h"
#include "devices/instruments/PhysicalSynth.h"
#include "devices/effects/Prism.h"
#include "devices/effects/Reverb.h"
#include "devices/instruments/RhythmMachine.h"
#include "devices/instruments/Sampler.h"
#include "devices/effects/Shutter.h"
#include "devices/effects/Strata.h"
#include "devices/instruments/Synth.h"
#include "devices/effects/Utility.h"
#include "devices/effects/Vintage.h"
#include "devices/effects/Vocoder.h"
#include "devices/instruments/VoltSynth.h"
#include "devices/instruments/WavetableSynth.h"

namespace nota {

// Fresh built-in instances, used to read a parameter's factory default value.
std::shared_ptr<Device> makeBuiltinDevice(int32_t kind) {
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
    case 22: return std::make_shared<Lens>();
    case 23: return std::make_shared<Flanger>();
    case 24: return std::make_shared<Phaser>();
    case 25: return std::make_shared<Chorus>();
    case 26: return std::make_shared<Vocoder>();
    default: return nullptr; // Rack / plugin: no simple per-param default
    }
}

std::shared_ptr<Instrument> makeBuiltinInstrument(int32_t kind) {
    switch (kind) {
    case 0: return std::make_shared<Synth>();
    case 1: return std::make_shared<Sampler>(); // empty; a sample is loaded later
    case 2: return std::make_shared<PhysicalSynth>();
    case 5: return std::make_shared<WavetableSynth>();
    case 6: return std::make_shared<VoltSynth>();
    case 7: return std::make_shared<BassSynth>();
    case 8: return std::make_shared<PendulumSynth>();
    case 9: return std::make_shared<OperatorSynth>();
    case 10: return std::make_shared<GrainSynth>(); // seeds a procedural default sample
    case 11: return std::make_shared<FluxSynth>();
    case 12: return std::make_shared<RhythmMachine>();
    case 13: return std::make_shared<Monolith>();
    case 14: return std::make_shared<Pentad>();
    case 15: return std::make_shared<Consort>();
    case 16: return std::make_shared<Keys>();
    case 17: return std::make_shared<Mosaic>(); // empty: a program is set later
    default: return nullptr;                    // Racks (3/4) / unknown: no simple swap
    }
}

std::shared_ptr<MidiDevice> makeMidiDevice(int32_t kind) {
    switch (kind) {
    case 0: return std::make_shared<Arpeggiator>();
    case 1: return std::make_shared<MidiChord>();
    case 2: return std::make_shared<MidiScale>();
    case 3: return std::make_shared<MidiNoteLength>();
    case 4: return std::make_shared<MidiVelocity>();
    case 5: return std::make_shared<MidiRandom>();
    default: return nullptr;
    }
}

} // namespace nota
