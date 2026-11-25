# Bug Fix: MIDI controller slice

- **Slug**: issue-1072
- **Fixed**: 2026-11-25
- **Assessment**: ./assessment.md
- **Status**: partial

## Summary

Use the boundary-aware controller slice for zero-start fragments and retain explicit rest points if the slice is otherwise empty.

## Changes and verification

`MidiClip.cpp` uses `sliceCtlPoints`; the controller self-test checks the middle of a shortened ramp and passed before the final rest-point fallback. The full Duplicate Time reproduction remains to be checked.

## Deviations and follow-ups

The fallback preserves previous explicit zero-start rest points, avoiding an unrelated controller reset change. Check curved and switch controllers and undo/redo.
