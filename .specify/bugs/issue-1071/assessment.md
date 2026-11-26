# Bug Assessment: Duplicate Time loses automation step

- **Slug**: issue-1071
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/1071 (github.com, allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report and symptom

Repeating Duplicate Time across an automation step loses one side of that step, changing the copied curve.

## Reproduction

Create a ramp at beats 0 and 8 and a following point at 12; duplicate [2,6), then [4,8), and compare beats 6.5 and 10.5. The reporter measured native routines; app verification remains pending.

## Suspected code and root cause

`laneSliceExact` in `Engine_Tracks.cpp` discards a second point at an equal beat, despite these pairs expressing a discontinuity. Confidence: medium until the regression test is run.

## Proposed remediation

Preserve both sides of internal discontinuities in the slice, retaining their order and segment curves. Test the reported repeated duplicate, curved ramps, boundary cases, undo and redo.

**Files likely to change**: `src/native/nota.engine/src/engine/Engine_Tracks.cpp`, smoke tests and changelog.

**Risks**: Overlapping points and their order are meaningful at joins; avoid changing unrelated automation behavior.

## Open questions

None.
