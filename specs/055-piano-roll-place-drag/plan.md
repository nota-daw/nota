# Implementation Plan: Shape a note while placing it

**Branch**: `055-piano-roll-place-drag` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

## Summary

The note grid's double-click placement already captures the pointer (for the audition). It
now also enters a new drag mode, `Place`, on the note just added. Each pointer move reads
the modifiers (`NotePlacementDrag.KindFor`) and, when the kind changed, re-anchors on the
note's current state; the move and length kinds reuse the existing Move / Resize drag code
(Move gains a row offset, `_rowGrab`, 0 for an ordinary note drag so it behaves as before),
the velocity kind maps vertical pixels to MIDI steps (`NotePlacementDrag.Velocity`). The
placement's own commit is the undo step, so the drag streams on the live path without
seeding another; release follows the existing drag release (pending placement, final push
or commit) and remembers the note as last touched.

## Technical Context

C# / .NET 10, Avalonia; no new dependency, no engine or ABI change. Not on the audio thread.

## Constitution Check

UI-only, no allocation on the audio thread, AGPL headers on new files, no dependency.

## Project Structure

- `src/managed/Nota.Application/Editing/NotePlacementDrag.cs` — pure: modifier → kind,
  drag → velocity.
- `src/managed/Nota.App/PianoRollView.cs` — `NoteGrid`: `Mode.Place`, `AnchorPlace`,
  `DragUpdate` branches, release, edge highlight while setting the length.
- `src/managed/Nota.App/PreferencesWindow.cs` — Shortcuts pane row.
- `tests/Nota.SmokeTest/NoteEditingTests.cs` — place-drag checks.
- `FEATURES.md`, `CHANGELOG.md`.
