# Quickstart & Validation Guide: Professional C++20 DSP Architecture Overhaul

**Feature**: `030-dsp-architecture-overhaul` | **Date**: 2026-10-19

## 1. Prerequisites
- macOS (Xcode 26+ / AppleClang, universal `arm64;x86_64`), Linux (GCC 13+ / Clang 17+), or Windows (MSVC 2022).
- CMake 3.24+ and Ninja.
- .NET 10 SDK.

## 2. Build & Verification Commands

### Step 1: Full Native + Managed + Smoke Test Build
Run the platform build script from the `nota/` repository root:
```bash
./scripts/build.sh
```
This configures CMake, builds `nota_pluginscanner`, `nota_pluginhost`, `nota_engine`, and `nota-scanworker`, builds the .NET 10 solution, and runs `tests/Nota.SmokeTest`.

### Step 2: Verify Symbol Exports (Boundary Discipline AR-7)
Verify that only `nota_*` symbols are exported from `libnota_engine`:
```bash
nm -gU src/native/nota.engine/build/libnota_engine.dylib | awk '{print $3}' | grep -v '^_nota_'
```
**Expected outcome**: Empty output (zero leaked C++ or JUCE symbols).

### Step 3: Verify Incremental `.cpp` Compilation Speed
Touch a single device implementation file and rebuild:
```bash
touch src/native/nota.engine/src/devices/effects/Chamber.cpp
cmake --build src/native/nota.engine/build
```
**Expected outcome**: Only `Chamber.cpp.o` and `libnota_engine.dylib` rebuild (1 compiler job + 1 link job), completing in $< 2\text{ s}$.

### Step 4: Verify Out-of-Process Plugin Scanner & Thread-Safe Catalog
Run the plugin store and scanner smoke tests:
```bash
dotnet run --project tests/Nota.SmokeTest -c Release -- --scan src/native/nota.engine/build/nota-scanworker
```
**Expected outcome**: All plugin scanner, DSP optimization (`DspOptimizationTests.cs`), and silence bypass (`DspSilenceBypassTests.cs`) checks pass 100%.
