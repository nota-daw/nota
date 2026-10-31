# Implementation Plan: Default waveform rendering

**Branch**: `057-waveform-rendering` | **Spec**: [spec.md](spec.md) | **Issue**: #267

## Summary

Add an RMS level to the engine's waveform buckets and draw a new default style, **Smooth**, in
the arrangement lanes and the audio clip editor through one shared painter.

## Audio-Thread Impact

None. Nothing in the render path changes: `Engine_Render` and the clip readers never touch
`rmsTable`, `rmsRange` or the levels calls.

- The RMS table is filled in `updatePeakTable`, which runs only where the peak table already
  is built: the decode in `AudioFile` / `AudioImportJob::step` / `finish` (loader thread) and
  `AuditionRig` (control thread). It is sized together with `peakTable` there. The cost is one
  extra `float` per 512 frames (at most 0.2% of the sample memory, for mono).
- `rmsRange` and `nota_clip_get_levels` are UI-thread queries on the same path as the
  existing `nota_clip_get_peaks*` calls. They write only into the caller's arrays, take no lock
  of their own and allocate nothing.
- AR-5: no graph or snapshot edits. AR-7: the managed side reaches the RMS only through the
  new C function, which uses plain C types. AR-9: no musical-time source added. No JUCE and no
  new dependency.

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| I. Real-Time Audio Safety | Pass | No audio-thread code changes (above). |
| II. Boundary Discipline | Pass | One new pure C call in `nota_engine.h`; existing calls unchanged. |
| III. Licensing Hygiene | Pass | New `WaveformPainter.cs` carries the SPDX header; no new dependency. |
| IV. Verification Gate | Pass | Smoke test extended (RMS vs brute-force scan, envelope bounds) and green. |
| V. One Logical Change per PR | Pass | Waveform rendering only. |
| VI. Documentation Stays in Sync | Pass | `CHANGELOG.md`, `FEATURES.md`; `nota-docs` appearance page is a follow-up in that repo. |
| VII. Spec-First | Pass | This spec/plan/tasks run. |
| VIII. Safety Rules Never Weakened | Pass | No guard or check relaxed. |

## Engine

- `SampleBuffer`: `rmsTable` (mean square of the stereo mid per `kPeakBlock`), built and
  sentinel-filled with `peakTable`; `rmsRange(f0, f1)` reads whole blocks from the table and
  scans the partial ends, like `peakRange`. A seeded (cached) peak table has no RMS until the
  decode finishes and rebuilds both.
- `Engine::getClip*Peaks*`: optional `float* outRms`; `reversePeaks` flips it too.
- ABI: `nota_clip_get_levels(engine, track, clip, view, a, b, out_min_max, out_rms, max)` with
  `NOTA_CLIP_LEVELS_*` views mapping to the five existing calls.

## Managed

- `IAudioEngine.GetClip*Peaks*` gain an optional `float[]? outRms`; `NotaEngine` routes to
  `nota_clip_get_levels` only when it is given.
- `WaveformPainter` (replaces the unused top-level `WaveformView`): folds buckets into
  device-pixel columns, fills the envelope then the body as two `StreamGeometry` polygons.
- Lanes: `ClipVM.Rms` / `DetailRms`, resize-preview RMS; Smooth path in `DrawWaveform` /
  `DrawResizeWaveform`; detail fetched at device-pixel resolution.
- Clip editor: overview and detail carry RMS; `DrawPeaks` / remapped path feed the painter;
  detail thresholds in device pixels.
- `WaveformStyle { Smooth, Solid, Bars }`; preference picker, settings doc, clamps.

## Testing

- Smoke test: RMS vs brute-force scan (frame-scan and block-table paths), min/max unchanged,
  RMS within the envelope on warped buckets.
- Screenshots: before/after, lanes at 6 / 28 / 220 px a beat, clip editor at 1× / 6× / 60×.
