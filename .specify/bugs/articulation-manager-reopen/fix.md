# Bug Fix: "Edit maps…" doesn't bring up the articulation map manager

- **Slug**: articulation-manager-reopen
- **Fixed**: 2026-11-09
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The owner confirmed the manager had been minimized. It is owned by the main window, so it has no
taskbar button, and `Open` only called `Activate()`, which doesn't restore a minimized window — the
button and menu did nothing. `Open` now restores a minimized manager, replaces one that was shown and
is no longer visible, and shows / activates after the click has finished.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.App/ArticulationMapManager.cs` | modified `Open` | restore from Minimized; drop a shown-but-invisible instance (`_wasShown` set on `Opened`); `Closed` clears only its own instance; show and `Activate` posted at Background priority (the `LensWindow` / `ScopeWindow` pattern); one log line per reopen with the window state |

## Diff Highlights (optional)

```csharp
if (_open is { _wasShown: true, IsVisible: false } gone) { … _open = null; }
if (_open is { } w)
{
    if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
    if (!w.Dirty) w.Reload(w.TrackMapId());
    Dispatcher.UIThread.Post(w.Activate, DispatcherPriority.Background);
    return;
}
```

## Tests Added or Updated

- None automated: the smoke test has no window harness (no Avalonia.Headless), as the assessment
  noted. Covered by the manual checks in `/speckit-bug-test`.

## Local Verification

- `dotnet build src/managed/Nota.App -c Release -o <scratch>` → 0 errors (the normal output was locked
  by the owner's running Nota, PID 1156, so the app was compiled to a scratch folder instead).
- `dotnet build tests/Nota.SmokeTest -c Release` → 0 errors.
- Full smoke test, twice → 5392 pass, 1 fail both times: "exporting 16 tracks in 8192-frame chunks:
  render threads do 0 % of the work (>= 20 %)". This is the render-pool timing check measured earlier
  in specs/085 as pre-existing flakiness (a `git archive` HEAD baseline fails it the same way); the
  same suite passed 5393/0 earlier today with Nota closed, and this change touches only window
  opening on the UI thread. Rerun with Nota closed to confirm.
- Manual: not yet — needs the rebuilt app (see Follow-ups).

## Deviations from Assessment

- None in the fix. The open question is answered: the manager was minimized (owner, 2026-11-09).

## Follow-ups

- Close Nota, rebuild (`dotnet build Nota.sln -c Release`), rerun the smoke test, then the manual
  checks: minimize the manager → "Edit maps…" restores it; close it → a new one opens; with unsaved
  edits, minimize and reopen → the edits are still there.
