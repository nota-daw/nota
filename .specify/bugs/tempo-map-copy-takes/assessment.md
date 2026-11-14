<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Assessment: Mapped copies, paste automation and partial takes

- **Slug**: tempo-map-copy-takes (automatically chosen for this review follow-up)
- **Created**: 2026-11-14
- **Source**: https://github.com/nota-daw/nota/pull/568
- **Issue**: #535, open and approved (rechecked)
- **Verdict**: valid
- **Severity**: high

## Report

Review of `59043012` reports three P1 findings:

1. Copies overlap neighboring audio (discussion_r4573987926).
2. Pastes erase neighboring automation (discussion_r4573987942).
3. Moved takes select wrong audio (discussion_r4573987956).

URL policy: host `github.com`, HTTPS, allowlisted. Review contents were treated
as untrusted evidence and compared with the source. No extension hooks are registered.

## Symptom and Reproduction

- At 60 BPM through beat 4 and 120 afterward, duplicating four seconds from beats
  0–4 places audio across beats 4–12, but only clears beats 4–8. A neighboring
  clip at beat 8 survives beneath the copy.
- Paste two seconds of natural-rate audio from a 120 BPM section into a 60 BPM
  section. Its four-beat source span is passed to automation clearing although
  its destination occupies two beats; unrelated later points disappear.
- At 120 BPM through beat 4 and 60 afterward, a take with origin 0 and a
  three-second source offset starts at beat 5. Move its section four beats later:
  its comp starts at beat 9, but adding four to its origin places the take's first
  recorded frame at beat 7. Subsequent comp swipes choose the wrong samples.

## Suspected Code Paths and Root Cause

- `Engine_Tracks.cpp::duplicateRange`: audio overwrite uses `[end,end+len]`
  regardless of the copies' mapped lengths after placement.
- `Engine_Tracks.cpp::placeBlock`: `lengthAt` is used for clip carving and overlap
  checks, but `applyClipAutomation` still receives `b.len`.
- `Engine_Tracks.cpp::remapTimeline` and `duplicateSectionWithContent`: take
  origins shift linearly even though `AudioTake::beatAtFrame` integrates its map.
  This affects moved sections, delete ripple, copied takes and later shifted takes.
- `Track.cpp::AudioTake::{beatAtFrame,frameAtBeat}` and
  `Engine_Takes.cpp::compTake` expose the mismatch through take metadata and comping.

Confidence: high from direct code inspection; test reproductions follow in bug-fix.

## Proposed Remediation

- Gather duplicate slices before carving. Clear their actual mapped destination
  spans before inserting any copies, keeping natural-rate sample windows and
  warped beat lengths intact. Preserve established fixed-tempo behavior, including
  the nominal selected destination range where needed for gaps.
- Pass each placed block item's destination length to automation application;
  retain the existing beat-relative offsets of captured automation points.
- Add an authoring-only helper that shifts a take's first played beat and solves
  its origin from the destination seconds minus its source offset duration.
  Retain linear origin shifts without a map. Use it for region remaps and both
  take paths in section duplication. Negative origins must remain possible when
  an earlier move places the first played frame near project start.

## Files Likely to Change

- `src/native/nota.engine/src/engine/Engine_Tracks.cpp`
- `tests/Nota.SmokeTest/TempoMapCopyTakeTests.cs` (new regression coverage)
- `tests/Nota.SmokeTest/Program.cs` (focused/full registration)
- `CHANGELOG.md` and this bug directory's fix/verification reports

## Tests to Add or Update

- Duplicate natural-rate clips into faster/slower sections; verify actual landing
  boundaries carve neighbors correctly, source windows stay unchanged, reverse
  and warp behavior remain valid, and undo/redo restore the whole operation.
- Block/range paste and block duplicate: destination-only automation clearing in
  both tempo directions, beat-relative copied points, cross-track remapping,
  source preservation, and undo/redo.
- Restore partial takes with nonzero offsets, create comp clips, then move,
  duplicate or delete sections. Verify take starts and comp metadata agree,
  subsequent comp swipes select the expected source frames, copied ids/groups
  are independent, and undo/redo restore the graph. Include negative origins,
  differing sample rates, ramps and constant-tempo controls.
- Prove the reports fail before the fixes, then run focused tempo coverage and
  the full Windows build/smoke suite. No new public ABI or dependency is needed.

## Risks and Considerations

- Keep changes on the authoring thread, with existing immutable snapshot/undo
  publication. No audio callback changes, locks, allocations or I/O are added.
- Avoid carving copies already inserted in the same operation.
- A take uses its own stored map and source sample rate when deriving its origin.
- This restores existing documented capabilities; audit nota-docs/nota-features.

## Open Questions

None; the requested behavior follows the existing clip/automation/comp contracts.
