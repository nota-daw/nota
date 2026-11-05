# Bug Fix: Primary lane tabs become unreachable

- **Slug**: primary-lane-tabs (from assessment context)
- **Fixed**: 2026-11-02
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary and Changes

`PianoRollView.ControllerLanes.cs::FitControllerLanes` clamps the final primary row
height to the existing `VelH` (72 pixels), after saved preference normalization and
available-space fitting. Extra CC rows still use the generic 48-pixel minimum.
No preference migration is required. Spec, plan, data model and changelog reflect
the distinction between saved values and the primary layout minimum.

## Tests Added or Updated

`CCProbe::CheckPrimaryLaneTabs` drives the divider, hit-tests all five tabs through
the actual window visual tree and sends pointer events to select each mode. It
reopens a short editor using preferences reloaded from disk, tests the controller
tab again and checks the note viewport, extra lane minimum and absence of MIDI edits.
It captures the same content before and after the fix for comparison.

## Local Verification

Before the fix, the expanded probe exits 1 with
`Controller tab receives pointer hit after shrinking primary lane`.
The definitive old-code run waits for the compositor's hit-test scene and reports
`hit ContentPresenter`, `row 48`, confirming that the toolbar receives the hit.
Post-fix checks and build results are recorded in test.md.

## Deviations from Assessment

None. Extension configuration has no hooks. Design uses existing tab geometry and
the existing 72-pixel constant. The nota-docs audit needs no manual change: resizing
and tab selection already have this documented workflow. FEATURES and shortcuts
introduce no new capability or binding and need no changes.

## Follow-ups

Complete the Windows gate and regression checks, attach evidence and update PR #425.
