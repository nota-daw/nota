---
description: "Task list for Phase 2 of nota-daw/nota#1050"
---

# Tasks: Shared SilenceGate, Built-in/Plugin Sleep Gating, and Active Submix Buses

**Input**: Design documents from `/specs/115-instrument-plugin-sleep-gate/`  
**Prerequisites**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/c-abi.md`, `quickstart.md`

## Phase 1: Foundational Abstractions (`SilenceGate` & `ActiveBusMask`)

- [x] T001 Create `dsp::SilenceGate` with lock-free epoch-guarded `wake()`, `beginBlock()`, `observe()`, `shouldSkip()`, `isSleeping()`, and `setNeverSleep()` in `src/native/nota.engine/src/dsp/core/SilenceGate.h`
- [x] T002 Create `RenderBusState` / `ActiveBusMask` with per-block activity tracking and dirty-only clearing helpers in `src/native/nota.engine/src/engine/RenderBusState.h`
- [x] T003 [P] Add `PluginSleepMode` (`Auto`, `Never`), `setPluginSleepMode`/`pluginSleepMode`, and `Instrument::needsIdleRender()` virtual contract in `src/native/nota.engine/src/devices/Device.h` and `src/native/nota.engine/src/devices/Instrument.h`

**Checkpoint**: Shared gating primitives and device base contracts ready.

---

## Phase 2: C ABI & Managed Boundary (`NotaPluginSleepMode`)

- [x] T004 Declare `NotaPluginSleepMode`, `nota_plugin_set_sleep_mode`, and `nota_plugin_sleep_mode` in `src/native/nota.engine/include/nota/nota_engine.h`
- [x] T005 Implement `Engine::setPluginSleepMode` / `Engine::pluginSleepMode` in `src/native/nota.engine/src/engine/Engine.h`, `Engine_Devices.cpp`, and C ABI bindings in `src/native/nota.engine/src/abi/nota_engine_device.cpp`
- [x] T006 [P] Expose `PluginSleepMode` on `EngineContracts.cs`, `IAudioEngine.cs`, `NativeMethods.Devices.cs`, and `NotaEngine.Devices.cs`

**Checkpoint**: Full native-to-managed sleep mode contract wired and compiling cleanly.

---

## Phase 3: Built-in Instruments & Racks Sleep Gating (US1)

- [x] T007 Migrate the 10 built-in instruments to `SilenceGate` (thread-safe `wake()` + early-out in `render()`, preserving sequencer/held-chord/snapshot reclamation guards):
  - `BassSynth.{h,cpp}`, `Consort.{h,cpp}`, `FluxSynth.{h,cpp}`, `Monolith.{h,cpp}`, `OperatorSynth.{h,cpp}`
  - `PendulumSynth.{h,cpp}`, `Pentad.{h,cpp}`, `Synth.{h,cpp}`, `VoltSynth.{h,cpp}`, `WavetableSynth.{h,cpp}`
- [x] T008 Override `needsIdleRender() => true` on `Twin`, `Keys`, `Mosaic`, and `RhythmMachine` (`Twin.h`, `Keys.h`, `Mosaic.h`, `RhythmMachine.h`) so their lightweight idle phase/pattern/preview clocks keep running cleanly
- [x] T009 Implement `hasTail()`, `isIdle()`, `wake()` across `RackDevice.{h,cpp}`, `RackInstrument.{h,cpp}`, `DrumRack.{h,cpp}`, and `MidiDevice::isIdle()` across MIDI effects (`Arpeggiator`, `MidiChord`, `MidiNoteLength`, `MidiRandom`, `MidiRack`), waking cleanly on structural chain updates, MIDI events, and param changes

**Checkpoint**: All 17 built-in instruments and all 3 racks support thread-safe idle sleep and instant wake.

---

## Phase 4: Hosted VST3 Sleep Gating (US2)

- [x] T010 Wire `dsp::SilenceGate` into `PluginEffect.{h,cpp}` checking main + sidechain input and output silence, respecting `plugin_->getTailLengthSeconds()` (>=0.25s hangover, infinite tail disables auto-sleep), waking on parameter/state/sample-rate changes and `PluginSleepMode::Never`
- [x] T011 Wire `dsp::SilenceGate` into `PluginInstrument.{h,cpp}` tracking held MIDI/GUI notes & sustain pedal + pending MIDI + `plugin_->getTailLengthSeconds()`, waking on note/CC/expression/program/param/state/sample-rate changes and `PluginSleepMode::Never`

**Checkpoint**: Hosted VST3 plugins auto-sleep when silent past tail + hangover and respect `PluginSleepMode.Never`.

---

## Phase 5: Engine Instrument Skip & Active Submix Buses (US1 + US3)

- [x] T012 Skip rendering settled idle instruments (when `!takesBlockMidi() && !needsIdleRender() && n == 0 && xn == 0 && an == 0 && !liveExprApplies`) in `Engine::playEvents` and route idle settled instrument tracks to `LeafPath::Skip` in `prepareLeafWork()` (`Engine_Render.cpp`)
- [x] T013 Integrate `RenderBusState` (`ActiveBusMask`) in `Engine.h` / `Engine.cpp` / `Engine_Render.cpp`: clear only previously active buses, mark active buses across Pass 1 leaves/sends and Pass 1.5 child groups, and skip silent `ChainRest::Settled` Group (`Pass 1.5`) & Return (`Pass 2`) buses

**Checkpoint**: End-to-end silent instrument, plugin, and submix-bus skipping operational and bit-identical across thread counts.

---

## Phase 6: Verification, Changelog & Benchmarks

- [x] T014 Add silence-gate and bus-mask smoke tests in `tests/Nota.SmokeTest/SilenceGateAndBusMaskTests.cs` (wiring into `Program.cs`) and extend `tests/Nota.SmokeTest/HostedRenderPoolTests.cs` for VST3 `PluginEffect`/`PluginInstrument` sleep, tail preservation, audio/MIDI wake, and `PluginSleepMode.Never`
- [x] T015 Update `CHANGELOG.md` `[Unreleased]` with Phase 2 summary
- [x] T016 Run `./scripts/build.sh`, `-- --perf`, `nota_idle_cpu`, and `./scripts/bench-engine.sh -r 3` comparing vs `develop` baseline; check off tasks (`/speckit-converge`)
