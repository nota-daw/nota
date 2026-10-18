# Tasks: Pause, Show playhead and loop-region editing

**Input**: [spec.md](spec.md)

- [x] T001 `TransportViewModel.PlayPause` (#51); `transport.playPause` in the catalog, Transport menu, ⇧Space in `OnGlobalTransportKey`
- [x] T002 `ArrangementView.RevealPlayhead`, `PlayheadOffscreen`; `view.showPlayhead` (P) in the catalog, View menu, `MainWindow.ShowPlayhead`
- [x] T003 Follow suspension: `NoteManualScroll` from wheel / scrollbar / overview / auto-scroll, resume on Play, Show playhead, Follow click; `LaneControl.IsGesturing` guard; `.paused` Follow button style + status text
- [x] T004 Ruler: off-screen arrow, loop edge hover / resize, brace move, drag readout, right-click menu with Clear loop region; transport cell after a clear
- [x] T005 Settings → Shortcuts rows, CHANGELOG, FEATURES.md, nota-docs EN + RU
- [x] T006 Verified in the headless app (transport positions, scroll, loop ranges), `scripts/build-linux.sh` green; macOS check on the Mac
