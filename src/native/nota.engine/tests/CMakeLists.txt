# SPDX-License-Identifier: AGPL-3.0-only

add_executable(test_core
    test_core.cpp
)
nota_apply_common_target_settings(test_core)
nota_enable_strict_cxx(test_core)
target_link_libraries(test_core PRIVATE nota::core)
add_test(NAME nota_core COMMAND test_core)

add_executable(test_math
    test_math.cpp
)
nota_apply_common_target_settings(test_math)
nota_enable_strict_cxx(test_math)
target_link_libraries(test_math PRIVATE nota::math)
add_test(NAME nota_math COMMAND test_math)

add_executable(test_media
    test_media.cpp
)
nota_apply_common_target_settings(test_media)
nota_enable_strict_cxx(test_media)
target_link_libraries(test_media PRIVATE nota::media nota::math)
add_test(NAME nota_media COMMAND test_media)

add_executable(test_dsp
    test_dsp.cpp
)
nota_apply_common_target_settings(test_dsp)
nota_enable_strict_cxx(test_dsp)
target_link_libraries(test_dsp PRIVATE nota::dsp nota::media nota::math)
add_test(NAME nota_dsp COMMAND test_dsp)

add_executable(test_devices
    test_devices.cpp
)
nota_apply_common_target_settings(test_devices)
nota_enable_strict_cxx(test_devices)
target_link_libraries(test_devices PRIVATE nota::devices nota::dsp nota::media nota::math)
add_test(NAME nota_devices COMMAND test_devices)

add_executable(test_pluginscanner
    test_pluginscanner.cpp
)
nota_apply_common_target_settings(test_pluginscanner)
nota_enable_strict_cxx(test_pluginscanner)
target_link_libraries(test_pluginscanner PRIVATE nota::pluginscanner nota::dsp nota::core)
add_test(NAME nota_pluginscanner COMMAND test_pluginscanner)

add_executable(test_pluginhost
    test_pluginhost.cpp
)
nota_apply_common_target_settings(test_pluginhost)
nota_enable_strict_cxx(test_pluginhost)
target_link_libraries(test_pluginhost PRIVATE nota::pluginhost nota::dsp nota::math)
add_test(NAME nota_pluginhost COMMAND test_pluginhost)

add_executable(test_io
    test_io.cpp
    fixtures/FakeAsioDriver.cpp
)
nota_apply_common_target_settings(test_io)
nota_enable_strict_cxx(test_io)
target_include_directories(test_io PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(test_io PRIVATE nota::io nota::dsp nota::core nota::math)
add_test(NAME nota_io COMMAND test_io)

set(_header_check_dir "${CMAKE_CURRENT_BINARY_DIR}/header_checks")
file(MAKE_DIRECTORY "${_header_check_dir}")

set(_header_targets "")
foreach(_spec IN LISTS NOTA_PUBLIC_HEADER_SPECS)
    string(REPLACE "|" ";" _parts "${_spec}")
    list(GET _parts 0 _tgt)
    list(GET _parts 1 _inc_root)
    list(GET _parts 2 _abs_hdr)

    if(NOT TARGET "${_tgt}")
        continue()
    endif()

    file(RELATIVE_PATH _rel_hdr "${_inc_root}" "${_abs_hdr}")
    string(MAKE_C_IDENTIFIER "${_tgt}_${_rel_hdr}" _id)
    set(_gen_cpp "${_header_check_dir}/${_id}.cpp")
    file(WRITE "${_gen_cpp}" "// Generated standalone public-header compile check\n#include \"${_rel_hdr}\"\n")

    list(APPEND "_hdr_srcs_${_tgt}" "${_gen_cpp}")
    list(APPEND _header_targets "${_tgt}")
endforeach()

list(REMOVE_DUPLICATES _header_targets)

set(_header_main "${_header_check_dir}/main.cpp")
file(WRITE "${_header_main}" "// Generated standalone header check entry point\nint main() { return 0; }\n")

set(_header_objs "")
foreach(_tgt IN LISTS _header_targets)
    set(_obj_tgt "nota_header_check_${_tgt}")
    add_library(${_obj_tgt} OBJECT ${_hdr_srcs_${_tgt}})
    nota_apply_common_target_settings(${_obj_tgt})
    nota_enable_strict_cxx(${_obj_tgt})
    target_link_libraries(${_obj_tgt} PRIVATE ${_tgt})
    list(APPEND _header_objs "$<TARGET_OBJECTS:${_obj_tgt}>")
endforeach()

add_executable(test_headers_compile "${_header_main}" ${_header_objs})
nota_apply_common_target_settings(test_headers_compile)
nota_enable_strict_cxx(test_headers_compile)
add_test(NAME headers_compile COMMAND test_headers_compile)

add_executable(test_engine_cpp
    test_engine_cpp.cpp
)
nota_apply_common_target_settings(test_engine_cpp)
nota_enable_strict_cxx(test_engine_cpp)
target_link_libraries(test_engine_cpp PRIVATE nota::engine nota::dsp nota::io nota::math)
add_test(NAME nota_engine_cpp COMMAND test_engine_cpp)

if(NOTA_BUILD_C_ABI)
    add_executable(test_c_abi
        test_c_abi.cpp
    )
    nota_apply_common_target_settings(test_c_abi)
    nota_enable_strict_cxx(test_c_abi)
    target_link_libraries(test_c_abi PRIVATE nota::c_abi)
    add_test(NAME nota_c_abi COMMAND test_c_abi)
endif()
