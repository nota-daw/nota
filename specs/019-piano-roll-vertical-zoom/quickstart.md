# Quickstart: validating piano roll vertical zoom

## Prerequisites

- `scripts/build.sh` passes (builds native + managed, runs the smoke test — unaffected:
  no engine change).
- Isolate app data with `NOTA_DATA_DIR=<tmp dir>` for UI runs (`HOME` does not isolate
  JUCE).
- Test clips: A) notes C3..C5; B) one note E4; C) empty; D) notes A0 and C8.

## Scenarios

Gestures and limits: [contracts/ui-gestures.md](contracts/ui-gestures.md). State rules:
[data-model.md](data-model.md).

| # | Steps | Expected |
|---|-------|----------|
| 1 | Open clip A, ⌘⌥-scroll up over E4 | Rows grow, E4 stays under the pointer (US1-1) |
| 2 | Keep zooming in / out past the limits | Stops at 36 px / 6 px, no jump (US1-2/3) |
| 3 | At 6 px and 36 px: click to add, drag, resize, box-select notes | The pitch edited is the one under the pointer (SC-004) |
| 4 | Plain wheel, ⌘-wheel, ⇧-wheel | Vertical scroll, horizontal zoom, horizontal scroll — as before |
| 5 | At 6 px with a panel ≥ 540 px tall | All 88 keys visible, range pinned to the top (SC-001) |
| 6 | Press C4 on the keys, drag down then up | Taller then shorter, C4 stays at press height, no note sounds (US3) |
| 7 | ⌥ + pinch on a Mac trackpad | Rows zoom anchored at the pointer (US4) |
| 8 | Clip A zoomed elsewhere, press ⇧F in the grid | C3..C5 fill the view with one row margin; no note F plays (US2-1) |
| 9 | Clip D, Fit; clip B, Fit; clip C, Fit | Min height centred; ≤ 36 px centred on E4; 12 px around C5 (US2-2/3/4) |
| 10 | Select two notes in clip A, Fit | Fits all notes, not the selection (US2-5) |
| 11 | Header "Fit" button; palette "Fit to notes" | Same as ⇧F |
| 12 | Zoom clip A, open clip B, back to A | A restores its zoom and position; B opens at 12 px (SC-005, Q1) |
| 13 | Zoom, then ⌘Z | Undo does not touch the zoom; project not marked modified (FR-010) |
| 14 | Start a note drag and ⌘⌥-scroll mid-drag | Zoom ignored until release |
| 15 | Hold keys / record MIDI while zoomed | Held-key highlights match the row height |
| 16 | Preferences → Shortcuts → Piano roll | Lists the four new rows |
| 17 | ⇧F with focus outside the piano roll | Behaves as before |

## Headless capture

Use the recipe in agent memory `nota-ui-verify`: a throwaway console project referencing
`Nota.App.csproj` with `Avalonia.Headless` + `Avalonia.Skia` 12.0.3, host a
`PianoRollView` in a `Window`, seed the clips above with `SetClipIdentity` + `SetNotes`,
raise synthetic `PointerWheelEventArgs` / pointer press-move-release on the grid and keys
column, and `CaptureRenderedFrame()` at 6 / 12 / 36 px and after Fit.
