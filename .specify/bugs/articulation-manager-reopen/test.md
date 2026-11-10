# Bug Verification: "Edit maps…" doesn't bring up the articulation map manager

- **Slug**: articulation-manager-reopen
- **Tested**: 2026-11-09
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

With the fix built, the owner reports the manager works ("everything works", 2026-11-09), including
bringing it back after it was minimized — the case the assessment identified and the owner
confirmed. The full smoke test passes with Nota closed.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix) | Owner, in the rebuilt app: minimize the manager, click "Edit maps…" | pass | owner's report; not observed by the agent |
| New / updated tests | — | not-run | no window harness in the smoke test (no Avalonia.Headless), as the assessment noted |
| Regression suite | `scripts`-equivalent local run: `Nota.SmokeTest.dll` (full) | pass | 5401 pass, 0 fail, Nota closed |
| Build | `dotnet build Nota.sln -c Release` | pass | 0 errors |

## Output Excerpts

```
EXIT=0 pass 5401 fail 0 art 153 | SMOKE TEST PASSED
```

The render-pool timing check that failed in fix.md's runs (Nota was open then) passed here.

## Residual Risks

- The window behaviour is covered by the owner's manual check only; a regression would not be caught
  by the smoke test.

## Recommendation

Close the bug — the owner verified it in the app and the regression suite is green.
