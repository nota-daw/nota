# Bug Fix: Duplicate Time automation step

- **Slug**: issue-1071
- **Fixed**: 2026-11-25
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Keep equal-beat points in an extracted automation slice so the outgoing side of a step survives a subsequent duplicate.

## Changes

`Engine_Tracks.cpp` preserves the ordered points; `Program.cs` exercises the reported two duplicates.

## Local verification

`dotnet run --project tests/Nota.SmokeTest -c Release -- --issue-1071`: pass, 0.3125 on both sides. The same test failed before the fix (0.3125 / 0.6875).

## Deviations from assessment

None.

## Follow-ups

Verify curved steps and undo/redo and run the complete suite on the final changes.
