<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Fix: Mapped copies, paste automation and partial takes

- **Slug**: tempo-map-copy-takes (from the assessment in this session)
- **Fixed**: 2026-11-14
- **Assessment**: ./assessment.md
- **Status**: applied
- **Issue**: #535; PR #568

## Summary

Range duplication carves actual landing spans when the map changes the copies'
beat lengths. Block paste clears automation using the placed item's length.
Section edits re-anchor partial takes from their shifted first recorded frame,
so later comp edits address the original source audio.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Engine_Tracks.cpp` | modified | Destination-aware duplication and paste, plus authoring-only take-anchor helper used by region remaps and both section-duplication paths. |
| `tests/Nota.SmokeTest/TempoMapCopyTakeTests.cs` | added | 186 checks through the production C ABI. |
| `tests/Nota.SmokeTest/Program.cs` | modified | Include regressions in focused tempo and full smoke runs. |
| `CHANGELOG.md` | modified | Record the three corrected editing behaviors under Unreleased. |

## Diff Highlights

- Duplicate slices are captured first. If their lengths change after placement,
  all destination spans are carved before inserting any copy; copies cannot be
  carved by an earlier insertion. Stable-length ranges retain their established
  gap/silence overwrite behavior, including a constant map and warped clips.
- `placeBlock` passes `lengthAt(b, start)` to `applyClipAutomation` after shared
  placement/remapping. Captured point offsets remain beat-relative.
- `shiftTake` finds the moved first played beat, then subtracts the take's source
  offset in seconds before inverting through its own map. It uses the source sample
  rate and permits negative frame-zero origins. Without a map, the original linear
  shift is retained.
- Region remaps cover section moves and deletion ripple. Section duplication
  re-anchors both copied and later shifted takes while retaining new ids/groups
  for the copies.

## Tests Added or Updated

- 28 range-duplication checks: growing/shrinking destinations, forward/reversed
  audio, natural-rate/warped audio, source-frame retention, neighbor preservation
  or carving, undo/redo, and constant-tempo gap semantics.
- 30 automation checks: block paste, range paste and block duplicate in both tempo
  directions; cross-track remapping, outside-span preservation, full destination
  clearing, unchanged source automation and beat-relative offsets, atomic undo/redo.
- 128 take checks: move later/earlier, duplicate, delete earlier; two sample rates,
  both stepped tempo directions, linear ramps and legacy constant tempo. Assert
  actual take and comp starts, source offsets, copied group identity, snapshots
  through undo/redo, and selected sample frames from later comp swipes.

## Local Verification

- Before remediation, the initial reproduction suite failed 86 assertions:
  4 duplicate-span checks, 10 paste-automation checks and 72 mapped-take checks.
  Legacy constant-tempo take controls passed.
- After remediation, focused tempo coverage passes all 329 checks (143 existing
  plus 186 new); no failures. Logs: `build/tempo-copy-takes-before.log` and
  `build/tempo-copy-takes-after.log`.
- The final full Windows build and smoke results are recorded in `test.md`.

## Deviations from Assessment

- No source-file scope expansion, new dependency or ABI change.
- The initial swipe probes used half-sample positions at 44.1 kHz, where native
  and managed rounding of a tie differed. Probes use .1/.2-beat positions to test
  source alignment without an unrelated rounding-mode assumption.

## Documentation Audit

Applied nota-docs and nota-features. These fixes restore the described copy/paste,
automation-follow and section/comp workflows; they add no capability, UI label,
option or shortcut. `FEATURES.md` and the existing arrangement/automation manual
pages already describe the intended behavior, so they need no edits. No sibling
repository files were modified.

## Follow-ups

Hosted cross-platform CI and the unchanged engine performance gate must pass on
the published commit. No audio callback, DSP, rendering, or thread-pool path changed.
No extension hooks are registered for this workflow.
