# Bug Fix: playback stutters once the playhead reaches an audio clip

- **Slug**: audio-editor-playhead-redraw
- **Fixed**: 2026-10-30
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The audio clip editor's playhead line now lives on its own hit-transparent layer stacked
over the waveform view. A transport tick repaints only that layer, not the whole editor.
Every existing full repaint of the waveform view also repaints the layer, so the line follows
zoom, scroll, trims, reverse and resizes as before.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.App/AudioClipEditorView.cs` | modified | `WaveformView.PlayheadLayer` (nested `PlayheadOverlay`), `Redraw()` replaces the view's `InvalidateVisual()` calls, `SetPlayhead` invalidates only the layer, the line moved out of `Render`; the host adds the layer between the view and the BPM-box canvas |
| `CHANGELOG.md` | modified | `Fixed` entry under `[Unreleased]` |

The layer follows the arrangement's `LaneOverlayControl` (#55): the moving decoration is
drawn by a separate control, so a tick doesn't re-record the expensive surface underneath.
The piano roll already does the same thing in a different way (`PianoRollView.SetPlayhead`
repaints only its grid and ruler).

## Tests Added or Updated

- None automated. The smoke test has no UI harness and doesn't compile
  `AudioClipEditorView.cs`, and the engine is untouched, so a `RenderPoolTests` guard
  couldn't fail on the old code (the maintainer agreed to skip it for this fix). The
  before/after benchmark is a UI render count instead (see `test.md`).

## Local Verification

- Ubuntu 24.04 x64 (Xvfb), .NET SDK 10, GCC 13:
  - `scripts/build-linux.sh`: the build is clean, and the smoke test passes apart from the two
    render-pool timing checks, which are flaky on this 4-core VM (see `test.md`).
- Benchmark and manual checks are in `test.md`.

## Deviations from Assessment

- None.

## Follow-ups

- With Follow on, the arrangement lanes repaint every tick while the view scrolls, and
  `EnsureDetailPeaks` re-fetches the visible slice's peaks each time the slice shifts by more
  than half a pixel. That cost doesn't depend on reaching a clip, so it isn't this bug, but
  it's worth measuring on Windows.
