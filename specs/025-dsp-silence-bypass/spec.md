# Feature Specification: DSP Silence Detection & Dynamic Sleep Bypass

**Feature Branch**: `025-dsp-silence-bypass`

**Created**: 2026-10-17

**Status**: Implemented

**Input**: User request: "Design and implement engine-wide silence detection and dynamic sleep bypass across the DSP to eliminate redundant audio processing." — implement global silence detection across the DSP engine, enabling tracks and effect devices to enter a low-overhead sleep/bypass state when inputs and tails are silent, avoiding redundant computation on blocks of zeros.

## Nota Context

**Feature area**:
- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind**:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules, especially AR-6 real-time audio thread restrictions),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR expectations),
[`BUILD.md`](../../BUILD.md), [`FEATURES.md`](../../FEATURES.md).

---

## User Scenarios & Testing

### User Story 1 - Track-Level Silence Bypass (Priority: P1)

A user works on a project with 30+ tracks (drums, synths, vocals, guitars, FX). Across any given section (intro, breakdown, verse), most tracks are not currently sounding (no clips playing on the arrangement timeline, no active synth voices, no live input monitoring). The engine detects that a track's signal generator produces digital silence and its insert device chain has finished decaying, allowing the engine to skip the entire insert chain processing and send bus additions for that track in that block.

**Why this priority**: In typical DAW arrangements, the majority of tracks are idle at any single moment. Skipping device chains on inactive tracks provides the largest single reduction in baseline CPU usage.

**Independent Test**: Create a project with 16 tracks with heavy effect chains (Eq, Compressor, Chamber, Delay). Play a section where only 2 tracks have active clips. Verify that the audio output is indistinguishable from processing all tracks, while CPU load is drastically lower.

**Acceptance Scenarios**:
1. **Given** an arrangement audio/instrument track with no active clips, no playing session slot, and no live input monitoring, **When** rendered during playback, **Then** the engine checks whether its device chain is idle/asleep; if idle, the engine skips device processing, sets track meters to zero, and avoids unnecessary send-bus summing.
2. **Given** a silent track, **When** an audio clip starts or a MIDI note triggers an instrument, **Then** the track instantly wakes up on the exact sample of onset without latency, dropped notes, or clicks.

---

### User Story 2 - Device-Level Auto-Sleep with Tail Hangover (Priority: P1)

Effects with memory and time-based tails (`Delay`, `Reverb`, `Chamber`, `Compressor`, `Ceiling`) monitor their input and output energy. When a track stops feeding audio to the effect, the device continues rendering its tail until the energy drops below a designated silence threshold (e.g. $< -100\text{ dBFS}$ / $10^{-5}$) for a safe hangover duration (e.g. 50–100 ms). Once the tail has decayed into silence, the device marks itself as asleep/idle and skips its inner DSP loop on subsequent silent blocks until new input arrives.

**Why this priority**: Time-based effects like reverb and delay are the most CPU-intensive native devices. Allowing them to naturally ring out and then sleep prevents continuous computation on trailing zeroes.

**Independent Test**: Render an audio impulse into a `Chamber` or `Delay` device followed by silence. Verify that the tail rings out completely to below $-100\text{ dBFS}$, transitions to idle, and upon a new impulse immediately produces the correct reverb/delay response without glitching.

**Acceptance Scenarios**:
1. **Given** an active `Chamber` or `Delay` device receiving audio, **When** input becomes silent, **Then** the device continues processing normally until output signal falls below $-100\text{ dBFS}$ for the hangover window.
2. **Given** a sleeping `Chamber` or `Delay` device, **When** incoming buffer is silent, **Then** `process()` returns immediately without running the FDN or delay line processing loop.
3. **Given** a sleeping `Chamber` or `Delay` device, **When** a non-silent audio block arrives, **Then** the device wakes up immediately and processes the incoming audio starting at sample 0.

---

### User Story 3 - Memoryless / Instant Silence Passthrough (Priority: P2)

Stateless or memoryless devices (`Eq`, `AutoPan`, `Utility`, `Amp`) have no acoustic tail. When the incoming stereo buffer is pure silence (all samples zero or below $10^{-6}$), running biquad filter equations, waveshapers, or panning formulas is mathematically guaranteed to produce silence. These devices immediately bypass processing on silent blocks.

**Why this priority**: Eliminates redundant per-block math on lightweight devices that sit in long chains before or after silent sources.

**Independent Test**: Feed a block of exact zeroes into `Eq`, `AutoPan`, and `Utility`. Verify that processing is bypassed and the buffer remains bit-identical zeroes.

**Acceptance Scenarios**:
1. **Given** an `Eq` or `AutoPan` device, **When** input buffer is detected as silent and internal filter states are decayed, **Then** processing is skipped.
2. **Given** an `Eq` device with active resonance, **When** input ceases, **Then** the filter state is allowed to settle to zero before entering bypass.

---

### User Story 4 - Real-Time Audio Safety & Perceptual Transparency (Priority: P1)

All silence detection and sleep-state transitions strictly follow Real-Time Audio Rule AR-6: zero heap allocations, zero mutexes or locks, zero blocking calls. The mechanism must be completely transparent to the user: no audible clicks, no truncated reverb tails, no missed attack transients, and full compatibility with freeze, bounce, and automation.

**Why this priority**: A silence optimization that cuts off tails or introduces clicks upon waking destroys professional audio fidelity.

**Independent Test**: Run full smoke test suite `tests/Nota.SmokeTest` and verify that all audio renders, bounces, and tests pass with 100% fidelity.

**Acceptance Scenarios**:
1. **Given** any track or device entering or exiting sleep state, **Then** zero heap allocations occur on the audio thread.
2. **Given** a quiet acoustic instrument decay, **Then** the tail is never abruptly truncated; sleep only engages when levels are safely below $-100\text{ dBFS}$.
3. **Given** offline bounce or export, **Then** rendered output with silence bypass is bit-identical or within $< 0.001\text{ dB}$ of continuous rendering.

---

### Edge Cases

- **Denormal signals**: Micro-residual denormal floating point values ($< 10^{-15}$) must be flushed to zero (FTZ/DAZ) so they do not falsely keep devices awake indefinitely.
- **Sidechain signals**: A device whose primary input is silent but whose sidechain input is active (e.g. a sidechained compressor ducking a reverb tail or sidechain keyed gate) must remain awake as long as the sidechain source has active signal.
- **DC Bias / Asymmetry**: Devices with DC bias (e.g. tube saturation in `Vintage`) must ensure DC-corrected output so baseline offset is not mistaken for live audio.
- **Extreme Reverb Decay / Freeze**: When `Chamber` or `Delay` has `freeze` enabled or infinite decay, the output never decays below threshold, so the device must correctly remain awake and never enter sleep.
- **Automated Parameter Changes**: If a user tweaks a knob (e.g. turning up dry/wet or changing filter frequency) on a sleeping device, the device must wake up if the parameter change generates sound or alters state.

---

## Requirements

### Functional Requirements

- **FR-001**: Engine MUST provide a fast, vector-friendly utility `isBufferSilent(const float* buf, int32_t frames, float threshold)` in `src/native/nota.engine/src/` to test whether a stereo audio buffer contains signal above a configurable threshold (default $10^{-5}$ / $-100\text{ dBFS}$).
- **FR-002**: `Device` base class in `Device.h` MUST expose virtual methods for sleep lifecycle:
  - `bool isIdle() const`: returns whether the device is currently sleeping and producing no tail.
  - `bool hasTail() const`: indicates whether the device has an acoustic/filter tail (e.g. `true` for reverb/delay, `false` for utility).
  - `void wake()`: explicitly wakes the device from idle.
- **FR-003**: Time-based native devices (`Delay`, `Reverb`, `Chamber`, `Compressor`, `Ceiling`) MUST maintain a tail hangover counter (in frames/blocks). When input is silent, they must continue processing until their output decays below $-100\text{ dBFS}$ for at least the hangover period (e.g. 50 ms), then mark themselves as idle.
- **FR-004**: Memoryless native devices (`Eq`, `AutoPan`, `Utility`, `Amp`) MUST bypass `process()` when the input buffer is silent and internal state is zero/cleared.
- **FR-005**: In `Engine_Render.cpp`, `Engine::mixGraph()` MUST evaluate track-level activity: if a track's audio generator is inactive and all devices in its chain are `isIdle()`, the track's insert processing loop, PDC, and send accumulation MUST be bypassed for that block.
- **FR-006**: In `Engine_Render.cpp`, master track and return tracks MUST support device sleep: when bus inputs are silent and devices are idle, redundant processing MUST be skipped.
- **FR-007**: When any device receives a buffer containing signal above threshold, it MUST wake up on sample 0 of that block without latency, attack smearing, or missing transients.
- **FR-008**: All silence detection routines MUST satisfy Real-Time Audio Rule AR-6 with zero heap allocations, zero locks, and no blocking calls in audio callbacks.

---

## Success Criteria

### Measurable Outcomes

- **SC-001**: Baseline CPU utilization on idle projects (playback rolling over empty arrangements or silent bars) reduced by at least 60% compared to continuous full-graph processing.
- **SC-002**: CPU utilization on large sessions (30+ tracks with 4–6 actively sounding) reduced by at least 30–50%.
- **SC-003**: Zero audible clicks, pops, or truncated tails across all wake/sleep transitions, verified by automated transient and tail decay smoke tests.
- **SC-004**: The full test suite (`scripts/build.sh` driving `Nota.SmokeTest`) passes with 100% success rate.
- **SC-005**: Strictly zero memory allocations or deallocations during sleep, wake, or steady-state rendering.

---

## Assumptions

- Silence threshold of $-100\text{ dBFS}$ ($10^{-5}$ amplitude) is safely below the audible threshold of 24-bit audio reproduction and ambient listening environments.
- A tail hangover time of 50–100 ms is sufficient to bridge inter-sample zeros and zero-crossings in low-frequency audio without premature sleep triggers.
- External hosted plugins (VST3/AU in `pluginhost/`) continue to use standard host silence flags (`ProcessData::processContext` / `kNoData`) where applicable without breaking ABI.
