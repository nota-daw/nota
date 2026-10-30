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
- **Primary Dependencies**: none new (Avalonia as today).
- **Storage**: one new app setting, `ClipEditorTrackMode` (settings JSON); nothing in the
  project file.
- **Audio-Thread Impact**: None. The change is UI-thread only: which clips the editor opens and
  how it lays them out. Note edits still reach the engine through the existing
  `SetClipNotes` / `SetClipNotesLive` C ABI calls, grouped with `BeginUndoGroup` /
  `EndUndoGroup` as in spec 029; the engine publishes its snapshot for them as before (AR-5).
  No allocation, lock, I/O or logging is added on the audio thread (AR-6), no command-queue
  change (AR-4), and the playhead / seek still convert through `Transport` beats (AR-9).
- **Testing**: `tests/Nota.SmokeTest/MultiClipTests.cs` (`--multiclip`), full smoke via
  `scripts/build-linux.sh`.

## Nota: Read Before Planning

UI work → [`DESIGN.md`](../../DESIGN.md): the switch is the existing `segmented` control, the
active clip keeps the orange accent in the clip strip, no new colours.
[`CONTRIBUTING.md`](../../CONTRIBUTING.md): no new dependency, no new source file.

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | Pass — no audio-thread code touched (see Audio-Thread Impact) |
| II. Boundary discipline | Pass — no new C ABI call, no JUCE change |
| III. Licensing hygiene | Pass — no new dependency, no new source file |
| IV. Verification gate | Pass — no engine behaviour change; `ClipSet` track-mode rules added to the smoke test |
| V. One logical change | Pass — one feature |
| VI. Docs in sync | CHANGELOG `[Unreleased]` and FEATURES.md in this change; the user manual (`../nota-docs`, piano-roll page EN + RU) follows in a nota-docs change; no new shortcut, so the Shortcuts pane is unchanged |
| VII. Spec-first | Pass — this spec |
| VIII. Rules not weakened | Pass |

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
