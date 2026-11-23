# Bug Assessment: Nota Color review

- **Slug**: nota-color-review
- **Created**: 2026-11-23
- **Source**: PR #999 Greptile review comments
- **Verdict**: valid
- **Severity**: medium

## Report and reproduction

Select a keyboard note, then undo. Change Gate or Pre enabled on a tagged preset. Write a scale value exactly halfway between choices. Undo should restore the whole keyboard edit, preset changes should be visible, and displayed notes should match the engine.

## Suspected code paths and root cause

`ColorDeviceBody.Build` performs separate undo gestures and uses midpoint-to-even rounding. `Engine::presetModified` ignores all latency configuration parameters even though Gate and Pre enabled are preset sound parameters. Confidence is high from the call paths.

## Proposed remediation

Group the note edits, use midpoint-away-from-zero rounding for root and scale, and exclude only FFT from preset comparison. Add regression coverage for preset modification and run the Color and full smoke suites. Test the keyboard interaction and grouping through a headless card where feasible.

## Risks & considerations

Preserve automation gestures and preset handling for other devices. Keep FFT outside preset comparisons.
