# Phase 0 Research: Professional C++20 DSP Architecture Overhaul & Plugin Scanner Separation

**Feature**: `030-dsp-architecture-overhaul` | **Date**: 2026-10-19

## 1. Idiomatic `.h` / `.cpp` Separation vs. Monolithic Header-Only Devices

### Decision
Split all non-template classes across `dsp/primitives/`, `devices/` (25 effects, 16 instruments, 6 MIDI effects, 4 rack classes), `engine/`, `audio_io/`, `media/`, and `pluginhost/` into concise declaration headers (`.h`) and implementation translation units (`.cpp`). Keep only small inline math/SIMD functions (`FastMath.h`, `Simd.h`, `SimdAudio.h`, `AudioSpan.h`, `RtAttributes.h`, `ScopedNoDenormals.h`, `Hadamard.h`, `Random.h`) and class templates (`SpscRingBuffer`, `SnapshotHistory`, `PolyVoiceManager`) in headers.

### Rationale
- Currently, `Engine_Devices.cpp`, `Engine_Render.cpp`, `RackCore.h`, and `BuiltinDevices.h` `#include` dozens of 500–1,800 line header-only devices. Modifying a single line in `Chamber.h` or `VoltSynth.h` triggers massive recompilation of core engine translation units.
- In idiomatic C++ (and as demonstrated throughout `zrythm/src/dsp/`), separating interface (`.h`) from implementation (`.cpp`):
  1. Reduces header size from ~1,000 lines to ~60–120 lines per device.
  2. Hides internal helper functions, lookup tables, and parameter metadata inside anonymous namespaces in `.cpp` files.
  3. Enables `BuiltinDevices.cpp` and `DeviceFactory.cpp` to be the only translation units that include all device headers, while `Engine_Render.cpp` only needs `Device.h`, `Instrument.h`, and `MidiDevice.h`!
  4. Allows Ninja to compile all 50+ device `.cpp` files in parallel across all CPU cores.

### Alternatives Considered
- **Keeping header-only devices in subdirectories**: Rejected by user requirement ("try to split things into more .cpp and .h files the way C++ is normally written") and because header-only architecture scales poorly in build times and symbol isolation.

---

## 2. Plugin Scanner Separation (`nota_pluginscanner` vs. `nota_pluginhost` vs. `nota_engine`)

### Decision
Divide `pluginhost/` into three distinct layers:
1. **`pluginhost/bridge/`**: Pure C++ headers (`PluginHostBridge.h`, `PluginCatalogBridge.h`) with zero JUCE types.
2. **`pluginhost/scanner/` (`nota_pluginscanner` static library + `nota-scanworker` executable)**:
   - `PluginCatalog.h` / `PluginCatalog.cpp`: Thread-safe registry owning `plugins.xml` and `scanpaths.txt`, guarded by `std::shared_mutex` so background scans (`Task.Run` in C#) and UI thread reads (`nota_pluginhost_plugin_desc`, `nota_pluginhost_index_of_id`) never race.
   - `OutOfProcessScanner.h` / `OutOfProcessScanner.cpp`: Manages `nota-scanworker` child process execution, timeout enforcement, sentinel slicing (`<<<NOTA_PLUGINS_BEGIN>>>`..`<<<NOTA_PLUGINS_END>>>`), and batch/coordinator scanning inspired by Zrythm's `OutOfProcessPluginScanner`.
   - `abi_pluginscanner.cpp`: Implements the `nota_pluginhost_scan*` and `nota_pluginhost_plugin_*` C ABI functions.
   - `worker/PluginScannerWorker.h` / `.cpp` + `worker/main.cpp`: The out-of-process scanner executable (`nota-scanworker`).
3. **`pluginhost/hosting/` (`nota_pluginhost` static library)**:
   - `PluginHost.h` / `.cpp`, `NotaPlayHead.h` / `.cpp`, `PluginEditorWindow.h` / `.cpp`, `PluginInstrument.h` / `.cpp`, `PluginEffect.h` / `.cpp`.
   - Queries `PluginCatalogBridge` to obtain plugin descriptions by index/ID and instantiates `juce::AudioPluginInstance` on the main thread.

### Rationale
- Completely decouples plugin scanning and catalog management from `nota::Engine`.
- Fixes the latent thread-safety bug where `PluginScan.cs` calls `nota_pluginhost_scan` on a .NET worker thread while the UI thread reads `catalog()`.
- Preserves 100% of `include/nota/nota_engine.h` C ABI by linking `nota_pluginscanner` and `nota_pluginhost` into `nota_engine` with `WHOLE_ARCHIVE`.

---

## 3. Zrythm DSP Patterns Adopted in Nota

### Decision
Adopt the following proven C++ patterns from Zrythm (`zrythm/src/dsp/` and `zrythm/src/utils/`), adapted to Nota's JUCE-free engine rules:
1. **Compiler Real-Time Safety Annotations (`RtAttributes.h`)**:
   - Use `[[clang::nonblocking]]` (`NOTA_RT_SAFE`) and `[[gnu::hot]]` (`NOTA_HOT`) on all audio-thread entry points and callees, enabling Clang's RealtimeSanitizer (`-fsanitize=realtime`).
2. **Explicit Processor Preparation Lifecycle**:
   - Allocate all buffers strictly during `setSampleRate(sr, maxBlock)` / `prepare()` on the message thread.
   - Fix `Engine::computeBlockMidi()` so `blockMidi_` and `blockExpr_` are pre-allocated when the `Graph` is published on the message thread, never resized on the audio thread.
3. **`std::span`-Based Buffer Views (`AudioSpan.h`) & Vectorized Float Ranges (`Simd.h`, `SimdAudio.h`)**:
   - Provide `StereoSpan` and `ConstStereoSpan` wrapping `std::span<float>` alongside pointer overloads.
   - Provide vectorized `deinterleaveStereo`, `interleaveStereo`, `scaleStereoMeter`, `addStereoScaledMeterMap`, `processMasterBus`, `isSilent`, and `peakAndSumSquares`.
4. **Cache-Aligned Fork-Join Parallel Executor (`AudioThreadPool.h` / `.cpp`)**:
   - Inspired by `zrythm::dsp::graph::ForkJoinExecutor`:
     - Align job slots (`alignas(64)`) to prevent false sharing across CPU cores.
     - Maintain per-worker scratch buffers (`workerScratch_[workerId]`) instead of allocating a 128 KB `scratchBuf` on every `Track` instance.
     - Ensure every worker thread sets `ScopedNoDenormals` (FTZ/DAZ) in its worker loop!
5. **Reusable Polyphonic Voice Management (`PolyVoiceManager.h` / `SynthVoice.h`)**:
   - Inspired by `zrythm::dsp::PolyVoiceManager` and `SynthVoice`, adapted to fixed-capacity non-allocating storage for Nota's built-in instruments.

---

## 4. Consolidation of DSP Optimization Patches (`0001` & `0002`)

### Decision
Incorporate all optimizations from `0001-feat-Comprehensive-real-time-DSP-performance-optimiz.patch` and `0002-feat-Polyphonic-synth-FastMath-SIMD-audio-vectorizat.patch` directly into the new modular `.h`/`.cpp` architecture, and extend them to the remaining unoptimized devices and instruments:
- **`FastMath.h`**: `exp2f`, `log2f`, `dbToLin`, `linToDb`, `tanh`, `tanhPrecise`, `tanhRatio`, `sin`, `cos`, `tanPrewarp`.
- **`Simd.h` & `SimdAudio.h`**: NEON and SSE/AVX/FMA vectorization, 16-sample early-exit `isSilent`, single-pass `peakAndSumSquares`, `StereoMeterAccumulator`, `processMasterBus` with branchless NaN/Inf suppression (`squashNonFinite`), and `dot16` for 4x True-Peak FIR limiting.
- **`SilenceDetector`**: Dual-threshold hysteresis ($-80\text{ dBFS}$ attack / $-90.5\text{ dBFS}$ release) with `lastMaxPeak` caching to avoid redundant buffer scans.
- **UI Telemetry Decimation**: Batch ring-buffer write index updates (`ringW_`) to once per audio block across all analyzers and scopes (`Eq`, `DynamicEq`, `Compressor`, `Ceiling`, `Prism`, `OperatorSynth`, `Chorus`, `Flanger`, `Phaser`, `AutoFilter`, `Vintage`, `Lens`).
