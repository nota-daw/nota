# SPDX-License-Identifier: AGPL-3.0-only
# Nota plugin scanner module: format-agnostic catalog, out-of-process coordinator,
# wire codec, and private JUCE VST3/AU discovery/probing backend (no GUI).

set(NOTA_PLUGINSCANNER_PUBLIC_HEADERS
    include/nota/pluginscanner/Export.h
    include/nota/pluginscanner/IPluginFormatBackend.h
    include/nota/pluginscanner/PluginFormatRegistry.h
    include/nota/pluginscanner/OutOfProcessScanner.h
    include/nota/pluginscanner/PluginCatalogImpl.h
    include/nota/pluginscanner/PluginScannerImpl.h
    include/nota/pluginscanner/PluginScannerModule.h
    include/nota/pluginscanner/wire/ScanWireCodec.h
)

set(NOTA_SCANNER_JUCE_DEFS
    JUCE_STANDALONE_APPLICATION=0
    JUCE_USE_CURL=0
    JUCE_WEB_BROWSER=0
    JUCE_MODAL_LOOPS_PERMITTED=0
    JUCE_PLUGINHOST_VST3=1
)
if(APPLE)
    list(APPEND NOTA_SCANNER_JUCE_DEFS JUCE_PLUGINHOST_AU=1)
endif()

# Private JUCE backend static lib: compiles JUCE module sources once without applying
# Nota's -Werror flags to third-party JUCE sources, while our own backend wrapper TU
# retains strict compiler diagnostics.
add_library(nota_pluginscanner_juce STATIC
    src/backends/juce/JucePluginFormatBackend.cpp
)
nota_apply_common_target_settings(nota_pluginscanner_juce)
set_target_properties(nota_pluginscanner_juce PROPERTIES POSITION_INDEPENDENT_CODE ON)
target_compile_definitions(nota_pluginscanner_juce PRIVATE ${NOTA_SCANNER_JUCE_DEFS})
target_include_directories(nota_pluginscanner_juce
    PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/include"
        "${CMAKE_CURRENT_SOURCE_DIR}/src"
)
target_link_libraries(nota_pluginscanner_juce
    PRIVATE
        nota::dsp
        juce::juce_audio_processors
        juce::juce_recommended_config_flags
)
if(APPLE)
    target_link_libraries(nota_pluginscanner_juce PRIVATE "-framework CoreAudioKit")
endif()
if(NOTA_ENABLE_SANITIZERS)
    if(MSVC)
        target_compile_options(nota_pluginscanner_juce PRIVATE /fsanitize=address)
    elseif(CMAKE_CXX_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(nota_pluginscanner_juce PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer)
    endif()
endif()
if(NOT MSVC)
    set_source_files_properties(src/backends/juce/JucePluginFormatBackend.cpp PROPERTIES
        COMPILE_OPTIONS "-Wall;-Wextra;-Wshadow;-Wnon-virtual-dtor;-Woverloaded-virtual;-Wnull-dereference;-Wformat=2;$<$<BOOL:${NOTA_STRICT_WARNINGS_AS_ERRORS}>:-Werror>")
endif()

set(NOTA_PLUGINSCANNER_SOURCES
    src/xml/SimpleXml.cpp
    src/PluginFormatRegistry.cpp
    src/wire/ScanWireCodec.cpp
    src/OutOfProcessScanner.cpp
    src/PluginCatalogImpl.cpp
    src/PluginScannerImpl.cpp
    src/PluginScannerModule.cpp
)

nota_add_shared_module(nota_pluginscanner
    ALIAS         pluginscanner
    OUTPUT_NAME   nota_pluginscanner
    EXPORT_MACRO  NOTA_PLUGINSCANNER
    PUBLIC_HEADERS ${NOTA_PLUGINSCANNER_PUBLIC_HEADERS}
    SOURCES        ${NOTA_PLUGINSCANNER_SOURCES}
)

target_link_libraries(nota_pluginscanner
    PUBLIC
        nota::dsp
    PRIVATE
        nota::core
        nota_pluginscanner_juce
)
if(APPLE)
    target_link_libraries(nota_pluginscanner PRIVATE "-framework CoreAudioKit")
endif()

# --- Out-of-process scan worker ---------------------------------------------
add_executable(nota-scanworker
    src/worker/main.cpp
)
nota_apply_common_target_settings(nota-scanworker)
nota_enable_strict_cxx(nota-scanworker)
target_link_libraries(nota-scanworker PRIVATE nota::pluginscanner nota::dsp)

# --- Out-of-process scanner resilience self-test ----------------------------
add_executable(nota-scanner-selftest
    tests/ScannerSelfTest.cpp
)
nota_apply_common_target_settings(nota-scanner-selftest)
nota_enable_strict_cxx(nota-scanner-selftest)
target_link_libraries(nota-scanner-selftest PRIVATE nota::pluginscanner nota::dsp)
