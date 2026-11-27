# SPDX-License-Identifier: AGPL-3.0-only
# Nota in-process plugin hosting module: JUCE VST3/AU hosting, background binary loader,
# editor windows & screen snapshots, and host workgroup integration. Never depends on pluginscanner.

set(NOTA_PLUGINHOST_PUBLIC_HEADERS
    include/nota/pluginhost/Export.h
    include/nota/pluginhost/PluginHostImpl.h
    include/nota/pluginhost/PluginHostModule.h
)

set(NOTA_HOST_JUCE_DEFS
    JUCE_STANDALONE_APPLICATION=0
    JUCE_USE_CURL=0
    JUCE_WEB_BROWSER=0
    JUCE_MODAL_LOOPS_PERMITTED=0
    JUCE_PLUGINHOST_VST3=1
)
if(APPLE)
    list(APPEND NOTA_HOST_JUCE_DEFS JUCE_PLUGINHOST_AU=1)
endif()

# Private JUCE compilation unit holder so third-party JUCE .cpp/.mm sources build without
# Nota's strict -Werror flags, while our own hosting C++ sources remain strictly checked.
add_library(nota_pluginhost_juce STATIC
    src/hosting/JuceFormatHost.cpp
)
nota_apply_common_target_settings(nota_pluginhost_juce)
set_target_properties(nota_pluginhost_juce PROPERTIES POSITION_INDEPENDENT_CODE ON)
target_compile_definitions(nota_pluginhost_juce PRIVATE ${NOTA_HOST_JUCE_DEFS})
target_include_directories(nota_pluginhost_juce
    PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/include"
        "${CMAKE_CURRENT_SOURCE_DIR}/src"
)
target_link_libraries(nota_pluginhost_juce
    PRIVATE
        nota::dsp
        juce::juce_audio_processors
        juce::juce_gui_basics
        juce::juce_recommended_config_flags
)
if(APPLE)
    target_link_libraries(nota_pluginhost_juce PRIVATE "-framework CoreAudioKit")
endif()
if(NOTA_ENABLE_SANITIZERS)
    if(MSVC)
        target_compile_options(nota_pluginhost_juce PRIVATE /fsanitize=address)
    elseif(CMAKE_CXX_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(nota_pluginhost_juce PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer)
    endif()
endif()
if(NOT MSVC)
    set_source_files_properties(src/hosting/JuceFormatHost.cpp PROPERTIES
        COMPILE_OPTIONS "-Wall;-Wextra;-Wshadow;-Wnon-virtual-dtor;-Woverloaded-virtual;-Wnull-dereference;-Wformat=2;$<$<BOOL:${NOTA_STRICT_WARNINGS_AS_ERRORS}>:-Werror>")
endif()

set(NOTA_PLUGINHOST_SOURCES
    src/PluginHostImpl.cpp
    src/PluginHostModule.cpp
    src/hosting/NotaPlayHead.cpp
    src/hosting/PluginCommon.cpp
    src/hosting/EditorSnapshot.cpp
    src/hosting/PluginEditorWindow.cpp
    src/hosting/PluginLoader.cpp
    src/hosting/PluginInstrument.cpp
    src/hosting/PluginEffect.cpp
)
if(APPLE)
    list(APPEND NOTA_PLUGINHOST_SOURCES
        src/hosting/PluginHost_mac.mm
        src/hosting/PluginEditorWindow_mac.mm
        src/hosting/EditorSnapshot_mac.mm
    )
    set_source_files_properties(
        src/hosting/PluginHost_mac.mm
        src/hosting/PluginEditorWindow_mac.mm
        src/hosting/EditorSnapshot_mac.mm
        PROPERTIES
            COMPILE_OPTIONS "-Wno-old-style-cast;-Wno-unused-parameter"
    )
endif()

nota_add_shared_module(nota_pluginhost
    ALIAS         pluginhost
    OUTPUT_NAME   nota_pluginhost
    EXPORT_MACRO  NOTA_PLUGINHOST
    PUBLIC_HEADERS ${NOTA_PLUGINHOST_PUBLIC_HEADERS}
    SOURCES        ${NOTA_PLUGINHOST_SOURCES}
)

target_compile_definitions(nota_pluginhost PRIVATE
    ${NOTA_HOST_JUCE_DEFS}
    $<TARGET_PROPERTY:juce::juce_audio_processors,INTERFACE_COMPILE_DEFINITIONS>
    $<TARGET_PROPERTY:juce::juce_gui_basics,INTERFACE_COMPILE_DEFINITIONS>
)

target_include_directories(nota_pluginhost SYSTEM PRIVATE
    $<TARGET_PROPERTY:juce::juce_audio_processors,INTERFACE_INCLUDE_DIRECTORIES>
    $<TARGET_PROPERTY:juce::juce_gui_basics,INTERFACE_INCLUDE_DIRECTORIES>
)

target_link_libraries(nota_pluginhost
    PUBLIC
        nota::dsp
        nota::math
    PRIVATE
        nota::core
        nota_pluginhost_juce
)
if(APPLE)
    target_link_libraries(nota_pluginhost PRIVATE
        "-framework CoreAudioKit"
        "-framework AppKit"
        "-framework AudioToolbox"
        "-framework CoreFoundation"
        "-framework CoreGraphics"
    )
endif()

# --- Deterministic VST3/AU render test fixture ------------------------------
option(NOTA_BUILD_RENDER_PROBE "Build the hosted render-pool test plugin" ON)
if(NOTA_BUILD_RENDER_PROBE)
    set(NOTA_RENDER_PROBE_FORMATS VST3)
    if(APPLE)
        list(APPEND NOTA_RENDER_PROBE_FORMATS AU)
    endif()
    juce_add_plugin(nota-render-probe
        COMPANY_NAME Nota
        PLUGIN_MANUFACTURER_CODE Nota
        PLUGIN_CODE Nrpr
        PRODUCT_NAME "Nota Render Probe"
        FORMATS ${NOTA_RENDER_PROBE_FORMATS}
        IS_SYNTH TRUE
        NEEDS_MIDI_INPUT TRUE
        COPY_PLUGIN_AFTER_BUILD FALSE
        VST3_AUTO_MANIFEST FALSE)
    target_sources(nota-render-probe PRIVATE tests/RenderProbe.cpp)
    target_link_libraries(nota-render-probe PRIVATE juce::juce_audio_processors
        juce::juce_recommended_config_flags)
    target_compile_definitions(nota-render-probe PRIVATE JUCE_USE_CURL=0
        JUCE_WEB_BROWSER=0 JUCE_VST3_CAN_REPLACE_VST2=0)
    if(TARGET nota-render-probe_VST3)
        add_custom_command(TARGET nota-render-probe_VST3 POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E make_directory "${CMAKE_BINARY_DIR}/nota-render-probe_artefacts/$<CONFIG>"
            COMMAND ${CMAKE_COMMAND} -E remove_directory "${CMAKE_BINARY_DIR}/nota-render-probe_artefacts/$<CONFIG>/VST3"
            COMMAND ${CMAKE_COMMAND} -E copy_directory
                "${CMAKE_CURRENT_BINARY_DIR}/nota-render-probe_artefacts/$<CONFIG>/VST3"
                "${CMAKE_BINARY_DIR}/nota-render-probe_artefacts/$<CONFIG>/VST3"
        )
    endif()
    if(APPLE AND TARGET nota-render-probe_AU)
        add_custom_command(TARGET nota-render-probe_AU POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E make_directory "${CMAKE_BINARY_DIR}/nota-render-probe_artefacts/$<CONFIG>"
            COMMAND ${CMAKE_COMMAND} -E remove_directory "${CMAKE_BINARY_DIR}/nota-render-probe_artefacts/$<CONFIG>/AU"
            COMMAND ${CMAKE_COMMAND} -E copy_directory
                "${CMAKE_CURRENT_BINARY_DIR}/nota-render-probe_artefacts/$<CONFIG>/AU"
                "${CMAKE_BINARY_DIR}/nota-render-probe_artefacts/$<CONFIG>/AU"
        )
    endif()
endif()
