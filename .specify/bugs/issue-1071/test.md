# Bug Verification: Duplicate Time automation step

- **Slug**: issue-1071
- **Tested**: 2026-11-25
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Checks performed

The automated reproduction failed before the fix (0.3125 / 0.6875). `dotnet run --project tests/Nota.SmokeTest -c Release -- --issue-1071` passed afterwards (0.3125 / 0.3125). The full smoke scenario also passed for linear and curved ramps and undo/redo. The desktop gesture has not been individually verified.

## Recommendation

Run the full suite on the final tree and verify the remaining scenarios before closing.
