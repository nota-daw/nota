# Bug Fix: PR #549 muted releases and edit overhead

- **Slug**: pr549-mute-perf (from assessment)
- **Fixed**: 2026-11-09
- **Assessment**: ./assessment.md
- **Status**: applied; Windows validation passed; Linux instruction gate pending

## Changes

`Engine_Render.cpp` clears pending articulation releases when a Session track becomes silent through mute or solo gating and its voices are flushed. Native self-test 38 exercises actual offline rendering: launch a latching key, silence the track during its hold, unmute into a momentary articulation using that key, and verify the obsolete release cannot interrupt it. The test fails on the old code at check 38 and passes with the fix.

`Track.h` adds a constructor taking the existing Session player. `Engine::cloneTrack` passes it directly, eliminating construction and destruction of a temporary player and its fixed release storage during every edit. A separate constructor for fresh tracks keeps the clone path free of a runtime conditional. The shared player's playback state is preserved.

## Validation and benchmark

- Old-code reproduction: focused articulation suite exits 1, native failed check 38.
- Fixed code: focused suite passes all 414 checks; native self-test returns zero.
- Local performance harness uses the repository's `edit-warp-split` scenario, with 1000 repeated cycles and the same 8 splits/8 undos per cycle. Both engine DLLs run an identical Release harness; fades are disabled as in the Linux gate. Nine rounds alternate execution order.
- Windows median wall times: before `8abb3ad1` 3.818 µs/edit, candidate 3.903 µs/edit (+2.2%). Ranges overlap: 3.731–4.525 and 3.740–4.345 µs. These measurements do not establish an instruction-count improvement; Linux/Valgrind CI must verify the reported +4.59% work regression is removed.
- Complete Windows validation passed on the first full run: native/managed builds, scanner self-test, full smoke suite and scan-scale check. Remote CI results are recorded in `test.md` as they become available.

Raw CSV, chart and machine details are published under `pr549-mute-perf/` on the fork's `pr-assets` branch and linked from `test.md`, rather than committed to the source branch. No guard thresholds were changed. No audio-thread allocation or locking was introduced.
