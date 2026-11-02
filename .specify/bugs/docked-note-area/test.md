# Bug Verification: Short docked MIDI editors lose their notes

- **Slug**: docked-note-area
- **Tested**: 2026-11-02
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The real MainWindow and ClipEditorView reproduce a zero-height note viewport on
the old code at a restored 190-pixel docked panel. The fix keeps notes and all
five tabs usable, including with extra lanes, the clip strip, horizontal scrollbar
and docked Mixer.

## Checks Performed

| Check | Command / Action | Result | Notes |
| --- | --- | --- | --- |
| Before-fix reproduction | Build `MainWindow.Detail.cs` from `a80d1654`, run expanded CCProbe | Expected failure, exit 1 | Panel 190, roll 119, notes 0 |
| After-fix UI regression | `dotnet run --project specs/071-multiple-cc-lanes/validation/CCProbe.csproj -c Release -- specs/071-multiple-cc-lanes/validation` | Pass, exit 0 | 60 assertions, real docked host and pointer selection |
| Fresh-process settings | `dotnet specs/071-multiple-cc-lanes/validation/bin/Release/net10.0/CCProbe.dll --read-lanes specs/071-multiple-cc-lanes/validation` | Pass, exit 0 | Lane heights still restore independently |
| Windows build gate | `scripts/build-win.ps1` in VS developer PowerShell | Pass, exit 0 | Native and managed build, scanner self-test, smoke and scan-scale suites |
| Diff check | `git diff --check` | Pass | No whitespace errors |
| Visual comparison | Real MainWindow capture, same MIDI note and requested panel height | Pass | Before no notes; after note grid visible |

## Output Excerpts

Before: `Docked layout: panel 190, roll 119, notes 0`

After: `Docked layout: panel 300, roll 229, notes 104`

With clip strip, scrollbar and extra lanes: `notes 57, primary 72`.

With Mixer: `window 869, panel bottom 847, notes 57`; note and all five tabs
received real pointer hits and selection events. Hiding the Mixer restored the
original 680-pixel window minimum; non-MIDI Detail content retained 120 pixels.

`SMOKE TEST PASSED` / `SCAN SCALE PASSED`.

## Residual Risks

Windows x64 tested; macOS/Linux not run locally. The full app build reports existing
warnings. With both panels docked, the window minimum grows to fit both; available
desktop space depends on the screen. No extension hooks are registered.

## Recommendation

Accept the docked editor correction in PR #425; the original zero-note layout
no longer reproduces.
