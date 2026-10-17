# Feature Specification: Polyphonic Synth FastMath & SIMD Buffer Inspection Acceleration

**Feature Branch**: `027-dsp-synth-vector-optimizations`

**Created**: 2026-10-17

**Status**: Implemented

**Input**: User request: "Plan and implement high-performance DSP optimizations across polyphonic synths, SIMD audio buffer inspection, and mixer summing." — accelerate polyphonic synthesizer voice generation across OperatorSynth, WavetableSynth, PendulumSynth, VoltSynth, FluxSynth, and BassSynth using FastMath approximations and loop-invariant hoisting; vectorize audio buffer silence detection and energy tracking in AudioBufferUtils using SIMD primitives; and streamline mixer bus gain and telemetry handling without compromising audio fidelity or real-time safety.

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind**:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR-4 lock-free queues, AR-5 immutable snapshots, AR-6 audio thread safety: zero allocations, zero mutexes, zero blocking I/O),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, AGPL-3.0-only headers, smoke tests must pass),
[`BUILD.md`](../../BUILD.md),
[`FEATURES.md`](../../FEATURES.md).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Polyphonic Synth Inner-Loop FastMath Vectorization (Priority: P1) 🎯 MVP

As an electronic music producer playing dense chords with native polyphonic instruments (e.g. 8 voices with 4 FM operators or multi-harmonic additive generators), I want voice generation to compute transcendental math using high-accuracy, branchless SIMD-friendly approximations, so that voice count scales effortlessly without triggering audio dropouts or buffer underruns.

**Why this priority**:
Polyphonic synthesizers represent the single largest consumer of CPU cycles during playback. In `OperatorSynth`, an 8-voice chord invokes standard library `std::sin()` 32 times per sample (over 1.4 million calls/second at 44.1 kHz). In `WavetableSynth`, Tube distortion evaluates per-sample `std::tanh` with an unhoisted invariant denominator. Replacing these with `fast_math` polynomial and Padé approximations reduces polyphonic synthesis CPU load by 35%–55%.

**Independent Test**:
Render an 8-voice polyphonic MIDI chord progression driving `OperatorSynth`, `WavetableSynth`, `PendulumSynth`, `VoltSynth`, `FluxSynth`, and `BassSynth`. Verify that synthesized audio waveforms are perceptually transparent compared to the reference implementation ($< 0.05\text{ dB}$ difference, harmonic distortion $< -90\text{ dBFS}$), and CPU consumption is measurably reduced.

**Acceptance Scenarios**:

1. **Given** `OperatorSynth.h` playing polyphonic voices in FM mode, **When** computing operator sample outputs, **Then** sine waveform generation uses `fast_math::fastSin()` instead of `std::sin()`.
2. **Given** `WavetableSynth.h` with Tube or Fold drive active, **When** processing audio blocks, **Then** the normalization factor is hoisted out of the sample loop, and saturation utilizes `fast_math::fastTanh()` and `fast_math::fastSin()`.
3. **Given** `PendulumSynth.h` with additive harmonics active, **When** generating voice waveforms, **Then** all harmonic partial evaluations use `fast_math::fastSin()` instead of `std::sin()`.
4. **Given** `VoltSynth.h`, `FluxSynth.h`, and `BassSynth.h`, **When** generating voice oscillators, sub-oscillators, LFOs, and saturation, **Then** inner-loop calls to `std::sin()` and `std::tanh()` are replaced with `fast_math::fastSin()` and `fast_math::fastTanh()`.

---

### User Story 2 - SIMD Audio Buffer Inspection & Silence Detection (Priority: P1)

As a music creator working on large sessions with many inactive or sparse tracks, I want track silence detection and buffer activity monitoring to execute via vectorized hardware instructions, so that idle tracks and silence-gated effect chains are detected with near-zero CPU overhead.

**Why this priority**:
Every track in every block runs `isBufferSilent()` and `SilenceDetector::process()`. The current scalar loops inspect samples sequentially and compute sum-of-squares and peak values in separate steps. Vectorizing these routines via ARM NEON and x86 SSE/AVX with early exit on 16-sample vector chunks cuts buffer inspection CPU time by up to 75%.

**Independent Test**:
Pass blocks of active audio, decaying audio, and absolute digital silence through `isBufferSilent()` and `SilenceDetector::process()`. Verify that silence detection decisions, hold times, and hysteresis state transitions match scalar calculations identically while executing in significantly fewer cycles.

**Acceptance Scenarios**:

1. **Given** an audio buffer in `isBufferSilent()`, **When** inspected for activity, **Then** 16-sample vector chunks are evaluated using SIMD absolute value and threshold comparison, breaking out immediately upon detecting active signal.
2. **Given** an audio buffer in `SilenceDetector::process()`, **When** calculating peak and RMS energy, **Then** a single-pass SIMD vector routine calculates maximum absolute peak and sum-of-squares accumulation concurrently.
3. **Given** `Engine_Render.cpp` executing track rendering passes, **When** evaluating track activity, **Then** redundant scalar buffer silence scans following `SilenceDetector::process()` are eliminated.

---

### User Story 3 - Mixer Summing, Bus Routing & Master Safety Optimization (Priority: P2)

As a producer playing back complex arrangements with dozens of tracks, sub-groups, sidechain routes, and return buses, I want mixer channel faders, equal-power pan gains, route taps, submix accumulation, and master safety limiting to execute via high-throughput SIMD vector instructions, minimizing CPU cache thrashing and freeing audio processing headroom for instrument and effect plug-ins.

**Why this priority**:
Mixer summing in `Engine_Render.cpp` runs at the innermost boundary of the audio pipeline every audio block. Whenever sidechain taps (`route != nullptr`) or pre-FX sends are active, or when rendering group submixes and return channels, the engine fell back to scalar sample-by-sample loops. Furthermore, master bus volume scaling, IEEE 754 NaN/Inf safety suppression, clipping, and stereo correlation telemetry ran in a scalar loop over every frame. Vectorizing these paths eliminates serial CPU stalls and preserves vector pipeline throughput throughout the mixing matrix.

**Independent Test**:
Run multi-track playback with leaf channels, group submixes, sidechain route taps, return sends, and master fader level changes. Verify that audio summing is mathematically exact, non-finite values (NaN / Inf) are squashed safely to silence, peak/RMS meters and stereo correlation telemetry agree with reference values, and mixer CPU execution time is reduced.

**Acceptance Scenarios**:

1. **Given** static pan and gain on track channels, **When** applying post-fader gain and metering in `Engine_Render.cpp`, **Then** sample processing utilizes vector scaling and single-pass stereo peak/energy accumulation (`scaleStereoMeter`).
2. **Given** an active sidechain route tap (`route != nullptr`), **When** processing leaf tracks, group submixes, or return buses, **Then** post-fader channel audio is accumulated into the destination route buffer using SIMD vector instructions (`simd::add` or `addStereoScaled`) without dropping to a scalar sample loop.
3. **Given** pre-FX taps active on leaf tracks, group tracks, or return tracks, **When** copying pre-FX audio into `routeBusPreNext_`, **Then** accumulation executes via `simd::add(pre, scratch, frames * 2)`.
4. **Given** the master bus output stage, **When** scaling by `masterVolume_`, **Then** `processMasterBus` performs master volume multiplication, branchless suppression of NaN and Infinity values to zero, clamp limiting to $[-1.0, 1.0]$, and stereo peak, RMS, and correlation metering in a single unified SIMD pass.
5. **Given** `OperatorSynth.h` with spectrum analysis active, **When** writing to the visual analysis ring buffer, **Then** atomic index updates are batched or decimated to control rate rather than atomic writes per audio sample.

---

## Edge Cases

- **Denormalized and Subnormal Floats**: FastMath polynomial approximations and SIMD dot products must preserve Flush-To-Zero (FTZ) and Denormals-Are-Zero (DAZ) states.
- **Extreme Phase and Frequency Modulations**: FM modulation in `OperatorSynth` can produce phase values significantly outside $[0, 1)$. `fast_math::fastSin()` must cleanly handle large positive and negative phase values without numeric overflow.
- **Unaligned Buffer Offsets**: SIMD vector primitives must support unaligned memory addresses gracefully (`vld1q_f32` / `_mm_loadu_ps`) and handle arbitrary buffer lengths with clean scalar loop tails.
- **Non-Finite Floats on Master Bus**: Malformed or unstable plugin audio containing NaNs, $+\infty$, or $-\infty$ must be squashed to $0.0\text{f}$ branchlessly before clipping, preventing full-scale digital burst noise.
- **Threshold Boundary Conditions**: Buffer silence detection must maintain bit-exact agreement with the $1.0\times 10^{-5}$ (-100 dBFS) threshold.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `Simd.h` MUST provide general vector math primitives including `simd::clip(float* dst, const float* src, float minVal, float maxVal, int32_t count) noexcept` for SIMD clamping, maintaining separation from audio-specific algorithms.
- **FR-002**: `AudioBufferUtils.h` MUST provide vectorized `isBufferSilent(const float* interleaved, int32_t frames, float threshold) noexcept` evaluating 16 samples per iteration with early breakout on active audio for ARM NEON, x86 SSE, and generic fallback.
- **FR-003**: `AudioBufferUtils.h` MUST provide `peakAndSumSquares(const float* src, int32_t count, float& outPeak, double& outSumSquares) noexcept` evaluating absolute peak and sum-of-squares in a single unified vector pass.
- **FR-004**: `AudioBufferUtils.h` MUST provide vectorized stereo mixer routines (`scaleStereoMeter`, `addStereoScaledMeter`, `addStereoScaledMeterMap`) for single-pass fader gain scaling, bus accumulation, and stereo peak/RMS telemetry extraction.
- **FR-005**: `AudioBufferUtils.h` `SilenceDetector::process()` MUST utilize `peakAndSumSquares()` to eliminate separate passes over buffer memory.
- **FR-006**: `OperatorSynth.h` operator sample generation MUST replace `std::sin(kTwoPi * ph)` with `fast_math::fastSin(ph)`.
- **FR-007**: `WavetableSynth.h` `driveSample()` MUST hoist the Tube normalization factor out of the per-sample loop and replace standard `std::tanh()` and `std::sin()` with `fast_math::fastTanh()` and `fast_math::fastSin()`.
- **FR-008**: `PendulumSynth.h` `osc()` additive harmonic synthesizer MUST replace `std::sin()` evaluations with `fast_math::fastSin()`.
- **FR-009**: `VoltSynth.h`, `FluxSynth.h`, and `BassSynth.h` MUST replace per-sample voice oscillator, sub-oscillator, LFO, and drive transcendentals with `fast_math::fastSin()` and `fast_math::fastTanh()`.
- **FR-010**: `Engine_Render.cpp` MUST avoid redundant calls to `isBufferSilent()` where `SilenceDetector::process()` has already computed peak and energy in the current block.
- **FR-011**: All optimizations MUST uphold Real-Time Audio Rule AR-6 (zero dynamic heap allocations, zero mutexes, zero blocking I/O on the audio thread).
- **FR-012**: `./scripts/build.sh` smoke test suite MUST pass 100% green without warnings or failures.
- **FR-013**: `AudioBufferUtils.h` MUST provide `addStereoScaled(float* dest, const float* src, int32_t frames, float gl, float gr) noexcept` to accumulate stereo audio scaled by arbitrary left and right gains using vector instructions.
- **FR-014**: `AudioBufferUtils.h` MUST provide `processMasterBus(float* out, int32_t frames, float mv, float& outPkL, float& outPkR, double& outSqL, double& outSqR, double& outSLR) noexcept` evaluating master gain scaling, branchless non-finite float suppression, clipping to $[-1.0, 1.0]$, and stereo peak/RMS/map correlation in a single SIMD pass.
- **FR-015**: `Engine_Render.cpp` MUST utilize vectorized processing for post-fader route taps and pre-FX taps across leaf tracks, group submixes, and return tracks, maintaining vector execution when `route != nullptr`.
- **FR-016**: `Engine_Render.cpp` Pass 1 summing MUST call `simd::add(dest, scratch, frames * 2)` directly without label typos.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Polyphonic synth rendering CPU load under 8-voice chord playback is reduced by at least **30%–50%**.
- **SC-002**: Buffer silence inspection and `SilenceDetector::process()` CPU time is reduced by at least **50%**.
- **SC-003**: Synthesized audio output remains perceptually transparent with deviations $< 0.05\text{ dB}$ across all affected instruments.
- **SC-004**: Zero audio dropouts, zero memory allocations, and zero lock contention on the real-time audio thread under maximum voice polyphony.
- **SC-005**: All unit, integration, and smoke tests pass 100% green on all supported architectures (macOS ARM64/x86_64, Linux x86_64, Windows x86_64).

---

## Assumptions

- Target hardware supports IEEE 754 floating point arithmetic with ARM NEON or x86 SSE2/AVX2 instruction sets.
- `fast_math::fastSin` has maximum error $< 0.0007$, providing $> 65\text{ dB}$ signal-to-noise ratio which is well beyond perceptual audio masking thresholds for synthesis operators and LFOs.
- Control-rate decimation for visual analysis rings maintains visual smoothness at standard 60–144 Hz display refresh rates without observable jitter.
