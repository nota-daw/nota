<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Verification: Tempo-map correctness across editing and capture

- **Slug**: tempo-map-correctness (reused from the fix report)
- **Tested**: 2026-11-12
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

All seven reported cases pass their automated reproductions after the fix. The complete
Windows build/smoke/UI checks and the unchanged Linux performance gate also pass.
The develop merge has no unresolved index entries, conflict markers or whitespace errors.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Editing/freeze reproduction before fixes | Focused smoke against previous native behavior | pass (reproduced) | Eight failing assertions for forward/reversed split/trim, range slicing, freeze capacity/onsets/reload; `build/tempo-correctness-before.log`. |
| Lane/grid/waveform reproduction before fixes | Current UI regressions against 420649a6 | pass (reproduced) | Four BPM/grid checks and all eight waveform checks fail; same source/map/window/theme as after. Native sources at 420649a6 equal 44bd1eb8, whose Release library was used. |
| Original symptoms after fixes | `dotnet run --project tests/Nota.SmokeTest -c Release -- --tempo` | pass | 63 checks, including 19 new correctness checks; mapped full/partial loop passes, comp offsets, early/late frozen onsets and range bounce. |
| Rendered UI and real gestures | `dotnet run --project tests/Nota.TempoUiTest -c Release -- out/tempo-correctness-ui.png` | pass | All 40 checks; smooth/solid, forward/reverse, overview/detail waveform positions. |
| Full regression suite | `scripts/build-win.ps1` in VS x64 environment | pass | Native/scanner/managed build, full smoke, render CPU guards, scan-scale tests; exit 0. |
| Linux native/performance regression | `bash scripts/perf-gate.sh e191a57bfc6f3390b3a24d616f57aadda41d0c1c` | pass | All 64 scenarios; unchanged 1% instruction and 15% wall-time limits, five interleaved timing rounds, no acceptance override. |
| Snapshot identity | Compare native Git subtree hashes in Windows and Linux | pass | Both `c6e0bedc00387ed4d08eb391e8890fabeffe35f8`; Linux checkout is 420649a6 plus the tracked merge/fix patch. |
| Conflicts/whitespace | `git ls-files -u`, conflict-marker scan, `git diff --cached --check` | pass | Empty results. Latest fetched develop: e191a57b. |
| Docs/features/shortcuts | Audit existing claims and bindings | pass | Restores documented behavior; no workflow, capability, label, or binding changes to document separately. |

## Output Excerpts

```text
SMOKE TEST PASSED
SCAN SCALE PASSED
[PASS] tempo correctness: mapped loop capture preserves full and partial passes
[PASS] tempo correctness: frozen notes follow musical beats after the tempo map changes
[PASS] tempo correctness: legacy constant-tempo frozen PCM remains readable
[PASS] mapped Smooth waveform detail reversed peak follows its audible beat (183/0)
PASS: no scenario does more than 1% more work or takes more than 15% longer than the base.
```

The largest work increase is 0.25% (`sparse-32`), below the 1% limit. Logs are under
`build/tempo-correctness-*.log` and `build/tempo-correctness-linux/results/`; publishable
reports, raw measurements, charts and screenshots accompany the PR on `pr-assets` at
`issue-535/tempo-correctness/`. The full Windows run preceded a test-only switch to the
non-obsolete PNG encoder overload; the final UI rerun also passes all 40 checks.

## Residual Risks

- Recording tests use the real capture ring without a physical audio device.
- Local full managed/smoke validation is Windows; Linux validation builds the native
  engine and runs the complete performance gate. Hosted cross-platform CI must pass
  on the pushed commit before merging.
- This verification covers programmed tempo changes and the reported editing/capture
  cases, rather than every combination of live tempo edits during hardware recording.

## Recommendation

The seven findings are fixed and locally verified. Review the before/after evidence
and require hosted CI to pass before merging PR #568. No extension verification hooks
are registered (`.specify/extensions.yml`: `hooks: {}`).
