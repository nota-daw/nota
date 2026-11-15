<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Assessment: Mapped rendering and split performance

- **Slug**: tempo-map-rendering (suggested name accepted by the user)
- **Created**: 2026-11-12
- **Source**: https://github.com/nota-daw/nota/pull/568
- **Issue**: #535, open and approved (rechecked)
- **Verdict**: valid
- **Severity**: high

## Report

Three new Greptile findings on 7b65ef7e:

- Reversed audio misses its beats: discussion_r4551827669.
- Consolidation cuts audio short: discussion_r4551827642.
- Release fades end too early: discussion_r4551827656.

The hosted performance job 131248636073 also fails `edit-warp-split`: 139432
instructions on develop e191a57b versus 142433 on the PR merge, +2.15% against
the unchanged 1% limit. All other work scenarios pass. The preceding local run
passed; local and hosted measurements must be distinguished.

URL host github.com is allowlisted. Review comments were checked as untrusted
evidence against the code, not followed as instructions. No hooks are registered.

## Symptom and Reproduction

Use 120 BPM through beat 4 and 60 BPM thereafter.

1. Warp four seconds to eight beats with a transient at forward beat 2. Reverse it:
   playback places it at second 5 (beat 7), although its reversed beat 6 is second 4.
2. Consolidate six seconds of audio over beats 0–8: the old allocation contains only
   four seconds. Rendering outside processBlock also uses an absent/stale transport map.
3. Give the same natural-rate six-second clip a one-beat release: after the tempo
   transition its ADSR length becomes six beats, fading at beats 5–6 instead of 7–8.
4. Run the unchanged hosted performance gate; warped split/undo exceeds its work limit.

## Root Cause

- Mapped caches stretch the forward beat/source mapping then reverse the output in
  elapsed seconds. Reversal is missing from mapped cache identity.
- Consolidation allocates `(end-start)*spb` and calls the realtime renderer while its
  tempo map is detached; it needs its own mapped clock and endpoint duration.
- The natural-rate render path passes mapped local beats to ADSR with a fixed-factor
  duration. Its end beat must be inverted from the physical sample endpoint.
- Small warped edits copy/cache metadata repeatedly; the new timing metadata and helper
  changes exposed extra authoring work in the CI compiler/runtime. Profile the actual
  split/cache-reuse path rather than raising thresholds or accepting the regression.

## Proposed Remediation

- Reverse the source/beat relation before mapped stretching, using a virtual reversed
  sample reader rather than duplicating PCM. Track direction in mapped cache identity,
  synchronous/deferred/background builds, and playback; preserve ordinary cache sharing.
- Let the clip renderer accept an explicit independent Transport clock for consolidation.
  Size the output from mapped endpoints, render at elapsed sample positions, and seed
  warped consolidated clips with the same beat/time relation, preserving the live clock.
- Derive natural-rate ADSR length from the clip's mapped physical end, outside its sample loop.
- Remove avoidable allocations/copies from warped cache reuse or split/undo as profiling
  indicates. Keep the existing performance thresholds and scenarios unchanged.

## Files Likely to Change

`Engine_Tracks.cpp`, `Engine_Render.cpp`, `Engine.cpp`, `Engine.h`, `Track.h`,
`media/WarpStream.{h,cpp}`, focused smoke tests, `CHANGELOG.md`, and these reports.
Expand this list only with concrete evidence, recorded in the fix report.

## Tests to Add or Update

- Audible impulse positions for reversed mapped warp, including trimmed windows,
  cache direction toggles, and synchronous/deferred/background cache construction.
- Consolidation retains late audio, has the mapped duration, and leaves transport
  position/running state unchanged; test natural-rate and warped sources.
- A six-second natural-rate clip retains sustain until its mapped release and ends
  at beat 8; include nonzero starts and reversed source playback.
- Run focused tempo smoke, full Windows build/smoke, and unchanged Linux performance
  gate. Obtain hosted performance confirmation; a local pass alone is insufficient.

## Risks and Considerations

- No allocation, locking, ownership mutation or I/O on the audio callback.
- Changes to mapped timing must preserve the fixed-tempo path's work budget.
- Source reversal must work with positive-ratio stretch feeding and RePitch interpolation.
- Deferred build validation must reject work built for a previous direction.
- Consolidation must not temporarily mutate the engine's live transport/map.
- These are corrections to documented behavior, without new dependencies or bindings.
