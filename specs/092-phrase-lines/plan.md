# Implementation Plan: Phrase lines in the arrangement

**Branch**: `092-phrase-lines` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

## Summary

A Settings → Appearance choice (Off / 4 / 8 / 16 / 32) that makes the arrangement draw every
Nth bar line, counted from bar 1, with a heavier neutral pen on the lanes, the footer rows and
the ruler.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia.

**Primary Dependencies**: none new.

**Audio-Thread Impact**: None. Drawing on the UI thread and one more settings value.

**Testing**: `PhraseLinesTests` in the smoke test (`-- --phrase-lines` alone): the choices,
`Normalize`, `StartsPhrase` / `IsPhraseLine` in 4/4, 3/4 and an unset meter, and a sweep that
every phrase line is one of `SnapGrid.GridLines` at every step and meter. The localization
check scans the new texts. Manual check in the running app.

## Design

- `Nota.Application/Editing/PhraseLines` — pure: `Choices` (0, 4, 8, 16, 32), `Normalize`,
  `StartsPhrase(bar, n)` for the ruler's bar loop and `IsPhraseLine(beat, n, beatsPerBar)` for
  the grid's beat positions.
- `Settings.ArrangementPhraseBars` (int, 0 = off), next to `ArrangementWaveform`.
- `ArrangementView`:
  - `PhraseBars` — normalised; a change calls `Redraw()` (ruler, lanes, footer rows).
  - `PhrasePen` — `NotaPalette.Derived` mixing `GridBar` 35 % toward `TextTertiary`: a step
    above the bar line in each of the four themes and in user themes, re-tinted on a theme
    switch. A theme token was not added because user theme files must carry the exact
    catalogue (`ThemeFile` rejects a file missing a token).
  - `DrawGrid` picks `PhrasePen` for a phrase line before the bar / beat / sub-beat pens.
    Every bar step (1, 2, 4 bars) divides every phrase length and sub-bar steps include the bar
    lines, so phrase lines are always among the drawn lines.
  - `RulerControl.RenderBars` draws a phrase bar's line full height in `PhrasePen` instead of
    the 7 px tick; the time-mode ruler is untouched.
- `MainWindow` applies the setting at start-up and on `Settings.Changed`, like the waveform.
- `PreferencesWindow.PhraseLinesPicker` — a segmented control like `WaveformPicker`, with a
  caption, between Waveform and Track headers. Texts in `en.json`.

## Constitution Check

UI-only, no engine change, no dependency, no new ABI. Colours from the palette (derived, no
hex), texts through `Loc`.
