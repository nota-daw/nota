# Bug Fix: MIDI controller timing at small blocks

- **Slug**: issue-1073
- **Fixed**: 2026-11-25
- **Assessment**: ./assessment.md
- **Status**: partial

## Summary

Anchor continuous controller event cadence to clip time instead of restarting at each render block.

## Changes and verification

`Engine_Render.cpp` emits grid events at block starts. The native self-test exercises 32, 64 and 128 frames for Arrangement and Session; `--midi-ctl` passed.

## Deviations and follow-ups

Check non-aligned clip starts, tempo maps and full-app output, and benchmark the render path.
