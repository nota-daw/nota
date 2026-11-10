# Bug Fix: PR #549 articulation review

- **Slug**: pr549-review (reused from assessment)
- **Fixed**: 2026-11-09
- **Assessment**: ./assessment.md
- **Status**: applied; split into focused commits for PR #549

## Summary

Completed the existing uncommitted review fixes. Playback resolves latching changes across the track, holds shared momentary keys until the last dependent note ends, restores switches on Session launches, and schedules Session notes and expression across every pass their shifted lifetimes require. The manager opens project copies ahead of differing library copies.

## Changes

| Files | Change |
|---|---|
| `ArticulationTable.cpp`, `.h` | Retain repeated latching points for track-wide decisions; preserve chord deduplication. |
| `ArticulationSchedule.cpp`, `.h` | Track-wide selection by trigger time, shared-key holds across clips/articulations (including default channel 0 aliasing explicit channel 1), launch chase, full 20 ms chased releases, pass ranges without a fixed neighboring-pass cap, and Session momentary tails spanning loops. |
| `Engine_Render.cpp` | Share shifted Session note/expression timing and per-note pass ranges; chase the first launch/legato block; retain chase origin across blocks. Specialize arrangement scheduling at compile time to skip articulation lookups and timing arithmetic when the track has neither a map nor an offset. |
| `Engine.h`, `SessionPlayer.h` | Audio-thread chase origins on the arrangement and slot clocks; no locks, allocations or public ABI changes. |
| `Engine.cpp` | Check timeline currency before scanning the playing graph, with a cheap no-map path. |
| `ArticulationMapUsage.cs`, `ArticulationMapManager.cs`, `en.json` | Prefer the project's map; label differences from the library and permit saving the restored copy. |
| `ArticulationEngineTests.cs`, `ArticulationManagerTests.cs` | C ABI short-loop onset regressions and the real save/apply/undo/reopen/edit sequence. |
| `CHANGELOG.md`, `specs/085-articulation-maps/tasks.md`, `plan.md` | Describe fixes and chase-origin design; remove the draft's unsupported claim that the new performance gate passed. |

## Tests

Native scheduler/delivery checks 26–35 cover full 20 ms chase holds across 512-frame and one-frame segments, overlapping clips, shared keys across different articulations/clips, 48-pass offsets, shifted Session expression at the instrument input, long note releases, and regular/legato launch restoration through the Session renderer. Existing checks 1–25 remain in place.

`dotnet run --project tests/Nota.SmokeTest -c Release -- --articulations` passed: 412 checks, zero failures, including both 1.5-second delayed onset cases and the native self-test returning zero.

The complete Windows build/scanner/smoke/scan-scale script is recorded in `test.md` after completion. Logs are local ignored artifacts alongside these reports.

## Deviations from assessment

The draft shortened a chased hold to the first block. This was replaced by preserving the chase origin and emitting the release at its full 20 ms duration in a later block. Session notes and expression now share one enumeration rather than independently expanding a common 32-pass window. Long Session note tails also determine the release window.

Inspection of `HEAD` confirmed that `ArtEv` was already trivially default-constructible in the original PR. The assessment's array-initialization hypothesis was incorrect; no performance improvement is attributed to changing that array. The actual performance edits are the no-map/no-offset loop specialization, avoiding out-of-line lookups, cheaper event-offset minimum, and cheap timeline checks ahead of the graph scan.

The first validation attempt caught a stack overflow from a large local `Engine` in the new native test harness. It now uses `std::make_unique<Engine>()`; the focused suite passes after this correction. This allocation is test-only.

## Follow-ups

Linux/Valgrind instruction-count CI must validate the performance changes. No new CI pass is claimed; the original PR's performance failure was the remote result inspected during local verification. WSL cannot run on this machine. Unrelated deferred feature/convergence tasks remain open in the existing feature plan. The user subsequently authorized committing each fix to PR #549; the changes are organized as eight fix commits and a verification/documentation commit. No review-thread reply or PR description edit is included.
