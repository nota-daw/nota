# Bug Assessment: MP3 gapless metadata

- **Slug**: issue-1074
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/1074 (github.com, allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report and symptom

Exported MP3 decodes late and longer than its source even in a gapless-aware decoder. The report measures a 48 kHz one-second export as 50,688 frames, with impulses 2,257 frames late.

## Reproduction

Export a one-second stereo signal with separate impulses, decode it with a gapless-aware decoder, and compare length and peak positions. Compare FLAC and a correctly tagged LAME file. The reporter measured the native components; packaged app verification remains pending.

## Suspected code and root cause

`src/native/nota.engine/src/abi/nota_encoder.cpp` flushes LAME but never writes its finalized tag frame. Confidence: high.

## Proposed remediation

After flush, write the finalized LAME tag to the reserved first frame and propagate seek/write errors. Add a round-trip test checking frame count and impulse positions, plus close/error controls.

**Files likely to change**: `src/native/nota.engine/src/abi/nota_encoder.cpp`, encoder smoke tests and changelog.

**Risks**: Metadata and decoder compatibility; preserve flush and error handling.

## Open questions

None.
