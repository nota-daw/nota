# Quickstart: verify #308

## Prerequisites

- `scripts/build.sh` green (Release engine in `build/`).
- Headless Avalonia harness (console net10.0 project referencing `Nota.App.csproj`,
  `Avalonia.Headless` + `Avalonia.Skia`), `NOTA_DATA_DIR=/tmp/detail308/data`. Show the real
  `MainWindow` with its view model, add an instrument track with a MIDI clip, an audio
  track with an audio clip, then drive clicks with synthetic `PointerPressed/Released`
  events (`ClickCount` 1 or 2) on the header card, the lane control and Session slots.
  After each step log `DetailPanel.IsVisible`, which tab button is checked, and the hosted
  content type.

## Scenarios (setting off unless stated)

| # | Start | Action | Expect |
|---|-------|--------|--------|
| 1 | panel hidden | click track header | hidden; selection = track |
| 2 | panel hidden | click MIDI clip | hidden; selection = clip |
| 3 | panel hidden | keyboard ↓ to next track | hidden |
| 4 | panel hidden, Session | click filled slot | hidden |
| 5 | panel hidden | double-click instrument track header | visible, Devices; no plug-in window |
| 6 | panel hidden | double-click MIDI clip | visible, Clip, piano roll on that clip |
| 7 | panel hidden | double-click audio clip | visible, Clip, audio editor |
| 8 | panel hidden, Session | double-click filled slot | visible, Clip |
| 9 | panel visible on Devices | click another track | Devices of that track |
| 10 | panel visible on Clip | click another clip | Clip on that clip |
| 11 | panel popped out | click another track / clip | window follows |
| 12 | panel hidden, after #1 | View → toggle Detail | visible on Devices of the selected track, Devices height |
| 13 | setting **on**, panel hidden | click track header | visible, Devices (old behaviour) |
| 14 | panel hidden | double-click empty instrument lane | new MIDI clip; panel hidden |
| 15 | Session | double-click column header | rename prompt |
| 16 | — | restart with setting on | setting still on |

Capture before/after screenshots of scenario 1 and 5 (`CaptureRenderedFrame`) for the PR,
and the Preferences → Appearance page with the new switch.
