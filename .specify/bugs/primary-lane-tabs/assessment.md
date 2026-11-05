# Bug Assessment: Primary lane tabs become unreachable

- **Slug**: primary-lane-tabs (generated from the report)
- **Created**: 2026-11-02
- **Source**: https://github.com/nota-daw/nota/pull/425#
- **Verdict**: valid
- **Severity**: high

## Report and symptom

Shrinking the primary lane to 48 pixels hides its fifth tab behind the CC toolbar.
Saved heights restore the same inaccessible controller selector.
Review: https://github.com/nota-daw/nota/pull/425#discussion_r4441629908
URL policy: github.com is allowlisted; fetched PR review through GitHub's API.
Approved issue #422 remains open and covers the lane implementation being corrected.

## Reproduction

1. Drag the primary lane divider down until its row reaches 48 pixels.
2. Try clicking the controller tab at y=60: the following toolbar receives the hit.
3. Reopen the editor with a saved primary height of 48 and repeat.

## Suspected Code Paths and Root Cause

`PianoRollView.ControllerLanes.cs::FitControllerLanes` uses the generic 48-pixel
minimum for the primary lane, including the available-space cap. `LaneTabs` in
`PianoRollView.Expression.cs` draws five fixed rows within the original 72-pixel
velocity height. Its last row is outside a 48-pixel control. Confidence: high.

## Proposed Remediation

Use `VelH` (72) as the primary row's minimum after both saved-height normalization
and available-space fitting. Extra CC lanes retain the 48-pixel minimum. This also
makes old saved values usable without changing the settings schema or tab geometry.

Files likely to change: `PianoRollView.ControllerLanes.cs`, the existing CCProbe,
CHANGELOG.md, the lane spec/plan/data model and validation report. Add bug fix/test
reports here. Keep the documented workflow and shortcuts unchanged.

Tests: real divider drag; actual visual hit testing and pointer selection of all five
tabs; old saved heights loaded from disk into a reopened editor; short-window layout;
extra lanes still shrink to 48; no MIDI commits. Capture matching before/after images.
Run the complete Windows build gate and expanded UI probe.

## Risks & Considerations

The primary lane reserves 24 more pixels at minimum; check the existing short-editor
note viewport guard. No engine, dependencies or settings migration is needed.
The generic height helper still normalizes stored preferences to 48–480; the primary
view imposes its stronger layout minimum. No extension hooks are registered.

## Open Questions

None.
