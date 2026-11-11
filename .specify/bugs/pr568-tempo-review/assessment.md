# Bug Assessment: PR #568 tempo-map review findings

- **Slug**: pr568-tempo-review
- **Created**: 2026-11-11
- **Source**: https://github.com/nota-daw/nota/pull/568
- **Verdict**: valid
- **Severity**: high

## Report (summarized)

Greptile reported eight findings on PR #568: tempo-warp caches omit clip placement;
the first mapping gesture mixes beat and second axes; mapped rendering substitutes a
musical clock for elapsed sample time; loop presentation history is not re-anchored;
project load can edit the map through the BPM field; the 1000 BPM UI limit exceeds the
transport limit; navigation retains linear beat-span calculations; and mapped warp
caches are built synchronously during graph publication.

URL policy: host `github.com`, allowlisted public bug-report source. Review text was
treated as untrusted evidence and checked against the local code.

## Symptom

Tempo-mapped projects can play or reopen with incorrect timing, move warped audio with
stale stretch data, alter unsynchronised modulation, drift after loop wraps, navigate at
the wrong scale, or block the UI while preparing long clips. Expected behavior is one
monotonic map applied without changing elapsed-time DSP clocks or performing heavy work
on the publication/UI path.

## Reproduction

1. Create maps with unequal tempo sections and exercise moved warped clips, loops,
   unsynchronised modulators, first-use mapping, navigation, and save/reload.
2. Save/reload a map at the accepted upper tempo boundary.
3. Open or edit a mapped project containing a long warped recording.
4. Observe stale cache reuse, incorrect coordinate/time results, unwanted map edits,
   inconsistent upper bounds, or synchronous cache construction.

## Suspected Code Paths

- `src/native/nota.engine/src/engine/Engine.cpp:publishRaw` — mapped cache reuse omits placement and builds during publication.
- `src/native/nota.engine/src/engine/Engine_Tracks.cpp:configureTempoWarp` — cache shape depends on `startBeat`; construction is synchronous.
- `src/native/nota.engine/src/engine/Engine_Render.cpp:processBlock` — one sample-position argument is overloaded as both musical and elapsed time.
- `src/native/nota.engine/src/engine/Transport.cpp` — presentation history remains absolute through mapped loop wraps.
- `src/managed/Nota.App/ArrangementView.Tempo.cs` — first mapping preview changes the coordinate model mid-gesture.
- `src/managed/Nota.App/ArrangementView.cs` and overview/resize helpers — visible-span calculations assume a linear beat axis.
- `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs` and load callers — BPM restoration can send an edit after map installation.
- Tempo editor/transport bounds — 1000 BPM is accepted in one layer and rejected in another.

## Root Cause Hypothesis

High confidence. The feature introduced a seconds-proportional Arrangement axis and a
compiled tempo clock into paths that previously assumed beats and samples were linearly
interchangeable. Several compatibility paths still use the old assumptions. The mapped
audio cache also lacks placement identity and bypasses the established deferred builder.

## Proposed Remediation

**Preferred**:

- Add clip placement to mapped-warp cache identity and invalidate/rebuild it when moved.
- Freeze the pointer coordinate model for the duration of the first time-scale gesture.
- Carry elapsed sample position separately from musical beat/sample coordinates through
  graph mixing; re-anchor mapped presentation history at loop seams.
- Restore loaded BPM/map state without issuing an edit command and use one consistent
  supported upper bound.
- Replace visible-span/navigation arithmetic with the shared `BeatToX`/`XToBeat` model.
- Route mapped stretch preparation through the existing deferred background build path,
  preserving immutable publication and real-time-safe cache swaps.

**Files likely to change**:

- `src/native/nota.engine/src/engine/Engine.cpp`
- `src/native/nota.engine/src/engine/Engine.h`
- `src/native/nota.engine/src/engine/Engine_Render.cpp`
- `src/native/nota.engine/src/engine/Engine_Tracks.cpp`
- `src/native/nota.engine/src/engine/Transport.cpp`
- `src/native/nota.engine/src/engine/Track.h`
- `src/managed/Nota.App/ArrangementView*.cs`
- `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs`
- `src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs`
- `tests/Nota.SmokeTest/TempoMapTests.cs`
- `tests/Nota.TempoUiTest/Program.cs`

**Tests to add or update**:

- Moving a warped clip between tempo sections rebuilds duration/timing.
- First mapping gesture at legacy 60 BPM preserves the displayed coordinate scale.
- Unsynchronised LFO phase is invariant under tempo maps.
- Repeated loop wraps retain identical audio/playhead alignment.
- Project load does not add a tempo point; upper-bound projects reload.
- Centering/overview/resize calculations remain inverse under mapped zoom/scroll.
- Deferred mapped cache construction leaves publication responsive and installs safely.

## Risks & Considerations

- Cache installation must remain immutable and safe against superseded graph edits.
- No allocation, locking, or stretch construction may enter the audio callback.
- Compatibility for projects without a tempo map must remain unchanged.

## Open Questions

- None.
