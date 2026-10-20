---
description: "Task list for Feature 030: Professional C++20 DSP Architecture Overhaul, .h/.cpp Modularization & Plugin Scanner Separation"
---

# Tasks: Professional C++20 DSP Architecture Overhaul, `.h`/`.cpp` Modularization & Plugin Scanner Separation

**Input**: Design documents from `/specs/030-dsp-architecture-overhaul/` (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`)

**Organization**: Tasks are grouped by user story and ordered so each phase leaves the build (`./scripts/build.sh`) and smoke tests (`tests/Nota.SmokeTest`) 100% green.

---

## Phase 1: Setup & Foundational Infrastructure (`dsp/core/` & `dsp/math/`)

**Purpose**: Establish directory structure, compiler RT-safety attributes, denormal guards, `FastMath`, `Simd`, `SimdAudio`, and `SilenceDetector` primitives that all subsequent phases depend on.

- [x] T001 Create directory hierarchy under `src/native/nota.engine/src/` (`abi/`, `dsp/core/`, `dsp/math/`, `dsp/primitives/`, `dsp/poly/`, `devices/racks/`, `devices/effects/`, `devices/instruments/`, `devices/midi_fx/`, `engine/`, `audio_io/macos/`, `audio_io/desktop/`, `media/`) and `src/native/nota.engine/pluginhost/` (`bridge/`, `scanner/worker/`, `hosting/`)
- [x] T002 [P] Implement `RtAttributes.h` (`NOTA_RT_SAFE`, `NOTA_HOT`, `NOTA_ALWAYS_INLINE`, `NOTA_CACHE_ALIGNED`, `NOTA_CPU_PAUSE`) in `src/native/nota.engine/src/dsp/core/RtAttributes.h`
- [x] T003 [P] Implement unified `ScopedNoDenormals.h` RAII FTZ/DAZ guard for ARM64 and x86_64 in `src/native/nota.engine/src/dsp/core/ScopedNoDenormals.h`
- [x] T004 [P] Implement `AudioSpan.h` (`StereoSpan`, `ConstStereoSpan`, `PlanarAudioSpan` over `std::span<float>`) in `src/native/nota.engine/src/dsp/core/AudioSpan.h`
- [x] T005 [P] Implement `FastMath.h` (`exp2f`, `log2f`, `dbToLin`, `linToDb`, `tanhRatio`, `tanh`, `tanhPrecise`, `sin`, `cos`, `tanPrewarp`) in `src/native/nota.engine/src/dsp/math/FastMath.h`
- [x] T006 [P] Implement `Simd.h` (ARM NEON and x86 SSE/AVX/FMA `add`, `addScaled`, `scale`, `mul`, `dot16`, `maxAbs`, `clip`, `squashNonFinite`) in `src/native/nota.engine/src/dsp/math/Simd.h`
- [x] T007 [P] Implement `SimdAudio.h` (`isSilent`, `peakAndSumSquares`, `deinterleaveStereo`, `interleaveStereo`, `interleaveStereoAdd`, `scaleStereo`, `addStereoScaled`, `StereoMeterAccumulator`) in `src/native/nota.engine/src/dsp/math/SimdAudio.h`
- [x] T008 [P] Implement `AudioBufferUtils.h` (`isBufferSilent`, `scaleStereoMeter`, `addStereoScaledMeter`, `addStereoScaledMeterMap`, `processMasterBus`) in `src/native/nota.engine/src/dsp/math/AudioBufferUtils.h`
- [x] T009 [P] Implement `Random.h` deterministic audio-thread PRNG in `src/native/nota.engine/src/dsp/math/Random.h`
- [x] T010 Implement `SilenceDetector.h` and `SilenceDetector.cpp` (dual-threshold hysteresis $-80\text{ dBFS}$ / $-90.5\text{ dBFS}$, sample-accurate hold counter, `lastMaxPeak` cache) in `src/native/nota.engine/src/dsp/core/SilenceDetector.h` and `src/native/nota.engine/src/dsp/core/SilenceDetector.cpp`
- [x] T011 Implement `Parameter.h` and `Parameter.cpp` (normalized parameter mappings, `OnePoleSmoother`, `ParameterChangeTracker`) in `src/native/nota.engine/src/dsp/core/Parameter.h` and `src/native/nota.engine/src/dsp/core/Parameter.cpp`

**Checkpoint**: Core `dsp/core/` and `dsp/math/` headers and translation units compile cleanly with zero warnings.

---

## Phase 2: User Story 2 - Standalone Plugin Scanner & Hosting Separation (Priority: P1)

**Goal**: Decouple plugin scanning and catalog management (`nota_pluginscanner` + `nota-scanworker`) from `Engine` and `nota_pluginhost`, making `PluginCatalog` thread-safe and splitting `PluginHost.cpp` into clean `.h`/`.cpp` modules.

**Independent Test**: Build `nota_pluginscanner`, `nota_pluginhost`, `nota-scanworker`, and `nota_engine`; run `PluginStoreTests` and `--scan` smoke tests.

- [x] T012 [P] [US2] Create `PluginCatalogBridge.h` and move `PluginHostBridge.h` into `src/native/nota.engine/pluginhost/bridge/`
- [x] T013 [US2] Implement thread-safe `PluginCatalog.h` and `PluginCatalog.cpp` (guarded by `std::shared_mutex`, managing `plugins.xml`, `scanpaths.txt`, and ID fallback lookup) in `src/native/nota.engine/pluginhost/scanner/PluginCatalog.h` and `src/native/nota.engine/pluginhost/scanner/PluginCatalog.cpp`
- [x] T014 [US2] Implement `OutOfProcessScanner.h` and `OutOfProcessScanner.cpp` (child process coordinator, timeout management, sentinel XML extraction) in `src/native/nota.engine/pluginhost/scanner/OutOfProcessScanner.h` and `src/native/nota.engine/pluginhost/scanner/OutOfProcessScanner.cpp`
- [x] T015 [US2] Implement `abi_pluginscanner.cpp` (`nota_pluginhost_scan`, `nota_pluginhost_plugin_count`, `nota_pluginhost_plugin_desc`, `nota_pluginhost_plugin_id`, `nota_pluginhost_index_of_id`, `nota_pluginhost_add_scan_path`, `nota_pluginhost_remove_scan_path`, `nota_pluginhost_scan_path_count`, `nota_pluginhost_scan_path`) in `src/native/nota.engine/pluginhost/scanner/abi_pluginscanner.cpp`
- [x] T016 [US2] Split `nota-scanworker` into `PluginScannerWorker.h`, `PluginScannerWorker.cpp`, and `main.cpp` in `src/native/nota.engine/pluginhost/scanner/worker/`
- [x] T017 [P] [US2] Extract `NotaPlayHead.h` and `NotaPlayHead.cpp` in `src/native/nota.engine/pluginhost/hosting/`
- [x] T018 [P] [US2] Extract `PluginEditorWindow.h` and `PluginEditorWindow.cpp` (`PluginEditorWindow`, `EditorHost`, macOS `NSEvent` key monitor) in `src/native/nota.engine/pluginhost/hosting/`
- [x] T019 [P] [US2] Extract `PluginInstrument.h` and `PluginInstrument.cpp` (using `simd_audio::deinterleaveStereo` / `interleaveStereoAdd`) in `src/native/nota.engine/pluginhost/hosting/`
- [x] T020 [P] [US2] Extract `PluginEffect.h` and `PluginEffect.cpp` (using `simd_audio::deinterleaveStereo`, `fast_math::dbToLin` sidechain scaling, and tail/idle support) in `src/native/nota.engine/pluginhost/hosting/`
- [x] T021 [US2] Implement `PluginHost.h` and `PluginHost.cpp` (instance factories, `nota_pluginhost_open_test_window`, `nota_pluginhost_pump`) and register `nota_pluginscanner` + `nota_pluginhost` targets in `src/native/nota.engine/CMakeLists.txt`

**Checkpoint**: `nota_pluginscanner`, `nota_pluginhost`, and `nota-scanworker` build as separated modules; all C ABI `nota_pluginhost_*` symbols resolve and pass smoke tests.

---

## Phase 3: User Story 3 - Shared C++20 DSP Primitives & Polyphony (`dsp/primitives/` & `dsp/poly/`) (Priority: P1)

**Goal**: Extract and implement shared, reusable `.h`/`.cpp` DSP building blocks so devices and instruments no longer duplicate filters, delay lines, convolutions, oscillators, envelopes, or voice managers.

**Independent Test**: Compile all `dsp/primitives/` and `dsp/poly/` translation units and verify behavior via smoke tests.

- [x] T022 [P] [US3] Implement `SvfFilter.h` and `SvfFilter.cpp` (Cytomic TPT SVF + `Lr4Splitter` 4th-order Linkwitz-Riley crossover) in `src/native/nota.engine/src/dsp/primitives/`
- [x] T023 [P] [US3] Implement `BiquadCascade.h` and `BiquadCascade.cpp` (DF2T chunked 64-sample block filter cascade + RBJ/Orfanidis coefficient calculators) in `src/native/nota.engine/src/dsp/primitives/`
- [x] T024 [P] [US3] Implement `LadderFilter.h` and `LadderFilter.cpp` (4-pole zero-delay feedback transistor ladder with `fast_math::tanhRatio`) in `src/native/nota.engine/src/dsp/primitives/`
- [x] T025 [P] [US3] Implement `DelayLine.h` and `DelayLine.cpp` (power-of-two circular buffer with bitmask indexing, linear, 4-point Hermite cubic, and allpass reads) in `src/native/nota.engine/src/dsp/primitives/`
- [x] T026 [P] [US3] Implement `Hadamard.h` (branchless unrolled `hadamard4` and `hadamard8`) in `src/native/nota.engine/src/dsp/primitives/Hadamard.h`
- [x] T027 [P] [US3] Implement `Oscillator.h` and `Oscillator.cpp` (polyBLEP / polyBLAMP waveforms and tempo-synced `LfoGenerator` using `fast_math::sin`) in `src/native/nota.engine/src/dsp/primitives/`
- [x] T028 [P] [US3] Implement `Envelope.h` and `Envelope.cpp` (`AdsrEnvelope`, `ArEnvelope`, `EnvelopeFollower` using `fast_math::linToDb`/`dbToLin`) in `src/native/nota.engine/src/dsp/primitives/`
- [x] T029 [P] [US3] Split `Oversampler` into `Oversampler.h` and `Oversampler.cpp` (with vectorized `deinterleaveStereo`/`interleaveStereo`) in `src/native/nota.engine/src/dsp/primitives/`
- [x] T030 [P] [US3] Extract `ConvKernel` zero-latency partitioned FFT convolution from `Chamber` into `Convolution.h` and `Convolution.cpp` in `src/native/nota.engine/src/dsp/primitives/`
- [x] T031 [P] [US3] Implement `TruePeakMeter.h` and `TruePeakMeter.cpp` (32-float double-buffered 16-tap FIR with `simd::dot16`) in `src/native/nota.engine/src/dsp/primitives/`
- [x] T032 [P] [US3] Split `CompensationDelay` into `CompensationDelay.h` and `CompensationDelay.cpp` (with `isIdle()` support) in `src/native/nota.engine/src/dsp/primitives/`
- [x] T033 [P] [US3] Implement `SynthVoice.h`, `SynthVoice.cpp`, `PolyVoiceManager.h`, and `PolyVoiceManager.cpp` in `src/native/nota.engine/src/dsp/poly/`

**Checkpoint**: All shared DSP primitives compile cleanly as independent translation units.

---

## Phase 4: User Story 1 & 4 - Modular `.h`/`.cpp` Split & Optimization of All Devices, Effects, Instruments & Racks (Priority: P1)

**Goal**: Organize `abi/`, `audio_io/`, `media/`, `engine/`, and `devices/` into domain folders; split all 25 audio effects, 16 instruments, 6 MIDI effects, and 4 rack classes into `.h` + `.cpp` pairs; apply `FastMath`, `Simd`, UI telemetry decimation, and `isIdle()`/`hasTail()`/`wake()` auto-sleep across every device.

**Independent Test**: Rebuild `nota_engine` and run `Nota.SmokeTest`, verifying that changing any single device `.cpp` recompiles only 1 file and all device/instrument tests pass.

### 4A. Move & Split `abi/`, `audio_io/`, `media/`, and Base `devices/` Interfaces
- [x] T034 [US1] Move all `nota_engine_*.cpp`, `nota_encoder.cpp`, and `nota_engine_internal.h` into `src/native/nota.engine/src/abi/`
- [x] T035 [P] [US1] Move hardware audio/MIDI/gamepad backends and config files into `src/native/nota.engine/src/audio_io/`, `audio_io/macos/`, and `audio_io/desktop/`; split `MpeInput` into `MpeInput.h` and `MpeInput.cpp`
- [x] T036 [P] [US1] Move `SampleBuffer`, `AudioFile`, `AudioImport`, `Warp`, `WarpStream`, `TempoDetect`, and `SampleAnalysis` into `src/native/nota.engine/src/media/`; split `SampleBuffer` into `SampleBuffer.h` and `SampleBuffer.cpp`
- [x] T037 [US1] Move `Device.h`, `Instrument.h`, `MidiDevice.h`, `DeviceFactory.h`, and `BuiltinDevices.h` into `src/native/nota.engine/src/devices/` and implement `Device.cpp`, `Instrument.cpp`, `MidiDevice.cpp`, `DeviceFactory.cpp`, and `BuiltinDevices.cpp` (removing all 47 concrete device `#include`s from headers!)

### 4B. Split & Optimize Racks (`src/devices/racks/`)
- [x] T038 [US1] Split `RackCore`, `RackDevice`, `RackInstrument`, and `DrumRack` into `.h` and `.cpp` pairs in `src/native/nota.engine/src/devices/racks/`

### 4C. Split & Optimize All 25 Audio Effects (`src/devices/effects/`)
- [x] T039 [P] [US1] [US3] [US4] Split and optimize EQ & Filter effects into `.h` + `.cpp` pairs in `src/native/nota.engine/src/devices/effects/`: `Eq.h`/`.cpp` (64-sample block biquad, FastMath, auto-sleep), `Eq3.h`/`.cpp`, `DynamicEq.h`/`.cpp` (`linToDb`/`exp2f`, auto-sleep), `AutoFilter.h`/`.cpp` (`fast_math::tanh`/`sin`, auto-sleep)
- [x] T040 [P] [US1] [US3] [US4] Split and optimize Dynamics effects into `.h` + `.cpp` pairs in `src/native/nota.engine/src/devices/effects/`: `Compressor.h`/`.cpp` (`linToDb`/`dbToLin`, decimated `keyRing_`, auto-sleep), `Ceiling.h`/`.cpp` (`TruePeakMeter` SIMD `dot16`, hoisted `invCeil`, auto-sleep), `Prism.h`/`.cpp` (`SvfFilter` LR4 crossover, `FastMath`, auto-sleep), `AutoGain.h`/`.cpp`
- [x] T041 [P] [US1] [US3] [US4] Split and optimize Reverb & Delay effects into `.h` + `.cpp` pairs in `src/native/nota.engine/src/devices/effects/`: `ChamberIrSynth.h`/`.cpp`, `Chamber.h`/`.cpp` (`Convolution`, `Hadamard`, `FastMath` ducking & soft-clip, unrolled diffusion, auto-sleep), `Reverb.h`/`.cpp` (`fast_math::sin` LFO, tail auto-sleep), `Delay.h`/`.cpp` (hoisted `invTanhDrive`, `fast_math::sin` wow LFO, tail auto-sleep), `BeatRepeat.h`/`.cpp`, `Strata.h`/`.cpp`
- [x] T042 [P] [US1] [US3] [US4] Split and optimize Modulation & Spatial effects into `.h` + `.cpp` pairs in `src/native/nota.engine/src/devices/effects/`: `Chorus.h`/`.cpp`, `Flanger.h`/`.cpp`, `Phaser.h`/`.cpp`, `AutoPan.h`/`.cpp` (hoisted `invTanhKk`, `fast_math::sin`, instant silence bypass), `Shutter.h`/`.cpp`, `Utility.h`/`.cpp` (instant silence bypass), `Lens.h`/`.cpp`
- [x] T043 [P] [US1] [US3] [US4] Split and optimize Saturation & Pitch effects into `.h` + `.cpp` pairs in `src/native/nota.engine/src/devices/effects/`: `Vintage.h`/`.cpp` (`fast_math::tanh`/`sin`/`cos`, precomputed `kTanh06`, auto-sleep), `Amp.h`/`.cpp`, `Crush.h`/`.cpp`, `Forge.h`/`.cpp`, `AutoShift.h`/`.cpp`

### 4D. Split & Optimize All Instruments (`src/devices/instruments/`)
- [x] T044 [P] [US1] [US3] [US4] Split and optimize Analog/Subtractive Synths into `.h` + `.cpp` pairs in `src/native/nota.engine/src/devices/instruments/`: `Synth.h`/`.cpp`, `VoltSynth.h`/`.cpp` (`fast_math::sin`/`exp2f`, `isIdle()`), `BassSynth.h`/`.cpp` (hoisted `preMakeup`/`outMakeup`, `fast_math::tanh`/`sin`, `isIdle()`), `Monolith.h`/`.cpp`, `Pentad.h`/`.cpp` (unified `FastMath.h`), `Consort.h`/`.cpp` (unified `ScopedNoDenormals.h` & `FastMath.h`)
- [x] T045 [P] [US1] [US3] [US4] Split and optimize Digital/FM/Wavetable/Generative Synths into `.h` + `.cpp` pairs in `src/native/nota.engine/src/devices/instruments/`: `WavetableSynth.h`/`.cpp` (hoisted `driveInvNorm`, `fast_math::tanh`/`sin`), `OperatorSynth.h`/`.cpp` (`fast_math::sin`, block-decimated `ringW_`), `FluxSynth.h`/`.cpp` (`fast_math::sin`/`tanh`), `PendulumSynth.h`/`.cpp` (`fast_math::sin`/`cos` harmonics)
- [x] T046 [P] [US1] [US3] [US4] Split and optimize Physical, Sampler & Drum Instruments into `.h` + `.cpp` pairs in `src/native/nota.engine/src/devices/instruments/`: `PhysicalSynth.h`/`.cpp`, `Keys.h`/`.cpp` (unified `ScopedNoDenormals.h` & `FastMath.h`), `Sampler.h`/`.cpp`, `GrainSynth.h`/`.cpp`, `RhythmVoiceDsp.h`/`.cpp`, `RhythmMachine.h`/`.cpp`, `MosaicProgram.h`/`.cpp`, `Mosaic.h`/`.cpp`

### 4E. Split All 6 MIDI Effects (`src/devices/midi_fx/`)
- [x] T047 [P] [US1] Split `Arpeggiator`, `MidiChord`, `MidiScale`, `MidiNoteLength`, `MidiVelocity`, and `MidiRandom` into `.h` + `.cpp` pairs in `src/native/nota.engine/src/devices/midi_fx/`

**Checkpoint**: All 25 effects, 16 instruments, 6 MIDI effects, and 4 racks compile as separate `.cpp` translation units and pass all device smoke tests.

---

## Phase 5: User Story 5 - Engine Core `.h`/`.cpp` Split, Multi-Core `AudioThreadPool` & SIMD Mixer (`src/engine/`) (Priority: P2)

**Goal**: Move engine classes to `src/engine/`, split model headers (`Track`, `Graph`, `Transport`, `Automation`, `Modulation`, `MidiClip`, `SessionPlayer`, `NoteExpression`, `MidiController`) into `.h` + `.cpp`, fix the `computeBlockMidi` AR-6 allocation hazard, and integrate `AudioThreadPool` (with per-worker scratch buffers) and SIMD mixer summing in `Engine_Render.cpp`.

**Independent Test**: Run `DspOptimizationTests` and `DspSilenceBypassTests` in `tests/Nota.SmokeTest`.

- [x] T048 [P] [US1] Move and split `Graph` (`Graph.h`/`.cpp`), `Track` (`Track.h`/`.cpp`), `Transport` (`Transport.h`/`.cpp`, `TransportInfo.h`), `SessionPlayer` (`SessionPlayer.h`/`.cpp`), `Automation` (`Automation.h`/`.cpp`), `Modulation` (`Modulation.h`/`.cpp`), `MidiClip` (`MidiClip.h`/`.cpp`), `MidiController` (`MidiController.h`/`.cpp`), `NoteExpression` (`NoteExpression.h`/`.cpp`), `CommandQueue.h`, `SnapshotHistory.h`, and `Audition.h`/`.cpp` in `src/native/nota.engine/src/engine/`
- [x] T049 [US5] Implement `AudioThreadPool.h` and `AudioThreadPool.cpp` (lock-free Fork-Join worker pool with `NOTA_CACHE_ALIGNED` atomics, `ScopedNoDenormals` per worker thread, and pre-allocated per-worker `workerScratch_` buffers) in `src/native/nota.engine/src/engine/`
- [x] T050 [US3] [US5] Pre-allocate `blockMidi_` and `blockExpr_` buffers on the message thread during `Engine::publishRaw()` / `propagateSampleRate()` to eliminate `std::vector::resize()` on the audio thread in `Engine::computeBlockMidi()` (fixing AR-6)
- [x] T051 [US4] [US5] Update `Engine.h`, `Engine.cpp`, and `Engine_Render.cpp` in `src/native/nota.engine/src/engine/` to:
  1. Dispatch Pass 1 leaf tracks in parallel via `AudioThreadPool` using `workerScratchBuffer(workerId)`.
  2. Skip `playEvents` and redundant buffer scans when `inst->isIdle()` and `n == 0`.
  3. Propagate `SilenceDetector` and `currentSilent` down leaf, group, return, and master device chains.
  4. Vectorize leaf/group/return fader scaling, route taps (`routeBus_`, `routeBusPre_`), and master bus processing (`processMasterBus`).
- [x] T052 [US1] Update `src/native/nota.engine/CMakeLists.txt` with the complete modular source list and include directories

**Checkpoint**: Engine compiles and links cleanly; parallel rendering, silence bypass, and SIMD mixer summing operate with zero AR-6 violations.

---

## Phase 6: Verification, Smoke Tests & Documentation Sync

**Purpose**: End-to-end validation, regression tests, and Nota documentation sync.

- [x] T053 [P] Add `DspOptimizationTests.cs` and `DspSilenceBypassTests.cs` in `tests/Nota.SmokeTest/` and wire them into `tests/Nota.SmokeTest/Program.cs`
- [x] T054 [P] Update `ARCHITECTURE.md`, `BUILD.md`, `.agents/skills/nota-instrument/SKILL.md`, and `.claude/skills/nota-instrument/SKILL.md` to document the new folder structure, `.h`/`.cpp` convention, and separated `nota_pluginscanner` module
- [x] T055 [P] Add `.clang-format` in repository root and `CHANGELOG.md` `[Unreleased]` entry documenting the C++20 DSP architecture overhaul, `.h`/`.cpp` modularization, plugin scanner separation, multi-core rendering, SIMD vectorization, and auto-sleep bypass
- [x] T056 Verify AGPL-3.0-only two-line SPDX header on every new `.h`, `.cpp`, and `.cs` file
- [x] T057 Run `./scripts/build.sh` until native build, symbol export check, managed build, and `tests/Nota.SmokeTest` pass 100% green

---

## Dependencies & Execution Order

- **Phase 1 (`dsp/core` & `dsp/math`)**: No dependencies — starts immediately.
- **Phase 2 (`pluginhost/scanner` & `pluginhost/hosting`)**: Depends on Phase 1 (`FastMath`, `SimdAudio`). Can run in parallel with Phase 3.
- **Phase 3 (`dsp/primitives` & `dsp/poly`)**: Depends on Phase 1.
- **Phase 4 (`devices/`, `abi/`, `audio_io/`, `media/` `.h`/`.cpp` split & optimization)**: Depends on Phases 1 & 3. Tasks T039–T047 are highly parallelizable across device families.
- **Phase 5 (`engine/` `.h`/`.cpp` split, `AudioThreadPool` & `Engine_Render.cpp`)**: Depends on Phases 1–4.
- **Phase 6 (Tests, Docs & Final Gate)**: Depends on Phases 1–5.
