<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Verification: Mapped move spans and scroll extent

- **Slug**: tempo-map-move-scroll
- **Tested**: 2026-11-14
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

Both original review findings reproduce before remediation and pass afterward.
The resolved merge includes develop `8390c344`; its editable shortcuts work in
the tempo lane and retain the tempo mouse-reference rows. Full Windows checks pass.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Move reproduction before fix | Rebuilt native engine; focused tempo suite with move draft registered | expected failure | 14 failed assertions across all move APIs and mismatched layouts. |
| Scroll reproduction before fix | Headless production ArrangementView at 240→20 and 20→240 BPM | expected failure | Six failed checks, including unreachable final clip. |
| Move reproduction after fix | `dotnet run --project tests/Nota.SmokeTest -c Release -- --tempo` | pass | All 143 checks, including 42 move checks, pass. |
| Real-view reproduction and merge integration | `dotnet run --project tests/Nota.TempoUiTest -c Release -- out/pr568-fixed-ui.png` | pass | All 73 checks; actual final clip is on screen, mapped extent is stable, custom keys work. |
| Full regression suite | `scripts/build-win.ps1` in the VS x64 developer shell | pass | Native/managed builds, scanner self-test, full smoke including localization/key bindings/render-pool guards, and scan-scale; exit 0. |
| Visual inspection | Opened headless scrollbar and Shortcuts captures | pass | Final clip visible; filtered Shortcuts pane retains tempo gestures within develop's editor. |
| Whitespace | `git diff --check` and `git diff --cached --check` | pass | No whitespace errors. |
| Merge resolution | Unmerged-index and conflict-marker searches | pass | Both conflicts resolved without dropping either branch's changelog entries. |
| Documentation/capabilities | nota-docs, nota-features and nota-shortcuts audit | pass | Fix restores existing behavior. Defaults remain valid; five gesture rows migrated to the new layout. |
| Hosted CI / Linux performance gate | GitHub checks after publishing | not-run | Work kept local; no push requested. |

## Output Excerpts

Before:

```text
[FAIL] tempo move: lift the whole source span and keep its neighbor (method 0, slow source False, locked False)
[FAIL] tempo move: mismatched layouts carry the complete mix span and retain device automation, block True
[FAIL] last clip is reachable through the scrollbar (240 → 20)
```

After:

```text
[PASS] tempo move: lift the whole source span and keep its neighbor (method 0, slow source False, locked False)
[PASS] last clip is reachable through the scrollbar (240 → 20)
[PASS] rebound Cancel keeps its own modifiers while the mapping modifiers are held
[PASS] Settings → Shortcuts renders the tempo gesture group in its editable pane
SCAN SCALE PASSED
```

Local logs (ignored build output): `build/pr568-baseline-tempo.log`,
`build/pr568-baseline-ui.log`, `build/pr568-fix-tempo.log`,
`build/pr568-fixed-ui.log`, `build/pr568-full-build.log`.
Screenshots: `out/pr568-fixed-ui-scroll.png` and
`out/pr568-fixed-ui-shortcuts.png`.

## Residual Risks

- Windows verified locally; macOS/Linux and hosted performance checks remain for CI.
- The sibling docs checkout's shortcut generator still targets the old static list;
  its catalog/layout migration is separate from these app fixes. No sibling files
  or GitHub review threads were modified.
- Automation offsets retain their existing beat-relative semantics. This corrects
  span selection and removal without introducing time-stretching of automation.

## Recommendation

Both review findings are verified locally. Publish the completed branch when ready,
and require hosted cross-platform and performance checks before merging PR #568.
No extension hooks are registered for bug fixing or verification.
