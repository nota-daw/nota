# SPDX-License-Identifier: AGPL-3.0-only

set(NOTA_ENGINE_PUBLIC_HEADERS
    include/nota/engine/Export.h
    include/nota/engine/Types.h
    include/nota/engine/ModelTypes.h
    include/nota/engine/Engine.h
    include/nota/engine/Transport.h
    include/nota/engine/Mixer.h
    include/nota/engine/TrackCollection.h
    include/nota/engine/Track.h
    include/nota/engine/Clip.h
    include/nota/engine/DeviceChain.h
    include/nota/engine/Device.h
    include/nota/engine/Rack.h
    include/nota/engine/RackChain.h
    include/nota/engine/Session.h
    include/nota/engine/Slot.h
    include/nota/engine/Recorder.h
    include/nota/engine/Automation.h
    include/nota/engine/Modulation.h
    include/nota/engine/Preview.h
    include/nota/engine/AudioIo.h
    include/nota/engine/MidiIo.h
    include/nota/engine/History.h
    include/nota/engine/Diagnostics.h
    include/nota/engine/NotaEngine.h
)

set(NOTA_ENGINE_SOURCES
    src/core/AudioThreadPool.cpp
    src/core/EngineHistory.cpp
    src/core/EngineLifecycle.cpp
    src/core/EngineTransport.cpp
    src/core/Transport.cpp

    src/model/ArticulationSchedule.cpp
    src/model/ArticulationTable.cpp
    src/model/Graph.cpp
    src/model/MidiClip.cpp
    src/model/TimelineIndex.cpp
    src/model/Track.cpp

    src/render/HybridBufferController.cpp
    src/render/RenderAudioClips.cpp
    src/render/RenderBlockLoop.cpp
    src/render/RenderGraphExecutor.cpp
    src/render/RenderGraphWaves.cpp
    src/render/RenderLiveMidi.cpp
    src/render/RenderMidiAndSynth.cpp
    src/render/RenderRoutingPlan.cpp
    src/render/RenderSelfTests.cpp
    src/render/RenderSessionAndMonitor.cpp

    src/tracks/RhythmKitOps.cpp
    src/tracks/TrackFreeze.cpp
    src/tracks/TrackLifecycle.cpp

    src/clips/ClipArrangementOps.cpp
    src/clips/ClipClipboardOps.cpp
    src/clips/ClipPersistenceOps.cpp
    src/clips/ClipRangeOps.cpp
    src/clips/ClipSectionOps.cpp
    src/clips/ClipWarpOps.cpp

    src/devices/EngineDeviceParams.cpp
    src/devices/EngineDeviceSidechain.cpp
    src/devices/EngineDeviceTransfer.cpp
    src/devices/EngineDevices.cpp
    src/devices/EnginePluginLoads.cpp
    src/devices/EnginePresets.cpp
    src/devices/EngineRack.cpp
    src/devices/EngineRackChains.cpp
    src/devices/EngineRackControls.cpp
    src/devices/EngineRackGrouping.cpp

    src/session/EngineSession.cpp
    src/session/EngineSessionPass.cpp
    src/session/EngineSessionScenes.cpp
    src/session/EngineSessionSlots.cpp
    src/session/SessionPass.cpp
    src/session/SessionPlayer.cpp

    src/recording/EngineRecording.cpp
    src/recording/EngineRecordingAudio.cpp
    src/recording/EngineRecordingInputs.cpp
    src/recording/EngineRecordingMidi.cpp
    src/recording/EngineTakeRetry.cpp
    src/recording/EngineTakes.cpp
    src/recording/RecordingMidiFold.cpp

    src/automation/EngineAutomation.cpp
    src/automation/EngineAutomationClipMaster.cpp
    src/automation/EngineAutomationWrite.cpp
    src/automation/EngineModulation.cpp
    src/automation/EngineModulationApply.cpp
    src/automation/Modulation.cpp

    src/io/AudioSessionManager.cpp
    src/io/Audition.cpp
    src/io/MidiSurface.cpp

    src/api/EngineCoreApi.cpp
    src/api/TrackAndClipApi.cpp
    src/api/ClipAndAutomationApi.cpp
    src/api/DeviceChainApi.cpp
    src/api/DeviceAndRackApi.cpp
    src/api/RackApi.cpp
    src/api/SessionAndIoApi.cpp
)

nota_add_shared_module(nota_engine_core
    ALIAS          engine
    OUTPUT_NAME    nota_engine_core
    EXPORT_MACRO   NOTA_ENGINE_CORE
    EXPORT_NAMESPACES nota::Engine nota::engine
    PUBLIC_HEADERS ${NOTA_ENGINE_PUBLIC_HEADERS}
    SOURCES        ${NOTA_ENGINE_SOURCES}
)

target_link_libraries(nota_engine_core
    PUBLIC
        nota::dsp
        nota::media
        nota::devices
        nota::io
    PRIVATE
        nota::math
        nota::pluginhost
        nota::pluginscanner
)

if(WIN32)
    target_compile_definitions(nota_engine_core PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN _USE_MATH_DEFINES)
    target_link_libraries(nota_engine_core PRIVATE advapi32 user32 avrt)
elseif(UNIX AND NOT APPLE)
    target_link_libraries(nota_engine_core PRIVATE pthread dl m)
endif()
