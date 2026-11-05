# Tasks: 056 Clip editor fills the window, edge double-click reset

- [x] T1 `Glyph`: `Expand` / `Shrink` path kinds
- [x] T2 Detail header button (`DetailMaximizeBtn`), View menu item, catalog command `view.maximizeDetail` (⌘⇧F)
- [x] T3 `MainWindow.Detail.cs`: `SetDetailMaximized` (rows, browser, docked Mixer), `ShowDetail` keeps / leaves it, the panel hiding leaves it; `LayoutBottomPanels` / `FitBottomPanels` respect it
- [x] T4 Leave it from `SetView`, View ▸ Toggle browser and `DockMixer`
- [x] T5 Double-click reset: Detail splitter and edge (tab default, forget `_detailHeight`), Mixer top edge (forget the dock height), browser splitter (344)
- [x] T6 Settings → Shortcuts rows, CHANGELOG, FEATURES
- [x] T7 Smoke test; headless check of every scenario against the real window; before/after screenshots
- [x] T8 nota-docs EN + RU (detail panel page, shortcuts page rows) — nota-docs#51
