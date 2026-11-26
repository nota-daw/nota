# Bug Assessment — Windows vocoder performance

- **Slug** — `windows-vocoder-performance`
- **Created** — 2026-11-26
- **Source** — [Approved issue #1117](https://github.com/nota-daw/nota/issues/1117), read through `gh` on allowlisted host `github.com`
- **Verdict** — Valid
- **Severity** — Medium. The Windows smoke job blocks merges and the report measures an overrun of the vocoder CPU budget.

## Report

The 32-band vocoder took 85.3 ms versus Chamber's 72.5 ms to render three seconds of audio on `develop`. The same check failed at 85.0 ms versus 77.0 ms on the MIDI routing branch. The vocoder's other checks passed in the reported run. [Develop CI log](https://github.com/nota-daw/nota/actions/runs/45890366199/job/140170117626), [MIDI routing CI run](https://github.com/nota-daw/nota/actions/runs/45891746969)

## Symptom

The Windows smoke test fails because the baseline-adjusted vocoder time exceeds 110% of the baseline-adjusted Chamber time. The target is 32 bands at 48 kHz.

## Reproduction

1. Run `pwsh scripts/build-win.ps1` in the `windows-2022` CI job, as on `develop` at `637f20e8`.
2. Check the vocoder group for the 32-band performance assertion.
3. Run the same assertion locally with `dotnet run --project tests/Nota.SmokeTest -c Release -- --vocoder` after building the native engine.

## Suspected Code Paths

- `Vocoder::process` calls `computeBands` every 32 samples. Each call recalculates the centre frequencies and both sets of SVF coefficients for every band, even in a steady render. At neutral Formant the two coefficient sets are equal.
- The sample loop also converts Pitch, Tilt, and Mix from their smoothed values on every frame, including steady renders.
- `tests/Nota.SmokeTest/VocoderTests.cs` exercises the track carrier path with 32 bands and defaults for the other parameters. The same suite covers the device's carrier transitions, parameter sweeps, presets, and deterministic rendering.

## Root Cause Hypothesis

High confidence in the redundant work, with its share of Windows runtime to be verified. A steady three-second render performs about 144,000 band centre calculations and up to 288,000 filter prewarps. The sample loop adds repeated exponential and trigonometric calls for constant values.

## Proposed Remediation

**Preferred** — In `Vocoder.cpp` and `Vocoder.h`, reuse band coefficients until the effective layout changes and reuse the converted Pitch, Tilt, and Mix values while their controls are steady. Invalidate the layout at sample-rate reset. Keep the existing double-precision formulas, glide cadence, and 32-band CPU assertion.

**Tests to add or update** — In `VocoderTests.cs`, verify that changing the band count and frequency range after the layout has settled updates the reported band centres. Run the existing vocoder and broader smoke suites, compare steady-state timings before and after, and compare audio and telemetry through parameter changes and sample-rate resets.

## Risks & Considerations

- The cache key needs the effective Formant semitone, the smoothed Low, High and Q controls, and the active band count. A sample-rate change must invalidate the cached layout even if the controls are unchanged.
- Carrier transitions still need continuous oscillator and filter state. Keep advancing them regardless of the active carrier.

## Open Questions

- Windows timing after the fix requires a Windows CI run.
