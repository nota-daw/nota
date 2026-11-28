# Bug Verification: Scrollbars expand to wide unstyled Fluent appearance on hover

- **Slug**: scrollbar-hover-style
- **Tested**: 2026-11-26
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The dedicated `ScrollBar` `ControlTheme` (`NotaScrollBarThumb`, `NotaScrollBarPageButton`) and hidden `ScrollViewer` corner separator (`PART_ScrollBarsSeparator`) resolve the bug across rest, hover, expansion (`IsExpanded = true`), press, and drag states in both dark and light themes, with full `scripts/build.sh` and `Nota.ModularUiTest` passing.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix) | Headless Avalonia `ScrollViewer` + standalone `ScrollBar` (`Height = 10`) in Studio Dark / Studio Light / Classic | pass | Vertical/horizontal bars stay 9 px wide/tall (10 px standalone), 5 px pill thumb (`Margin="2"`), no `TrackRect` or arrow buttons, `PART_ScrollBarsSeparator` hidden (`IsVisible = false`), thumb transitions `Brush.Rule` → `Brush.TextFaint` → `Brush.TextMuted`; drag and `PART_PageUpButton` scroll work. |
| Visual inspection | Rendered headless PNGs (`scrollbar-rest-dark.png`, `scrollbar-hover-dark.png`, `scrollbar-hover-light.png`) | pass | Verified thin pill thumb and transparent track at rest and on hover/expand in both dark and light themes. |
| Full build + smoke test | `bash scripts/build.sh` | pass | Native engine + .NET Release build (0 errors), `SCANNER SELFTEST PASSED`, `SMOKE TEST PASSED` (`8510` `[PASS]` checks, including `DesignTokenCheck.RunControls()` scrollbar check), `DEFAULT SCAN PATHS UPGRADE PASSED`. |
| Modular UI suite | `dotnet run --project tests/Nota.ModularUiTest -c Release --nologo` | pass | `All Modular UI checks passed.` |
| Skill sync check | `bash scripts/sync-agent-skills.sh --check` | pass | No skills out of sync. |

## Output Excerpts

```text
  [PASS] ScrollBar has its own 9px ControlTheme (no Fluent hover expansion, track box, or arrows) and ScrollViewer hides its corner separator
SMOKE TEST PASSED
DEFAULT SCAN PATHS UPGRADE PASSED
All Modular UI checks passed.
```

## Residual Risks

- `scripts/build.sh` ran on macOS (`osx-arm64`); Windows and Linux CI runners will validate those targets when a PR is opened. (An earlier concurrent `scripts/build.sh` run hit one transient `-- render pool --` timing check failure under background load before passing cleanly on the subsequent run.)

## Recommendation

Ready to commit on `fix/1113-scrollbar-hover-style` and open a pull request targeting `develop` with `Closes #1113`.
