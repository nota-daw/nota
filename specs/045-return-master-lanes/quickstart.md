# Quickstart: validating return / master automation and heights

## Prerequisites

- `scripts/build.sh` passes, including the new return-automation smoke check
  (`tests/Nota.SmokeTest/ReturnAutomationTests.cs`).
- Isolate app data with `NOTA_DATA_DIR=<tmp dir>` (HOME does not isolate JUCE).
- A test project: 6+ tracks sending to two returns; return A has a reverb device.

## Scenarios

| # | Steps | Expected |
|---|-------|----------|
| 1 | New project | Master row is 30 px (Compact); add a return → it is 30 px too (FR-012) |
| 2 | Open a project saved before this feature | Returns and master Compact; no errors |
| 3 | Automation mode → return A pill → Volume; double-click to add a ramp; play | Return level follows; header fader and mixer move (FR-001–003) |
| 4 | Return A pill → reverb decay; draw points, Alt-bend a segment, Shift-select a range, copy/paste | Same as on a track lane (FR-002) |
| 5 | Undo / redo a return point edit | Steps like on a track (FR-004) |
| 6 | Save, close, reopen | Return lanes and points restored |
| 7 | Master lane in automation mode at 30 / 64 / 128 px | Master-volume points editable at every height (FR-005) |
| 8 | Drag a return's bottom edge (header, then lane) | Row resizes, snaps to 64 / 128; rows below move ([gestures](contracts/ui-gestures.md)) |
| 9 | Right-click return / master → Height → each preset | Row takes 30 / 64 / 128; current preset marked (FR-007) |
| 10 | Select both returns, drag one edge | Both take the height |
| 11 | Double-click a footer seam | Back to 30 (FR-008) |
| 12 | Alt-drag a track edge; track menu "all tracks" Large | Tracks change; returns and master don't (FR-016) |
| 13 | Make every footer row Large in a short window | Footer stops at ~half the height and scrolls; tracks stay visible (FR-015) |
| 14 | Screenshot return / master headers at 30 / 61 / 62 / 111 / 112 / 256 px × widths 160 / 228 / 480 | Compact / standard / large layouts; nothing clipped (SC-005) |
| 15 | Drop an effect from the browser onto each footer row at mixed heights | Lands on the highlighted bus (FR-013) |
| 16 | Save with resized returns / master; inspect `track-view.json` | Matches [the sidecar delta](contracts/track-view-sidecar.md); reopen restores the heights |
| 17 | Regression: track automation, track resizing, clip edits on track lanes | Unchanged (editor refactor) |

## Screenshot harness

Recipe in agent memory `nota-ui-verify`: an env-guarded in-app hook that sets heights and
then `screencapture`, or an Avalonia.Headless offscreen render of `ArrangementView` with
a seeded project.
