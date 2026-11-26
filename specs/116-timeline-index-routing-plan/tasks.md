---
description: "Task list for Phase 3 of nota-daw/nota#1050"
---

# Tasks: Indexed Timeline Lookup (`TimelineIndex`) & Precompiled Routing (`RenderRoutingPlan`)

**Input**: Design documents from `/specs/116-timeline-index-routing-plan/`  
**Prerequisites**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/internal-engine.md`, `quickstart.md`

## Phase 1: Binary-Search Curve Lookups & Snapshot Guards

- [x] T001 [P] Upgrade `AutomationLane::valueAt()` (`src/native/nota.engine/src/engine/Automation.cpp`) and `ControllerCurve::movesIn()` (`src/native/nota.engine/src/engine/MidiController.cpp`) to `std::upper_bound` (`O(log P)`)
- [x] T002 [P] Add monotonic `uint64_t generation` stamped per commit to `RackCore::RackState` (`src/native/nota.engine/src/devices/racks/RackCore.h`) for lock-free compiled endpoint validation

## Phase 2: Core Abstractions (`TimelineIndex` & `RenderRoutingPlan`)

- [x] T003 Create `nota::TimelineIndex` (`src/native/nota.engine/src/engine/TimelineIndex.h` & `.cpp`) with interval/prefix-max pruning for MIDI clips, MIDI notes (offset-padded, sorted + deterministic index ordering), and Audio clips (warped + unwarped conservative bounds)
- [x] T004 Create `nota::RenderRoutingPlan` (`src/native/nota.engine/src/engine/RenderRoutingPlan.h` & `.cpp`) with `findTrackIndex()` (`O(log T)`), Group/Return ancestry slots, MIDI-In fast cache, active track lists (monitor/automation/modulators/CV/sidechain), and generation-guarded `CompiledParamTarget` endpoints
- [x] T005 Register `TimelineIndex.cpp` & `RenderRoutingPlan.cpp` in `src/native/nota.engine/CMakeLists.txt`, add `timelineIndex` on `Track.h`, `routingPlan` on `Graph.h`, and build/reuse them in `Engine::publishRaw()` (`Engine.cpp`) + bump `routingEpoch_` in `Engine_Devices.cpp`

## Phase 3: Engine Render Integration (US1 + US2 + US3)

- [x] T006 Wire `TimelineIndex` into `Engine_Render.cpp`: `gatherInstrumentNotes()`, `clipCtlEvents()` lower-bound point scan, `renderInstrumentRaw()` MIDI clip volume envelopes, `renderAudioClipsRaw()`, and `renderAudioClipsAt()`
- [x] T007 Wire `RenderRoutingPlan` into `Engine_Render.cpp`: `mixGraph()` Group/Return setup & `leafMute`/`leafSolo`/`destSlot`/`pslot`, `feedsMonitor()`, `renderInstrumentRaw()` MIDI-In source & live expression routing, `feedMidiKey()`, `applyMidiCcRouting()`, and sidechain `routingEpoch_` level cache
- [x] T008 Wire `RenderRoutingPlan` compiled targets into `Engine_Automation.cpp` (`applyAutomation`, `applyMasterLanes`, `masterPanLane`) and `Engine_Modulation.cpp` (`updateModulators`, `applyModulation`) with safe `RackState::generation` fallback

## Phase 4: Verification, Tests, Changelog & Benchmarks

- [x] T009 Add `tests/Nota.SmokeTest/TimelineIndexAndRoutingTests.cs` (wired in `Program.cs`) verifying long-timeline MIDI/Audio/Automation/CC bit-identity, track offsets, loop seams, deactivated clips, lock-free MIDI rerouting, live nested-rack CV/automation mutations, and 0 vs 4 thread parity
- [x] T010 Update `CHANGELOG.md` `[Unreleased]` with Phase 3 entry
- [x] T011 Run `./scripts/build.sh`, `-- --perf`, `nota_idle_cpu`, Phase 3 scaling microbenchmark, and `./scripts/bench-engine.sh -r 3` comparing vs Phase 2 & `develop` baseline; check off tasks (`/speckit-converge`)
