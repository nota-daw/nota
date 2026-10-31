# Implementation Plan: Event lane — MIDI CC lanes and the velocity wheel

**Branch**: `063-event-lane-cc-velocity-wheel` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

## Summary

Managed-only. The piano roll gains a fifth lane mode backed by the existing clip controller
store (specs/020 PR 1), and the note grid's Shift + wheel changes velocity when the pointer
is over a note. No engine, ABI or persistence change.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia 11. **Testing**: `tests/Nota.SmokeTest`
(`VelocityWheelTests`), `scripts/build.sh`. **Constraints**: no audio-thread work; controller
writes go through `IAudioEngine.SetMidiClipController` / `SetSessionController`, each an
undo checkpoint in the engine.

**Audio-thread impact**: none. Everything here runs on the UI thread. Controller writes use
the existing specs/020 path (`SetMidiClipController` / `SetSessionController`), which
publishes the clip's new controller curve to the audio thread the way every clip edit does;
velocity changes use the piano roll's existing note commit and live-preview path. No new
C ABI function, no engine code, no new allocation, lock, I/O or logging on the audio thread.

## Constitution Check

- AGPL headers on new files; no new dependency.
- UI follows `DESIGN.md` (Studio): neutral viz ink, accent only for hover and selection,
  sentence-case labels, theme tokens only.
- User-visible: `CHANGELOG.md`, `FEATURES.md`, Shortcuts pane.

## Design

### Controller lane (`App/PianoRollView.Controllers.cs`)

- Host delegates on `PianoRollView`: `ControllersInClip` (ctls with data in the active clip),
  `GetController(ctl)` and `SetController(ctl, points)` (clip-local beats). The roll caches
  curves per ctl and drops the cache on every reload (`Load`) and active-clip change, so undo,
  redo and recording refresh it the way they refresh notes.
- Time: clip-local beat ↔ editor beat through `ClipSet.ToEditor/ToLocal(Active, …)`.
- Switch controllers: the curve is read as spans where value ≥ 0.5 (blocks); gestures edit a
  block list, written back as step points (on value at start, 0 at end; a block keeps its
  stored value, so half-pedal values survive moves). Partial values draw as fill height.
- Continuous controllers: the MPE lane's stroke model over one clip-wide curve — the stroke
  replaces the stretch it covers, the old curve resumes after it; RDP thinning on release.
  Existing points keep their `Curve` shaping; new points are linear.
- One `SetController` call per gesture (on release) → one undo step.

### Lane tabs (`App/PianoRollView.Expression.cs`)

- `LaneMode.Controller` + `_laneCtl` (resolved on first use: first ctl with data, else 64).
- Five rows fit the 72 px velocity row (13 px steps). The fifth row's label is the short name
  when it fits the gutter, else "CC"; a small chevron marks the picker.
- Picker: `MenuFlyout` with the data ctls (automation glyph), the common six, and
  **Other CC ▸** in groups of 16, skipping `MidiController.IsReserved`.

### Velocity wheel

- `Nota.Application/Editing/VelocityWheel.cs`: pure step accumulator — notches × multiplier ×
  direction, carried fractions, ±2 notches cap per event; `Settings` gains
  `PianoRollVelocityWheelReversed` and `PianoRollVelocityWheelMultiplier` (0–2, default 1).
- `NoteGrid.OnPointerWheelChanged`: Shift + hit on an editable note and multiplier > 0 → the
  targets (selection if the hit is selected, else the hit). First step of a burst seeds undo
  with the pre-burst notes (as a note drag does), later steps stream live; a 0.6 s idle timer
  ends the burst (commits when there is no live path, i.e. Session slots).
- Settings reach the roll as static properties seeded by `MainWindow` (like `AuditionEnabled`)
  and updated by Preferences.

## Project Structure

```text
specs/063-event-lane-cc-velocity-wheel/   spec.md, plan.md, tasks.md
src/managed/Nota.Application/Editing/VelocityWheel.cs        (new)
src/managed/Nota.Application/Settings.cs
src/managed/Nota.App/PianoRollView.Controllers.cs            (new)
src/managed/Nota.App/PianoRollView.Expression.cs
src/managed/Nota.App/PianoRollView.cs
src/managed/Nota.App/MainWindow.Detail.cs, MainWindow.axaml.cs
src/managed/Nota.App/PreferencesWindow.cs
tests/Nota.SmokeTest/VelocityWheelTests.cs                   (new), Program.cs
```
