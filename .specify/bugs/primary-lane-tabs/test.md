# Bug Verification: Primary lane tabs become unreachable

- **Slug**: primary-lane-tabs
- **Tested**: 2026-11-02
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The original controller-tab obstruction reproduces on the old code with a real
window hit test. The fixed primary minimum keeps every tab clickable after dragging,
mode changes, disk-restored preferences and reopening in a short window.

## Checks Performed

| Check | Command / Action | Result | Notes |
| --- | --- | --- | --- |
| Reproduction before fix | Temporarily build ControllerLanes.cs from `20cbc8f3`; run the expanded CCProbe | Expected failure, exit 1 | Controller tab's position hits toolbar ContentPresenter; row height 48 |
| After fix / UI regression | `dotnet run --project specs/071-multiple-cc-lanes/validation/CCProbe.csproj -c Release -- specs/071-multiple-cc-lanes/validation` | Pass, exit 0 | 39 checks, including actual tab hits/selections, saved preferences and extra lane minimum |
| Fresh process | `dotnet specs/071-multiple-cc-lanes/validation/bin/Release/net10.0/CCProbe.dll --read-lanes specs/071-multiple-cc-lanes/validation` | Pass, exit 0 | Existing independent heights still restore |
| Full Windows gate | `scripts/build-win.ps1` in VS developer PowerShell | Pass, exit 0 | Native and managed build, scanner self-test, complete smoke suite, scan-scale |
| Restored build | `dotnet build src/managed/Nota.App/Nota.App.csproj -c Release --nologo` | Pass | Restores fixed binaries after the old-code reproduction |
| Whitespace | `git diff --check` | Pass | No whitespace errors |
| Visual comparison | Studio Light, 1000 × 720, same clip and shrink gesture | Pass | Fifth tab visible after fix |

## Output Excerpts

Before: `Tab hit mismatch: expected Controller, hit ContentPresenter, tabs 0, 1, 47, 47, row 48, point 20, 710`

After: `Controller tab receives pointer hit after shrinking primary lane`

`reopened short editor clamps saved 48-pixel primary height to 72`

`SMOKE TEST PASSED` / `SCAN SCALE PASSED`

## Residual Risks

Windows x64 tested; macOS/Linux not run locally. Existing compiler warnings remain.
The harness waits for the compositor to publish new-window hit-test data before each
tab check; without that wait a null target is a harness timing artifact.
No extension hooks are registered.

## Recommendation

Accept this review correction in PR #425. All five primary tabs remain accessible.
