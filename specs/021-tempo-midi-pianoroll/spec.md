# Feature Specification: Tempo Track, MIDI Tempo and Piano Roll Event Editing

**Feature Branch**: `feature/tempo-track-midi-pianoroll`

**Created**: 2026-10-17

**Status**: Draft

**Input**: Add a dedicated tempo track and transport controls; import/export MIDI tempo and controller data; make the piano-roll event editor and playhead interaction usable and performant; correct the transport position when tempo changes during playback.

## Context

The transport is the sole source of musical time. Any tempo-map implementation must keep arrangement clips, MIDI clips, controller automation, loop boundaries, metronome clicks, live MIDI timestamps, and hosted-plugin playhead data on one musical timeline. The audio thread must remain allocation-free and lock-free. The existing MIDI clip controller model and APIs from specs/020 are reused rather than duplicated.

This is a cross-layer feature spanning the native engine, project serialization, MIDI file parsing/writing, arrangement UI, Piano Roll, input handling, and smoke tests. The work is split into independently testable milestones so the editor does not pretend to support tempo interpolation before playback and file persistence actually do.

## User Stories

### Story 1 — Correct transport behavior when tempo changes (P1)

When the tempo changes during playback, the playhead remains at the same musical position at the tempo-change boundary and continues advancing at the new rate. Stop/seek anchors and loop boundaries remain stable.

**Acceptance scenarios**
1. Given playback at a non-zero beat, when BPM changes, the reported beat does not jump forward or backward.
2. Given the playhead is parked at an insert marker, when BPM changes and Stop is pressed, the transport returns to the same marker beat.
3. Given a loop is enabled, when BPM changes during playback, the next loop boundary is still reached at the configured beat.
4. Invalid BPM values are ignored without corrupting the current position.

### Story 2 — Tempo track and transport toggle (P1)

The arrangement can show or hide a dedicated tempo track above all ordinary tracks. The right-click transport tempo control toggles the track with the labels “Show tempo track” and “Hide tempo track”. Ctrl+T toggles the track on Windows/Linux; Cmd+T toggles it on macOS. The transport exposes the same action. Tempo points can be box-selected; double-clicking a point edits its BPM. When one or two points are selected, the interpolation control offers Linear, Square, Triangle, and Smooth and applies only to those selected points. Ctrl/Cmd+A selects every tempo point when the tempo track has focus.

**Acceptance scenarios**
1. The tempo track is rendered above track headers and clip lanes and follows the project's horizontal timeline.
2. Toggling the track changes only its visibility; it does not move or rewrite audio/MIDI clips.
3. Tempo point edits are undoable, survive save/reload, and are reflected by the transport.
4. Interpolation selection changes the segment shape without altering unselected tempo segments.
5. The same keyboard shortcut works with platform-appropriate modifiers and does not steal Ctrl/Cmd+A from other focused editors.

### Story 3 — MIDI import/export with tempo and controllers (P1)

Standard MIDI files retain note, supported controller, pitch wheel, channel pressure, and tempo meta-events. Tempo data is placed on the tempo track. A file that carries only one initial tempo event still creates one tempo point at beat zero; a file with no tempo meta-event preserves the current project tempo. Export writes tempo points and controller curves as MIDI meta/controller events.

**Acceptance scenarios**
1. Importing a MIDI file with one initial tempo meta-event creates a beat-zero tempo point even if the tempo remains constant.
2. Importing a file with multiple tempo events preserves their tick positions and BPM values.
3. Controller events survive import into clip controller curves with the existing normalized value conventions; pitch wheel and channel pressure map to the existing controller IDs.
4. Exporting and re-importing a clip/project preserves note timing, controller timing/values, and tempo changes within MIDI tick resolution.
5. Unsupported SMPTE time division and malformed MIDI files fail with a useful message and no partial project mutation.

### Story 4 — Piano Roll follows the arrangement playhead (P1)

The existing Piano Roll ruler seek and playhead-line drag/scrub behavior delegates to the shared transport. This feature adds Follow Playhead without replacing or duplicating those interactions; the arrangement and Piano Roll continue to share one transport position.

**Acceptance scenarios**
1. Clicking the Piano Roll ruler seeks the engine to the clicked beat in the arrangement, accounting for the edited clip's global start.
2. When Follow Playhead is enabled, horizontal scrolling keeps the playhead visible during playback; when disabled, user scroll position is not changed by transport ticks.
3. Seeking while playing uses the existing engine seek/chase path and does not create an independent editor clock.

### Story 5 — Event Line Editor controls and editing (P2)

The lower editor lane can switch among velocity, MPE expression, and any supported MIDI controller. A controller picker lists controllers carrying data in the clip first, followed by named common controllers and all other non-reserved CCs. The lane can be collapsed, expanded, and resized with a separate top-edge handle; resizing the lane does not change note-row zoom. Its grid follows Piano Roll grid settings and its parameter snap toggle is separate from note snap.

Shift+mouse wheel changes selected-note velocities; with no selection, it targets the note beneath the pointer. Wheel up increases velocity and wheel down decreases it. The gesture is a no-op when no note is under the pointer and no notes are selected.
The direction and multiplier are user preferences persisted in Settings; the default multiplier is 1× and the supported range is 0×–2×. A 0× multiplier leaves notes unchanged.

**Acceptance scenarios**
1. Every non-reserved CC is selectable; controller edits use the existing clip-controller API and are one undo step per gesture.
2. Switch controllers (CC64–69) are presented as thresholded blocks while retaining the full value; continuous controllers use an editable curve.
3. The lane's size/collapse state is independent of pitch-row zoom.
4. Shift+wheel adjusts velocity according to the configured direction and multiplier; defaults are up = plus, down = minus and 1×.
5. A multiplier of 0 disables the velocity change; values from 0× to 2× are persisted in Settings.
6. Event lane snap does not silently change Piano Roll note snap.

### Story 6 — Piano Roll geometry and performance (P2)

At minimum zoom and Fit, the visible pitch rows remain anchored directly above the Event Line Editor, without blank space at the bottom. Rendering and selection scale to large clips by skipping unchanged/off-screen work with a small overscan region. Box selection should not scan/draw the entire clip for every pointer event when most notes are outside the selection rectangle.

**Acceptance scenarios**
1. At minimum row height, Fit-to-notes, and a viewport taller than the 88-key range, the bottom pitch row meets the event lane.
2. Notes and grid objects outside the viewport do not cause unnecessary draw work; near-edge objects still render correctly during scrolling.
3. Box selection remains correct for chords, overlapping notes, and a selection that begins or ends outside the current viewport.

## Functional Requirements

- **FR-001**: The transport MUST preserve musical beat position and the stopped-position anchor when BPM changes.
- **FR-002**: The tempo track MUST be stored in project data and integrated with the transport; it MUST be visible above ordinary arrangement tracks.
- **FR-003**: Tempo points MUST support selection, BPM editing, the four requested interpolation shapes, undo/redo, and save/reload.
- **FR-004**: Tempo-track visibility MUST be available from the tempo control's context menu, the transport action, and Ctrl/Cmd+T.
- **FR-005**: MIDI import MUST preserve tempo meta-events and existing supported controller data; a static tempo MUST still produce one tempo point at beat zero.
- **FR-006**: MIDI export MUST serialize tempo points, notes, and controller curves without modifying the project.
- **FR-007**: MIDI file tick conversion MUST use the file's PPQ division and preserve stable event ordering for equal ticks.
- **FR-008**: The existing Piano Roll ruler seek and playhead drag/scrub MUST remain delegated to the shared transport and map clip-local beats to global beats; this feature MUST NOT add a competing ruler-click handler.
- **FR-009**: Piano Roll Follow Playhead MUST be independent from arrangement follow state unless explicitly synchronized by existing UI behavior.
- **FR-010**: Event lane controller selection MUST reuse the existing `MidiController` IDs and `GetMidiClipController`/`SetMidiClipController` APIs.
- **FR-011**: Event lane sizing/collapse and event snap MUST not modify Piano Roll pitch zoom or note snap state.
- **FR-012**: Shift+wheel velocity editing MUST use selected notes or only the hovered note when the selection is empty.
- **FR-013**: Piano Roll layout/rendering optimizations MUST preserve visual correctness and pointer hit testing.
- **FR-014**: The implementation MUST extend smoke tests for transport, MIDI parsing/writing, and any engine changes.
- **FR-015**: User-facing changes MUST be documented under `CHANGELOG.md`'s `[Unreleased]` section.
- **FR-016**: Shift+wheel velocity editing MUST support a persisted direction setting (default: up increases, down decreases) and a persisted multiplier from 0× to 2× (default: 1×); 0× MUST disable velocity changes without consuming unrelated gestures.

## Non-functional Requirements

- No allocation, lock, file I/O, or logging on the audio callback thread.
- The native/managed boundary remains a pure C ABI; no C++ objects cross it.
- New runtime code follows `ARCHITECTURE.md`; new UI follows `DESIGN.md` and uses existing theme resources.
- Existing projects without tempo points load as a constant tempo equal to their saved BPM.
- New dependencies are not required for this feature.
- No change is made directly to `develop`; no Pull Request is opened until explicitly requested.

## Delivery Order

1. Transport continuity and focused smoke regression.
2. Tempo model/serialization and tempo-aware transport semantics.
3. Tempo track editing and MIDI tempo import/export.
4. Piano Roll ruler seek/follow, controller event lane and velocity gestures.
5. Geometry/performance improvements and cross-platform verification.
