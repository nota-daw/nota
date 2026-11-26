# Bug Fix — Windows vocoder performance

- **Slug** — `windows-vocoder-performance`
- **Fixed** — 2026-11-26
- **Assessment** — [assessment.md](assessment.md)
- **Status** — Applied

## Summary

Nota Vocoder reuses the filter coefficients until the effective band layout changes. It also reuses the already converted Pitch, Tilt, and Mix values whenever their next smoothing step leaves the value unchanged.

## Changes

| File | Change |
| --- | --- |
| `src/native/nota.engine/src/devices/effects/Vocoder.cpp` | Compute filter coefficients on layout changes and reuse the analysis coefficients when the synthesis centre is identical. Keep Pitch, Tilt, and Mix conversions sample-accurate during glides. |
| `src/native/nota.engine/src/devices/effects/Vocoder.h` | Store the last computed layout. Sample-rate reset invalidates it. |
| `tests/Nota.SmokeTest/VocoderTests.cs` | Retune a settled 32-band instance and check the reported centre frequencies at both ends of the bank. |

## Tests Added or Updated

- The retuning check keeps the band count constant while changing the frequency range to 1–2 kHz. Both measured centres must match their logarithmic positions.
- The existing 32-band timing assertion still enforces 110% of Chamber's baseline-adjusted time.

## Local Verification

A standalone release build of the device ran 11 three-second renders per version at 48 kHz with 32 bands and a stereo track carrier. The before and after programs used the same inputs and Clang flags.

| macOS architecture | Before, median | After, median |
| --- | ---: | ---: |
| ARM64 | 34.991 ms | 30.956 ms |
| x86-64 via Rosetta | 86.532 ms | 77.918 ms |

`cmp` found bit-identical rendered audio and scope samples for each architecture, including parameter changes, carrier switches, Hold, and sample-rate changes with steady settings.

## Deviations from Assessment

None.
