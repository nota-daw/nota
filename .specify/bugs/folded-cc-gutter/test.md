# Bug Verification: Folded CC lane gutter

- **Slug**: folded-cc-gutter
- **Tested**: 2026-11-02
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The original wrong-beat pointer edit reproduces before the fix and passes after it.
All 22 UI assertions and the complete Windows build gate pass.

## Checks Performed

| Check | Command / Action | Result | Notes |
| --- | --- | --- | --- |
| Before-fix reproduction | Expanded CCProbe against `836f736c` app binaries | Expected failure | Double-click beneath beat-2 note writes at another beat |
| Post-fix reproduction and UI regression | `dotnet run --project specs/071-multiple-cc-lanes/validation/CCProbe.csproj -c Release -- specs/071-multiple-cc-lanes/validation` | Pass, exit 0 | 22 assertions; actual layout, pointer events, CC callback and fold transitions |
| Fresh-process restoration | `dotnet specs/071-multiple-cc-lanes/validation/bin/Release/net10.0/CCProbe.dll --read-lanes specs/071-multiple-cc-lanes/validation` | Pass, exit 0 | Saved lane heights restored |
| Build and regression gate | `scripts/build-win.ps1` in VS developer PowerShell | Pass, exit 0 | Native + managed, scanner self-test, complete smoke and scan-scale suite; existing pinned CPM sources reused |
| Whitespace | `git diff --check` | Pass | No whitespace errors |
| Visual evidence | Matching Studio Light 1000 × 720 captures | Pass | Before gutter starts at 48; after aligns with notes at 96 |

## Output Excerpts

Before: `System.Exception: click directly below a folded drum note writes CC at the same beat`

After: `click directly below a folded drum note writes CC at the same beat`

`SMOKE TEST PASSED` and `SCAN SCALE PASSED`.

## Residual Risks

Windows x64 verified; macOS/Linux not run locally. Existing compiler warnings remain.
The separate review finding concerning primary-lane tab reachability is outside this
reported gutter correction and is not claimed resolved here. No extension hooks exist.

## Recommendation

Accept the folded-gutter correction in PR #425; the original interaction is verified.
