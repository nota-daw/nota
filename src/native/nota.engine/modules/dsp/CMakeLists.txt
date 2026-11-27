# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

set(NOTA_DSP_PUBLIC_HEADERS
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/Export.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/MidiTypes.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/EditorPreview.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/Device.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/Instrument.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/MidiDevice.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/PluginContracts.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/DeviceRegistry.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/capabilities/ISlotAnnotationDevice.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/capabilities/ISampleInstrument.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/capabilities/IMosaicInstrument.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/capabilities/IWavetableInstrument.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/capabilities/IRhythmInstrument.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/capabilities/IRack.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/RtAttributes.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/AudioSpan.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/PlanarStereoBuffer.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/ScratchArena.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/RenderExecutor.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/SpscRingBuffer.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/SnapshotHistory.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/SilenceDetector.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/SilenceGate.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/DeviceTelemetry.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/Parameter.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/TransportInfo.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/AutomationTypes.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/MidiControllerTypes.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/ExpressionTypes.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/rt/OutputRouting.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/BiquadCascade.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/CompensationDelay.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/Convolution.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/DelayLine.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/Envelope.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/Hadamard.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/LadderFilter.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/LevelHistory.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/Oscillator.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/Oversampler.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/SpectrumAnalyzer.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/SteinerFilter.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/SvfFilter.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/primitives/TruePeakMeter.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/poly/SynthVoice.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/poly/PolyVoiceManager.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/dsp/poly/PolyVoiceScheduler.h
)

set(NOTA_DSP_SOURCES
    src/rt/SilenceDetector.cpp
    src/rt/SilenceGate.cpp
    src/rt/Parameter.cpp
    src/rt/RenderExecutor.cpp
    src/rt/AutomationTypes.cpp
    src/rt/MidiControllerTypes.cpp
    src/rt/ExpressionTypes.cpp
    src/EditorPreview.cpp
    src/Device.cpp
    src/Instrument.cpp
    src/MidiDevice.cpp
    src/Capabilities.cpp
    src/DeviceRegistry.cpp
    src/capabilities/WavetableAsset.cpp
    src/primitives/BiquadCascade.cpp
    src/primitives/CompensationDelay.cpp
    src/primitives/Convolution.cpp
    src/primitives/DelayLine.cpp
    src/primitives/Envelope.cpp
    src/primitives/LadderFilter.cpp
    src/primitives/Oscillator.cpp
    src/primitives/Oversampler.cpp
    src/primitives/SpectrumAnalyzer.cpp
    src/primitives/SvfFilter.cpp
    src/primitives/TruePeakMeter.cpp
    src/poly/SynthVoice.cpp
    src/poly/PolyVoiceManager.cpp
)

nota_add_shared_module(nota_dsp
    ALIAS dsp
    OUTPUT_NAME nota_dsp
    EXPORT_MACRO NOTA_DSP_API
    PUBLIC_HEADERS ${NOTA_DSP_PUBLIC_HEADERS}
    SOURCES ${NOTA_DSP_SOURCES}
)

target_include_directories(nota_dsp
    SYSTEM PRIVATE
        ${hiir_SOURCE_DIR}
)

target_link_libraries(nota_dsp
    PUBLIC
        nota::math
        nota::media
)
