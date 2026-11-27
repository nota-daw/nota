# SPDX-License-Identifier: AGPL-3.0-only
# nota_core: Process-wide non-DSP runtime support (config file I/O, data directory resolution, editor key queue).

nota_add_shared_module(nota_core
    ALIAS core
    OUTPUT_NAME nota_core
    EXPORT_MACRO NOTA_CORE
    PUBLIC_HEADERS
        ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/core/Export.h
        ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/core/ConfigFile.h
        ${CMAKE_CURRENT_SOURCE_DIR}/include/nota/core/EditorKeys.h
    SOURCES
        src/ConfigFile.cpp
        src/EditorKeys.cpp
)
