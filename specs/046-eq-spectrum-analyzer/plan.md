# Implementation Plan: EQ spectrum analyzer

**Branch**: `feat/eq8-analyzer` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)

## Summary

Replace the 96-band analysis in EQ-8 and EQ-3 with a shared engine `SpectrumAnalyzer`
(256 points, 1/6-octave band energy, fast-rise / 300 ms fall, optional 1.5 s peak hold, falls on
silence), still run from `scopeRead` off the audio thread. EQ-8 gains Range and Peak params
(appended, default 90 dB / off). The cards draw the spectrum as a filled low-contrast area under
the curve through one shared helper.

## Technical Context

C++20 engine (Signalsmith RealFFT), .NET 10 / Avalonia app. No new dependencies. Audio thread
untouched: the rings and their writes stay as they are.

## Constitution Check

- Audio-thread rules: analysis stays on the reading thread under the device mutex (pass).
- Pure C ABI: no ABI change; telemetry goes through the existing `nota_device_scope` (pass).
- Backwards compatibility: params appended, older projects load with defaults (pass).
- Tests: smoke test drives the analyzer with sine, pink noise and silence (pass).

## Project Structure

```text
src/native/nota.engine/src/dsp/primitives/SpectrumAnalyzer.{h,cpp}   new
src/native/nota.engine/src/devices/effects/Eq.{h,cpp}               uses it, 2 params, layout
src/native/nota.engine/src/devices/effects/Eq3.{h,cpp}              uses it, layout
src/native/nota.engine/CMakeLists.txt                                new source
src/managed/Nota.App/Controls/SpectrumFill.cs                        new drawing helper
src/managed/Nota.App/Controls/Eq8Views.cs, Eq3Views.cs              fill, peak, range
src/managed/Nota.App/DeviceCards/Bodies/EqDeviceBody.cs, Eq3DeviceBody.cs  layout, controls
tests/Nota.SmokeTest/EqAnalyzerTests.cs (+ Program.cs `--eq-analyzer`)
```

## Phases

1. Engine analyzer + EQ-8/EQ-3 wiring + smoke tests (sine level, pink flatness, flicker, fall
   time, silence, timing, params default on an old-layout project, bit-identical render).
2. App drawing and controls.
3. Docs (EN + RU), CHANGELOG, FEATURES; macOS check with before/after screenshots.
