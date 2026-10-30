# Feature Specification: Clip editor fills the window; double-click a panel edge to reset it

**Feature Branch**: `cursor/detail-maximize-splitter-reset-bc46`

**Created**: 2026-10-30

**Status**: Implemented

**Input**: Issues nota-daw/nota#304 (a button and a shortcut that make the clip editor — the
Detail panel's Clip / Pattern tab — fill the window, and toggle back) and #305 (double-click
the separator between the arrangement and the clip editor to reset its default height).

## Nota Context

**Feature area**: UI (Avalonia, `src/managed/Nota.App`). No engine change.

What exists today:

- `BodyGrid` is `[main *] [mixer splitter, mixer]? [detail splitter, detail]`
  (`MainWindow.Detail.cs`, specs/054). `ShowDetail(height, …)` sizes the Detail row per tab:
  250 for the piano roll and audio editor, 260 for Pattern, the card height for Devices
  (not resizable). The Clip tabs remember a dragged height in `_detailHeight`; the asked-for
  height is `_detailWanted`, which `FitBottomPanels` fits into the window.
- With the Mixer docked the Detail panel is resized by `DetailEdge` instead of its
  GridSplitter. The browser column and the docked Mixer have their own splitters.
- View ▸ Toggle full screen (F11) already exists for the OS window.

## User Scenarios

1. **Fill the window (P1).** With a clip open in the Clip or Pattern tab, the new button at
   the right of the Detail header (before pop-out), **View ▸ Clip editor fills the window**,
   the palette command of that name or ⌘⇧F / Ctrl+Shift+F make the clip editor take the
   whole body of the window: the browser, the Arrangement / Session / Modular view and a
   docked Mixer fold away. The button turns into its "back" form and the menu item is ticked.
2. **Back to the split (P1).** The same button, menu item or key brings back the browser,
   the main view and the docked Mixer at the heights they had.
3. **Reset by double-click (P1).** Double-clicking the edge above the Detail panel (the
   splitter, or the edge under a docked Mixer) sets the panel back to its tab's default
   height and forgets the height the Clip tab remembered.
4. **Same on the other edges (P3).** Double-clicking the browser splitter resets the browser
   to 344 px; double-clicking the docked Mixer's top edge forgets its dragged height and
   docks it at the first-dock default again.

## Decisions

- "Fill the window" is inside the main window (#304's note), not OS full screen; the two
  combine. It is not saved: a new launch opens on the split.
- Only the Clip / Pattern tabs can fill the window — the Devices cards have a fixed height,
  so the button is hidden on Devices (as the Pattern tab is hidden where it means nothing).
- The shortcut is ⌘⇧F / Ctrl+Shift+F: free in every view and in the piano roll (which uses
  ⇧F and ⌥⇧F), next to F11's full screen in Settings → Shortcuts.
- Anything that asks to see the hidden parts goes back to the split: picking a view,
  View ▸ Toggle browser, showing the Mixer docked, the Devices tab (also by Tab), closing,
  hiding or popping out the panel. Opening another clip keeps the editor filling the window.

## Requirements

- **FR-1** Filling the window collapses the main row to 0 (its 120 px minimum lifted), hides
  `BrowserGrid`, leaves the docked Mixer's rows out of the grid, and gives the Detail row
  the rest (star). The Detail splitter / edge are hidden meanwhile.
- **FR-2** Going back restores the Detail row from `_detailWanted` and re-lays the bottom
  panels, so the Mixer gets `_mixerWanted` and the window fit applies as before.
- **FR-3** `view.maximizeDetail` is in the command catalog (⌘⇧F), bound in
  `MainWindow.Commands.cs`, unavailable in the palette unless a clip tab is shown docked
  (or it is already filling the window); run from the menu or key otherwise it says why in
  the status line.
- **FR-4** A double-click on an edge is taken in the tunnel phase and handled, so no drag
  starts and no old height is recorded afterwards.
- **FR-5** Settings → Shortcuts lists ⌘⇧F and the double-click (the palette smoke test checks
  every catalog shortcut is listed).

## Success Criteria

- Smoke test (palette: every catalog command bound and listed) passes.
- Checked headlessly against the real `MainWindow` and demo song: fill / back through the
  command and Ctrl+Shift+F, heights restored, double-click resets on the splitter and on the
  Mixer-docked edge, a docked Mixer folded and restored, Devices / close go back to the split.
- Before/after screenshots on `pr-assets`.
