# Research Notes: Phase 3 — `TimelineIndex` & `RenderRoutingPlan`

## 1. Timeline Hotspots Identified in Current Codebase

1. `Engine::gatherInstrumentNotes()` (`Engine_Render.cpp:1668-1746`):
   - Iterates over every `MidiClip` in `t.midiClips` and every `Note` in `clip.notes` on every audio block, even when 99% of clips and notes lie far outside `[blockStart, blockStart + frames]`.
   - Calls `clipCtlEvents()`, which loops linearly over all `c.points` inside every `ControllerCurve` (`L477`).
   - Loops again over all `t.midiClips` inside `Engine::renderInstrumentRaw()` (`L2118-2133`) for `volumeEnvelope` scaling.
2. `Engine::renderAudioClipsRaw()` / `Engine::renderAudioClipsAt()` (`Engine_Render.cpp:2290-2308`):
   - Iterates linearly over every `AudioClip` in `t.clips`, evaluating `TempoMap`/envelope setups even for inactive/out-of-window clips.
3. `AutomationLane::valueAt()` (`Automation.cpp:10-25`):
   - Uses a linear `for (size_t i = 1; i < p.size(); ++i)` loop instead of `std::upper_bound`, making every block automation read and **every sample** of audio/MIDI clip `volumeEnvelope` / `panEnvelope` scale linearly in breakpoint count!
4. `ControllerCurve::movesIn()` (`MidiController.cpp:24-29`):
   - Linearly scans `points` from index `1` instead of binary searching past `b0`.

## 2. Routing & Resolution Hotspots Identified in Current Codebase

1. `Engine::mixGraph()` (`Engine_Render.cpp:3195-3282`, `3515-3548`, `3763`, `3836-3920`):
   - Linearly enumerates `Group` tracks, builds local `slotOf(id)` lambdas that linearly scan `grpId[0..numGroups-1]` for every group ancestor walk, every leaf `leafMute`/`leafSolo`, and every `destSlot`.
   - Linearly scans `g->tracks` on every block in `applyAutomation()` (even empty tracks), `updateModulators()`, and `applyModulation()` (even tracks with zero modulators/CV links).
2. `Engine::renderInstrumentRaw()`, `applyLiveExpression()`, `feedMidiKey()`, and `instrumentHasBlockInput()`:
   - Linearly search `g->tracks` comparing `p->id() == srcId` every block whenever a track listens to another track's MIDI or sidechain.
3. `Engine_Automation.cpp` (`applyTrackLanes`) & `Engine_Modulation.cpp` (`applyModulation`):
   - Re-run `resolveNested()` twice per nested automation lane and three times per CV link (`targetValid`, `targetGet`, `targetSet`), plus linear `t.modulators` ID scans and `g->tracks` scans for cross-track CV targets.
4. `Engine::recomputeRouting()` & sidechain `leafKeyNeeds`:
   - Can notify a monotonic `routingEpoch_` so blocks without routing changes skip rebuilding invariant route lookups when leaf activity allows, while still tracking `LeafPath::Skip` / `slotLeaf` safely per block.

## 3. Concurrency & Invariant Design Choices

- **Rack mutations without `publishRaw()`**: `RackCore::commit()` publishes `RackState` into its own `SnapshotHistory<RackState>` independently of `Engine::publishRaw()`. Therefore, any compiled parameter pointer to a child inside a rack MUST validate `RackState::generation` (incremented under message thread lock before `states_.publish`) against the generation observed at compile time, falling back gracefully to `resolveNested()` if mutated between graph publishes.
- **Lock-free `Track::setMidiFromTrackId()`**: `Engine_Recording.cpp:613` writes `midiFromTrackId_` atomically on the authoring/audio track without republishing `Graph`. Therefore `RenderRoutingPlan` caches `(expectedMidiFromId, resolvedIndex)` and checks `t.midiFromTrackId() == entry.expectedMidiFromId` ($O(1)$ fast path), falling back to $O(\log T)$ `findTrackIndex(liveId)` on live updates.
- **Interval indexing (`maxEnd` prefix + `startBeat` sort) with order preservation**:
  - Notes and clips can overlap arbitrarily and vary in length. Storing items sorted by `startBeat` paired with prefix max end bounds (`prefixMaxEnd`) lets a binary search bound the upper end (`startBeat <= winEnd`) and binary/block skip any prefix whose `prefixMaxEnd < winStart` (or scan active candidates from an augmented binary tree / block-skip table in $O(\log N + k)$).
  - Using small stack buffers or bitset/index merge sorted by `(originalClipIndex, originalNoteIndex)` (with graceful fallback when overlapping active count exceeds stack capacity, e.g. >1024 stacked notes) guarantees **exact bit-for-bit ordering** identical to linear traversal with zero heap allocation on the audio thread (`AR-6`).
- **Unwarped `AudioClip` bounds under dynamic sample rates / tempo maps**:
  - For warped clips, beat span `[startBeat, startBeat + warpPlayLen()]` (plus small rounding margin `0.01` beats) is sample-rate invariant.
  - For unwarped clips (`!warpEnabled`), device duration scales as `effectiveLength() / (sourceSampleRate * pitchRatio())` seconds. Storing both `startBeat` and `unwarpedSec` (and computing `blockMaxEndBeat` cleanly using `clock.beatAtSamples(...)` for the current block, or minimum BPM lower bound across the `TempoMap` + current `spb`) guarantees conservative upper/lower beat bounds with zero risk of missing a clip across export sample-rate switches or tempo automation jumps.
