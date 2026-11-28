# SPDX-License-Identifier: AGPL-3.0-only
# nota_math: Universal SIMD vector, buffer, stereo, fast-math, PRNG, windowing, and Pimpl FFT library.

nota_add_shared_module(nota_math
    ALIAS math
    OUTPUT_NAME nota_math
    EXPORT_MACRO NOTA_MATH
    PUBLIC_HEADERS
        include/nota/math/Export.h
        include/nota/math/CpuFeatures.h
        include/nota/math/SimdVec.h
        include/nota/math/BufferOps.h
        include/nota/math/StereoOps.h
        include/nota/math/ComplexOps.h
        include/nota/math/FastMath.h
        include/nota/math/Random.h
        include/nota/math/Fft.h
    SOURCES
        src/CpuFeatures.cpp
        src/RealFft.cpp
        src/Windowing.cpp
)

target_include_directories(nota_math SYSTEM PRIVATE
    ${signalsmith_linear_SOURCE_DIR}/include
)

if(APPLE)
    target_compile_definitions(nota_math PRIVATE SIGNALSMITH_USE_ACCELERATE)
    target_link_libraries(nota_math PRIVATE "-framework Accelerate")
else()
    if(NOT TARGET nota_pffft)
        add_library(nota_pffft STATIC
            ${pffft_SOURCE_DIR}/src/pffft.c
            ${pffft_SOURCE_DIR}/src/pffft_common.c
        )
        set_target_properties(nota_pffft PROPERTIES POSITION_INDEPENDENT_CODE ON)
        target_include_directories(nota_pffft PRIVATE ${pffft_SOURCE_DIR}/include/pffft)
        target_compile_definitions(nota_pffft PUBLIC PFFFT_STATIC_DEFINE)
    endif()
    target_include_directories(nota_math SYSTEM PRIVATE ${pffft_SOURCE_DIR}/include)
    target_compile_definitions(nota_math PRIVATE SIGNALSMITH_USE_PFFFT)
    target_link_libraries(nota_math PRIVATE nota_pffft)
endif()
