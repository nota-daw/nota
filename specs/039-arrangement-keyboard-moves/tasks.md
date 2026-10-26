# Tasks: 039 Arrangement keyboard moves

- [x] T1 Engine: `Engine::addTrackLike` + ABI `nota_engine_add_track_like` + header doc
- [x] T2 Interop: `NativeMethods.AddTrackLike`, `NotaEngine.AddTrackLike`
- [x] T3 `Nota.Application/Editing/ArrangementMoves.cs`: NudgeDelta, RowShift, Range
- [x] T4 `ArrangementView.KeyMoves.cs`: NudgeSelectedClips, MoveSelectedClipsToTrack
- [x] T5 `MainWindow.Input.cs`: arrow keys route to the Arrangement
- [x] T6 Track headers: Shift-click range, ⌘-click toggle, anchor
- [x] T7 LaneControl: ghost rows below the last track; `MoveClipsToNewTracks` on release
- [x] T8 Smoke test `ArrangementMovesTests` (`--moves`) and the full run
- [x] T9 Settings → Shortcuts rows, CHANGELOG
- [x] T10 Real-app check on Linux and macOS, screenshots on `pr-assets`
- [x] T11 nota-docs EN + RU (nota-docs#31)
- [x] T12 Review fixes: open editors follow clips re-indexed by a move and by its undo / redo; arrows stay in the floating detail window; `addTrackLike` skips copying content
