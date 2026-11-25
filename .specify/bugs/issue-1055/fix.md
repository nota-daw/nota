# Bug Fix: plug-in state change detection

- **Slug**: issue-1055
- **Fixed**: 2026-11-25
- **Assessment**: ./assessment.md
- **Status**: partial

## Summary

Fingerprint captured state blob hashes alongside the manifest without hashing sample audio.

## Changes and verification

`ProjectStore.cs` hashes captured state blobs in deterministic path order, excluding editor previews. Existing unchanged-project and regular-edit checks passed. A hosted plug-in fixture passed the state-only fingerprint check. The GUI prompt remains to be verified.

## Deviations and follow-ups

None; measure frequent-path overhead and test plug-in state stability.
