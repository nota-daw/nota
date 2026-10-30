# Implementation Plan: Clip Editor Track Mode

**Branch**: `cursor/clip-editor-track-mode-6414` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

## Summary

Track mode is a spec 029 open clip set holding every MIDI clip of one track, on the
arrangement axis. `ClipSet` gains a `TrackMode` flag (origin at arrangement beat 0, reads as
multi-clip even with one clip) and `Add` (a clip that appeared on the track joins without a
rebuild). `MainWindow` expands the set to the track when the mode is on, follows the selection
by switching the active clip, and saves the mode in settings. The inspector's Clip section gets a
Clip | Track segmented control (the header has no room left at the default window width).

## Technical Context

- **Language**: C# / .NET 10, Avalonia. No engine change, no new C ABI call.
- **Touched**: `Nota.Application/Editing/ClipSet.cs`, `Nota.Application/Settings.cs`,
  `Nota.Application/Palette/CommandCatalog.cs`, `Nota.App/MainWindow.Detail.cs`,
  `Nota.App/MainWindow.Commands.cs`, `Nota.App/ClipEditorView.cs`, `Nota.App/ClipPropsView.cs`,
  `Nota.App/PianoRollView.cs`.
- **Testing**: `tests/Nota.SmokeTest/MultiClipTests.cs` (`--multiclip`), full smoke via
  `scripts/build-linux.sh`.

## Constitution Check

- Audio thread: untouched (edits go through `SetClipNotes` / `SetClipNotesLive` as today).
- AR-7: no new native crossing. No new dependency. No new files beyond the spec.

## Design

- `ClipSet(..., trackMode)`: `Origin = 0`, `IsMulti = Count > 1 || TrackMode`. Every multi-clip
  path in the roll (clip strip, per-slot colours, envelope offset, rescroll, seek/playhead via
  `Origin`) then works for a one-clip track too.
- `ClipSet.Add(OpenClip)`: inserts in start order and keeps the active clip; the host reloads
  the roll's notes right after, so note owners are rebuilt from the engine.
- `MainWindow`: `ClipTrackMode` (setting), `SetClipTrackMode` (switch + rebuild on the active
  clip), `MidiClipsOf(track)`, `FocusTrackClip` (same-track selection → `SetActiveClip` +
  `RevealClip`, no rebuild), `AddNewTrackClips` in `ReloadEditorNotes` and on a geometry change
  of a clip not yet open. `BuildClipEditor` swaps its clip list for the track's in track mode,
  so every caller (double-click, Clip tab, retarget after moves) gets it for free.
- `PianoRollView.RevealClip(slot)`: scrolls a clip's start into view when it is off screen.

## Complexity Tracking

None.
