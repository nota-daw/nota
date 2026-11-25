# Feature Specification: Shared SilenceGate, Built-in/Plugin Sleep Gating, and Active Submix Buses

**Feature Branch**: `perf/silence-gate-active-buses`  
**Created**: 2026-11-24  
**Status**: Approved (`nota-daw/nota#1050` — Phase 2)  
**Input**: [GitHub Issue #1050](https://github.com/nota-daw/nota/issues/1050) and [implementation_plan.md](file:///Users/rafal/.gemini/antigravity-cli/brain/106800d4-4fa8-4ee9-93f4-ec477e9904c6/implementation_plan.md)

## Nota Context

- **Real-time audio**: Lock-free sleep gating and bus activity tracking on the audio thread (`AR-6`), bit-identical across single- and multi-threaded rendering (`RenderPoolTests`).
- **Affected boundaries**:
  - `src/native/nota.engine/include/nota/nota_engine.h` & `src/abi/nota_engine_device.cpp`
  - `src/native/nota.engine/src/dsp/core/SilenceGate.h` (new shared abstraction)
  - `src/native/nota.engine/src/engine/RenderBusState.h` (new `ActiveBusMask` helper)
  - `src/native/nota.engine/src/devices/{Device,Instrument}.h` & `devices/{instruments,racks}/*`
  - `src/native/nota.engine/pluginhost/hosting/{PluginInstrument,PluginEffect}.{h,cpp}`
  - `src/native/nota.engine/src/engine/{Engine.h,Engine_Devices.cpp,Engine_Render.cpp}`
  - `src/managed/Nota.Application/Engine/{EngineContracts,IAudioEngine}.cs`
  - `src/managed/Nota.Infrastructure/Interop/{NativeMethods,NotaEngine}.Devices.cs`
- **Hard Readability Gate (#1050)**: Consolidate sleep-hangover and wake-request mechanics behind `dsp::SilenceGate`, keep bus activity flags in `ActiveBusMask` rather than inline bit-fiddling in `Engine_Render.cpp`, and expose human-readable `PluginSleepMode` (`Auto`, `Never`) terminology across the C ABI and `IAudioEngine`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Silent Built-in Instruments & Racks Stop Burning CPU (Priority: P1)

As a producer building a large project with many instrument tracks, silent tracks holding any of Nota's built-in synths or nested racks automatically enter sleep once all voices and effect tails have decayed, instead of executing empty block DSP and voice loops on every audio buffer.

**Why this priority**: Currently only 7 of 17 built-in instruments return early when `idle_`, the engine's `playEvents()` and `prepareLeafWork()` never consult `inst.isIdle()`, and `RackInstrument` / `DrumRack` / `RackDevice` never report `isIdle()`. Gating those 10 synths, racks, and engine dispatch removes massive idle CPU burn in large arrangements.

**Independent Test**: Load tracks with each of the 10 newly-gated built-in synths (`BassSynth`, `Consort`, `FluxSynth`, `Monolith`, `OperatorSynth`, `PendulumSynth`, `Pentad`, `Synth`, `VoltSynth`, `WavetableSynth`) plus `RackInstrument`, `DrumRack`, and `RackDevice`; render MIDI notes, verify identical output against single-thread / awake baselines while tails ring, let them settle past hangover, trigger notes/params again and confirm instant sample-accurate wake.

**Acceptance Scenarios**:

1. **Given** an instrument track holding a quiet built-in synth or rack with no incoming MIDI/automation/sidechain activity and all voices/tails finished, **When** audio blocks continue rendering past the hangover window, **Then** `isIdle()` transitions to `true`, `playEvents()` / `prepareLeafWork()` skips heavy DSP, and the track outputs exact zeros.
2. **Given** a sleeping built-in instrument or rack, **When** a MIDI note, sustain/expression/controller message, parameter change, preset state load, or transport edge arrives (from any thread), **Then** `wake()` takes effect immediately on the next audio block with zero dropped attack transients or stuck tails.
3. **Given** an instrument with an internal idle clock (`Twin` pattern lane / `Keys` LFO continuity) or active internal generator/sequencer (`Consort` `seqRun_`, `PendulumSynth` held chord), **When** playing or idling, **Then** active generators stay awake and clock-dependent synths advance their idle clock cleanly without glitches or snapshot leaks (`SnapshotHistory::RenderScope`).

---

### User Story 2 - Hosted VST3 Plugins Auto-Sleep with Tail Awareness & Opt-Out (Priority: P2)

As a producer running third-party VST3 instruments and effects, quiet hosted plugins automatically sleep after their declared tail length (`getTailLengthSeconds()`) and a safety hangover, while specific noise-generating or self-oscillating plugins can opt out via `PluginSleepMode.Never`.

**Why this priority**: Hosted VST3 plugins previously hardcoded `isIdle() => false`, keeping every VST3 track and every track carrying a VST3 effect permanently `ChainRest::Opaque` and burning full plugin CPU even when idle.

**Independent Test**: Using the real `Nota Render Probe` VST3 fixture in isolated `Nota.SmokeTest`, verify a hosted VST3 instrument and effect sleep once quiet past hangover, preserve tails and wake instantaneously on audio input, MIDI notes, editor keyboard notes, parameter changes, and state loads, and respect `PluginSleepMode.Never` / `Auto` through `IAudioEngine`.

**Acceptance Scenarios**:

1. **Given** a `PluginEffect` or `PluginInstrument` in `PluginSleepMode.Auto`, **When** input and output stay silent past `max(0.25s, min(tailSec, 10.0s))` (with finite tail and no active held notes/sustain/pending MIDI), **Then** `isIdle()` becomes `true` and the engine skips `processBlock`.
2. **Given** a sleeping `PluginEffect`, **When** non-silent main or sidechain audio arrives, or a plugin parameter / state changes, **Then** the effect wakes on that very block without losing the first sample of audio.
3. **Given** a hosted plugin whose sleep mode is set to `PluginSleepMode.Never` (or reporting an infinite tail `>= 1e6s`), **When** audio and MIDI are silent, **Then** `isIdle()` stays `false` and the plugin continues rendering every block.

---

### User Story 3 - Quiet Groups and Return Buses Skip Redundant Mixing & Chains (Priority: P3)

As a producer routing tracks through multiple group buses and return buses, idle groups and unused return buses avoid zero-buffer effect processing, PDC, panning, and RMS/peak metering every block.

**Why this priority**: Even after leaf tracks sleep, `Engine_Render.cpp` unconditionally zero-filled every `groupBus_` and `returnBus_`, copied zeroes into `scratch_`, and ran devices + panning + meter `sqrt` across silent buses.

**Independent Test**: Render a session with active and inactive group buses and return buses (with insert effects and sends); verify bit-identical mix output compared to serial/unskipped rendering and confirm inactive buses skip processing when no upstream leaf/send/child writes to them and their own chain is `ChainRest::Settled`.

**Acceptance Scenarios**:

1. **Given** a Group or Return bus that receives no non-silent audio from child leaves/child groups/track sends in the current block and whose own FX/PDC chain is settled and tail-quiet, **When** `mixGraph()` runs Pass 1.5 / Pass 2, **Then** the bus skips effect processing, pan/meter calculation, and upstream accumulation while resetting its meter atomics cleanly to zero.
2. **Given** multi-threaded worker rendering in Pass 1 and parallel Group levels in Pass 1.5, **When** any leaf/child/send contributes audio into a group or return bus, **Then** `ActiveBusMask` records bus activity without data races or missed upstream signals, yielding bit-identical output for `RenderThreads == 0` vs `N`.

### Edge Cases

- Concurrent `wake()` from the UI/message thread (parameter change, custom wavetable upload, state load) overlapping an audio thread block finishing `observe()` — epoch guard prevents the block end from overwriting a concurrent wake into sleep.
- `Twin` and `Keys` have cheap `needsIdleRender()` phase/clock continuity inside `render()` when idle — engine skips only when `!inst.needsIdleRender()`.
- `WavetableSynth`, `RackInstrument`, `DrumRack`, and `RackDevice` must never strand retired `SnapshotHistory` allocations when sleeping — structural edits wake the instrument or flush `RenderScope`.
- `RackInstrument` stays awake while chain MIDI FX (`MidiArp` etc.) hold notes or glide is active (`glideLeft > 0`), and `RackInstrument` / `DrumRack` / `RackDevice` only report `isIdle()` when every non-bypassed chain device / chain instrument is idle and the hangover settles.
- Group buses can feed parent Group buses and send to Return buses (`Pass 1.5 -> Pass 2`): an active child group must activate both its own group bus and propagate activity to its parent group / active return sends.
- `PluginEffect` with zero dry/wet mix or active sidechain input checks sidechain input silence (`scBuf`) when `hasSidechain_` is driven.
- `device_index == -1` addresses a track's instrument for `nota_plugin_set_sleep_mode` / `nota_plugin_sleep_mode`, matching `nota_track_set_device_bypassed`; out-of-range indices return `NOTA_ERR_INVALID_ARG` / `NOTA_PLUGIN_SLEEP_AUTO`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `nota::dsp::SilenceGate` MUST live in `src/native/nota.engine/src/dsp/core/SilenceGate.h`, provide lock-free `wake()` safe from any thread, audio-thread `observe()` / `shouldSkip()` / `isSleeping()`, configurable `hangoverSeconds`, release threshold alignment (`1e-5f`), and `setNeverSleep(bool)`.
- **FR-002**: Built-in instruments `BassSynth`, `Consort`, `FluxSynth`, `Monolith`, `OperatorSynth`, `PendulumSynth`, `Pentad`, `Synth`, `VoltSynth`, and `WavetableSynth` MUST early-out when idle via thread-safe gating without dropping tails, held notes, sustain, active sequencers/chords, or `SnapshotHistory` reclamation.
- **FR-003**: `Instrument::needsIdleRender()` MUST allow `Twin` and `Keys` to preserve their lightweight idle clock continuity while all other idle built-in and hosted instruments can be skipped cleanly by `Engine_Render.cpp`.
- **FR-004**: `RackDevice`, `RackInstrument`, and `DrumRack` MUST implement `hasTail()`, `isIdle()`, and `wake()`, delegating recursively across chain MIDI/audio devices and instruments and waking on structural or parameter edits.
- **FR-005**: `nota::hosting::PluginInstrument` and `PluginEffect` MUST use `dsp::SilenceGate` respecting `plugin_->getTailLengthSeconds()` (with a minimum `0.25s` safety hangover, capped auto hangover at `10.0s`, infinite tail disabling auto-sleep), tracking active held MIDI/GUI notes & sustain on `PluginInstrument`, waking on audio/sidechain/MIDI/param/state changes, and honoring `PluginSleepMode::Never`.
- **FR-006**: `nota_engine.h` MUST define `NotaPluginSleepMode` (`NOTA_PLUGIN_SLEEP_AUTO = 0`, `NOTA_PLUGIN_SLEEP_NEVER = 1`), `nota_plugin_set_sleep_mode`, and `nota_plugin_sleep_mode`, mirrored cleanly in `Engine`, `IAudioEngine`, `NativeMethods`, and `NotaEngine` using `DeviceSleepMode` / `PluginSleepMode` naming.
- **FR-007**: `Engine_Render.cpp` MUST skip rendering settled idle instruments in `playEvents()` (when `n == 0 && xn == 0 && an == 0 && !liveExprApplies && !takesBlockMidi() && !needsIdleRender()`) and in `prepareLeafWork()`, and MUST skip inactive `ChainRest::Settled` Group (`Pass 1.5`) and Return (`Pass 2`) buses using `ActiveBusMask` (`RenderBusState.h`) while keeping `1`- vs `N`-thread outputs sample-identical.
- **FR-008**: Every new header/test file MUST carry the two-line AGPL-3.0-only SPDX header and comments explaining *why* invariants exist.

### Success Criteria

- **SC-001**: `./scripts/build.sh` compiles cleanly and passes all existing tests plus new Phase 2 smoke tests (`Phase2SleepGateTests.cs` and extended `HostedRenderPoolTests` checks covering all 10 synths, racks, VST3 instrument/effect sleep & wake, `PluginSleepMode.Never`, and idle group/return buses).
- **SC-002**: `dotnet run --project tests/Nota.SmokeTest --no-build -c Release -- --perf` exits cleanly with `PERF PASSED`.
- **SC-003**: Idle multi-track projects containing the 10 newly-gated built-in synths, racks, silent VST3 tracks, and unused Group/Return buses show measurable reduction in render CPU / wall time vs the Phase 1 commit and `develop` baseline in `./scripts/bench-engine.sh -r 3`.
- **SC-004**: `RenderPoolTests` and hosted multi-thread vs single-thread regression tests remain 100% bit-identical (`SequenceEqual`).
