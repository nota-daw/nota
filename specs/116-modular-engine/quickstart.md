<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Quickstart & Validation Guide (`specs/116-modular-engine`)

## 1. Local Developer Configure & Test (`clangd` clean single-arch)

```bash
# Pure OOP C++20 engine without C ABI
cmake -S src/native/nota.engine --preset dev-cpp --fresh
cmake --build src/native/nota.engine/build-dev-cpp
ctest --test-dir src/native/nota.engine/build-dev-cpp --output-on-failure

# Full engine including optional C ABI target (libnota_engine.*)
cmake -S src/native/nota.engine --preset dev --fresh
cmake --build src/native/nota.engine/build-dev
ctest --test-dir src/native/nota.engine/build-dev --output-on-failure
./src/native/nota.engine/build-dev/nota-scanner-selftest
```

## 2. `clangd` Static Checks & Header Self-Containment

```bash
clangd --compile-commands-dir=src/native/nota.engine/build-dev \
  --check=src/native/nota.engine/modules/engine/src/core/EngineLifecycle.cpp
clangd --compile-commands-dir=src/native/nota.engine/build-dev \
  --check=src/native/nota.engine/modules/io/src/audio/AudioDriverRegistry.cpp
clangd --compile-commands-dir=src/native/nota.engine/build-dev \
  --check=src/native/nota.engine/modules/pluginscanner/src/OutOfProcessScanner.cpp
ctest --test-dir src/native/nota.engine/build-dev -R headers_compile --output-on-failure
```

## 3. Release Universal Build, Export Verification & Perf Checks

```bash
cmake -S src/native/nota.engine --preset release
cmake --build src/native/nota.engine/build-release

# Verify zero JUCE symbols exported dynamically (macOS example)
nm -gU src/native/nota.engine/build-release/libnota_pluginhost.dylib | grep -i juce && exit 1 || echo "OK: no JUCE exports in pluginhost"
nm -gU src/native/nota.engine/build-release/libnota_pluginscanner.dylib | grep -i juce && exit 1 || echo "OK: no JUCE exports in pluginscanner"

# Verify universal architectures on macOS
lipo -info src/native/nota.engine/build-release/libnota_*.dylib

# Native performance benchmarks
cmake -G Ninja -S tests/perf -B build-perf -DCMAKE_BUILD_TYPE=Release \
  -DNOTA_ENGINE_DIR="$PWD/src/native/nota.engine" \
  -DNOTA_ENGINE_BUILD_DIR="$PWD/src/native/nota.engine/build-release"
cmake --build build-perf
./build-perf/nota_perf list
./build-perf/nota_idle_cpu
```
