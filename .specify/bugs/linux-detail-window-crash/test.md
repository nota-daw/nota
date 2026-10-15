# Bug Verification: Linux — app aborts when the piano roll is popped out into its own window

- **Slug**: linux-detail-window-crash
- **Tested**: 2026-10-15
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The original reporter's setup (Fedora, X11 back end, AppImage) no longer crashes when the piano roll is
popped out, with a build that contains only this fix on top of `develop`. The headless repro fails with the
exact exception without the fix and passes with it. The full build and smoke test pass.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix), real environment | Tester on Fedora ran `Nota-0.53.1-detailfix-x86_64.AppImage` (sha256 `d2c46a0a…f4f87`, built from `develop` HEAD `3d38e2f` + the fix only) and popped out Devices / Clip | pass | Reported by the user: "Все ок, работает". The same action crashed 0.53.x before. |
| Headless repro, generic | `/tmp/crosswinharness`: `NOTA_DATA_DIR=/tmp/crosswinharness/data dotnet run` | pass | Raw cross-window move throws `InvalidateArrange on wrong LayoutManager`; the real `MainWindow.SetHost` doesn't |
| Headless repro, real MainWindow | same harness, `AppCase` | pass | After pop-out, the main window's layout queue holds only its own `Grid` (before the fix it held dozens of DetailWindow controls); pop-out and re-dock run without exceptions |
| Regression suite | `scripts/build.sh` (macOS arm64) | pass | Run during `/speckit-bug-fix`, on the same source as now (no changes since): exit 0, `SMOKE TEST PASSED` |
| Linux x64 build | `scripts/package-linux.sh` in `mcr.microsoft.com/dotnet/sdk:10.0` (linux/amd64) | pass | Native engine + self-contained publish + AppImage |
| Lint / type-check | — | not-run | No separate linter; the C# compile in the build/harness is the type-check |

## Output Excerpts

```
B shown=True resized=True: raw move → ArgumentException: Attempt to call InvalidateArrange on wrong LayoutManager. | SetHost → ok
app: main queue after pop-out: m[Grid] a[]
PASS app: pop out
PASS app: re-dock (clip editor in nothing)
ALL PASS
```

## Residual Risks

- The Fedora check is a report relayed by the user (one machine, X11). Wayland-native and other desktops
  were not tried. Avalonia 12 runs through XWayland there, so this is the same back end.
- No automated test in the repo. The harness lives in `/tmp` and isn't in CI, so a regression in `SetHost`
  would only show up on Linux by hand.
- Windows and macOS were not re-checked by hand. The change only adds a layout flush when a control moves
  between windows. The smoke test passes.

## Recommendation

Close the bug — verified end-to-end on the reporter's Linux setup plus headless repro. Commit the two source
files and the CHANGELOG entry. Keep the `CHANGELOG.md` hunk separate from the unrelated Linux settings-folder
entry that's also uncommitted there. A headless UI test project for checks like this is a separate decision
(new dependency).
