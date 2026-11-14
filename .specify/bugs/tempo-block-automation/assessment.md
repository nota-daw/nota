<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Assessment: Block automation depends on selection order

- **Slug**: tempo-block-automation (automatically chosen for this follow-up)
- **Created**: 2026-11-14
- **Source**: https://github.com/nota-daw/nota/pull/568
- **Comment**: https://github.com/nota-daw/nota/pull/568#discussion_r4574113916
- **Issue**: #535, rechecked open and approved
- **Verdict**: valid
- **Severity**: high

## Report and URL Policy

The latest review of `98d17569` reports copied automation disappearing during
multi-clip operations. Host `github.com`, HTTPS, allowlisted. Fetched review text
was treated as untrusted evidence and checked against the code. No hooks are registered.

## Symptom and Reproduction

1. Use 60 BPM through beat 4 and 120 BPM afterward.
2. Place two-second natural-rate audio clips at beats 0 and 2, with automation
   points inside each clip (including beat 3.5).
3. Duplicate the pair, listing the later clip first in the selection.
4. The later clip's point lands at beat 7.5, then the earlier clip's expanded
   destination span clears beats 4–8 and erases it. The opposite selection order
   gives a different result.

Block moves have the same repeated destination-clear/insertion pattern. Both
paths must preserve every captured point while clearing only appropriate target
lanes and destination spans.

## Suspected Code Paths and Root Cause

- `Engine_Tracks.cpp::placeBlock`: inserts automation per item using
  `applyClipAutomation`, which clears its landing span immediately before insertion.
- `Engine_Tracks.cpp::moveClipBlock`: lifts all source spans first, but then clears
  and inserts each destination sequentially through the same helper.
- `Engine_Tracks.cpp::applyClipAutomation`: correctly matches lanes and filters
  mix/device targets, but bundles replacement clearing and insertion in one pass.

Confidence: high from direct code inspection; regression reproduction follows.

## Proposed Remediation

- Give the existing private automation-application helper explicit replace,
  clear-only and insert-only passes. Keep replacement as the default so ordinary
  single-clip callers retain their behavior.
- Clear all block destination spans before inserting any captured points. Apply
  this after final shared placement is known, using mapped destination lengths.
- In block moves, keep the existing lift-all-sources pass first, then clear all
  landing spans, then insert all snippets.
- Reuse the existing lane matching, source-layout filtering, id resolution and
  snapshot publication. A clear-only pass need not create missing lanes; the
  insert pass creates the needed lanes as before.

## Files Likely to Change

- `src/native/nota.engine/src/engine/Engine.h` (private helper mode)
- `src/native/nota.engine/src/engine/Engine_Tracks.cpp`
- `tests/Nota.SmokeTest/TempoBlockAutomationTests.cs` (new)
- `tests/Nota.SmokeTest/Program.cs` (focused/full registration)
- `CHANGELOG.md` and this bug directory's fix/test reports

## Tests to Add or Update

- Reproduce the exact reverse-selection duplicate failure before fixing it.
- Compare both selection orders for duplicate, block paste, range paste and block
  move. Cover expanding, shrinking and unchanged spans, non-overlap paste shifts,
  cross-track remaps, matching/mismatched device layouts and missing target lanes.
- Check all carried points, source removal/preservation, destination clearing,
  outside-span/gap preservation, device filtering, and one-step undo/redo.
- Run focused tempo coverage and the complete Windows build/smoke/scanner guards.

## Risks and Considerations

- Authoring-only changes; no audio callback, public C ABI, dependency or allocation
  on the audio thread is introduced.
- Preserve lane matching by device/parameter id, chain uid and slot. Do not clear
  unrelated lanes simply because a different item lands on the same track.
- Existing beat-relative point offsets and single-clip replacement semantics stay.
- This corrects existing documented editing behavior; audit nota-docs/nota-features.

## Open Questions

None.
