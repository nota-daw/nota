# Quickstart: validating Mixer Groups and Compact Strips

How to prove the feature works end to end. Controls and measures:
[contracts/ui-mixer.md](contracts/ui-mixer.md); stored state:
[data-model.md](data-model.md).

## Prerequisites

- `scripts/build.sh` green (Release engine in `build/`, managed app, smoke test).
- A headless Avalonia harness in a scratch folder (net10.0 console, `ProjectReference` to
  `Nota.App.csproj`, `Avalonia.Headless` + `Avalonia.Skia`), run with
  `NOTA_DATA_DIR=<scratch>/data` so no real user settings are touched. It shows the real
  `MainWindow` (DataContext from `App.Services`), builds tracks through the engine, and
  saves `CaptureRenderedFrame()` PNGs. Synthetic pointer events drive the real handlers.

## Test project

Built by the harness through the engine API:

- `Bus` (group) → `Drums` (group) → `Kick`, `Snare`, `Hats` (audio)
- `Bus` → `Bass` (instrument)
- `Vox` (audio, ungrouped), `Keys` (instrument, ungrouped), returns A and B
- Engine order deliberately scrambled (create `Vox` first, `Hats` before `Drums`), so the
  hierarchical order is actually exercised.

## Scenarios

| # | Steps | Expected |
|---|---|---|
| 1 | Show the Mixer docked | strips: Bus, Drums, Kick, Snare, Hats, Bass, Vox, Keys, A, B, Master; Bus bar over Bus…Bass, Drums bar below it over Drums…Hats; Vox / Keys / returns without bars (US1-1, -2, -4) |
| 2 | Move `Vox` into `Bus` in the Arrangement, then undo | bands follow both steps without reopening (US1-3) |
| 3 | Switch to Stereo map | strip column shows the same bars (US1-5) |
| 4 | Click the Drums triangle in the Mixer | Kick / Snare / Hats hidden, `+3` tag; the Arrangement's Drums row collapses too (US2-1, clarified A) |
| 5 | Solo Snare, arm Kick, then fold Drums | S and arm dots on Drums; offline render equals the unfolded render byte for byte (US2-3, SC-005) |
| 6 | Fold Drums, fold Bus, unfold Bus | Bus members back; Drums stays folded (US2-2) |
| 7 | Save, New, Open | Drums still folded in both views (US2-4) |
| 8 | Select Hats in the Arrangement with Drums folded | Drums strip outlined and scrolled into view; nothing unfolds (US2-5) |
| 9 | Pop the Mixer out to its window | same order, bands, fold state (FR-012) |
| 10 | Time fold / unfold with 100 tracks in 10 groups | < 100 ms each, no strip rebuild (SC-002) |
| 11 | Footer `Compact` on, at a 1440 px window with 24 tracks | strips 72 px; ≥ 2× the regular count visible; setting survives a restart (US3-1, SC-003, FR-013) |
| 12 | Drag the triangle handle / the meter; type `-6` in the dB number; double-click | volume follows on the meter's dB scale, `-6.0 dB`, reset to 0 dB; one undo step each (US3-2, -3) |
| 13 | MIDI-learn volume, mute, solo on a compact strip, then switch to regular | mappings work in both layouts, same targets (US3-4) |
| 14 | Click M / S / Arm on compact strips | full-width toggles, colours as today (US3-5) |
| 15 | Shrink the docked panel | I/O hides, then sends, then the meter shortens; buttons stay (US3-6) |
| 16 | Long track name in compact | ellipsised, full name in tooltip (US3-7) |
| 17 | Fresh `NOTA_DATA_DIR` | Mixer opens with regular strips (US3-8) |
| 18 | Studio Dark, Studio Light, Ember Graphite, Ember Paper | screenshots: bands readable, strip colours not drowned (FR-020) |
| 19 | Interface language French | new strings translated (FR-021) |

## Gate

`scripts/build.sh` green; every scenario above passes; screenshots for the PR (before /
after, regular and compact, folded and unfolded) go to the `pr-assets` branch via `nota-pr`.
