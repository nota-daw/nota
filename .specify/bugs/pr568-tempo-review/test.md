# Bug Verification: PR #568 tempo-map review findings

- **Slug**: pr568-tempo-review
- **Tested**: 2026-11-11
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Status**: passed locally

## Automated Verification

- `dotnet run --project tests/Nota.TempoUiTest/Nota.TempoUiTest.csproj -c Debug -- out/tempo-ui-review.png`
  passed all checks, including the new first-gesture coordinate regression and the existing
  zoomed/scrolled stepped-transition checks.
- `dotnet run --project tests/Nota.SmokeTest/Nota.SmokeTest.csproj -c Debug -- --tempo-map`
  completed with `SMOKE TEST PASSED` before the final focused assertions were added.
- `pwsh scripts/build-win.ps1` passed: native engine, scanner self-test, managed Release
  build, complete smoke suite (including the new assertions), design checks, and scan-scale test.

## Finding Coverage

| Finding | Verification |
|---------|--------------|
| Moved mapped audio cache | Cache identity includes `startBeat`; deferred installation rechecks placement. |
| First mapping gesture | Headless pointer test at legacy 60 BPM passes. |
| Unsynchronised LFO clock | Code path now receives elapsed samples independently of mapped musical samples. |
| Loop presentation drift | Existing mapped loop smoke test passes; sample-domain re-anchoring covers both clock modes. |
| Project-load map edit | New `RestoreBpm` regression preserves the tempo-point sequence. |
| 1000 BPM reload failure | New boundary assertions accept 999 and reject 1000. |
| Nonlinear navigation | All affected navigation and resize paths use the shared conversion helpers. |
| Synchronous mapped stretch | Mapped caches use the existing deferred sliced/background worker and stale-job guards. |

## Remaining Issues

- None found in the corrected behavior. The PR's earlier performance workflow reported a
  broad fixed instruction-count increase in the original feature commit; the review fix
  does not add work to the audio callback, and the updated branch will rerun that gate.
