# Bug Fix: WAV alias

- **Slug**: issue-1003
- **Fixed**: 2026-11-25
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Dispatch `.wave` to the same WAV decoder as `.wav` after case normalization.

## Changes and verification

`AudioImport.cpp` accepts the alias. Smoke tests open and decode an uppercase alias and reject malformed data; `scripts/build.sh` passed with these changes. Desktop drag and preview were not exercised.

## Deviations and follow-ups

Compare decoded audio against the original in a full-app test.
