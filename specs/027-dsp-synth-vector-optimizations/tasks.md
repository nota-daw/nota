# Tasks: Polyphonic Synth FastMath & SIMD Buffer Inspection Acceleration

**Feature**: `027-dsp-synth-vector-optimizations` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

---

## Phase 1: Setup & SIMD Primitives

**Purpose**: Core SIMD instruction primitives providing vector silence inspection, single-pass peak/energy accumulation, and clamping across ARM NEON and x86 SSE.

- [X] T001 Implement basic vector math utilities (`simd::clip`, `simd::scale`, `simd::add`, `simd::mul`, `simd::maxAbs`) in `src/native/nota.engine/src/Simd.h`
- [X] T002 Implement vectorized `peakAndSumSquares()` single-pass peak and energy accumulator in `src/native/nota.engine/src/AudioBufferUtils.h`
- [X] T003 Implement `simd::clip()` vector buffer clamp in `src/native/nota.engine/src/Simd.h`

---

## Phase 2: Foundational (AudioBufferUtils Vectorization)

**Purpose**: Modernize core buffer inspection tools used by all devices and tracks in the engine.

- [X] T004 Vectorize `isBufferSilent()` with 16-sample vector chunking and early breakout in `src/native/nota.engine/src/AudioBufferUtils.h`
- [X] T005 Vectorize `SilenceDetector::process()` using `peakAndSumSquares()` and cache `lastMaxPeak` in `src/native/nota.engine/src/AudioBufferUtils.h`

**Checkpoint**: Foundation ready — buffer inspection is vectorized across all architectures.

---

## Phase 3: User Story 1 - Polyphonic Synth Inner-Loop FastMath (Priority: P1) 🎯 MVP

**Goal**: Eliminate heavy `std::sin()` and `std::tanh()` transcendentals from the voice inner loops of all native synthesizers.

**Independent Test**: Render multi-voice chords across each synth; verify perceptual transparency ($< 0.05\text{ dB}$ deviation, harmonic SNR $> 65\text{ dB}$) and measurable CPU reduction.

### Implementation for User Story 1

- [X] T006 [P] [US1] Replace FM operator sine calculation with `fast_math::fastSin(ph)` and decimate `ringW_` atomic updates to block boundary in `src/native/nota.engine/src/OperatorSynth.h`
- [X] T007 [P] [US1] Hoist invariant Tube drive denominator, use `fast_math::fastTanh()`, and replace Fold drive, LFO, and chorus tap `std::sin()` in `src/native/nota.engine/src/WavetableSynth.h`
- [X] T008 [P] [US1] Replace additive harmonic partial calculations (`oscHarmonics`) and LFO with `fast_math::fastSin()` in `src/native/nota.engine/src/PendulumSynth.h`
- [X] T009 [P] [US1] Replace sine oscillator mode in `oscBlep()`, vibrato LFO, and modulation LFOs with `fast_math::fastSin()` in `src/native/nota.engine/src/VoltSynth.h`
- [X] T010 [P] [US1] Replace analog drift LFO, sub-oscillator, and drive saturation with `fast_math::fastSin()` and `fast_math::fastTanh()` in `src/native/nota.engine/src/FluxSynth.h`
- [X] T011 [P] [US1] Replace morph oscillator sine, sub-oscillator, LFO, and drive saturation with `fast_math::fastSin()` and `fast_math::fastTanh()` in `src/native/nota.engine/src/BassSynth.h`

**Checkpoint**: Polyphonic synthesizers operate with zero scalar `std::sin()` / `std::tanh()` calls in audio rate loops.

---

## Phase 4: User Story 2 - SIMD Buffer Silence Elimination (Priority: P1)

**Goal**: Accelerate track silence detection and eliminate redundant buffer scans across mixing passes.

**Independent Test**: Run playback with active and silent tracks; verify accurate silence gating, zero pops on activity onset, and reduced mixer CPU overhead.

### Implementation for User Story 2

- [X] T012 [US2] Eliminate redundant `isBufferSilent()` scans following `SilenceDetector::process()` for master, leaf, and return tracks in `src/native/nota.engine/src/Engine_Render.cpp`

**Checkpoint**: Track activity gating uses cached peak measurements without secondary memory passes.

---

## Phase 5: User Story 3 - Mixer Summing, Bus Routing & Master Safety Optimization (Priority: P2)

**Goal**: Accelerate track post-fader gain application, bus accumulation, route tap sends, pre-FX routing, and master safety limiting using SIMD vector instructions.

**Independent Test**: Multi-track project playback with group buses, sidechain route taps, return sends, and master fader movements maintains accurate peak/RMS metering, bit-exact summing, and safe suppression of non-finite floats.

### Implementation for User Story 3

- [X] T013 [US3] Optimize leaf track post-fader gain and stereo peak/RMS accumulation (`scaleStereoMeter`) in `src/native/nota.engine/src/AudioBufferUtils.h` and `src/native/nota.engine/src/Engine_Render.cpp` for static pan law
- [X] T014 [US3] Optimize group bus summing (`addStereoScaledMeter`) and return bus mixing (`addStereoScaledMeterMap`) in `src/native/nota.engine/src/AudioBufferUtils.h` and `src/native/nota.engine/src/Engine_Render.cpp` using vector scaling and accumulation
- [X] T018 [US3] Implement `addStereoScaled(float* dest, const float* src, int32_t frames, float gl, float gr)` in `src/native/nota.engine/src/AudioBufferUtils.h` and vectorize route tap accumulation in leaf, group submix, and return bus passes in `src/native/nota.engine/src/Engine_Render.cpp`
- [X] T019 [US3] Implement `processMasterBus(...)` in `src/native/nota.engine/src/AudioBufferUtils.h` with branchless NaN/Inf suppression, SIMD clipping, and vector stereo correlation, and replace scalar loop in `src/native/nota.engine/src/Engine_Render.cpp`
- [X] T020 [US3] Vectorize pre-FX route tap accumulation with `simd::add`, fix Pass 1 leaf summing typo `simd::add`, and optimize buffer zeroing in `src/native/nota.engine/src/Engine_Render.cpp`
- [X] T021 [US3] Add automated tests for `addStereoScaled`, `processMasterBus`, and mixer route taps in `tests/Nota.SmokeTest/DspOptimizationTests.cs`

**Checkpoint**: Mixer summing, route taps, and master safety net execute via streamlined vector operations.

---

## Phase 6: Polish & Verification

**Purpose**: Validation, automated regression coverage, and release documentation.

- [X] T015 [P] Add automated smoke tests covering polyphonic synth stability and silence gating / mixer summing in `tests/Nota.SmokeTest/DspOptimizationTests.cs`
- [X] T016 Run `scripts/build.sh` (or platform equivalent) until native build and smoke test suite are 100% green
- [X] T017 Update `CHANGELOG.md` under `[Unreleased]` with details of polyphonic synth FastMath and SIMD buffer inspection optimizations

---

## Dependencies & Execution Order

### Phase Dependencies
- **Phase 1 (Setup & SIMD Primitives)**: No dependencies — start immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1 completion.
- **Phase 3 (User Story 1 - Synths)**: Depends on Phase 2; tasks T006–T011 can be executed in parallel.
- **Phase 4 (User Story 2 - Buffer Scans)**: Depends on Phase 2.
- **Phase 5 (User Story 3 - Mixer)**: Depends on Phase 2.
- **Phase 6 (Polish & Verification)**: Depends on Phases 1–5.

### Parallel Execution Opportunities
- Tasks T006, T007, T008, T009, T010, T011 edit distinct synthesizer source files and can be executed concurrently.
- Task T015 can be drafted in parallel with engine edits.

---

## Implementation Strategy (MVP First)

1. Complete Phase 1 (SIMD primitives in `Simd.h`).
2. Complete Phase 2 (vectorize `AudioBufferUtils.h`).
3. Complete Phase 3 (FastMath replacements across polyphonic synths — MVP!).
4. Complete Phase 4 & 5 (mixer and buffer scan streamlining in `Engine_Render.cpp`).
5. Complete Phase 6 (run `./scripts/build.sh` and update `CHANGELOG.md`).
