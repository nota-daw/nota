# Feature Specification: Indexed Timeline Lookup (`TimelineIndex`) and Precompiled Graph Routing (`RenderRoutingPlan`)

**Feature Branch**: `perf/timeline-index-routing-plan`  
**Created**: 2026-11-24  
**Status**: Approved (`nota-daw/nota#1050`)  
**Input**: [GitHub Issue #1050](https://github.com/nota-daw/nota/issues/1050)

## Nota Context

- **Real-time audio**: Zero audio-thread allocations or locks (`AR-6`), lock-free immutable snapshot publication via `Engine::publishRaw()` / `SnapshotHistory<Graph>`, bit-identical output across 1 vs N threads (`RenderPoolTests`) and exact parity with legacy linear scans across tempo maps, track/articulation MIDI offsets, loop seams, chase notes, deactivated clips, and nested rack mutations.
- **Affected boundaries**:
  - `src/native/nota.engine/CMakeLists.txt`
  - `src/native/nota.engine/src/engine/TimelineIndex.{h,cpp}` (new domain abstraction)
  - `src/native/nota.engine/src/engine/RenderRoutingPlan.{h,cpp}` (new domain abstraction)
  - `src/native/nota.engine/src/engine/{Track.h,Graph.h,Automation.cpp,MidiController.cpp,Engine.h,Engine.cpp,Engine_Render.cpp,Engine_Automation.cpp,Engine_Modulation.cpp,Engine_Devices.cpp}`
  - `src/native/nota.engine/src/devices/racks/RackCore.h` (`RackState::generation` guard for lock-free nested rack endpoint validation)
  - `tests/Nota.SmokeTest/{TimelineIndexAndRoutingTests.cs,Program.cs}`
- **Hard Readability Gate (#1050)**: Decompose hot-path timeline scanning and routing resolution out of `Engine_Render.cpp`, `Engine_Automation.cpp`, and `Engine_Modulation.cpp` into small, domain-named `TimelineIndex` and `RenderRoutingPlan` modules with explicit window query methods rather than inline index arrays or ad-hoc binary searches scattered through `Engine`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Constant/Logarithmic Audio Block Clip & Note Lookup on Long Arrangements (Priority: P1)

As a producer working on a long arrangement (hundreds of audio clips, thousands of MIDI notes, overlapping clips, long automation curves, and recorded MIDI CC controllers), playback and offline bounce remain fast regardless of total clip/note/breakpoint count outside the current block window.

**Why this priority**: Currently `gatherInstrumentNotes()`, `renderAudioClipsRaw()`, `renderAudioClipsAt()`, `renderInstrumentRaw()` MIDI clip envelope loops, `AutomationLane::valueAt()`, `ControllerCurve::movesIn()`, and `clipCtlEvents()` linearly scan every clip, note, automation point, and CC event on every audio callback (`1.33 ms` at 64 frames).

**Independent Test**: Build a long timeline containing hundreds of audio clips, thousands of MIDI notes (including overlapping sustained notes, pickup notes, deactivated clips, track offsets, tempo changes, loop seams), dense `AutomationLane` breakpoints, and multi-thousand-point `ControllerCurve` lanes; compare audio output sample-for-sample (`maxDiff == 0` / bit-identical) against serial reference and verify near-constant per-block lookup cost as timeline size grows 10×.

**Acceptance Scenarios**:

1. **Given** a MIDI track with many clips and notes across a long song, **When** `gatherInstrumentNotes()` or MIDI clip envelope evaluation queries `[blockStart, blockStart + frames)` (and loop seam / chase edges), **Then** `TimelineIndex` returns only overlapping clips and notes in deterministic original order, preserving deactivated clip note-offs, track offsets, articulation offsets, chase notes, and MPE expressions identically.
2. **Given** an audio track with many unwarped, warped, reversed, and tempo-mapped audio clips, **When** `renderAudioClipsRaw()` / `renderAudioClipsAt()` renders a block, **Then** `TimelineIndex` visits only clips overlapping the rendered window while summing floating-point samples in identical clip order.
3. **Given** dense automation lanes and recorded MIDI CC curves, **When** `AutomationLane::valueAt()`, `ControllerCurve::movesIn()`, or `clipCtlEvents()` runs, **Then** lookup executes via $O(\log P)$ binary search with bit-identical output.

---

### User Story 2 - Precompiled Graph Routing, Sidechain Levels & Track ID Lookups (Priority: P1)

As a producer mixing a large 100+ track session with nested Groups, Return buses, MIDI-In track routing, live monitoring, and sidechain dependencies, the audio engine avoids $O(T^2)$ linear ID scans, per-block group depth walks, and repeated nested rack tree traversals on every audio buffer.

**Why this priority**: Currently `mixGraph()` scans `g->tracks` repeatedly for `slotOf(groupId)`, `leafMute()`, `leafSolo()`, `feedsMonitor()`, `renderInstrumentRaw()` MIDI source scanning, `applyLiveExpression()`, `feedMidiKey()`, and recomputes `leafKeyNeeds` + topological `kMaxKeyLevels` across every device and rack child on every block even when sidechain routing has not changed.

**Independent Test**: Render a 64+ track session combining nested groups (depth >= 3), return sends, MIDI-In forwarding, armed/monitoring tracks, and multi-stage sidechain keys (including rack children and dynamic MIDI route changes) on 0 and 4 worker threads; verify bit-identical output (`SequenceEqual`) and eliminate linear per-track lookups in `mixGraph()`.

**Acceptance Scenarios**:

1. **Given** a published `Graph`, **When** `publishRaw()` commits it, **Then** `RenderRoutingPlan` precomputes sorted `TrackId -> trackIndex` mapping ($O(\log T)$), parent group slots, effective ancestor group ancestry chains, return count, and default MIDI source indices off the audio thread.
2. **Given** live atomic changes that do not trigger `publishRaw()` (such as lock-free `Track::setMidiFromTrackId()`), **When** the audio thread resolves MIDI routing, **Then** `RenderRoutingPlan` takes its $O(1)$ pre-resolved slot when unchanged and falls back seamlessly to $O(\log T)$ `findTrackIndex()` when modified between publishes.
3. **Given** sidechain routing (`recomputeRouting()`), **When** routing has not changed across blocks and no relevant leaf transitioned skip state arbitrarily, **Then** pre-pass/key resolution skips redundant full-device/rack walks and avoids scanning absent sidechain slots on non-sidechained projects.

---

### User Story 3 - Compiled Automation, CV Modulation & Arp CC Endpoints (Priority: P2)

As a producer using modular CV links, LFOs, multi-track parameter modulation, and rack-nested parameter automation across a large track count, idle tracks with empty automation/modulators/CV cost zero loop overhead and active endpoints resolve directly without repeating linear track scans or `resolveNested()` chain searches three times per link.

**Why this priority**: Currently `applyAutomation()`, `updateModulators()`, `applyModulation()`, and `applyCcRoute()` scan every track in the graph and perform repeated `resolveNested()` rack searches (`targetValid`, `targetGet`, `targetSet` + dual `applyTrackLanes` passes) on every block.

**Independent Test**: Exercise track automation (Volume, Pan, DeviceParam, PluginParam, MidiDeviceParam, nested rack chains, master Pan/device lanes), cross-track CV modulation (Modulator + Param source -> top-level & rack targets, Add/Multiply/Override modes), and Arp CC routing while mutating rack chains live between graph publishes; verify identical output and zero dangling pointers.

**Acceptance Scenarios**:

1. **Given** a 100+ track project where only a subset of tracks carry automation lanes, modulators, or CV links, **When** `applyAutomation()`, `updateModulators()`, and `applyModulation()` execute, **Then** only indexed tracks carrying those components are visited.
2. **Given** automation lanes, CV links, and Arp CC routes targeting top-level devices or nested rack children, **When** `RackCore::RackState::generation` matches the compiled state, **Then** parameter reads/writes resolve in $O(1)$ without linear track or rack chain scans, falling back safely to live `resolveNested()` if a rack mutates its internal chain snapshot between graph publishes.

### Edge Cases

- Concurrent `RackCore` structural edits (adding/removing/swapping chains or child devices inside `RackInstrument`/`RackDevice`/`MidiRack`) publish a new `RackState` without calling `Engine::publishRaw()` — compiled nested targets MUST detect `RackState::generation` mismatch and safely resolve against the live `RackState` inside the active `RenderScope`.
- Lock-free `Track::setMidiFromTrackId()` updates `midiFromTrackId_` without calling `publishRaw()` — `RenderRoutingPlan` MUST check the live atomic against the cached source ID and resolve via $O(\log T)$ `findTrackIndex()` whenever it differs.
- Unwarped `AudioClip` effective duration depends on sample rate (`sr`) and `spb` or `TempoMap` (which can vary during export or tempo automation); `TimelineIndex` audio bounds MUST safely conservatively bound clips using immutable `sourceSampleRate`, `pitchRatio()`, `effectiveLength()`, and either warped beats or conservative max-beat span under the active block clock so no clip sample is ever skipped.
- Track/articulation MIDI offsets (`midiOffsetMs`, `articulationOffsetMs`) may shift notes earlier or later across block or clip boundaries (`shiftS`), including pickup notes starting before clip `0.0` (`#56`) and deactivated clips releasing hanging notes; `TimelineIndex` query windows MUST pad beat search ranges symmetrically by the track's max possible shift so all candidate notes are visited.
- Empty/zero-length/unsorted defensive inputs and loop seams (`seamOffs_`, `chaseNotes_`) MUST preserve bit-exact event ordering (`(clipIndex, noteIndex)`) and `AR-6` real-time constraints.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `nota::TimelineIndex` (`src/native/nota.engine/src/engine/TimelineIndex.{h,cpp}`) MUST build an immutable per-track spatial/interval index during `publishRaw()` covering MIDI clips (sorted by `startBeat` with prefix `maxEndBeat` and flag for clip volume envelopes), per-clip MIDI notes (sorted by `startBeat` with prefix `maxEndBeat` plus original note index ordering and track shift padding), and Audio clips (sorted by `startBeat` with prefix max end bounds handling both warped and unwarped/varispeed clips cleanly).
- **FR-002**: `TimelineIndex` MUST provide lock-free const query methods returning overlapping candidates in original deterministic index order without audio-thread heap allocations (`AR-6`).
- **FR-003**: `Engine::gatherInstrumentNotes()`, `Engine::renderInstrumentRaw()` MIDI clip envelope application, `Engine::renderAudioClipsRaw()`, `Engine::renderAudioClipsAt()`, and `Engine::clipCtlEvents()` MUST use `TimelineIndex` / binary search lookups instead of linear full-timeline scans, preserving sample/event parity across tempo maps, loop seams, chase notes, pickup notes, offsets, and deactivated clips.
- **FR-004**: `AutomationLane::valueAt()` (`Automation.cpp`) and `ControllerCurve::movesIn()` (`MidiController.cpp`) MUST use $O(\log P)$ binary search (`std::upper_bound`), eliminating $O(P)$ linear scans on dense automation and CC curves.
- **FR-005**: `nota::RenderRoutingPlan` (`src/native/nota.engine/src/engine/RenderRoutingPlan.{h,cpp}`) MUST be built on graph publication (`publishRaw()`) and held immutably on `Graph`, providing: sorted `trackId -> trackIndex` lookup ($O(\log T)$), precomputed Group/Return bus topology and ancestry chains, active index lists for monitoring/automation/modulators/CV links, and compiled parameter endpoints for automation lanes, CV links, and Arp CC routes.
- **FR-006**: `RackCore::RackState` MUST carry a monotonic `generation` counter incremented on every `RackCore::commit()` so compiled nested rack endpoints in `RenderRoutingPlan` verify snapshot currency in $O(1)$ without dangling pointers or ABA hazards.
- **FR-007**: `Engine_Render.cpp`, `Engine_Automation.cpp`, and `Engine_Modulation.cpp` MUST consume `RenderRoutingPlan` for group/parent slot lookups, effective mute/solo ancestry, MIDI-In source resolution (with atomic override guard), live expression routing, automated/modulated track dispatch, single-pass CV apply/read/write, Arp CC routing, and routing-epoch cached sidechain dependency propagation.
- **FR-008**: Every new header/source/test file MUST follow the `#1050` readability gate, carry 2-line `SPDX-License-Identifier: AGPL-3.0-only` headers, allocate zero memory on the audio thread (`AR-6`), and keep 1-thread vs N-thread output 100% bit-identical (`RenderPoolTests`).

### Success Criteria

- **SC-001**: `./scripts/build.sh` compiles cleanly across `arm64 + x86_64`, passes `SCANNER SELFTEST PASSED`, and passes the full `Nota.SmokeTest` suite (`SMOKE TEST PASSED`, including `TimelineIndexAndRoutingTests`).
- **SC-002**: `dotnet run --project tests/Nota.SmokeTest --no-build -c Release -- --perf` exits with `PERF PASSED`, and `nota_idle_cpu` passes (`<= 5.0%`, expected `0.0%`).
- **SC-003**: A targeted C++ Phase 3 benchmark verifies sub-linear / constant scaling on long multi-thousand-note / multi-hundred-clip arrangements, dense automation curves, and 128+ track routing/group/modulation graphs compared against the Phase 2 baseline, while `./scripts/bench-engine.sh -r 3` shows zero regressions (and gains on heavy/keyed multi-track scenarios) vs `develop` and Phase 2.
