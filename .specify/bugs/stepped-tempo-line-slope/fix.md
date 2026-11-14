# Bug Fix: Stepped tempo transitions render slightly diagonal

- **Slug**: stepped-tempo-line-slope
- **Fixed**: 2026-11-11
- **Assessment**: ./assessment.md
- **Issue**: nota-daw/nota#535
- **Status**: applied

## Summary

The tempo lane still samples the authoritative compiled tempo map for linear and smooth
curves. When a sampled interval crosses a stepped tempo event, the renderer now splits
that interval at the event's exact `BeatToX` coordinate: it holds the preceding BPM to
that X, draws the jump with the same X at both ends, and continues from the new BPM.
No tempo-engine or playback calculation was changed.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.App/ArrangementView.Tempo.cs` | modified | Split sampled paths at stepped boundaries and derive both jump endpoints from one transformed event X. |
| `tests/Nota.TempoUiTest/Program.cs` | modified | Assert exact shared X and old/new BPM endpoints, including after zoom and horizontal scrolling. |

## Verification

- Headless tempo UI test: 27/27 checks passed.
- Rendered capture inspected at `out/tempo-ui-step-fix.png`; step transitions are vertical and coincide with their point/grid position.
- `scripts/build-win.ps1`: passed (native engine, scanner, managed build, full smoke suite, design/localization checks, and scan-scale test).
