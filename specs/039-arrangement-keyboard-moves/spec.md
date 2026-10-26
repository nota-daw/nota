# Feature Specification: Keyboard clip moves, header range select, drop below the last track

**Feature Branch**: `feat/arrangement-keyboard-moves`

**Created**: 2026-10-26

**Status**: Implemented

**Input**: Issues nota-daw/nota#302 (move clips with the arrow keys), #249 (Shift / ⌘-click
track headers to select several tracks) and #301 (dragging a clip below the last track
should create a new track for it).

## Nota Context

**Feature area**: Engine (one new ABI call) and the managed app (Arrangement view).

What exists today:

- Clips move only by mouse. `MoveClipBlock` moves a set of clips atomically (one undo
  step) and `LastPlacedClips` reports where they landed.
- Track headers select one track per click; ⌘A selects all. The multi-track set
  (`_selTracks`) already drives group, duplicate, copy and delete.
- A clip dragged below the last lane has no target and snaps back.
- `DuplicateTrack` deep-copies a track (mixer, instrument, devices, clips, automation).

## User Scenarios

1. **Nudge (P1).** With clips selected in the Arrangement, ← / → move them by the current
   grid step; ⌥← / ⌥→ by a 64th note. The block moves as one and stops at the song start.
   Each press is one undo step.
2. **Move to another track (P1).** ⌥↑ / ⌥↓ move the selected clips to the nearest tracks
   above / below that take every clip (audio on audio tracks, MIDI on instrument tracks),
   at the same beats. When none does, the status bar says so and nothing moves.
3. **Range select headers (P2).** A Shift-click on a track header selects every track from
   the last plainly- or ⌘-clicked track to it; ⌘-click (Ctrl on Windows / Linux) adds or
   removes one track.
4. **Drop below the last track (P1).** Dragging clips into the empty space under the last
   track shows one "New track" drop ring per source track, with the clips previewed in it.
   Releasing creates, for each source track, a new track like it: same kind, instrument,
   devices and mix, but no clips, automation or session clips, not in a group, placed after
   the last regular track (before the returns). The clips move there at their dropped
   beats. One undo takes back both the move and the new tracks.

## Decisions (the open points in #301)

- The new track copies the source track's instrument and devices, so a MIDI clip sounds
  the same after the move.
- A multi-clip selection gets one new track per source track, in source order, so the
  layout of the selection is kept.

## Requirements

- **FR-1** ←/→ and ⌥←/⌥→ act only when the Arrangement has focus (not the detail panel
  or the piano roll) and no ⌘ / Shift is held.
- **FR-2** ⌥↑/⌥↓ never put a clip on a group, a return or a track of the other kind.
- **FR-3** `nota_engine_add_track_like(engine, trackId)` returns the new id, or -1 for
  the master, a return, a group or an unknown id. It checkpoints undo like any track add.
- **FR-4** If any step of a drop below the last track fails, the new tracks are taken
  back and the clips stay where they were.
- **FR-5** Esc during the drag removes the ghost rows and changes nothing.

## Success Criteria

- Smoke test `--moves` covers the nudge clamp, the row-shift rule, the header range and
  `AddTrackLike` (placement, instrument and devices, no clips or automation, top-level,
  one-step undo with the move, refused for return / group / unknown).
- Checked in the real app on Linux and macOS with before/after screenshots.
