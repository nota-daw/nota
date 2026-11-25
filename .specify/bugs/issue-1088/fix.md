# Bug Fix: Ceiling short true peaks

- **Slug**: issue-1088
- **Fixed**: 2026-11-25
- **Assessment**: ./assessment.md
- **Status**: partial

## Summary

An unfiltered true-peak safety detector holds attenuation across the lookahead and interpolation windows; true-peak mode uses at least 16 samples of lookahead. The gain-reduction meter includes the safety gain.

## Changes and verification

`Ceiling.cpp` and `Ceiling.h` add the detector; `CeilingTruePeakTests` exercises the reported burst at 32 and 257 frames. Both passed at -1.00 dBTP. An independently run FFmpeg ebur128 measurement read -1.0 dBTP for the 48 kHz Clean/3 ms fixture. Additional component probes covered 0/3/10 ms, three characters and deterministic noise.

## Deviations and follow-ups

True-peak mode adds a minimum 16-sample latency rather than eight. Check varied source material, dynamic parameter changes, performance, and full-app output before marking verified.
