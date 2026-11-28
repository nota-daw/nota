# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

set(NOTA_C_ABI_SOURCES
    src/nota_engine_core.cpp
    src/nota_engine_track.cpp
    src/nota_engine_clip.cpp
    src/nota_engine_takes.cpp
    src/nota_engine_session.cpp
    src/nota_engine_device.cpp
    src/nota_engine_automation.cpp
    src/nota_engine_modulation.cpp
    src/nota_engine_presets.cpp
    src/nota_engine_audio.cpp
    src/nota_engine_rack.cpp
    src/nota_engine_rack_device.cpp
    src/nota_engine_rack_midi.cpp
    src/nota_engine_import.cpp
    src/nota_engine_audition.cpp
    src/nota_encoder.cpp
    src/abi_pluginscanner.cpp
)

add_library(nota_engine SHARED ${NOTA_C_ABI_SOURCES})
add_library(nota::c_abi ALIAS nota_engine)

set_target_properties(nota_engine PROPERTIES OUTPUT_NAME "nota_engine")
nota_apply_common_target_settings(nota_engine)
nota_enable_strict_cxx(nota_engine)

target_compile_definitions(nota_engine PRIVATE NOTA_ENGINE_EXPORTS)

target_include_directories(nota_engine
    PUBLIC  "${CMAKE_SOURCE_DIR}/include"
    PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src"
)

target_link_libraries(nota_engine
    PRIVATE
        nota::core
        nota::engine
        nota::devices
        nota::dsp
        nota::media
        nota::io
        nota::pluginhost
        nota::pluginscanner
)

nota_apply_symbol_export_filter(nota_engine
    MACOS_EXPORTS        "${CMAKE_SOURCE_DIR}/nota_exports.txt"
    LINUX_VERSION_SCRIPT "${CMAKE_SOURCE_DIR}/nota_exports.map"
)

nota_register_public_headers(
    TARGET nota_engine
    INCLUDE_ROOT "${CMAKE_SOURCE_DIR}/include"
    HEADERS "${CMAKE_SOURCE_DIR}/include/nota/nota_engine.h"
)
