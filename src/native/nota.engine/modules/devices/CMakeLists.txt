# SPDX-License-Identifier: AGPL-3.0-only
# Nota built-in devices module: effects, instruments, MIDI FX, racks & registration.

set(NOTA_DEVICES_PUBLIC_HEADERS
    include/nota/devices/Export.h
    include/nota/devices/DeviceIds.h
    include/nota/devices/Register.h
    include/nota/devices/racks/RackCore.h
    include/nota/devices/racks/RackDevice.h
    include/nota/devices/racks/RackInstrument.h
    include/nota/devices/racks/DrumRack.h
    include/nota/devices/racks/MidiRack.h
)

set(NOTA_DEVICES_SOURCES
    src/DeviceIds.cpp
    src/Registration.cpp
    src/SelfTests.cpp

    src/effects/amp/Amp.cpp
    src/effects/auto_filter/AutoFilter.cpp
    src/effects/auto_gain/AutoGain.cpp
    src/effects/auto_pan/AutoPan.cpp
    src/effects/auto_shift/AutoShift.cpp
    src/effects/beat_repeat/BeatRepeat.cpp
    src/effects/ceiling/Ceiling.cpp
    src/effects/chamber/ChamberAlgo.cpp
    src/effects/chamber/Chamber.cpp
    src/effects/chamber/ChamberIrSynth.cpp
    src/effects/chorus/Chorus.cpp
    src/effects/color/ColorDsp.cpp
    src/effects/color/Color.cpp
    src/effects/compressor/Compressor.cpp
    src/effects/crush/Crush.cpp
    src/effects/delay/Delay.cpp
    src/effects/dynamic_eq/DynamicEq.cpp
    src/effects/eq/Eq.cpp
    src/effects/eq/EqDsp.cpp
    src/effects/eq/EqParams.cpp
    src/effects/eq3/Eq3.cpp
    src/effects/flanger/Flanger.cpp
    src/effects/forge/Forge.cpp
    src/effects/lens/Lens.cpp
    src/effects/phaser/Phaser.cpp
    src/effects/prism/Prism.cpp
    src/effects/reverb/Reverb.cpp
    src/effects/shutter/Shutter.cpp
    src/effects/strata/Strata.cpp
    src/effects/utility/Utility.cpp
    src/effects/vintage/Vintage.cpp
    src/effects/vocoder/Vocoder.cpp

    src/instruments/bass/BassSynth.cpp
    src/instruments/consort/Consort.cpp
    src/instruments/consort/ConsortDsp.cpp
    src/instruments/flux/FluxSynth.cpp
    src/instruments/grain/GrainSynth.cpp
    src/instruments/keys/Keys.cpp
    src/instruments/monolith/Monolith.cpp
    src/instruments/mosaic/Mosaic.cpp
    src/instruments/mosaic/MosaicProgram.cpp
    src/instruments/operator/OperatorSynth.cpp
    src/instruments/pendulum/PendulumSynth.cpp
    src/instruments/pentad/Pentad.cpp
    src/instruments/physical/PhysicalSynth.cpp
    src/instruments/rhythm/RhythmMachine.cpp
    src/instruments/rhythm/RhythmVoiceDsp.cpp
    src/instruments/sampler/Sampler.cpp
    src/instruments/synth/Synth.cpp
    src/instruments/twin/TwinParams.cpp
    src/instruments/twin/Twin.cpp
    src/instruments/twin/TwinMod.cpp
    src/instruments/twin/TwinSeq.cpp
    src/instruments/twin/TwinVoice.cpp
    src/instruments/twin/TwinVoices.cpp
    src/instruments/volt/VoltSynth.cpp
    src/instruments/wavetable/WavetableParams.cpp
    src/instruments/wavetable/WavetableSynth.cpp

    src/midi_fx/arpeggiator/Arpeggiator.cpp
    src/midi_fx/chord/MidiChord.cpp
    src/midi_fx/note_length/MidiNoteLength.cpp
    src/midi_fx/random/MidiRandom.cpp
    src/midi_fx/scale/MidiScale.cpp
    src/midi_fx/velocity/MidiVelocity.cpp

    src/racks/RackCore.cpp
    src/racks/RackState.cpp
    src/racks/RackDevice.cpp
    src/racks/RackInstrument.cpp
    src/racks/DrumRack.cpp
    src/racks/MidiRack.cpp
)

nota_add_shared_module(nota_devices
    ALIAS         devices
    OUTPUT_NAME   nota_devices
    EXPORT_MACRO  NOTA_DEVICES_API
    PUBLIC_HEADERS ${NOTA_DEVICES_PUBLIC_HEADERS}
    SOURCES        ${NOTA_DEVICES_SOURCES}
)

target_link_libraries(nota_devices
    PUBLIC
        nota::dsp
        nota::media
        nota::math
)

target_include_directories(nota_devices
    SYSTEM PRIVATE
        ${hiir_SOURCE_DIR}
)

if(UNIX AND NOT APPLE AND CMAKE_CXX_COMPILER_ID STREQUAL "GNU")
    # Preserve GCC's baseline register allocation in Synth::renderBlock without LTO.
    set_source_files_properties(src/instruments/synth/Synth.cpp
        PROPERTIES COMPILE_OPTIONS "-fsemantic-interposition"
    )
endif()
