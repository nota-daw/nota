# Bug Assessment: "Edit maps…" doesn't bring up the articulation map manager

- **Slug**: articulation-manager-reopen
- **Created**: 2026-11-09
- **Source**: pasted text (owner, in the session building specs/085)
- **Verdict**: likely valid, needs reproduction
- **Severity**: medium

## Report (verbatim or summarized)

> "the edit map button doesnt show up sometimes" — "after changing the values in note offset and
> saving it" — "The button is there, but clicking it doesn't bring up the manager."

## Symptom

After editing an articulation's note offset in the manager and saving, clicking "Edit maps…" in the
track inspector does nothing visible. Expected: the manager comes to the front (or opens).

## Reproduction

1. Open the manager from the inspector's "Edit maps…", change a note offset, Save.
2. [NEEDS CLARIFICATION: what happened to the manager window between Save and the next click —
   closed, minimized, or left open?]
3. Click "Edit maps…" again: no manager appears.

## Suspected Code Paths

- `src/managed/Nota.App/ArticulationMapManager.cs:Open()` — with an instance already recorded in
  `_open`, it only calls `w.Activate()`. `Activate` does not restore a minimized window, and the
  manager is owned by the main window, so it has no taskbar button of its own to restore it from:
  a minimized manager can't be brought back by the button or the menu.
- Same method — it trusts `_open` blindly. If the window is no longer shown but `Closed` hasn't
  cleared `_open`, every click activates a window nobody can see. `IsVisible` is never checked.
- Same method — the new window is shown synchronously inside the click; `LensWindow` and the scope
  window post the show/activate until after the click finishes so the clicked window doesn't take
  activation back.

Checked and ruled out: `TrackInspectorView.ArticulationsBody` always adds the button for instrument
tracks (the owner confirms it's there); its click goes to `MainWindow.ShowArticulationMaps`. The log
`%APPDATA%\Nota\logs\nota-20261109.log` has no exception, so nothing threw and was swallowed.

## Root Cause Hypothesis

The manager instance still exists but isn't visible — most plausibly minimized (an owned window with
no taskbar button) — and `Open` only calls `Activate()`, which neither restores nor re-shows it.
Confidence: medium. Saving itself doesn't hide the window in any code path found; it is the step
before the window was put away.

## Proposed Remediation

**Preferred**: make `Open` bring an existing manager back whatever its state: drop a recorded
instance that is no longer visible and open a new one; restore a minimized one to Normal; then
activate it after the click has finished (`Dispatcher.UIThread.Post`, as `LensWindow` does). Show a
new manager the same way.

**Files likely to change**:
- `src/managed/Nota.App/ArticulationMapManager.cs`

**Tests to add or update**:
- No window harness exists in the smoke test (no Avalonia.Headless). Verification is manual: minimize
  the manager, click "Edit maps…" — it comes back; close it, click — a new one opens; leave it open
  behind a dialog, click — it comes to the front.

## Risks & Considerations

- Restoring a minimized window while it holds unsaved edits must keep them (it does: the instance is
  reused, only its state changes).

## Open Questions

- [NEEDS CLARIFICATION: was the manager minimized, closed, or still open when the click did nothing?]
