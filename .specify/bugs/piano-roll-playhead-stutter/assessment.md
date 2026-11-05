<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Assessment: Piano-roll playhead still stutters

- **Slug**: piano-roll-playhead-stutter (user accepted)
- **Created**: 2026-11-01
- **Source**: pasted text
- **Verdict**: valid
- **Severity**: medium

## Report

> the aranger cursor works perfectly but not the piano roll

## Symptom and Reproduction

On the current Windows build, open an arrangement MIDI clip and play. The arrangement
cursor is smooth but the piano-roll cursor still advances in steps. Expected: both
follow the same authoritative transport presentation position.

## Suspected Code Paths and Root Cause

High confidence: MainWindow.Playhead.cs explicitly excludes the piano roll from its
animation-frame loop. MainWindow.axaml.cs feeds it only from PlayheadUpdated (the
33 ms Background dispatcher service timer). PianoRollView.SetPlayhead invalidates
the full NoteGrid and RulerLane, which draw the cursor alongside all notes/beat labels.
Thus the previous arrangement fix did not reach this editor. Session slots use their
own authoritative clock and must retain that mapping.

## Proposed Remediation

Put the ruler and note-grid cursor strokes in separate clipped, non-interactive
overlay controls. Invalidate only these for a cursor change; also invalidate them on
zoom/scroll/content changes. Keep all existing cursor styling, visibility/range and
ruler/grid seeking semantics. Feed arrangement MIDI cursors from the existing
bounded TransportDisplayPosition calculation on animation frames, using roll.Origin
for single/multiple clips. Suppress the slower service updates while frame delivery
is active, retaining stopped/fallback updates and the separate Session-slot clock.
Reuse existing docked/floating window ownership and shutdown/suspension checks.

**Files likely to change**:
- src/managed/Nota.App/MainWindow.Playhead.cs
- src/managed/Nota.App/MainWindow.axaml.cs
- src/managed/Nota.App/PianoRollView.cs
- CHANGELOG.md
- Diagnostics under this bug directory (no new dependencies).

**Tests to add or update**:
- Instrument the actual main window and its real clip editor before/after the fix;
  compare arrangement and piano-roll cursor changes per animation callback.
- Count NoteGrid/RulerLane render calls: cursor-only animation must not repaint them.
- Exercise stopped seeks, out-of-range hiding, horizontal zoom/scroll, pause/resume,
  song loops, floating editor with minimized main window, suspension and shutdown.
- Build Release and run the Windows smoke checks.

## Risks & Considerations

Overlays must stay clipped to the existing columns and scroll with their grid, allow
pointer input through to existing controls, and follow theme slots. Service ticks must
not overwrite interpolated beats. No native engine, audio processing or ABI changes.
Frame sampling measures cursor property changes, not physical monitor presentation.

## Open Questions

No blocking questions for the arrangement MIDI editor. Session-slot interpolation
is outside this remediation; its own clock and existing update cadence are retained.
