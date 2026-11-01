<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Fix: Piano-roll playhead still stutters

- **Slug**: piano-roll-playhead-stutter (from assessment context)
- **Fixed**: 2026-11-01
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Arrangement MIDI editor cursors now use the existing bounded frame-timed transport
presentation position. Independent note-grid and ruler overlays move without
re-recording static note/grid/label drawing commands.

## Changes

| File | Change | Notes |
|------|--------|-------|
| src/managed/Nota.App/MainWindow.Playhead.cs | modified | Deliver frame position minus editor Origin to visible arrangement MIDI editors. |
| src/managed/Nota.App/MainWindow.axaml.cs | modified | Retain stopped/fallback and Session updates; prevent service ticks overwriting frame positions. |
| src/managed/Nota.App/PianoRollView.cs | modified | Two non-interactive clipped cursor layers in existing layout cells; repaint on cursor or view-transform changes. Remove strokes from static content. |
| CHANGELOG.md | modified | Unreleased fix entry. |
| diagnostics/PianoProbe.csproj, diagnostics/Program.cs, diagnostics/.gitignore | added | Reproducible actual-App diagnostic using existing binaries and scratch settings. |

## Tests Added or Updated

PianoProbe compares actual piano-roll and arrangement cursor changes on animation
callbacks, and inspects composition draw-list replacement counts for the notes and
ruler. It exercises stopped seeks, hiding, resume, song loops, floating detail with
minimized main window, polling suspension and shutdown. Before mode records baseline
without asserting the new cadence/render-isolation requirements.

## Local Verification

- Release app build: passed, 0 errors (26 existing warnings).
- Actual-App baseline: 150 piano cursor changes / 1120 callbacks in five seconds,
  29.90 changes/s; 150 grid and 150 ruler draw-list replacements.
- Post-fix: 1116 cursor changes / 1116 callbacks, 223.18 changes/s; zero grid/ruler
  draw-list replacements and zero reported dropouts. Lifecycle checks passed.
- git diff --check: passed.
- Full Windows regression checks are recorded by the subsequent bug-test report.

## Deviations from Assessment

Composition draw-list identity replaces production render counters: it observes the
actual renderer re-recording static controls without adding instrumentation to the app.
No source scope expansion. No native/DSP/ABI or theme changes.

## Follow-ups

Run speckit-bug-test for the full Windows checks and final report. The nota-features
and nota-docs trigger rules were reviewed: this restores expected cursor smoothness,
adds no capability and changes no documented workflow, so no manual/feature-list
change is needed. Session-slot interpolation remains outside this fix.
