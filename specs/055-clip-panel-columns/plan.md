# Implementation Plan: Two-column clip inspector and MIDI tools

**Branch**: `cursor/clip-panels-two-columns-edc4` | **Spec**: [spec.md](spec.md) | **Issue**: nota-daw/nota#276

## Summary

UI only (`src/managed/Nota.App`); no engine, model or project-file change. The side panels keep
their sections as separate controls and a new host picks one or two columns at measure time from
the height it is given and the width it is offered.

## Pieces

- **`ClipEditor/PanelColumns.cs`** (no Avalonia, linked into the smoke test): `Stack` (sections
  plus gaps), `BestSplit` (the leading run that minimises the taller column, ties to the first
  column), `Choose` (one column if it fits / no width / a single section, else two).
- **`ClipPanelColumns.cs`**:
  - `SectionColumns`: the sections in one or two equal columns, a 1 px `GraphBorder` hairline
    centred in the gap between neighbours of a column (replaces the `StackPanel` + `Rule()`
    children `ClipEditorKit.Sections` built).
  - `ColumnFlow`: a vertical `ScrollViewer` around the sections with an optional foot pinned
    below. Measure: foot at one-column width, each section at the column's inner width, then
    `PanelColumns.Choose`; two columns are `2 × ColumnWidth + border` wide (the panel, border
    included, doubles) and the column gap is the side padding plus the border, so a section has
    the same width in either layout and never reflows. `Reserve`
    is the width the editor canvas keeps.
- **`ClipEditorKit`**: `Sections` returns `SectionColumns`; `Inspector` builds a `ColumnFlow`
  (column 231 px + 1 px border = 232 px; two columns 464 px, padding 14/16/14/8, hint as the foot) and hands it back;
  `Header` takes the inspector and keeps the clip cell at its width (`SizeChanged`);
  it also gives the inspector's `ColumnFlow` a `Beside` width (the rest of the header) that
  `PanelColumns.RoomForTwo` weighs against the canvas reserve;
  `MinCanvasW = 480`; `Rule()` goes (unused).
- **`MidiToolsView`**: sections = [Generate / Transform + tool list], parameters, Output, spacing
  10; foot = result line, Apply, Revert / Defaults; column 271 px + 1 px border = 272 px (544 px
  in two columns).
- **Editors**: the body `Grid` (`Auto,*,Auto` / `Auto,*`) becomes a `DockPanel` (inspector left,
  tools right, canvas fill), so the panels are measured with the real width (a Grid `Auto` column
  measures with infinite width) and in order: inspector first, tools from what is left.
  `ClipEditorView.SetTools` sets the inspector's `Reserve` to `MinCanvasW + ToolsW` while Tools
  is open.
- **Re-layout**: a section growing or shrinking changes the scroller's desired size whenever the
  choice could flip, which re-measures the host; no extra hook.

## Audio-thread impact and architecture rules

None. Every change is in the Avalonia layout pass on the UI thread (`Nota.App`); nothing
touches the engine, the C ABI, the graph or the transport.

- AR-6 (no allocation, locks or I/O on the audio thread): the audio thread runs no new code.
- AR-5 (graph snapshots): no structural edit; the graph is not touched.
- AR-7 (pure C ABI): no new or changed `nota_engine.h` call.
- No JUCE outside `pluginhost/`: no native code changes.
- AR-9 (`Transport` is the only source of musical time): nothing reads or keeps time.
- No new dependency.

## Testing

- Smoke test `--clipeditor`: `PanelColumns` cases (fits → one, overflow → two with the expected
  split, narrow → one, still too tall → two and scroll, lone section, best split, tie rule).
- Full `scripts/build-linux.sh` smoke run.
- Screenshots of a short editor before / after (MIDI with Tools, audio).
