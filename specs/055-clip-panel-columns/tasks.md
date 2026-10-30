# Tasks: Two-column clip inspector and MIDI tools

- [x] T1 `PanelColumns` layout math (stack, best split, choose)
- [x] T2 `SectionColumns` + `ColumnFlow` (columns, hairlines, pinned foot, reserve)
- [x] T3 `ClipEditorKit.Inspector` / `Sections` on the new host; header clip cell follows the inspector
- [x] T4 `MidiToolsView` on the new host
- [x] T5 MIDI and audio editor bodies as dock panels; inspector reserve follows the Tools toggle
- [x] T6 Smoke test cases (`--clipeditor`)
- [x] T7 CHANGELOG
- [x] T8 Screenshots before / after

## Phase 2: Convergence

- [x] T009 Make two columns exactly twice the one-column panel (464 px inspector, 544 px tools) per US1/AC2 (partial)
- [x] T010 Correct SC-001 to the measured editor height at which every inspector section fits without scrolling per SC-001 (partial)
- [x] T011 Update DESIGN.md's clip editor entry (inspector and tools widths) to the adaptive layout per Constitution VI (partial)
- [x] T012 Verify a tall editor renders as before (before / after pixel diff, Tools open) per SC-002 (partial)
