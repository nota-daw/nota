# Bug Fix: preserve failed recovery

- **Slug**: issue-1035
- **Fixed**: 2026-11-25
- **Assessment**: ./assessment.md
- **Status**: partial

## Summary

Project opening reports success. Failed recovery keeps the previous save path and avoids clearing or autosaving over the snapshot; the marker stays for the next launch.

## Changes and verification

`MainWindow.Files.cs`, `MainWindow.Recovery.cs`, and `MainWindow.Welcome.cs` handle the failure. The build passed before the final autosave guard was added. A disposable-profile desktop test is still required.

## Deviations and follow-ups

The welcome window's close handler and autosave/clean-shutdown paths also needed guards; these were discovered during implementation. A failed load may still partially change the live engine.
