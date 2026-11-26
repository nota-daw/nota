# Bug Assessment: Recovery snapshot removed on failed open

- **Slug**: issue-1035
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/1035 (github.com, allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report and symptom

A failed recovery load is reported as successful and the recovery bundle is removed, risking loss of recoverable media.

## Reproduction

In a disposable profile, make a recovery bundle fail to load, select Recover, and inspect the status, Save destination and bundle. Repeat with a valid bundle and both startup entry points. Full desktop reproduction remains pending.

## Suspected code and root cause

`MainWindow.Files.cs` catches load exceptions in `OpenProject` without returning a result. `MainWindow.Recovery.cs` unconditionally assigns the original path and clears the recovery bundle. Confidence: high.

## Proposed remediation

Return a success flag from `OpenProject` and only perform recovery success actions after success. Test both failed and successful recovery with disposable fixtures; check that the failure does not redirect Save.

**Files likely to change**: `src/managed/Nota.App/MainWindow.Files.cs`, `src/managed/Nota.App/MainWindow.Recovery.cs`, relevant tests and changelog.

**Risks**: Project loading can partially change engine state before a failure; preserving the snapshot is essential even if rollback is unavailable.

## Open questions

None.
