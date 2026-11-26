# Bug Assessment: Duplicate Time flattens MIDI controllers

- **Slug**: issue-1072
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/1072 (github.com, allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report and symptom

Duplicate Time through a continuous MIDI controller ramp changes the original and copied values instead of preserving their shape.

## Reproduction

Create a 12-beat clip with CC11 ramping from zero at beat 0 to one at 8. Duplicate [2,6) and compare values at 3, 8, and 11 with the source. Reporter reproduced at native component level; full app remains unverified.

## Suspected code and root cause

`MidiClip::sliceControllersFrom` takes a special path for slices starting at zero which drops the end boundary of a ramp. `sliceCtlPoints` already constructs the interpolated boundary. Confidence: high.

## Proposed remediation

Use the controller slice routine for zero-start slices too, retaining its switch behavior. Test linear and curved continuous controllers and switch controls through Duplicate Time, undo and redo.

**Files likely to change**: `src/native/nota.engine/src/engine/MidiClip.cpp`, smoke tests and changelog.

**Risks**: Boundary points at clip ends and switch semantics must remain correct.

## Open questions

None.
