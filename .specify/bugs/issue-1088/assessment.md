# Bug Assessment: Ceiling true-peak overshoot

- **Slug**: issue-1088
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/1088 (github.com, allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report and symptom

A short inter-sample burst exceeds the selected true-peak ceiling even with True Peak enabled. The report measured +0.8 dBTP against a -1 dB ceiling.

## Reproduction

Process the issue's alternating 0.85 burst at 48 kHz through Ceiling with True Peak on, comparing block sizes 32 and 257 and a separate reconstructed meter. Reporter reproduced at component level; full-app verification remains pending.

## Suspected code and root cause

`Ceiling.cpp` uses a smoothed detector followed by delayed audio and a sample-only hard clamp. It does not ensure sufficient gain is retained over the lookahead window and FIR support for short reconstructed peaks. Confidence: medium; exact detector/gain timing requires measurement.

## Proposed remediation

Measure the detector and gain against the burst, implement a real-time-safe lookahead strategy that covers the full reconstructed peak and maintains a documented tolerance, and test across block sizes, attack and release settings and independent true-peak metering. Benchmark the DSP path.

**Files likely to change**: `src/native/nota.engine/src/devices/effects/Ceiling.*`, true-peak tests and changelog.

**Risks**: Interpolation filter delay, post-gain reconstruction, latency and audible gain modulation. Avoid claiming a hard ceiling without independent verification.

## Open questions

None.
