# Bug Verification: Stepped tempo transitions render vertically

- **Slug**: stepped-tempo-line-slope
- **Tested**: 2026-11-11
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The original diagonal step no longer reproduces. Automated geometry checks, a rendered
headless capture, and the complete Windows build gate passed without a regression.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix) | Inspect `out/tempo-ui-step-fix.png` | pass | Each hold reaches the tempo point and each transition is visually vertical. |
| New / updated tests | `dotnet run --project tests/Nota.TempoUiTest/Nota.TempoUiTest.csproj --configuration Debug -- out/tempo-ui-step-fix.png` | pass | 27/27, including exact event X at normal and zoomed/scrolled transforms. |
| Regression suite | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-win.ps1` | pass | Native, scanner, managed, smoke, design/localization, performance/render-pool, and scan-scale checks passed. |
| Diff hygiene | `git diff --check` | pass | No whitespace errors in tracked changes. |

## Output Excerpts

```text
[PASS] stepped transition uses the exact event X for both endpoints
[PASS] stepped transition holds the old BPM then jumps to the new BPM
[PASS] stepped transition remains vertical after zoom and scroll
SCAN SCALE PASSED
==> Done. Run the app with: dotnet run --project src/managed/Nota.App
```

## Residual Risks

- DPI scaling is exercised through Avalonia's device-independent coordinate path and the
  shared transformed X, but the automated harness renders at its default headless scale.

## Recommendation

Close the rendering bug as verified. The engine timing was intentionally left unchanged.
