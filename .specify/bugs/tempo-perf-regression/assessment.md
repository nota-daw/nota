<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Assessment: Tempo track instruction regressions

- **Slug**: tempo-perf-regression (user selected)
- **Created**: 2026-11-12
- **Source**: https://github.com/nota-daw/nota/pull/568
- **Issue**: nota-daw/nota#535, confirmed approved
- **Verdict**: valid
- **Severity**: medium

## Report (summarized)

PR #568's Performance job 131000796576 in run 43093996433 fails seven
instruction-count scenarios against base c613167d, testing merge f72eb58b.
The increases are edit-warp-split 3.63%, audio-tails-8 2.06%, instrument-04
1.42%, instrument-01 1.40%, audio-warp-split 1.36%, instrument-03 1.29%, and
instrument-17 1.19%. All final wall-time verdicts pass.

URL trust: supplied URL above; parsed host github.com; policy allowlisted.
Fetched text was treated as evidence, not instructions.

## Symptom

Projects without tempo automation pay additional shared transport/render work
after the tempo-track feature. Split/undo also copies the enlarged AudioClip
unnecessarily. The existing 1% instruction limit rejects these scenarios.

## Reproduction

1. Build the base engine and PR engine in Release on Linux using the same
   tests/perf/nota_perf.cpp harness.
2. Run scripts/perf-gate.sh c613167d6f66b04e40e7e8c4f656797c8091b2bc.
3. Compare the seven scenarios listed above; CI already records their failure.

## Suspected Code Paths

- src/native/nota.engine/src/engine/Transport.h — fixed-tempo conversions and
  queries repeatedly derive sample/beat factors and test mapped state.
- src/native/nota.engine/src/engine/Transport.cpp — publish and transportInfo
  repeat common tempo/position queries even when no map is attached.
- src/native/nota.engine/src/engine/Engine_Render.cpp:processBlock — beatStart
  and beatEnd conversions run even when no map is active.
- src/native/nota.engine/src/engine/Engine_Tracks.cpp:splitClip — copies an audio
  source already held in an immutable old graph before copying its two halves.

## Root Cause Hypothesis

Medium confidence in attribution without a local Callgrind profile; high confidence
in the recorded regression. Similar absolute overhead across inexpensive render
scenarios suggests shared per-block work. The split scenario additionally copies
warp markers, envelopes, and cache ownership unnecessarily. This Windows machine
has no installed WSL distribution, so Linux instruction validation may need CI.

## Proposed Remediation

Cache the fixed-tempo samples-per-beat factor in Transport and update it when
sample rate or BPM changes. Consolidate fixed-tempo publication/query arithmetic
where useful, preserving mapped/detached-map behavior. Calculate segment beat
bounds only for mapped playback. Read split sources from the immutable old track
instead of copying a temporary AudioClip. Preserve fades, warp caches, undo and
all existing gate thresholds and measurement scenarios.

**Files likely to change**:

- src/native/nota.engine/src/engine/Transport.h
- src/native/nota.engine/src/engine/Transport.cpp
- src/native/nota.engine/src/engine/Engine_Render.cpp
- src/native/nota.engine/src/engine/Engine_Tracks.cpp
- tests/Nota.SmokeTest/TempoMapTests.cs

**Tests to add or update**:

- Fixed-tempo changes of sample rate/BPM, transitions into/out of a map, and
  seek/loop positions remain correct after caching conversion factors.
- Existing warped split/fade/reverse/undo smoke coverage must pass.
- Run the native/managed Windows build and full smoke suite.
- Run available failing performance scenarios before/after; distinguish Windows
  timing results from the authoritative Linux instruction gate.

## Risks & Considerations

- Keep conversion caches synchronized with setters and map removal.
- Preserve immutable graph ownership and audio-thread safety.
- Floating-point operation order can affect sample rounding; exercise seams.
- No new dependencies, ABI changes, benchmark exclusions, or relaxed thresholds.

## Open Questions

- Exact instruction deltas after optimization require Linux Callgrind validation.
