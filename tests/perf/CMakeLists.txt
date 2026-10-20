# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# Engine performance gate tools (specs/034-engine-perf-gate). Built against an
# already-built engine, so the same sources can be compiled against the base and the
# PR engine:
#   cmake -S tests/perf -B <out> -DNOTA_ENGINE_DIR=<repo>/src/native/nota.engine \
#         -DNOTA_ENGINE_BUILD_DIR=<engine build dir>

cmake_minimum_required(VERSION 3.22)
project(nota_perf LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Release)
endif()

set(NOTA_ENGINE_DIR "" CACHE PATH "Engine source dir (holds include/nota/nota_engine.h)")
set(NOTA_ENGINE_BUILD_DIR "" CACHE PATH "Engine build dir (holds the nota_engine library)")
if(NOT NOTA_ENGINE_DIR OR NOT NOTA_ENGINE_BUILD_DIR)
  message(FATAL_ERROR "Set NOTA_ENGINE_DIR and NOTA_ENGINE_BUILD_DIR")
endif()

find_library(NOTA_ENGINE_LIB nota_engine PATHS "${NOTA_ENGINE_BUILD_DIR}" NO_DEFAULT_PATH REQUIRED)

foreach(tool nota_perf nota_idle_cpu)
  add_executable(${tool} ${tool}.cpp)
  target_include_directories(${tool} PRIVATE "${NOTA_ENGINE_DIR}/include")
  target_link_libraries(${tool} PRIVATE "${NOTA_ENGINE_LIB}")
  set_target_properties(${tool} PROPERTIES BUILD_RPATH "${NOTA_ENGINE_BUILD_DIR}")
endforeach()
