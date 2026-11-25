# Internal Engine Contract: `TimelineIndex` & `RenderRoutingPlan`

This phase modifies internal engine data structures (`Engine`, `Graph`, `Track`, `Automation`, `MidiController`, `RackCore`) and adds no new external C ABI or managed P/Invoke entry points. Existing C ABI contracts (`nota_engine.h`) preserve identical functional and sample outputs.

## 1. `TimelineIndex` Contract

- **`TimelineIndex::buildForTrack(const Track& t)`** (message thread, `publishRaw()`):
  - Indexes all `t.midiClips` and `t.clips` into immutable interval structures.
  - Computes per-track `maxNoteAbsOffsetMs` / padding bounds when `articulationTable` exists so shifted notes are never clipped out by beat queries.
- **`forEachMidiClipCandidate(winStartBeat, winEndBeat, seamBeatOrNeg, fn)`** (audio thread, `const`, zero allocation):
  - Visits matching clip indices (in deterministic ascending clip order via bounded stack or merged scan) covering `[winStartBeat, winEndBeat]` plus optional `seamBeat`.
- **`forEachMidiNoteInClip(clipIdx, winStartBeat, winEndBeat, seamBeatOrNeg, includeDeactivated, fn)`** (audio thread, `const`, zero allocation):
  - Visits matching note indices in ascending `originalIndex` order via interval prefix pruning + bounded bitmask/stack sort when `k <= 1024`, falling back cleanly to linear scan only if a single clip exceeds 1024 overlapping simultaneous candidate notes in one block.
- **`forEachAudioClipCandidate(winStartBeat, winEndBeat, minTempoBpm, spb, mapped, fn)`** (audio thread, `const`, zero allocation):
  - Visits matching `AudioClip` indices in ascending original `clipIdx` order covering the active audio block.

## 2. `RenderRoutingPlan` Contract

- **`RenderRoutingPlan::build(const Graph& g)`** (message thread, `publishRaw()`):
  - Populates sorted `trackIds`, `groups`, `returnCount`, `leafRoutes`, `monitorFeeds`, `automatedTracks`, `modulatorTracks`, `cvTracks`, `masterLanes`, `masterPanLane`.
  - Pre-resolves sidechain consumer track indices and Arp CC routes (`ccRoutePlans`) using `RackCore::liveGeneration()`.
- **`findTrackIndex(int32_t trackId) const noexcept`**:
  - Binary search on `tracksById_` ($O(\log T)$).
- **`resolveMidiSourceIndex(size_t trackIdx, int32_t liveMidiFromId) const noexcept`**:
  - Fast $O(1)$ match against `leafRoutes[trackIdx].expectedMidiFromId`, falling back to `findTrackIndex(liveMidiFromId)` if atomically mutated since publish.
- **Automation / CV Resolution Safety**:
  - Top-level pointers valid for the lifetime of `Graph`'s `RenderScope`.
  - Nested rack endpoints compare `rack->liveGeneration() == target.expectedRackGen`; if stale, invoke `resolveNested(*target.track, target.host, target.chainUid, target.slot)` before accessing the child parameter.
