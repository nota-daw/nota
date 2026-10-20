# Implementation Plan: Professional C++20 DSP Architecture Overhaul, `.h`/`.cpp` Modularization & Plugin Scanner Separation

**Branch**: `030-dsp-architecture-overhaul` | **Date**: 2026-10-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/030-dsp-architecture-overhaul/spec.md`

## Summary

Restructure Nota's C++20 audio engine (`src/native/nota.engine/`) from a flat 138-file directory of monolithic header-only classes into a modular, domain-driven C++ architecture with clean `.h` declarations and `.cpp` implementations. Separate the plugin scanner and catalog (`nota_pluginscanner` + `nota-scanworker`) from the audio engine and in-process plugin host (`nota_pluginhost`) while preserving 100% of the public C ABI (`include/nota/nota_engine.h`). Integrate and expand the real-time DSP optimizations from patches `0001` and `0002` (`FastMath`, `Simd`, `SimdAudio`, `SilenceDetector`, `AudioThreadPool`, auto-sleep bypass) across all 25 audio effects, 16 instruments, and 6 MIDI effects, drawing architectural patterns (`[[clang::nonblocking]]`, `std::span` buffer views, cache-aligned fork-join execution, shared DSP primitives, and `PolyVoiceManager`) from Zrythm's DSP codebase.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-8 sample-accurate MIDI, AR-9 `Transport` is the only source of musical time).
- **A built-in instrument / audio or MIDI effect** → the `nota-instrument` skill (updated by this plan so DSP classes use `.h` + `.cpp` pairs).
- **Build / packaging** → [`BUILD.md`](../../BUILD.md).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing (AGPL-3.0-only headers), dependency policy, PR expectations.

## Technical Context

**Language/Version**: C++20 (native engine & pluginhost), Objective-C++ (`.mm` on macOS for CoreAudio/CoreMIDI/AppKit), C# / .NET 10 (`tests/Nota.SmokeTest`).

**Primary Dependencies**: Pure C++20 standard library, hardware SIMD intrinsics (`<arm_neon.h>`, `<immintrin.h>`), existing vendored libraries (`HIIR`, `dr_libs`, `signalsmith-stretch`, `signalsmith-linear`, `miniaudio`, `RtMidi`, `FLAC`, `LAME`, `nlohmann_json`), and `JUCE 8` (strictly confined to `pluginhost/`). Zero new external dependencies.

**Storage**: `plugins.xml` and `scanpaths.txt` managed thread-safely by `PluginCatalog` in `pluginhost/scanner/`.

**Audio-Thread Impact**:
- Strictly enforces **AR-6** across all threads: fixes latent `std::vector::resize()` in `Engine::computeBlockMidi()` by pre-allocating MIDI event buffers on the message thread; replaces per-`Track` 128 KB scratch allocations with per-worker cache-aligned scratch buffers in `AudioThreadPool`; applies `ScopedNoDenormals` on both the main audio thread and all worker threads; annotates all RT methods with `NOTA_RT_SAFE` (`noexcept [[clang::nonblocking]]`).

**Testing**: `./scripts/build.sh` running `tests/Nota.SmokeTest` (extended with `DspOptimizationTests.cs` and `DspSilenceBypassTests.cs`).

**Target Platform**: macOS 13+ (universal `arm64` + `x86_64`), Linux (`x86_64`, `aarch64`), Windows 10/11 (`x64`, `arm64`).

**Project Type**: Native shared library (`libnota_engine.{dylib,so}` / `nota_engine.dll`) + out-of-process scanner executable (`nota-scanworker`).

**Performance Goals**:
- 25%–55% CPU reduction across polyphonic synths and native effects via `FastMath` and SIMD.
- 40%–60% CPU reduction on silent/sparse tracks via `SilenceDetector` and device auto-sleep.
- 2x–4x multi-track rendering throughput via lock-free `AudioThreadPool`.
- 5x–10x faster incremental C++ rebuilds via `.h`/`.cpp` separation.

**Constraints**:
- 100% preservation of `include/nota/nota_engine.h` C ABI (AR-7).
- Zero JUCE includes outside `pluginhost/`.
- Perceptual audio transparency ($< 0.05\text{ dB}$ max deviation).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| **I. Real-Time Audio Safety** | PASS | Fixes `computeBlockMidi` resize hazard; zero allocations, zero locks, `[[clang::nonblocking]]` across all audio paths. |
| **II. Boundary Discipline** | PASS | `include/nota/nota_engine.h` untouched; JUCE confined strictly to `pluginhost/hosting/` and `pluginhost/scanner/`. |
| **III. Licensing Hygiene** | PASS | All new `.h` and `.cpp` files carry the two-line `AGPL-3.0-only` SPDX header; zero new third-party dependencies. |
| **IV. Verification Gate** | PASS | Validated end-to-end by `scripts/build.sh` and `tests/Nota.SmokeTest`. |
| **V. One Logical Change per PR** | PASS | Structured into self-contained, sequentially verifiable implementation phases. |
| **VI. Documentation Stays in Sync** | PASS | Updates `ARCHITECTURE.md`, `BUILD.md`, `.agents/skills/nota-instrument/SKILL.md`, and `CHANGELOG.md`. |
| **VII. Spec-First** | PASS | Complete spec, research, data-model, contracts, quickstart, plan, and tasks in `specs/030-dsp-architecture-overhaul/`. |
| **VIII. Safety Rules Never Weakened** | PASS | Strengthens AR-6 and thread safety. |

## Project Structure

### Documentation (this feature)

```text
specs/030-dsp-architecture-overhaul/
├── spec.md                              # Feature specification
├── plan.md                              # This implementation plan
├── research.md                          # Phase 0 architectural decisions
├── data-model.md                        # Phase 1 entities, primitives & concurrency model
├── quickstart.md                        # Phase 1 build & verification guide
├── checklists/
│   └── requirements.md                  # Spec quality checklist
├── contracts/
│   └── dsp-and-scanner-contracts.md     # C ABI, scanner bridge & RT DSP contracts
└── tasks.md                             # Phase 2 actionable task list
```

### Source Code Target Layout (`src/native/nota.engine/`)

Every non-template module is split into `.h` (declarations) and `.cpp` (implementations):

```text
src/native/nota.engine/
├── CMakeLists.txt
├── nota_exports.txt
├── nota_exports.map
├── include/
│   └── nota/
│       └── nota_engine.h                        # 100% preserved public C ABI
│
├── src/
│   ├── abi/                                     # C ABI translation units
│   │   ├── nota_engine_internal.h
│   │   ├── nota_engine_core.cpp
│   │   ├── nota_engine_audio.cpp
│   │   ├── nota_engine_track.cpp
│   │   ├── nota_engine_session.cpp
│   │   ├── nota_engine_device.cpp
│   │   ├── nota_engine_rack.cpp
│   │   ├── nota_engine_automation.cpp
│   │   ├── nota_engine_modulation.cpp
│   │   ├── nota_engine_import.cpp
│   │   ├── nota_engine_audition.cpp
│   │   └── nota_encoder.cpp
│   │
│   ├── dsp/                                     # Reusable, JUCE-free C++20 DSP Library
│   │   ├── core/
│   │   │   ├── RtAttributes.h                   # NOTA_RT_SAFE, NOTA_HOT, NOTA_CACHE_ALIGNED
│   │   │   ├── ScopedNoDenormals.h              # RAII FTZ/DAZ (ARM64 + x86_64)
│   │   │   ├── AudioSpan.h                      # StereoSpan / ConstStereoSpan (std::span)
│   │   │   ├── SilenceDetector.h
│   │   │   ├── SilenceDetector.cpp
│   │   │   ├── Parameter.h
│   │   │   └── Parameter.cpp
│   │   ├── math/
│   │   │   ├── FastMath.h                       # exp2f, log2f, dbToLin, linToDb, tanh, sin, cos, tanPrewarp
│   │   │   ├── Simd.h                           # NEON / SSE / AVX / FMA primitives
│   │   │   ├── SimdAudio.h                      # Stereo SIMD ops & StereoMeterAccumulator
│   │   │   ├── AudioBufferUtils.h               # Mixer SIMD helpers & processMasterBus
│   │   │   └── Random.h                         # Deterministic audio-thread PRNG
│   │   ├── primitives/
│   │   │   ├── SvfFilter.h
│   │   │   ├── SvfFilter.cpp
│   │   │   ├── BiquadCascade.h
│   │   │   ├── BiquadCascade.cpp
│   │   │   ├── LadderFilter.h
│   │   │   ├── LadderFilter.cpp
│   │   │   ├── DelayLine.h
│   │   │   ├── DelayLine.cpp
│   │   │   ├── Hadamard.h
│   │   │   ├── Oscillator.h
│   │   │   ├── Oscillator.cpp
│   │   │   ├── Envelope.h
│   │   │   ├── Envelope.cpp
│   │   │   ├── Oversampler.h
│   │   │   ├── Oversampler.cpp
│   │   │   ├── Convolution.h
│   │   │   ├── Convolution.cpp
│   │   │   ├── TruePeakMeter.h
│   │   │   ├── TruePeakMeter.cpp
│   │   │   ├── CompensationDelay.h
│   │   │   └── CompensationDelay.cpp
│   │   └── poly/
│   │       ├── SynthVoice.h
│   │       ├── SynthVoice.cpp
│   │       ├── PolyVoiceManager.h
│   │       └── PolyVoiceManager.cpp
│   │
│   ├── devices/                                 # Device Interfaces, Racks, Effects, Instruments, MIDI FX
│   │   ├── Device.h
│   │   ├── Device.cpp
│   │   ├── Instrument.h
│   │   ├── Instrument.cpp
│   │   ├── MidiDevice.h
│   │   ├── MidiDevice.cpp
│   │   ├── DeviceFactory.h
│   │   ├── DeviceFactory.cpp
│   │   ├── BuiltinDevices.h
│   │   ├── BuiltinDevices.cpp
│   │   ├── racks/
│   │   │   ├── RackCore.h
│   │   │   ├── RackCore.cpp
│   │   │   ├── RackDevice.h
│   │   │   ├── RackDevice.cpp
│   │   │   ├── RackInstrument.h
│   │   │   ├── RackInstrument.cpp
│   │   │   ├── DrumRack.h
│   │   │   └── DrumRack.cpp
│   │   ├── effects/                             # 25 Audio Effects (.h + .cpp each)
│   │   │   ├── Amp.h, Amp.cpp
│   │   │   ├── AutoFilter.h, AutoFilter.cpp
│   │   │   ├── AutoGain.h, AutoGain.cpp
│   │   │   ├── AutoPan.h, AutoPan.cpp
│   │   │   ├── AutoShift.h, AutoShift.cpp
│   │   │   ├── BeatRepeat.h, BeatRepeat.cpp
│   │   │   ├── Ceiling.h, Ceiling.cpp
│   │   │   ├── Chamber.h, Chamber.cpp
│   │   │   ├── ChamberIrSynth.h, ChamberIrSynth.cpp
│   │   │   ├── Chorus.h, Chorus.cpp
│   │   │   ├── Compressor.h, Compressor.cpp
│   │   │   ├── Crush.h, Crush.cpp
│   │   │   ├── Delay.h, Delay.cpp
│   │   │   ├── DynamicEq.h, DynamicEq.cpp
│   │   │   ├── Eq.h, Eq.cpp
│   │   │   ├── Eq3.h, Eq3.cpp
│   │   │   ├── Flanger.h, Flanger.cpp
│   │   │   ├── Forge.h, Forge.cpp
│   │   │   ├── Lens.h, Lens.cpp
│   │   │   ├── Phaser.h, Phaser.cpp
│   │   │   ├── Prism.h, Prism.cpp
│   │   │   ├── Reverb.h, Reverb.cpp
│   │   │   ├── Shutter.h, Shutter.cpp
│   │   │   ├── Strata.h, Strata.cpp
│   │   │   ├── Utility.h, Utility.cpp
│   │   │   └── Vintage.h, Vintage.cpp
│   │   ├── instruments/                         # 15 Instruments (.h + .cpp each)
│   │   │   ├── BassSynth.h, BassSynth.cpp
│   │   │   ├── Consort.h, Consort.cpp
│   │   │   ├── FluxSynth.h, FluxSynth.cpp
│   │   │   ├── GrainSynth.h, GrainSynth.cpp
│   │   │   ├── Keys.h, Keys.cpp
│   │   │   ├── Monolith.h, Monolith.cpp
│   │   │   ├── Mosaic.h, Mosaic.cpp
│   │   │   ├── MosaicProgram.h, MosaicProgram.cpp
│   │   │   ├── OperatorSynth.h, OperatorSynth.cpp
│   │   │   ├── PendulumSynth.h, PendulumSynth.cpp
│   │   │   ├── Pentad.h, Pentad.cpp
│   │   │   ├── PhysicalSynth.h, PhysicalSynth.cpp
│   │   │   ├── RhythmMachine.h, RhythmMachine.cpp
│   │   │   ├── RhythmVoiceDsp.h, RhythmVoiceDsp.cpp
│   │   │   ├── Sampler.h, Sampler.cpp
│   │   │   ├── Synth.h, Synth.cpp
│   │   │   ├── VoltSynth.h, VoltSynth.cpp
│   │   │   └── WavetableSynth.h, WavetableSynth.cpp
│   │   └── midi_fx/                             # 6 MIDI Effects (.h + .cpp each)
│   │       ├── Arpeggiator.h, Arpeggiator.cpp
│   │       ├── MidiChord.h, MidiChord.cpp
│   │       ├── MidiNoteLength.h, MidiNoteLength.cpp
│   │       ├── MidiRandom.h, MidiRandom.cpp
│   │       ├── MidiScale.h, MidiScale.cpp
│   │       └── MidiVelocity.h, MidiVelocity.cpp
│   │
│   ├── engine/                                  # Core Engine, Graph, Track, Transport & Thread Pool
│   │   ├── Engine.h, Engine.cpp
│   │   ├── Engine_Render.cpp
│   │   ├── Engine_Tracks.cpp
│   │   ├── Engine_Session.cpp
│   │   ├── Engine_Devices.cpp
│   │   ├── Engine_Rack.cpp
│   │   ├── Engine_Automation.cpp
│   │   ├── Engine_Modulation.cpp
│   │   ├── Engine_Recording.cpp
│   │   ├── AudioThreadPool.h, AudioThreadPool.cpp
│   │   ├── Graph.h, Graph.cpp
│   │   ├── Track.h, Track.cpp
│   │   ├── Transport.h, Transport.cpp, TransportInfo.h
│   │   ├── CommandQueue.h
│   │   ├── SnapshotHistory.h
│   │   ├── SessionPlayer.h, SessionPlayer.cpp
│   │   ├── Audition.h, Audition.cpp
│   │   ├── Automation.h, Automation.cpp
│   │   ├── Modulation.h, Modulation.cpp
│   │   ├── MidiClip.h, MidiClip.cpp
│   │   ├── MidiController.h, MidiController.cpp
│   │   └── NoteExpression.h, NoteExpression.cpp
│   │
│   ├── audio_io/                                # Hardware Audio / MIDI / Gamepad I/O & Config
│   │   ├── AudioBackend.h
│   │   ├── AudioBackendFactory.h, AudioBackendFactory.cpp
│   │   ├── AudioInput.h
│   │   ├── AudioConfig.h
│   │   ├── MidiInput.h
│   │   ├── MidiConfig.h
│   │   ├── MpeInput.h, MpeInput.cpp
│   │   ├── GamepadInput.h
│   │   ├── NotaConfigFile.h, NotaConfigFile.cpp
│   │   ├── ConfigJson.cpp
│   │   ├── macos/
│   │   │   ├── CoreAudioBackend.h, CoreAudioBackend.mm
│   │   │   ├── CoreAudioInput.h, CoreAudioInput.mm
│   │   │   ├── AudioConfig.mm
│   │   │   ├── MidiInput.mm
│   │   │   ├── MidiConfig.mm
│   │   │   └── GamepadInput.mm
│   │   └── desktop/
│   │       ├── MiniaudioBackend.h, MiniaudioBackend.cpp
│   │       ├── MiniaudioInput.h, MiniaudioInput.cpp
│   │       ├── MiniaudioDevices.h
│   │       ├── AudioConfig_miniaudio.cpp
│   │       ├── miniaudio_impl.cpp
│   │       ├── nota_miniaudio.h
│   │       ├── MidiInput_rtmidi.cpp
│   │       ├── MidiConfig_rtmidi.cpp
│   │       └── GamepadInput_stub.cpp
│   │
│   └── media/                                   # Offline Decoding, Import, Warping & Analysis
│       ├── SampleBuffer.h, SampleBuffer.cpp
│       ├── AudioFile.h, AudioFile.cpp
│       ├── AudioImport.h, AudioImport.cpp
│       ├── Warp.h
│       ├── WarpStream.h, WarpStream.cpp
│       ├── TempoDetect.h, TempoDetect.cpp
│       └── SampleAnalysis.h, SampleAnalysis.cpp
│
└── pluginhost/                                  # JUCE Module (Separated into Scanner & Hosting)
    ├── bridge/
    │   ├── PluginHostBridge.h
    │   └── PluginCatalogBridge.h
    ├── scanner/                                 # Static lib `nota_pluginscanner` (Engine-independent)
    │   ├── PluginCatalog.h, PluginCatalog.cpp
    │   ├── OutOfProcessScanner.h, OutOfProcessScanner.cpp
    │   ├── abi_pluginscanner.cpp
    │   └── worker/                              # Executable `nota-scanworker`
    │       ├── PluginScannerWorker.h, PluginScannerWorker.cpp
    │       └── main.cpp
    └── hosting/                                 # Static lib `nota_pluginhost`
        ├── PluginHost.h, PluginHost.cpp
        ├── NotaPlayHead.h, NotaPlayHead.cpp
        ├── PluginEditorWindow.h, PluginEditorWindow.cpp
        ├── PluginInstrument.h, PluginInstrument.cpp
        └── PluginEffect.h, PluginEffect.cpp
```

## Complexity Tracking

No constitution violations. All changes strictly reinforce Principles I–VIII.
