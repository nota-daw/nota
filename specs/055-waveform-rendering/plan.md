# Implementation Plan: Default waveform rendering

**Branch**: `055-waveform-rendering` | **Spec**: [spec.md](spec.md) | **Issue**: #267

## Summary

Add an RMS level to the engine's waveform buckets and draw a new default style, **Smooth**, in
the arrangement lanes and the audio clip editor through one shared painter.

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
