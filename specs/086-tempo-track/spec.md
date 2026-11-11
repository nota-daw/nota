<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Feature Specification: Tempo track and time scale

**Created**: 2026-11-11
**Status**: Accepted for local implementation
**Issue**: [#535, approved](https://github.com/nota-daw/nota/issues/535)
**Input**: The user's Tempo Track and Time Scale Tool requirements in this session.

## Nota Context

Engine, managed app and Arrangement UI. Transport remains the musical clock;
edits use immutable graph snapshots and the pure C ABI. Studio design applies.

## User Scenarios & Testing

### User Story 1 - Draw and shape tempo (Priority: P1)

The musician opens a collapsible, vertically resizable tempo lane, adds and drags
points, edits their BPM, and chooses stepped, linear or smooth Bezier transitions.
Handles shape a curve that changes playback timing. The initial point stays at zero.

**Independent Test**: Create all three shapes, compare audible beat timing, undo and
redo, then save and reload with identical points and timing.

**Acceptance Scenarios**:
1. Given a new project, adding and dragging points changes the musical grid and playback.
2. Given a smooth segment, dragging either handle changes its shape and beat timing.
3. Given an edited map, undo/redo and save/reload preserve every point and handle.

### User Story 2 - Select and remove points (Priority: P1)

Right-button drag selects all enclosed points with a visible marquee and highlights.
Delete removes the selection as one operation, retaining the mandatory initial point.
Escape and empty clicks clear selection; dragging never opens a context menu.

**Independent Test**: Select several points after scrolling and zooming, delete, undo
once, and verify exact map restoration.

### User Story 3 - Match bars to a performance (Priority: P1)

Ctrl hover shows a vertical location indicator and a time-scale cursor. Ctrl drag
moves a bar to a time in reference audio, previews grid and BPM, and commits once.
The previous anchor and earlier mapped music stay fixed. A marker is created on
release; Ctrl+Alt omits it. Escape restores the map. Nearby markers and available
transients snap without duplicate points. Reference audio keeps its physical timing.

**Independent Test**: Map three sections, cancel another drag, and undo the third;
the recording's sample positions remain unchanged throughout.

### Edge Cases

Nonfinite values, duplicate beats, crossing anchors, empty/all-selected deletion,
pointer capture loss, focus changes, offscreen points, extreme ramps, seek and loop
boundaries must leave a valid positive monotonic map and no stuck gesture.

## Requirements

- **FR-001**: Provide all point, curve, selection, resizing and collapse interactions above.
- **FR-002**: Every drag and multi-delete is exactly one undoable edit; cancel adds none.
- **FR-003**: Musical-time/seconds conversions use one authoritative map, remain monotonic,
  and account for the project's existing time signature.
- **FR-004**: MIDI, audio and metronome follow the map after edits, seek and looping.
- **FR-005**: Audio callbacks do no allocation, locking, I/O or curve integration.
- **FR-006**: Persist points, segment shapes and handles; old projects retain their BPM.
- **FR-007**: Respect Studio tokens, localization, keyboard focus and existing gestures.
- **FR-008**: Fix a related metronome extra click on a mid-beat start if reproduced.

### Key Entities

Tempo point: beat, positive BPM, outgoing shape and two normalized Bezier handles.
Tempo map: ordered points, immutable timing data, invertible seconds mapping.
Time-scale gesture: original map, chosen bar, previous anchor and desired seconds.
Selection: ephemeral point indices and beat/BPM marquee bounds.

## Success Criteria

- **SC-001**: Beat/seconds round trips differ by less than one sample at 48 kHz.
- **SC-002**: All requested gestures can complete or cancel without invalid maps.
- **SC-003**: Three mapped sections preserve earlier anchors within one sample.
- **SC-004**: Save/reload and undo/redo restore identical tempo data and timing.
- **SC-005**: Existing build and relevant timing smoke tests pass.

## Assumptions

This checkout currently has only global BPM, so the map and lane must be added.
Time-signature marker creation remains outside #535; existing numerator/denominator
are supported. Snap uses already available transient analysis, without new detection.
Unwarped arrangement audio is the reference performance and retains seconds timing.
No push, merge or pull request is authorized or needed.

## Nota Compliance Checklist

- [x] AGPL headers on new source files; Avalonia.Headless test-only MIT license checked.
- [x] Transport authority, immutable snapshots and real-time safety planned.
- [x] Pure C ABI only; no JUCE outside pluginhost.
- [x] Engine timing smoke tests and project round-trip validation planned.
- [x] CHANGELOG, FEATURES, user docs and shortcuts updates planned.
- [x] Studio design and localization required.
