# Bug Assessment: Folded CC lane gutter

- **Slug**: folded-cc-gutter (generated from the report)
- **Created**: 2026-11-02
- **Source**: https://github.com/nota-daw/nota/pull/425#
- **Verdict**: valid
- **Severity**: high

## Report

The review reports that adding a controller lane to an already folded Drum Rack
leaves its gutter at 48 pixels while the notes use 96, shifting edits to another beat.
GitHub host policy: allowlisted; PR and review comments retrieved through GitHub's API.
Review comment: https://github.com/nota-daw/nota/pull/425#discussion_r4441629915
Approved issue #422 remains open and covers this correction to its lane implementation.

## Symptom and reproduction

1. Open a Drum Rack clip and enable Fold.
2. Add a CC lane without changing the pad rows.
3. Observe its left edge and click beneath a note: the lane starts 48 pixels too far left.
4. Refresh the unchanged folded rows; the misalignment remains.

## Suspected Code Paths and Root Cause

High confidence: `PianoRollView.ControllerLanes.cs::AddControllerLane` initializes
the new column with `KeysW`, then registers it for future updates only.
`PianoRollView.Fold.cs::RebuildRows` returns early for unchanged rows before updating
registered columns. `_rowsArePads` already records the current layout mode.

## Proposed Remediation

Initialize the column from `_rowsArePads ? FoldedPadKeysW : KeysW`. Keep registration
in `_keyColumns` so later fold/unfold transitions continue to update it.

Files likely to change: `src/managed/Nota.App/PianoRollView.ControllerLanes.cs`,
`specs/071-multiple-cc-lanes/validation/Program.cs`, `validation.md`, and CHANGELOG.md.
Add a real Avalonia regression covering addition after folding, unchanged-row refresh,
pointer edits beneath a note, unfolding/refolding, and removal/readdition. Capture
matching before/after images with the probe. Run the Windows build/smoke gate.

## Risks & Considerations

UI-only initialization fix; no new state, dependencies, public API or audio-thread work.
Retain the normal 48-pixel melodic gutter and existing fold transitions. No manual,
capability-list or shortcut changes: this restores the already documented alignment.

## Open Questions

None. Extension configuration has no hooks.
