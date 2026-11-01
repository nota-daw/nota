<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Feature Specification: Logic Grey theme

**Feature Branch**: `dev/windows-fixes`
**Created**: 2026-11-01
**Status**: Ready for planning
**Input**: User attachment: implement a selectable Logic Pro-inspired neutral-grey theme,
preserving Nota's layout, controls, gestures, track/note colours and audio/MIDI behavior.

## Nota Context

**Feature area**: UI colour theme; existing Appearance selection and persistence.
**References**: [DESIGN.md](../../DESIGN.md), [CONTRIBUTING.md](../../CONTRIBUTING.md),
[ARCHITECTURE.md](../../ARCHITECTURE.md), [FEATURES.md](../../FEATURES.md).
Work stays on the user's existing branch and preserves earlier playhead fixes.

## User Scenarios & Testing

### User Story 1 - Select a professional grey workspace (Priority: P1)

A musician chooses Logic Grey in Appearance and immediately sees a neutral-grey workspace
with readable text, distinct timeline/piano-roll grids, subtle raised-control depth and a
warm accent. They continue working with the same layout and controls.

**Independent Test**: Select the theme with Arrangement, piano roll and mixer visible;
compare their appearance and geometry with the original theme.

**Acceptance Scenarios**:
1. Given the default workspace, selecting Logic Grey updates all visible Nota surfaces
   and popups without restarting or rebuilding the workspace.
2. Given coloured tracks and notes, switching themes preserves their palette colours and
   every clip/note/automation value and selection.
3. Given playback, the playhead stays visible and moves smoothly with no added animations.

### User Story 2 - Keep and reverse the preference (Priority: P1)

A musician reopens Nota and sees their chosen theme. They can return to the original
default or any existing theme through the same Appearance controls.

**Independent Test**: Save the selection, recreate settings, relaunch, then switch back.

**Acceptance Scenarios**:
1. Logic Grey is restored after restart; fresh settings still choose Studio Dark.
2. Existing Studio and Ember choices keep their previous colours and remain selectable.
3. Follow system can remember Logic Grey for dark mode while retaining its light choice.

### User Story 3 - Read and edit across Windows scales (Priority: P2)

The musician reads transport, clips, MIDI CC lanes, meters and dialogs at 100%, 125%,
150% and 200% scale, using the same existing controls and keyboard shortcuts.

**Independent Test**: Inspect real rendered controls at all four scales and run readability
checks for the theme; capture Arrangement, piano roll and mixer screenshots.

**Acceptance Scenarios**:
1. Primary, secondary and incidental text meets the existing theme readability bar.
2. Beat subdivisions, bar lines, row shading, selections, muted content and status colours
   remain distinguishable; meaningful meter/status hues retain their roles.
3. Switching or wearing the theme does not change control dimensions, docking, icons,
   editing gestures, audio behavior or third-party plugin interfaces.

### Edge Cases

- Unknown/stale preferences retain the existing fallback to Studio Dark.
- Open dialogs and custom-drawn controls repaint when switching themes.
- User track colours, colour-indexed notes and third-party plugin GUIs retain ownership.
- In a small Appearance window the additional preview remains reachable without losing
  existing controls; use the existing scrolling behavior rather than changing the layout.

## Requirements

### Functional Requirements

- **FR-001**: Add the selectable dark theme named `Logic Grey` alongside every existing theme.
- **FR-002**: Fresh installs retain the original default, Studio Dark; no existing theme is renamed.
- **FR-003**: Selection applies immediately to Nota-owned windows, menus, dialogs and custom views,
  and persists through the existing Appearance settings, including Follow system's dark pick.
- **FR-004**: Use the supplied neutral-grey palette as the starting point, refining text and
  grid contrast to meet Nota's readability rules. Keep saturation primarily in musical data/status.
- **FR-005**: Provide subtle static depth on raised controls and a clearly visible warm playhead;
  keep these theme properties centrally defined and preserve other themes' current rendering.
- **FR-006**: Preserve existing track, return, note, meter and meaningful status colours in their
  existing locations. Theme changes never modify project content or selected colour indices.
- **FR-007**: Preserve all layout, geometry, typography, widgets, icons, shortcuts and editing
  behavior; do not restyle third-party plugin interfaces or modify audio/MIDI processing.
- **FR-008**: No extra continuous animation, render-loop work or new external dependencies.
- **FR-009**: Build and validate on Windows x64; provide real screenshots where supported,
  including Arrangement, piano roll and mixer, plus a clear list of feature-modified files.

### Key Entities

- **Theme preset**: stable identity, displayed name, dark kind and complete semantic palette.
- **Appearance preference**: selected theme and remembered dark/light choices; existing storage.
- **Musical colours**: independently meaningful track/note/status colours, retained across switching.

## Success Criteria

- **SC-001**: Logic Grey can be selected in the existing Appearance controls, applies before
  the next visible repaint, and survives a process restart.
- **SC-002**: All existing theme readability pairings pass; original-theme colour values are
  unchanged for every pre-existing token and new effects render identically in those themes.
- **SC-003**: Track/note colours and serialized project content are identical before/after switching.
- **SC-004**: Arrangement, piano roll and mixer captures show the requested grey hierarchy,
  clear selection/grid states and consistent Nota-owned chrome at the four requested scales.
- **SC-005**: Windows build and smoke checks pass; an instrumented empty-project playback
  comparison introduces no systematic frame-cadence loss or audio dropouts attributable to the theme.

## Assumptions

- The user explicitly authorizes proceeding through specification, planning and implementation.
- “Default” refers to existing Studio Dark; retain its familiar name and all other theme options.
- This is an original Nota palette inspired by a general grey DAW atmosphere, not a copy of
  Logic Pro's interface. Subtle static raised-fill shading is allowed for this theme only.
- Supplied hex values may be lightened for readability; changing global contrast requirements
  or weakening design checks is outside scope.

## Clarification Review

Scope, preference behavior, layout boundaries, palette intent and validation are clear from
the attachment and current architecture. No blocking clarification is needed. Existing
Appearance controls satisfy the requested selector; no duplicate settings UI will be added.

## Nota Compliance Checklist

- [x] AGPL headers on new sources; no dependencies.
- [x] No audio-thread, graph, JUCE or ABI changes for this feature.
- [x] Existing themes/default and prior workspace work preserved.
- [x] Spec/plan/tasks precede implementation; Windows smoke verification required.
- [x] Changelog, design reference and feature documentation are in scope.
