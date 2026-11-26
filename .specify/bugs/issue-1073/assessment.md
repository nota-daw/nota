# Bug Assessment: MIDI controller ramps at small blocks

- **Slug**: issue-1073
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/1073 (github.com, allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report and symptom

Continuous MIDI clip controller ramps lose intermediate events with 32- and 64-frame blocks; endpoints remain.

## Reproduction

Play a two-beat CC11 ramp at 48 kHz with block sizes 32, 64, and 128; compare events and midpoint values. Reporter measured the native path; full-app capture remains pending.

## Suspected code and root cause

`Engine_Render.cpp`, `Engine::clipCtlEvents`, schedules interpolation from the first 64-sample boundary strictly after each block-relative first offset. Blocks at or below 64 frames contain no such offset. Confidence: high.

## Proposed remediation

Schedule ramp samples on a stable timeline across render blocks, including a block-start event when a sampling boundary falls there; avoid duplicate exact-point events where possible. Test varying block sizes, both arrangement and session, and preserve switch and endpoint behavior.

**Files likely to change**: `src/native/nota.engine/src/engine/Engine_Render.cpp`, smoke tests and changelog.

**Risks**: Real-time constraints, duplicate event ordering, loop boundaries and tempo maps.

## Open questions

None.
