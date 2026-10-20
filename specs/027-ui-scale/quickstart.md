# Quickstart: Validate the Interface Scale

## Prerequisites

- `scripts/build.sh` passes (it builds native and managed code and runs the smoke
  test, which covers `InterfaceScale.Normalize`).
- Isolated data dir: `export NOTA_DATA_DIR=/tmp/uiscale-data` (see the
  nota-ui-verify recipe; `HOME` does not isolate JUCE).
- If the display is asleep or GUI launch is blocked, use the headless Avalonia harness
  (`AppBuilder…UseHeadless(new() { UseHeadlessDrawing = false })`,
  `CaptureRenderedFrame()`). Popups do not render headless, so build their content
  into a window for shots.

## Scenarios

1. **Default unchanged** — fresh data dir, launch. Preferences shows *Interface scale*
   = 100 %. Capture the main window and compare with a capture from `develop`: identical.
2. **Live change** (US1, FR-002/003) — open a project with a few tracks, a device
   detail window and the mixer. Pick 125 %, then 150 %, then 75 %. Every open window
   redraws at once. Transport, selection and playback continue. Text in the track
   header measures 1.25×, 1.5× and 0.75× of its 100 % height (SC-001).
3. **Popups** (US1 sc. 3) — at 150 %, open a context menu on a track, the main menu
   (Windows/Linux), a flyout, a combo box dropdown and a tooltip. Each is 1.5× and
   anchored to its target.
4. **Sharpness** (FR-007, R5) — zoom screenshots of text, knobs and 1 px seams at
   75 / 125 / 150 / 200 %. Text is crisp. Note any seam softness at 125 % in the PR.
5. **Hit areas** (FR-004, US2 sc. 2) — at 75 % and 200 %, click a mute button,
   drag a knob, drag a clip and draw a note. Each responds where it is drawn.
6. **Content zoom** (US3) — at 150 %, zoom the arrangement and the piano roll around
   the cursor. The point under the cursor stays put, as at 100 %.
7. **Window limits** (R4, edge cases) — on a 1920×1080 screen at 150 %, the main
   window's minimum is 1650×1020. Its title bar drags, and the macOS traffic lights
   don't overlap the title. At 200 % the window still fits the screen.
8. **Persistence** (FR-005, SC-004) — set 125 %, quit, relaunch: opens at 125 % from
   the first frame. Edit `settings.json` to `"InterfaceScale": 130`, relaunch: 100 %.
9. **Audio untouched** (SC-005) — with playback running, switch the scale several
   times. No dropouts. CPU meter unchanged.
