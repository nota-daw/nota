# Bug Assessment: Plug-in-only edits bypass save prompt

- **Slug**: issue-1055
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/1055 (github.com, allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report and symptom

An edit stored only in a hosted plug-in's serialized state can be lost because leaving the project does not prompt to save.

## Reproduction

Save a project, change only plug-in internal state, and choose New Project. Verify Save, Don't Save and Cancel; unchanged projects should not prompt. Full plug-in GUI reproduction remains pending.

## Suspected code and root cause

`ProjectStore.Fingerprint` serializes a capture whose state blobs have sequential names. It excludes the blob bytes, so equal manifest names conceal a state change. Confidence: high.

## Proposed remediation

Include deterministic hashes of captured state blobs in the fingerprint while retaining cheap session names for audio. Test equal and changed serialized state and normal unmodified behavior.

**Files likely to change**: `src/managed/Nota.Infrastructure/Persistence/ProjectStore.cs`, persistence tests and changelog.

**Risks**: Fingerprinting is frequent; avoid reading or hashing audio samples and assess plug-in state retrieval cost.

## Open questions

None.
