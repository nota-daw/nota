# Implementation Plan: Phase 3 — `TimelineIndex` & `RenderRoutingPlan` (`#1050`)

**Branch**: `perf/timeline-index-routing-plan` | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/116-timeline-index-routing-plan/spec.md`

## Summary

Eliminate $O(N)$ per-block full-timeline clip/note/breakpoint scans and $O(T^2)$ per-block track/routing/rack lookups by introducing two small, human-readable engine abstractions built off the audio thread in `Engine::publishRaw()`:
1. `TimelineIndex` (`src/native/nota.engine/src/engine/TimelineIndex.{h,cpp}`): per-track immutable interval indices for MIDI clips, MIDI notes (prefix max-end pruning with offset padding & deterministic original ordering), and Audio clips (handling warped and unwarped/varispeed durations), paired with $O(\log P)$ binary search upgrades in `AutomationLane::valueAt()`, `ControllerCurve::movesIn()`, and `clipCtlEvents()`.
2. `RenderRoutingPlan` (`src/native/nota.engine/src/engine/RenderRoutingPlan.{h,cpp}`): per-graph precompiled routing plan holding $O(\log T)$ ID directory, Group/Return hierarchy and ancestry chains, MIDI source indices (with lock-free change validation), sidechain consumer routing cache, and compiled top-level + generation-guarded nested rack endpoints for automation lanes, CV modulation links, and Arp CC routes.

## Technical Context

**Language/Version**: C++20 (native engine `nota.engine`), C# / .NET 10 (`Nota.SmokeTest`)  
**Storage**: Immutable snapshot structures (`Graph`, `Track`, `RackCore::RackState`) + lock-free atomics (`AR-4`–`AR-8`)  
**Testing**: `./scripts/build.sh` (`Nota.SmokeTest`, including `TimelineIndexAndRoutingTests.cs`), `-- --perf`, `tests/perf/nota_idle_cpu.cpp`, `./scripts/bench-engine.sh -r 3`, plus dedicated scaling microbenchmark  
**Target Platform**: macOS universal (`arm64` + `x86_64`), Linux, Windows  
**Constraints**: Zero allocations/locks/blocking on audio thread (`AR-6`), 1 vs N thread bit-identity (`RenderPoolTests`), 2-line AGPL SPDX headers on all new files, hard readability gate (`#1050`)

## Constitution Check

*GATE: Passed pre-design and re-checked post-design.*

- **Rule I (Real-Time First / AR-6)**: All index/plan heap allocations happen inside `publishRaw()` on the message thread. Query helpers iterate pre-sorted spans using fixed stack bitsets/arrays (`<= 1024`) on the audio thread — zero heap allocations, zero locks.
- **Rule II (Readable Abstractions / `#1050`)**: `Engine_Render.cpp`, `Engine_Automation.cpp`, and `Engine_Modulation.cpp` become *smaller* and clearer by delegating timeline windowing to `TimelineIndex` and routing/target lookup to `RenderRoutingPlan`.
- **Rule III (Determinism)**: `1` vs `N` thread output remains bit-identical (`RenderPoolTests`) because overlapping candidate indices are re-emitted in strictly ascending `(originalClipIndex, originalNoteIndex)` order before sorting/accumulation.
- **Rule IV (Safe Concurrent Mutations)**: Lock-free `setMidiFromTrackId()` and asynchronous `RackCore::commit()` snapshot updates cannot leave dangling pointers or missed events due to cached-value + `RackState::generation` guards.

## Project Structure

### Documentation (this feature)

```text
specs/116-timeline-index-routing-plan/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── internal-engine.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code

```text
src/native/nota.engine/
├── CMakeLists.txt                                      (register TimelineIndex.cpp, RenderRoutingPlan.cpp)
└── src/
    ├── devices/racks/RackCore.h                        (RackState::generation monotonic counter)
    └── engine/
        ├── TimelineIndex.{h,cpp}                       (NEW)
        ├── RenderRoutingPlan.{h,cpp}                   (NEW)
        ├── Track.h                                     (std::shared_ptr<const TimelineIndex> timelineIndex)
        ├── Graph.h                                     (std::shared_ptr<const RenderRoutingPlan> routingPlan)
        ├── Automation.cpp                              (O(log P) std::upper_bound in valueAt)
        ├── MidiController.cpp                          (O(log P) std::upper_bound in movesIn)
        ├── Engine.h / Engine.cpp                       (publishRaw index/plan reuse & build, routingEpoch_)
        ├── Engine_Devices.cpp                          (routingEpoch_ bump on recomputeRouting)
        ├── Engine_Render.cpp                           (TimelineIndex queries + RenderRoutingPlan in mixGraph/MIDI/CC)
        ├── Engine_Automation.cpp                       (compiled lane apply + master pan lane)
        └── Engine_Modulation.cpp                       (indexed modulators + compiled CV links)
tests/Nota.SmokeTest/
├── TimelineIndexAndRoutingTests.cs                    (NEW)
└── Program.cs                                          (wire TimelineIndexAndRoutingTests)
```
