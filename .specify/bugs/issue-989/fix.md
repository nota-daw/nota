# Bug Fix: Shift in hosted editors

- **Slug**: issue-989
- **Fixed**: 2026-11-25
- **Assessment**: ./assessment.md
- **Status**: partial

## Summary

Do not claim Shift-modified key presses as bare typing keys on JUCE or macOS. The existing physical-key release logic remains ahead of the macOS modifier check.

## Changes and verification

`PluginEditorWindow.cpp` filters Shift alongside other modifiers. The macOS build passed. An end-to-end plug-in editor test and non-macOS build remain outstanding.

## Deviations and follow-ups

None.
