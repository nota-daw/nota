# Bug Fix: MP3 gapless metadata

- **Slug**: issue-1074
- **Fixed**: 2026-11-25
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

After encoder flush, replace the reserved first frame with LAME's final gapless metadata and propagate seek/write failure.

## Changes

`nota_encoder.cpp` writes the final tag. `Program.cs` tests an encoded one-second impulse through the native decoder.

## Local verification

The new round trip measured 48,000 frames and the impulse at frame 6,000. Full app export and other bitrates remain to check.

## Deviations from assessment

None.

## Follow-ups

Verify channel-specific impulses, more bitrates and streaming import.
