# Bug Fix: Linux — app aborts when the piano roll is popped out into its own window

- **Slug**: linux-detail-window-crash
- **Fixed**: 2026-10-15
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

When a control moves into another window, the old window's pending layout now runs between the
detach and the attach. Before, entries for the piano roll / device chain controls stayed in the main
window's layout queue after they had moved into `DetailWindow`. A later frame could then re-queue
them on the wrong `LayoutManager`, and Avalonia threw `ArgumentException`, which aborted the process.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.App/MainWindow.DetailWindow.cs` | modified | `SetHost`: when the source and target hosts are in different top-levels, detach, then `from.UpdateLayout()`, then attach |
| `src/managed/Nota.App/DetailWindow.cs` | modified | `OnClosing`: detach both hosts, then `UpdateLayout()`, before `DockRequested` re-hosts them in the main window |
| `CHANGELOG.md` | modified | `[Unreleased] → Fixed` entry |

## Diff Highlights

```csharp
if (content?.Parent is ContentControl prev && !ReferenceEquals(prev, host))
{
    var from = TopLevel.GetTopLevel(prev);
    prev.Content = null;
    if (from is not null && !ReferenceEquals(from, TopLevel.GetTopLevel(host))) from.UpdateLayout();
}
```

## Tests Added or Updated

No automated test in the repo. The smoke test doesn't reference Nota.App or Avalonia.Headless, and adding
a headless UI test project would be a new dependency the assessment didn't settle. The checks below ran
in a throwaway harness, `/tmp/crosswinharness`: a console project with a ProjectReference to Nota.App plus
Avalonia.Headless and Avalonia.Skia 12.0.3. Run it with `NOTA_DATA_DIR=/tmp/crosswinharness/data dotnet run`.

- **Generic case:** a control is queued for layout in window A, moved into window B, then B is resized and
  re-measured after `Show` (like X11's ConfigureNotify / scale event), then A's layout pass runs.
  - Raw move: `ArgumentException: Attempt to call InvalidateArrange on wrong LayoutManager.` This is the
    exact exception from the tester's log.
  - Through the real `MainWindow.SetHost` (reflected): before the fix it throws the same; after the fix, ok.
- **Real app:** a headless `MainWindow` with a MIDI clip open in the docked piano roll. Queue live
  updates, call `OnToggleDetailWindow`, then dump the main window's layout queue.
  - Before the fix: dozens of entries (ContentPresenter, Grid, ClipPropsView, …) that belong to
    `DetailWindow`. This is the stale state that crashes on X11.
  - After the fix: only the main window's own `Grid` (the collapsed detail row).
  - Pop-out and re-dock both run without exceptions.

## Local Verification

- `scripts/build.sh` (macOS arm64) → exit 0, `SMOKE TEST PASSED`.
- Headless harness as above → `ALL PASS` with the fix; `1 FAIL` (the exact exception) without it.
- Not run on Linux. The tester on Fedora (X11) should confirm, see `/speckit-bug-test`.

## Deviations from Assessment

- **Order of the flush.** The assessment proposed `UpdateLayout()` *before* the detach. The harness showed that
  the detach itself (`prev.Content = null`) queues the moved control and its children in the old window's
  layout manager again. So the flush runs *after* the detach and before the attach, in both `SetHost` and
  `DetailWindow.OnClosing`.
- **Mixer left alone.** The assessment expected the mixer pop-out to crash too. `_mixer` is created
  without a parent in the main window (`MainWindow.axaml.cs:387`), so nothing of it can be queued there when
  it moves to `MixerWindow`. `MainWindow.Mixer.cs` and `MixerWindow.cs` are unchanged.
- **No headless test in the repo** (see above).

## Follow-ups

- Ask the Fedora tester to try the build: pop out, close, pop out again, during playback.
- Optional safety net: `Dispatcher.UIThread.UnhandledException` → log, so a UI-only exception doesn't
  kill the session. This needs a separate decision, because it can hide broken state.
- A headless UI test project would let checks like this live in CI. Decide on the dependency first.
