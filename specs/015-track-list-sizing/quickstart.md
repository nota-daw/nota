# Quickstart: validating track-list sizing

## Prerequisites

- `scripts/build.sh` passes (it builds the app and runs the smoke test, which is
  unchanged by this feature).
- Isolate app data with `NOTA_DATA_DIR=<tmp dir>` when running UI checks. `HOME` does
  not isolate JUCE.
- A test project with at least 40 tracks (instrument + audio), one group with children,
  and two returns.

## Scenarios

| # | Steps | Expected |
|---|-------|----------|
| 1 | Drag a track's bottom edge (in the header) down by about 60 px | Only that row grows; clips/waveform/automation scale; rows below move ([ui-gestures](contracts/ui-gestures.md)) |
| 2 | Same from the lane edge | Same result; a clip touching the edge is not moved or trimmed |
| 3 | Drag past 256 and below 30 | Stops at the limits; passes 64/128 with a felt snap |
| 4 | Multi-select 3 tracks, drag one edge | All three take the height |
| 5 | Alt-drag any edge | Every track row takes the height; group bars, returns and master unchanged |
| 6 | Context menu ▸ Track height ▸ All tracks: Compact | ≥ 2× tracks visible vs. default (SC-001) |
| 7 | Double-click an edge / the column seam | Reset to 64 / 228 |
| 8 | Drag the header/lane seam to 160 and 480 | All header parts (zoom bar, overview, sections, tracks, footer) follow; no overlapping controls |
| 9 | Screenshot headers at 30 / 61 / 62 / 64 / 111 / 112 / 256 px × widths 160 / 228 / 480 | Compact, standard and large layouts; nothing clipped (SC-003) |
| 10 | Reorder a track by its title strip; trim and move clips; draw automation; drop a sample on a track | All work as before (SC-006) |
| 11 | Duplicate a 128 px track; copy and paste a 30 px track | The new tracks carry the heights |
| 12 | Save, close, reopen | Heights and width restored; `track-view.json` in the bundle matches [the contract](contracts/track-view-sidecar.md) |
| 13 | Open a project saved before this feature | Default layout, no errors |
| 14 | Restore an older version from History | Layout of that version restored |
| 15 | Alt-drag on a 40-track project | Row follows the pointer without visible lag (SC-005) |

## Screenshot harness

Use the recipe in agent memory (`nota-ui-verify`): an env-guarded in-app hook that
sets sizes, then `screencapture`, or an Avalonia.Headless offscreen render of
`ArrangementView` with a seeded project.
