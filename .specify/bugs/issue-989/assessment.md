# Bug Assessment: Shift note keys in plug-in editors

- **Slug**: issue-989
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/989 (github.com, allowlisted)
- **Verdict**: valid
- **Severity**: medium

## Report and symptom

Shift plus a bare note key is forwarded as the unmodified note in hosted plug-in editors. A held bare note must still be released after Shift changes.

## Reproduction

Enable musical typing, focus a plug-in editor, press A and Shift+A, then release A after pressing Shift. Verify an unassigned chord remains with the plug-in and the held note receives exactly one release. Reporter tested the non-macOS listener; desktop reproduction remains pending.

## Suspected code and root cause

`PluginEditorWindow.cpp` filters Command, Control and Alt but omits Shift in both its JUCE listener and macOS event monitor. Release handling already tracks the held physical key. Confidence: high.

## Proposed remediation

Include Shift in modifier filtering for presses and keep held-key release handling ahead of the modifier check. Test modified presses and releases across modifier changes on both paths where available.

**Files likely to change**: `src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.cpp`, keyboard tests and changelog.

**Risks**: Held-note releases must not be suppressed; existing configurable bindings remain authoritative.

## Open questions

None.
