# Bug Fix: New project (Ctrl+N) discards unsaved changes and leaves a blank detail panel

- **Slug**: new-project-unsaved-prompt
- **Fixed**: 2026-10-20
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

File → New, File → Open and opening a project from the browser now go through the
Save / Don't save / Cancel prompt, which now handles untitled projects like the quit prompt
does. A load now closes the docked detail panel completely (row collapsed, splitter hidden),
and a popped-out detail window is pointed at the new project's chain.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.App/MainWindow.History.cs` | modified | `ConfirmLeaveChangesAsync`: untitled → dialog closes, then `DoSaveAsync`; go on only if it saved |
| `src/managed/Nota.App/MainWindow.Recovery.cs` | modified | `ConfirmCloseAsync` reuses `ConfirmLeaveChangesAsync` (same behaviour, no duplicate) |
| `src/managed/Nota.App/MainWindow.Files.cs` | modified | `OnMenuNew` → `DoNewAsync` with the prompt; prompt in `DoOpenAsync` after the folder pick; `OpenProjectConfirmedAsync` for the browser; `RefreshAfterLoad` uses `ResetClipEditor()` + `CloseDetail()` and re-points a floating window |
| `src/managed/Nota.App/MainWindow.Browser.cs` | modified | browser Project item → `OpenProjectConfirmedAsync` |
| `src/managed/Nota.App/MainWindow.Detail.cs` | modified | `OnCloseDetail` body factored into `CloseDetail()` |
| `CHANGELOG.md` | modified | [Unreleased] → Fixed |

## Diff Highlights

```csharp
private async Task DoNewAsync()
{
    if (_vm is null) return;
    if (!await ConfirmLeaveChangesAsync("New project",
            "Save your changes before starting a new project?")) return;
    …
```

```csharp
ResetClipEditor();               // every clip editor points into the old graph
CloseDetail();                   // collapse the docked row too, not just hide the panel
…
if (DetailFloating) ShowDevices(Timeline.SelectedTrackId > 0 ? Timeline.SelectedTrackId : TrackIdAt(0));
```

## Tests Added or Updated

- None in the repo: the smoke test drives the engine, not MainWindow dialogs. Covered by the
  headless harness below.

## Local Verification

- `scripts/build.sh` → build OK, SMOKE TEST PASSED.
- Headless harness (`/tmp/newprojharness`, real `MainWindow` on Avalonia.Headless):
  - New on a clean project → no prompt.
  - Add a track + open Devices panel → dirty, row 305 px.
  - New → "New project" prompt; Cancel → still 5 tracks, still dirty, panel unchanged.
  - New → Don't save → template (4 tracks), clean, panel/splitter hidden, row 0 px.
  - Popped-out detail window, dirty, New → prompt; Don't save → window shows track 1's chain.
- Not exercised: Save from the prompt (needs the native file panel / a named bundle on disk);
  the code path is the one the quit prompt already used.

## Deviations from Assessment

- The floating window uses the selected track, else the first track — `LastInstrumentTrack()`
  only remembers instruments added this session and is 0 right after a load.

## Follow-ups

- None.
