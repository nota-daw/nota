# Bug Fix: PR #549 follow-up review

- **Slug**: pr549-follow-up (from assessment context)
- **Fixed**: 2026-11-09
- **Assessment**: ./assessment.md
- **Status**: applied

## Changes

### Tempo changes leave switches held

`ArticulationReleaseState` retains actual MIDI channel/key pairs and remaining rendered frames in fixed storage. Session scheduling queues only successfully emitted latching key-ons, for both natural and chased switches. Pending releases advance with rendered frames before new actions; they never depend on `localBeats * spb` after emission. Clearing a map still drains pending releases without consulting the old table.

The queue never grows on the audio thread. It rejects key-ons when no release slot remains and carries undeliverable releases to the next block. Session launch/stop clears the old queue when the caller flushes instrument voices. A user-provided `SessionPlayer` constructor prevents value-initialization from zero-filling the unused queue on every track clone.

Files: `ArticulationSchedule.h/.cpp`, `SessionPlayer.h/.cpp`, `Engine_Render.cpp`.

Native checks 36/37 capture actual instrument events at nonzero Session positions while changing from 120 BPM to 60 or 240 BPM during the hold. They cover natural/chased switches, map removal, saturation, small output buffers and launch/stop cleanup. Existing checks through 35 remain unchanged.

### Duplicate files block the manager

`ArticulationMapUsage.Catalog` uses an ordinal dictionary with `TryAdd`, keeping the first readable copy for each library map ID. It still prefers a differing project copy. Manager tests write copied files with both identical and differing content and verify deduplication in project-backed and library-only catalogs.

Files: `ArticulationMapUsage.cs`, `ArticulationManagerTests.cs`.

## Local verification

The focused articulation suite passes 414 checks with zero failures, including native self-test result zero. The final complete Windows build/smoke/scanner script passes with exit zero. Two earlier complete runs failed different render-pool timing guards; the isolated performance suite passed. All results are recorded in `test.md`; no guard thresholds or production code were changed between the full runs. Local logs remain ignored.

## Deviations and follow-ups

No deviation from the assessment. The previously pushed revision `96733e0e` passed the remote Linux instruction-count gate; the new commits must run CI again. No manual hosted-library audition or local macOS/Linux build is claimed.
