# Implementation Plan: Pick the Bend lane's range in the piano roll

**Branch**: `073-bend-lane-range` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

## Summary

Make the Bend lane's `±N st` label a button that opens a menu of the lane's range steps. The
pick is the least range the lane shows; the lane still widens to fit the bends on the notes.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia.

**Primary Dependencies**: none new.

**Audio-Thread Impact**: None. The range is a view setting of the piano roll on the UI thread;
bends are stored and played in semitones as before.

**Testing**: `BendLaneRangeTests` in the smoke test (`-- --bend-range` alone): the steps, the
range shown for a pick and a bend, and which steps fit. The localization check scans the new
Shortcuts texts. Manual check in the running app.

## Design

- `Nota.Application/Editing/BendLaneRange` — pure: `Steps` (2, 12, 24, 48, 96), `Default`,
  `Fits(range, largestBend)` and `Shown(picked, largestBend)`. It replaces the step list that
  was inline in the lane.
- `PianoRollView.Expression.cs`:
  - `BendPick` — the picked range, kept per clip in a static map keyed like `s_view`
    (`_clipKey`), with the clip's start beat: the pick applies only while the clip at that
    index still starts there, so a moved clip (which takes a new index) loses its pick instead
    of handing it to the clip that took its index. A view without a clip identity (track mode)
    keeps it on the instance.
    `ForgetBendPicks` clears the map from `MainWindow.RefreshAfterLoad`, so a clip in another
    project with the same track id and index opens at the default.
  - `ExpressionLane.UpdateRange` stores the largest bend and shows `BendLaneRange.Shown`.
  - `Render` draws the label with a caret (like the controller tab) in hot ink under the
    pointer and keeps its rect; `OnPointerMoved` sets the hover and hand cursor;
    `OnPointerPressed` opens `ShowRangeMenu` on a left press inside it unless a curve point is
    under the pointer.
  - `ShowRangeMenu` — a `MenuFlyout` of `MenuKit.Item`s, radio-checked on the range shown,
    disabled where `Fits` is false.
- `PreferencesWindow` — a Shortcuts row for the label; French in `fr.json`.

## Constitution Check

UI-only, no engine change, no dependency, no new ABI. Shared menu helpers, `Loc` for the new
texts, unit "st" through `NotaNum.Unit` as before.
