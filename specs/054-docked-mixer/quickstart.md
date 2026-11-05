# Quickstart: validating the Docked Mixer

## Prerequisites

- Release engine build in `build/` and the managed app built:
  `dotnet build src/managed/Nota.App/Nota.App.csproj`.
- Isolated user data: `NOTA_DATA_DIR=/tmp/dockmixer/data` (fresh `settings.json`).
- If the GUI can't be launched (sandbox / display asleep), use a headless harness: real
  `MainWindow` with `DataContext` from `App.Services`, tracks added through the engine,
  commands fired via the command registry, frames via `CaptureRenderedFrame()`.

## Scenarios

| # | Steps | Expected |
|---|---|---|
| 1 | Fresh settings, open a project with 6 tracks, ⌘⇧M | Mixer appears docked under the arrangement; arrangement still scrolls and edits (US1-1) |
| 2 | Move a fader, pan, mute; undo | Audible / meters move; undo reverts (US1-2, FR-010) |
| 3 | Switch Strips ↔ Stereo map in the panel toolbar | View changes; `settings.json` `MixerView` updated (US1-3) |
| 4 | Drag the Mixer's top edge up and down, restart | Height follows, clamps at min; restored after restart (US1-4, FR-005) |
| 5 | Drag it low | I/O hides, then sends, then fader shrinks; core stays; drag stops at min (FR-014) |
| 6 | At low height click footer `Sends` (muted) | Sends show, strip row scrolls vertically; click again → off |
| 7 | Show Devices panel too (select a track) | Mixer above, Devices below; both splitters work; hiding either keeps the other (FR-004) |
| 8 | Shrink the main window to ~500 px tall with both panels | Main view keeps ≥120 px; the Mixer shrinks first (down to its minimum), then Devices |
| 9 | Switch Arrangement → Session → Modular | Mixer stays docked, same view/height; Session's own I/O·Sends·Mixer unchanged and in sync (US2) |
| 10 | Panel pop-out button | Mixer moves to a window, panel gone, meters keep running (US3-1) |
| 11 | Window dock button | Window closes, Mixer docked (US3-2) |
| 12 | Floating, quit, relaunch | Mixer reopens floating; same for docked shown, and for hidden (FR-009, SC-004) |
| 13 | Floating, close window, ⌘⇧M | Opens floating again (US3-4) |
| 14 | Focus a docked fader, press Space / Return / ⌘Z / Esc | Transport + undo work; Esc does not hide the docked Mixer |
| 15 | MIDI learn armed, click a docked fader, move a controller | Mapping created and works |
| 16 | Palette from a focused docked control | Opens in the main window with Mixer origin |
| 17 | UI scale 125% + Studio Light theme | Panel restyles live; min heights scale |
| 18 | `view.mixerFloat` from the palette while hidden, then ⌘⇧M | Opens floating |

## Gate

`scripts/build.sh` passes (smoke test; no engine change expected).
