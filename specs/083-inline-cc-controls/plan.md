# Implementation Plan: Controls inside MIDI CC lanes

**Branch**: `feat/multiple-midi-cc-lanes` | **Date**: 2026-11-04
**Spec**: [spec.md](spec.md)

## Summary

Refactor PR #425's existing lane shell, preserving ControllerLane and its routing.
Each controller body becomes a header plus graph, with selector and close button in
the header and a non-interactive value scale in the shared left gutter. Put the add
action below the five primary sidebar entries. Remove the full-width toolbar.

## Technical Context

- Language/version: C# / .NET 10, Avalonia 12.1.4.
- Dependencies: existing controls, Glyph, NotaPalette and fonts; no new dependencies.
- Storage: existing PianoRollLaneHeights settings and existing clip MIDI curves.
- Audio-thread impact: none; managed UI only, no native/ABI/transport changes.
- Testing: extend the real Windows CCProbe and run scripts/build-win.ps1, including
  smoke/design/localization/scanner guards; matching before/after screenshots.
- Platforms: desktop Windows/macOS/Linux; local validation on Windows x64.
- Constraints: shared graph x-origin remains unchanged, including 96 px folded pad
  gutter; controls occupy a separate header, never an overlay over editable points.
- Scope: PianoRollView partials, localization/docs, existing validation harness.

## Constitution Check

All eight principles pass before and after design: UI-only changes, no ABI or audio
thread changes; existing palette/styles and dependencies; SPDX on new sources; full
build gate; one existing PR; feature/docs/changelog sync; spec before implementation;
no safety exceptions. User instruction overrides creating a new branch or PR.

## Project Structure

- `PianoRollView.ControllerLanes.cs`: shared header/graph shell, scale, sidebar action,
  existing add/remove/resize functions and available-height accounting.
- `PianoRollView.Controllers.cs`: update primary header on controller changes; remove
  duplicated graph name; retain data/edit mapping and existing picker.
- `PianoRollView.Expression.cs`: host primary controller shell, cancel gestures when
  leaving it, reserve sidebar action height, simplify controller tab selection.
- `PianoRollView.cs`: remove toolbar row; install sidebar action below primary tabs.
- `PianoRollView.ClipStrip.cs`: refit lanes when stacked strip height changes.
- `Languages/fr.json`, `FEATURES.md`, `CHANGELOG.md`: labels/workflow.
- `specs/071-multiple-cc-lanes/validation/Program.cs`: existing regression harness.
- `specs/083-inline-cc-controls/`: design and validation artifacts.
- Sibling `nota-docs`: paired piano-roll manual update; include a paired handoff in
  this branch to keep the requested submission contained within PR #425.

## Implementation decisions

A shared ControllerLaneBody helper serves primary and extra lane headers. Header
height consumes part of the existing saved row height; graph uses remaining height.
The 48 px extra minimum still permits a compact header and readable scale labels.
Primary minimum keeps five tabs plus the compact add action accessible.
Scale uses ControllerLane's ValueToY directly, translated into gutter coordinates;
no new x offset in drawing or pointer math. Pitch wheel uses signed endpoints and
zero; ordinary controllers show MIDI 127/64/0.

Available-height budgeting uses actual clip strip height, not one assumed row.
Extra lanes scroll within the remaining budget, leaving notes visible. Existing
minimum host protections remain. No unknown technology choices require research
delegation; findings come from the existing component implementation.
