// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Registration — populates nota::dsp::DeviceRegistry with all built-in devices and exposes cross-module helpers.

#include "nota/devices/Register.h"

#include "effects/amp/Amp.h"
#include "effects/auto_filter/AutoFilter.h"
#include "effects/auto_gain/AutoGain.h"
#include "effects/auto_pan/AutoPan.h"
#include "effects/auto_shift/AutoShift.h"
#include "effects/beat_repeat/BeatRepeat.h"
#include "effects/ceiling/Ceiling.h"
#include "effects/chamber/Chamber.h"
#include "effects/chorus/Chorus.h"
#include "effects/color/Color.h"
#include "effects/compressor/Compressor.h"
#include "effects/crush/Crush.h"
#include "effects/delay/Delay.h"
#include "effects/dynamic_eq/DynamicEq.h"
#include "effects/eq/Eq.h"
#include "effects/eq3/Eq3.h"
#include "effects/flanger/Flanger.h"
#include "effects/forge/Forge.h"
#include "effects/lens/Lens.h"
#include "effects/phaser/Phaser.h"
#include "effects/prism/Prism.h"
#include "effects/reverb/Reverb.h"
#include "effects/shutter/Shutter.h"
#include "effects/strata/Strata.h"
#include "effects/utility/Utility.h"
#include "effects/vintage/Vintage.h"
#include "effects/vocoder/Vocoder.h"
#include "instruments/bass/BassSynth.h"
#include "instruments/consort/Consort.h"
#include "instruments/flux/FluxSynth.h"
#include "instruments/grain/GrainSynth.h"
#include "instruments/keys/Keys.h"
#include "instruments/monolith/Monolith.h"
#include "instruments/mosaic/Mosaic.h"
#include "instruments/mosaic/MosaicProgram.h"
#include "instruments/operator/OperatorSynth.h"
#include "instruments/pendulum/PendulumSynth.h"
#include "instruments/pentad/Pentad.h"
#include "instruments/physical/PhysicalSynth.h"
#include "instruments/rhythm/RhythmMachine.h"
#include "instruments/sampler/Sampler.h"
#include "instruments/synth/Synth.h"
#include "instruments/twin/Twin.h"
#include "instruments/volt/VoltSynth.h"
#include "instruments/wavetable/WavetableSynth.h"
#include "midi_fx/arpeggiator/Arpeggiator.h"
#include "midi_fx/chord/MidiChord.h"
#include "midi_fx/note_length/MidiNoteLength.h"
#include "midi_fx/random/MidiRandom.h"
#include "midi_fx/scale/MidiScale.h"
#include "midi_fx/velocity/MidiVelocity.h"
#include "nota/devices/DeviceIds.h"
#include "nota/devices/racks/DrumRack.h"
#include "nota/devices/racks/MidiRack.h"
#include "nota/devices/racks/RackDevice.h"
#include "nota/devices/racks/RackInstrument.h"
#include "nota/dsp/DeviceRegistry.h"

#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <string_view>

namespace nota::devices {
namespace {

std::atomic<bool> gRegistered{false};

void doRegisterAll() {
    using dsp::DeviceRegistry;

    auto regFx = [](AudioEffectKind kind, DeviceRegistry::AudioEffectFactory factory, bool allowInRackChain = true) {
        DeviceRegistry::registerAudioEffect(static_cast<int32_t>(kind), std::move(factory), allowInRackChain);
    };
    auto regInst = [](InstrumentKind kind, DeviceRegistry::InstrumentFactory factory) {
        DeviceRegistry::registerInstrument(static_cast<int32_t>(kind), std::move(factory));
    };
    auto regMidi = [](MidiEffectKind kind, DeviceRegistry::MidiEffectFactory factory) {
        DeviceRegistry::registerMidiEffect(static_cast<int32_t>(kind), std::move(factory));
    };

    regFx(AudioEffectKind::Eq, [] { return std::make_shared<Eq>(); });
    regFx(AudioEffectKind::Compressor, [] { return std::make_shared<Compressor>(); });
    regFx(AudioEffectKind::Reverb, [] { return std::make_shared<Reverb>(); });
    regFx(AudioEffectKind::Delay, [] { return std::make_shared<Delay>(); });
    regFx(AudioEffectKind::Utility, [] { return std::make_shared<Utility>(); });
    regFx(AudioEffectKind::EffectRack, [] { return std::make_shared<RackDevice>(); }, false);
    regFx(AudioEffectKind::Amp, [] { return std::make_shared<Amp>(); });
    regFx(AudioEffectKind::AutoFilter, [] { return std::make_shared<AutoFilter>(); });
    regFx(AudioEffectKind::Vintage, [] { return std::make_shared<Vintage>(); });
    regFx(AudioEffectKind::AutoPan, [] { return std::make_shared<AutoPan>(); });
    regFx(AudioEffectKind::AutoShift, [] { return std::make_shared<AutoShift>(); });
    regFx(AudioEffectKind::BeatRepeat, [] { return std::make_shared<BeatRepeat>(); });
    regFx(AudioEffectKind::Crush, [] { return std::make_shared<Crush>(); });
    regFx(AudioEffectKind::DynamicEq, [] { return std::make_shared<DynamicEq>(); });
    regFx(AudioEffectKind::Ceiling, [] { return std::make_shared<Ceiling>(); });
    regFx(AudioEffectKind::Strata, [] { return std::make_shared<Strata>(); });
    regFx(AudioEffectKind::Eq3, [] { return std::make_shared<Eq3>(); });
    regFx(AudioEffectKind::Forge, [] { return std::make_shared<Forge>(); });
    regFx(AudioEffectKind::AutoGain, [] { return std::make_shared<AutoGain>(); });
    regFx(AudioEffectKind::Shutter, [] { return std::make_shared<Shutter>(); });
    regFx(AudioEffectKind::Chamber, [] { return std::make_shared<Chamber>(); });
    regFx(AudioEffectKind::Prism, [] { return std::make_shared<Prism>(); });
    regFx(AudioEffectKind::Lens, [] { return std::make_shared<Lens>(); });
    regFx(AudioEffectKind::Flanger, [] { return std::make_shared<Flanger>(); });
    regFx(AudioEffectKind::Phaser, [] { return std::make_shared<Phaser>(); });
    regFx(AudioEffectKind::Chorus, [] { return std::make_shared<Chorus>(); });
    regFx(AudioEffectKind::Vocoder, [] { return std::make_shared<Vocoder>(); });
    regFx(AudioEffectKind::Color, [] { return std::make_shared<Color>(); });

    regInst(InstrumentKind::Synth, [] { return std::make_shared<Synth>(); });
    regInst(InstrumentKind::Sampler, [] { return std::make_shared<Sampler>(); });
    regInst(InstrumentKind::PhysicalSynth, [] { return std::make_shared<PhysicalSynth>(); });
    regInst(InstrumentKind::InstrumentRack, [] { return std::make_shared<RackInstrument>(); });
    regInst(InstrumentKind::DrumRack, [] { return std::make_shared<DrumRack>(); });
    regInst(InstrumentKind::WavetableSynth, [] { return std::make_shared<WavetableSynth>(); });
    regInst(InstrumentKind::VoltSynth, [] { return std::make_shared<VoltSynth>(); });
    regInst(InstrumentKind::BassSynth, [] { return std::make_shared<BassSynth>(); });
    regInst(InstrumentKind::PendulumSynth, [] { return std::make_shared<PendulumSynth>(); });
    regInst(InstrumentKind::OperatorSynth, [] { return std::make_shared<OperatorSynth>(); });
    regInst(InstrumentKind::GrainSynth, [] { return std::make_shared<GrainSynth>(); });
    regInst(InstrumentKind::FluxSynth, [] { return std::make_shared<FluxSynth>(); });
    regInst(InstrumentKind::RhythmMachine, [] { return std::make_shared<RhythmMachine>(); });
    regInst(InstrumentKind::Monolith, [] { return std::make_shared<Monolith>(); });
    regInst(InstrumentKind::Pentad, [] { return std::make_shared<Pentad>(); });
    regInst(InstrumentKind::Consort, [] { return std::make_shared<Consort>(); });
    regInst(InstrumentKind::Keys, [] { return std::make_shared<Keys>(); });
    regInst(InstrumentKind::Mosaic, [] { return std::make_shared<Mosaic>(); });
    regInst(InstrumentKind::Twin, [] { return std::make_shared<Twin>(); });

    regMidi(MidiEffectKind::Arpeggiator, [] { return std::make_shared<Arpeggiator>(); });
    regMidi(MidiEffectKind::MidiChord, [] { return std::make_shared<MidiChord>(); });
    regMidi(MidiEffectKind::MidiScale, [] { return std::make_shared<MidiScale>(); });
    regMidi(MidiEffectKind::MidiNoteLength, [] { return std::make_shared<MidiNoteLength>(); });
    regMidi(MidiEffectKind::MidiVelocity, [] { return std::make_shared<MidiVelocity>(); });
    regMidi(MidiEffectKind::MidiRandom, [] { return std::make_shared<MidiRandom>(); });
    regMidi(MidiEffectKind::MidiRack, [] { return std::make_shared<MidiRack>(); });

    gRegistered.store(true, std::memory_order_release);
}

} // namespace

void registerBuiltinDevices() {
    static std::once_flag once;
    std::call_once(once, doRegisterAll);
    if (!dsp::DeviceRegistry::hasAudioEffect(static_cast<int32_t>(AudioEffectKind::Eq))) {
        // Re-populate if DeviceRegistry::clearForTests() reset the global table in a test run.
        doRegisterAll();
    }
}

bool builtinDevicesRegistered() noexcept {
    return gRegistered.load(std::memory_order_acquire) &&
           dsp::DeviceRegistry::hasAudioEffect(static_cast<int32_t>(AudioEffectKind::Eq));
}

int32_t twinParamFromV1(int32_t paramIndex) noexcept {
    return twin::paramFromV1(paramIndex);
}

void setMosaicRoot(std::string_view name, std::string_view path) {
    mosaic::setRoot(std::string(name), std::string(path));
}

} // namespace nota::devices
