<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Verification: Mapped copies, paste automation and partial takes

- **Slug**: tempo-map-copy-takes (from this session's assessment/fix)
- **Tested**: 2026-11-14
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

All three reports were reproduced through the production C ABI before remediation
and pass after the fixes. The full Windows build/smoke/scanner/scan-scale suite
passes. No source code was changed during this final verification phase.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Original reproductions | Initial `TempoMapCopyTakeTests` on `59043012`, through `--tempo` | expected failure | 86 failed assertions: 4 duplication, 10 paste automation, 72 take/comp checks. Legacy take controls passed. |
| Post-fix reproductions and focused regressions | `dotnet run --project tests/Nota.SmokeTest -c Release -- --tempo` | pass | Exit 0; 329 passing checks, including 186 new checks. |
| Full regression suite | `scripts/build-win.ps1` from VS x64 developer shell | pass | Exit 0; native/managed builds, scanner self-test, full smoke including render-pool guards, localization and scan-scale checks. |
| Real comp operations | Restore partial takes, comp, edit section, then swipe another comp range | pass | Selected source offsets and timeline positions match the moved first recorded frame in both tempo directions, ramps, two sample rates and legacy tempo. |
| Snapshot history | Undo/redo each duplicate, paste and section operation | pass | Audio, automation, take ids/groups and take/comp geometry restored together; no extra undo step. |
| Stable-span controls | Duplicate a range containing gaps at legacy and mapped constant tempo | pass | Original selected-range silence/overwrite semantics preserved. |
| Whitespace | `git diff --check` | pass | No whitespace errors. |
| Documentation/capabilities | nota-docs / nota-features audit | pass | Existing workflows restored; no new capability, control or shortcut to document. |
| Hosted CI / engine performance gate | Checks on the new published commit | not-run locally | Must complete on GitHub after pushing. |

## Output Excerpts

Before:

```text
[FAIL] tempo copies: duplicate clears the actual landing span (slow source True, reverse False, warped False)
[FAIL] tempo copies: clear automation only within the destination span (method 0, slow source False, cross-track False)
[FAIL] tempo takes: comp swipe selects the original recorded samples after section edit (move later, 120→60 Step, mapped True, 44100 Hz)
```

After:

```text
Passed: 329, failed: 0
SMOKE TEST PASSED
SCAN SCALE PASSED
```

Ignored local logs: `build/tempo-copy-takes-before.log`,
`build/tempo-copy-takes-native.log`, `build/tempo-copy-takes-after.log`,
`build/tempo-copy-takes-build.log`.

## Residual Risks

- Local verification was on Windows. macOS/Linux and the hosted performance gate
  remain CI responsibilities for the new commit.
- Captured automation keeps the existing beat-relative point offsets; the fix
  changes destination clearing, not automation stretching.
- No audio-device recording was required for reproduction. Tests use restored
  real sample buffers and production comp/edit APIs without an audio device.
- No UI control/layout or shortcut changed, so no new headless UI run was required.

## Recommendation

All three review findings are verified locally. Push this follow-up to PR #568 and
require hosted cross-platform/performance checks before merging. No workflow hooks
are registered in `.specify/extensions.yml`.
