<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Contract: CMake Shared Module DAG & Dependency Isolation

| Directory | CMake Target | Alias | Output Binary | `PUBLIC` Deps | `PRIVATE` Vendor Deps |
|---|---|---|---|---|---|
| `modules/core/` | `nota_core` | `nota::core` | `nota_core` | None | None (pure STL) |
| `modules/math/` | `nota_math` | `nota::math` | `nota_math` | None | `Accelerate` (Apple) / `pffft` (non-Apple) |
| `modules/media/` | `nota_media` | `nota::media` | `nota_media` | `nota::math` | `dr_libs`, `FLAC`, `lame`, `signalsmith_*` |
| `modules/dsp/` | `nota_dsp` | `nota::dsp` | `nota_dsp` | `nota::math` | None (pure STL) |
| `modules/devices/` | `nota_devices` | `nota::devices` | `nota_devices` | `nota::dsp`, `nota::math` | `nota::media`, `hiir` |
| `modules/pluginhost/` | `nota_pluginhost` | `nota::pluginhost` | `nota_pluginhost` | `nota::dsp`, `nota::math` | `juce::juce_audio_processors`, `juce::juce_gui_basics`, `CoreAudioKit` (Apple), export list |
| `modules/pluginscanner/` | `nota_pluginscanner` (+ `nota-scanworker`, `nota-scanner-selftest`) | `nota::pluginscanner` | `nota_pluginscanner` | `nota::dsp` | `juce::juce_audio_processors` (no GUI!), `CoreAudioKit` (Apple), export list |
| `modules/io/` | `nota_io` | `nota::io` | `nota_io` | `nota::dsp`, `nota::math` | `miniaudio`, `asiosdk` (Win), `rtmidi`, Apple frameworks, `nlohmann_json` |
| `modules/engine/` | `nota_engine_core` | `nota::engine` | `nota_engine_core` | `nota::dsp`, `nota::io`, `nota::media`, `nota::math` | `nota::devices`, `nota::pluginhost`, `nlohmann_json` |
| `modules/c_abi/` *(optional)* | `nota_engine` | `nota::c_abi` | `nota_engine` | `nota::dsp` (header types only if needed) | `nota::engine`, `nota::media`, `nota::pluginscanner`, `nota::io`, export map (`nota_*` only) |

## Hard Rules Enforced by `NotaModuleHelpers.cmake`
1. `CMAKE_CXX_VISIBILITY_PRESET hidden` + `CMAKE_VISIBILITY_INLINES_HIDDEN ON` on every target.
2. Only `modules/<name>/include` is exposed as `PUBLIC`; `modules/<name>/src` is strictly `PRIVATE`.
3. `BUILD_RPATH` / `INSTALL_RPATH` uses `@loader_path` (macOS) and `$ORIGIN` (Linux).
4. `nota_pluginhost` and `nota_pluginscanner` MUST NEVER link or include each other.
