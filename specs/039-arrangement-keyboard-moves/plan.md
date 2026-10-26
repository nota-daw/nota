# Implementation Plan: Arrangement keyboard moves

**Branch**: `feat/arrangement-keyboard-moves` | **Date**: 2026-10-26 | **Spec**: [spec.md](spec.md)

## Summary

Three Arrangement gestures (#302, #249, #301): arrow keys move the clip selection (←/→ by
the grid step or a 64th, ⌥↑/⌥↓ to the next track that takes every clip), Shift-click on a
track header selects a range, and dropping clips below the last track moves them onto new
tracks like their sources. Every move goes through the existing atomic `MoveClipBlock`, so
each gesture is one undo step. The only engine addition is `addTrackLike`.

## Technical Context

- **Engine**: `Engine::addTrackLike(trackId)` (`Engine_Tracks.cpp`), exposed as
  `nota_engine_add_track_like` (pure C ABI, AR-7). It clones the source with
  `deepCloneTrack(src, newId, withContent = false)`: instrument, MIDI effects and devices
  get their own DSP state; clips, Session slots and automation are not copied. Group
  membership, arm, name and colour are reset. The new track is published in a new graph
  snapshot (AR-5) right after the last non-return track. A track added after the returns
  exist sits after them in the graph, so "in front of the first return" is not enough.
  Returns -1 for the master, a return, a group or an unknown id.
- **Pure rules**: `Nota.Application/Editing/ArrangementMoves.cs`, UI-free and driven by
  the smoke test:
  - `NudgeDelta`: the block's shift, clamped at the song start.
  - `RowShift`: the nearest row offset where every clip lands on a track that takes it.
  - `Range`: Shift-click header ids.
  - `Reindex` / `Invert` / `SameStarts`: where clips are after a block move, and after its
    undo or redo.
- **App**:
  - `ArrangementView.KeyMoves.cs`: nudge, track moves, and `MoveBlock`, which wraps
    `MoveClipBlock` and returns a `ClipBlockMove`.
  - `ArrangementView.NewTrackDrop.cs`: inside one undo group, one `AddTrackLike` per
    source track, then a single `MoveBlock`. If any step fails, the group is undone.
  - `LaneControl`: draws the drop ring below the last track.
  - `MainWindow.Input.cs`: the arrows reach the Arrangement only when nothing else owns
    them: not the piano roll grid, the docked detail panel or the floating detail window,
    and not with ⌘/Ctrl or Shift held.
- **Open editors**: clips are addressed by index, and a move can re-index a track. A
  landing that carves away a clip shifts the clips after it, and a cross-track move
  appends. `MainWindow.OnClipsMoved` re-targets the piano roll, the audio editor and the
  step grid. It also keeps the recent moves, so an undo or redo of exactly that move
  takes the editors back or forward. The move is recognised when the touched tracks'
  clip starts match the starts from before or after it.

**Audio-Thread Impact**: None. All work happens on the UI thread, through the existing
snapshot publish (AR-5). `addTrackLike` and `MoveClipBlock` allocate and publish off the
audio thread; the audio thread only picks up the new snapshot. No new locks, allocations
or I/O on the audio thread (AR-6).

**Testing**: The smoke test section `ArrangementMovesTests` (`--moves`) covers:

- the pure rules;
- `addTrackLike` placement, contents and refusals;
- one-step undo of a drop onto new tracks;
- clip re-indexing after a carving move, and after its undo and redo.

The app was also checked by hand on Linux (Xvfb) and on macOS (Mac mini).

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| I. Real-Time Audio Safety | Pass | No audio-thread code; edits publish snapshots. |
| II. Boundary Discipline | Pass | One new C ABI call; the app uses only the interop layer. |
| III. Licensing Hygiene | Pass | New files carry the AGPL-3.0-only SPDX header; no new dependency. |
| IV. Verification Gate | Pass | Full smoke test with `CI=true`; new `--moves` section. |
| V. One Logical Change per PR | Pass | Three related Arrangement editing gestures, one spec. |
| VI. Documentation Stays in Sync | Pass | CHANGELOG, Settings → Shortcuts, nota-docs EN + RU. |
| VII. Spec-First | Pass | spec.md, plan.md, tasks.md. |
| VIII. Safety Rules Never Weakened | Pass | Nothing weakened. |

## Project Structure

```text
src/native/nota.engine/src/engine/Engine_Tracks.cpp   # addTrackLike, deepCloneTrack(withContent)
src/native/nota.engine/include/nota/nota_engine.h     # nota_engine_add_track_like (ABI: src/abi/nota_engine_track.cpp)
src/managed/Nota.Infrastructure/Interop/               # AddTrackLike
src/managed/Nota.Application/Editing/ArrangementMoves.cs
src/managed/Nota.App/ArrangementView.KeyMoves.cs
src/managed/Nota.App/ArrangementView.NewTrackDrop.cs
src/managed/Nota.App/ArrangementView.LaneControl.cs   # drop ring, drag commit via MoveBlock
src/managed/Nota.App/MainWindow.Input.cs               # arrow routing
src/managed/Nota.App/MainWindow.Detail.cs              # editors follow moves, undo / redo
tests/Nota.SmokeTest/ArrangementMovesTests.cs
```
