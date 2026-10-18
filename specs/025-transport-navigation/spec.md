# Feature Specification: Pause, Show playhead and loop-region editing

**Feature Branch**: `025-transport-navigation`

**Created**: 2026-10-18

**Status**: Implemented

**Input**: User description: three requests about getting around the Arrangement while it plays.
"Transport: Shift+Space / Ctrl+Space for pause-here and return-to-start-point" (nota-daw/nota#51);
"Show Playhead and Optional Playback Following" (nota-daw/nota#105); "Clear and Resize the
Arrangement Loop Region" (nota-daw/nota#125).

## Nota Context

**Feature area**: UI (Avalonia) — transport view model, Arrangement ruler, command catalog.
No engine change. The engine's Stop already rewinds to the start anchor (where playback
started, or the last seek), so "return to start point" exists today on Space; what is
missing is pause. The loop region is transport state: not saved with the project and not in
the undo history.

## Clarifications

### Session 2026-10-18

- Q: Which keys? → A: ⇧Space = Play / pause. Space keeps Play / Stop (stop returns to where playback started). ⌘Space / Ctrl+Space were not used: they are Spotlight and the input-source switch on macOS. No Preferences option for now; both behaviours are one key apart.
- Q: How does pause work without an engine change? → A: Stop, then seek to the position it stopped at; a seek moves the start anchor, so the next Play continues from there. A pause during a count-in just stops (nothing moved yet).
- Q: Show playhead's key? → A: P (free: not part of the computer-keyboard note map, which ends at K). Also View ▸ Show playhead and the command palette.
- Q: When does Show playhead scroll? → A: Only when the playhead is off screen or within 5 % of an edge; it then lands a quarter of the way in so what comes next is visible. Zoom, vertical position, selection and transport are untouched.
- Q: Follow suspension? → A: Any user scroll of the lanes during playback with Follow on (wheel, scrollbar, overview strip, edge auto-scroll while dragging a clip) pauses Follow. Show playhead, a click on the Follow button and the next Play pick it up again. While paused the button shows an accent outline instead of the solid key, the tooltip and status bar say how to resume. Follow never scrolls during a clip drag, trim, marquee, time-range or envelope edit.
- Q: Off-screen indicator? → A: A small accent arrow at that end of the ruler; a press on it (14 px zone) runs Show playhead instead of seeking.
- Q: Moving vs drawing the loop? → A: A drag that starts on the brace moves it (length kept); a drag on either edge (5 px each side) resizes that edge only; a drag elsewhere draws a new region as before; a click anywhere still seeks; a double-click on the brace still turns it off / on (#59). ⌥ ignores the grid. The other edge is never re-snapped.
- Q: Clear vs off? → A: Right-click on the ruler: Turn loop on / off, Loop selection, Clear loop region. Clear turns the loop off and forgets the range; the transport cell then shows the range Loop would use (1.1–5.1).
- Q: Undo for loop edits? → A: Not in this change: the loop region is not part of the project or its history.

## Requirements

- **FR-001**: ⇧Space MUST pause (stop with the playhead in place) while playing, and play while stopped.
- **FR-002**: Space MUST keep its current behaviour.
- **FR-003**: Show playhead MUST NOT seek, change zoom, vertical scroll or selection, and MUST NOT scroll when the playhead is already comfortably visible.
- **FR-004**: A user scroll during playback with Follow on MUST pause Follow; Show playhead, the Follow button and the next Play MUST resume it.
- **FR-005**: Dragging a loop edge MUST keep the other edge exactly; dragging the brace MUST keep the length.
- **FR-006**: Clear loop region MUST leave clips, notes and automation untouched.
- **FR-007**: Settings → Shortcuts, the command palette and the menus MUST list the new commands.
