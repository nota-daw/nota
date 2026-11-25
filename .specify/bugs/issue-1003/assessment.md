# Bug Assessment: WAV alias rejected

- **Slug**: issue-1003
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/1003 (github.com, allowlisted)
- **Verdict**: valid
- **Severity**: medium

## Report and symptom

The drop filter accepts `.wave`, but the streaming importer rejects it. Identical WAV data should decode under either extension.

## Reproduction

Import identical PCM WAV files named with `.wav` and `.wave`; compare decoded audio. Check uppercase and malformed input as controls. The reporter reproduced this in the native decoder; full app verification remains pending.

## Suspected code and root cause

`src/native/nota.engine/src/media/AudioImport.cpp`, `AudioImportJob::open`, lowercases the extension but dispatches WAV only for `wav`. Confidence: high.

## Proposed remediation

Accept `wave` in the existing WAV dispatch, retaining the same decoder and validation. Add a regression test comparing decoded frames for both names and rejecting invalid input.

**Files likely to change**: `src/native/nota.engine/src/media/AudioImport.cpp`, relevant smoke tests and changelog.

**Risks**: None beyond existing WAV decoder behavior.

## Open questions

None.
