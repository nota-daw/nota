# Research & Architectural Notes: Phase 2 (`SilenceGate` & Active Bus Skip)

## 1. Root Cause Findings from Codebase Audit

### Built-in Instruments (10 of 17 Missing Early-Out)
- **Already early-outing (`7`)**: `GrainSynth`, `PhysicalSynth`, `Sampler`, `Mosaic`, `RhythmMachine`, `Keys`, `Twin`.
  - Note: `Twin` updates `tFrames_` and `patLane_.rendered(...)` during idle; `Keys` advances tremor/vibrato phase. Both need `needsIdleRender() => true` so `Engine_Render.cpp` lets them run their cheap idle block clock instead of skipping into `LeafPath::Skip`.
- **Computing `idle_` without early-out (`10`)**: `BassSynth`, `Consort`, `FluxSynth`, `Monolith`, `OperatorSynth`, `PendulumSynth`, `Pentad`, `Synth`, `VoltSynth`, `WavetableSynth`.
  - All 10 set `idle_` at the bottom of `render()` expecting `Engine` to inspect `isIdle()`, yet `Engine_Render.cpp` never checked `inst.isIdle()` in either `playEvents()` or `prepareLeafWork()`.
  - Furthermore, their `bool idle_` fields were plain non-atomic booleans mutated concurrently by `wake()` on UI/message threads (`pluginParamSet`, `setSampleRate`, `setState`) and `render()` on the audio thread.

### Racks (`RackDevice`, `RackInstrument`, `DrumRack`)
- None of the three rack classes override `hasTail()` or `isIdle()`.
- `RackCore` manages chains containing MIDI FX (`midiDevices`), an optional `instrument`, and `devices`. A rack can report `isIdle() == true` only when every non-bypassed chain element reports `isIdle()`, `mono_.glideLeft == 0`, no chain MIDI FX holds/arpeggiates active notes, and no retired snapshots wait for reclamation.

### Hosted Plugins (`PluginInstrument`, `PluginEffect`)
- Both hardcode `bool isIdle() const override { return false; }` and `void wake() override {}`.
- `PluginEffect` must treat both main input (`buf`) and optional sidechain input (`scBuf`) plus plugin output tails cleanly, using `SilenceGate` with hangover derived from `plugin_->getTailLengthSeconds()`.
- `PluginInstrument` receives MIDI via `noteOn`/`noteOff`/`controller`/`programChange`/`noteExpression`, editor keyboard notes via `guiNotes_`, and host state/param changes. Tracking held MIDI notes (128-bit mask / count + sustain pedal state) keeps the gate awake whenever keys or sustain are held even if audio output momentarily pauses between envelope stages or delays.
- Some third-party plugins generate self-oscillating noise or free-running FX without incoming MIDI/audio or misreport `getTailLengthSeconds()`. Exposing `NotaPluginSleepMode` (`Auto = 0`, `Never = 1`) through the C ABI gives users and automation a clean per-device opt-out matching `device_index == -1` (instrument) / `>= 0` (effect).

### Group & Return Buses (`Engine_Render.cpp`)
- `returnBus_` and `groupBus_` are unconditionally zero-filled up front (`Engine_Render.cpp:3132, 3186`) and processed through `copy_n -> devices -> pdc -> PanStage -> meter (sqrt)` (`Pass 1.5` & `Pass 2`), even on tracks where zero upstream leaves/sends/child groups contributed signal and `ChainRest::Settled` holds.
- Because Pass 1 leaves run across worker threads and Pass 1.5 groups run topologically across level barriers, tracking active buses requires thread-safe conflict-free marking during parallel passes and clean clearing between passes. `ActiveBusMask` (`std::array<std::atomic<uint8_t>, kMaxGroups/kMaxReturns>`) avoids false sharing / read-modify-write races: workers only perform atomic relaxed byte stores `active[slot].store(1, relaxed)`, and barriers join before Pass 1.5 / Pass 2 reads! Zero locks, zero CAS loops, zero heap allocations.

## 2. Design Decisions

1. **Epoch-guarded lock-free `dsp::SilenceGate`**:
   - Stores `std::atomic<uint32_t> wakeEpoch_`, `std::atomic<uint32_t> seenEpoch_`, `std::atomic<bool> sleeping_`, `std::atomic<bool> neverSleep_`.
   - `wake()` bumps `wakeEpoch_` and stores `sleeping_ = false` with `release`.
   - `beginBlock()` captures `startEpoch` before DSP; `observe(stillActive, frames, startEpoch)` refuses to enter sleep if `wakeEpoch_ != startEpoch`.
   - Eliminates `wake()` vs `render()` data races across all 10 built-in synths and hosted plugins.
2. **Thread-safe `ActiveBusMask`**:
   - Preallocated fixed arrays of per-slot `std::atomic<uint8_t>` (`group_[kMaxGroups]`, `ret_[kMaxReturns]`) inside `RenderBusState`.
   - Wait-free single-byte relaxed store on signal contribution (`markGroup`, `markReturn`). Because `RenderPool` executes memory barriers before Pass 1.5 level transitions and Pass 2, relaxed stores are 100% visible after each join barrier with zero contention on the hot path.
   - Inactive buses skip clearing the previous block only when they were already inactive (`clearIfActive`), OR clear only dirty buses! Better yet: Track `active` from *this* block AND clear buffers on first activation or before Pass 1 for buses that were active previously (`wasActive`), saving `std::fill_n` on permanently silent buses too!
     Wait: if workers write `+=` to `groupBus_[slot]` / `returnBus_[b]`, any bus written in the current block MUST already start zeroed before workers `+=` to it! Clearing all `kMaxGroups`/`kMaxReturns` buffers whose `prevActive_[i] != 0` at start of `mixGraph()` guarantees every active bus starts at zero at cost `0` for unused buses!
