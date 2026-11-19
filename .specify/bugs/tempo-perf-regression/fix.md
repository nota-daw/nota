<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Fix: Tempo track instruction regressions

- **Slug**: tempo-perf-regression (from the user-selected assessment context)
- **Fixed**: 2026-11-12
- **Assessment**: ./assessment.md
- **Issue**: nota-daw/nota#535 (approved)
- **Status**: applied

## Summary

Fixed-tempo transport queries now use a cached samples-per-beat factor and small
accessors, with compiled-map lookups implemented out of line. Ordinary render
segments skip mapped beat-bound calculations. Audio splits read their source from
the immutable old track instead of making an extra temporary copy.

## Changes

| File | Change | Notes |
|------|--------|-------|
| src/native/nota.engine/src/engine/Transport.h | Cache fixed conversion factor; keep mapped helpers out of line | Setters refresh the factor; detached-map fallback remains intact. |
| src/native/nota.engine/src/engine/Transport.cpp | Implement mapped helpers and consolidated fixed-clock publication | No allocation, locking, or I/O added to rendering. |
| src/native/nota.engine/src/engine/Engine_Render.cpp | Calculate mapped segment bounds only with an attached map | Retains local tempo and elapsed DSP clocks for mapped rendering. |
| src/native/nota.engine/src/engine/Engine_Tracks.cpp | Reference immutable split source | Preserves carves, cache slices, fades, and undo ownership. |
| tests/Nota.SmokeTest/TempoMapTests.cs | Add nine clock regression checks | Exercise BPM/rate changes, anchors, map transitions, and loop endpoints. |

## Tests Added or Updated

- Three BPM/sample-rate combinations: 44100 Hz / 90 BPM, 96000 Hz / 137.5 BPM,
  and 48000 Hz / 120 BPM, each with playback and stop-anchor checks.
- Fixed-to-mapped playback after rate changes.
- Map removal followed by a BPM change.
- Fixed loop endpoints after conversion-factor changes.

## Local Verification

- `dotnet run --project tests/Nota.SmokeTest -c Release --nologo -- --tempo`:
  44 checks passed, including the nine new assertions.
- `scripts/build-win.ps1` under the Visual Studio x64 developer environment:
  passed on the final source, including scanner self-test, native and managed
  builds, full smoke suite, design checks, and scan-scale test.
- `git diff --check`: passed.
- Windows DynamoRIO diagnostics: fewer engine instructions in every failing
  scenario versus the original local PR build. These diagnostic counts cover
  engine code during measured API calls, not the exact Linux Callgrind metric.
- Linux instruction phase of the unchanged `scripts/perf-gate.sh`: all 64
  scenarios pass the 1% work limit. Full timing verdict is recorded by bug-test.

## Deviations from Assessment

- The host initially lacked a Linux distribution. An isolated Ubuntu 26.04.1
  WSL environment was prepared with GCC 15.2 and Valgrind to run the actual gate.
  Its disk and downloads live under ignored `build/tempo-linux/`. Source packages
  are the repository's existing pinned versions; no application dependency or
  production build configuration was changed.
- Linux testing applies this patch to the actual PR head 29749ced, comparing it
  against CI's exact base c613167d. The Windows checkout remains on its existing
  branch at 908272b3 with local changes.
- An interrupted initial Linux build left incomplete objects; those generated
  files were removed and rebuilt before collecting benchmark results.

## Follow-ups

- Final Linux timing verification and retained evidence: ./test.md.
- Commit/push remains a separate action; no GitHub labels, workflow thresholds,
  benchmark exclusions, or public PR contents were changed.
