# Bug verification — Chroma bass spectrum

- **Tested** 2026-11-20 on macOS, Apple M2
- **Assessment** [Chroma bass spectrum](assessment.md)
- **Fix** [Chroma bass spectrum](fix.md)

| Check | Result | Evidence |
|---|---|---|
| Dry and Tuned, 44.1, 48 and 96 kHz | Pass | The 55 and 82 Hz inputs produce two Dry peaks and two shifted Tuned peaks in phase and antiphase. The Dry bass levels match exactly between the two polarity cases. The FFT bins are approximately 5.38, 5.21 and 5.21 Hz apart. |
| Dry peak width | Pass | A 55 Hz tone's width at 6 dB below its peak falls from 75 to 19 scope columns at 44.1 kHz, 77 to 20 at 48 kHz and 116 to 20 at 96 kHz. |
| 250–400 Hz blend | Pass | In the 250, 305 and 400 Hz fixture, neighboring Dry levels across either crossover endpoint differ by at most 1.05 dB across the tested rates. Both traces match the baseline at and above 400 Hz. |
| Cards S and L at 1× and 2× | Pass | Compared live headless renders. Flat Dry and Tuned scans found one physical pixel per line, with a two-pixel interval throughout the scanned regions. |
| Audio comparison | Pass | Bit-identical stereo float output for the two-tone fixture at all three rates and both stereo polarities. A separate 48 kHz fixture matches at 64 and 256 sample blocks with active correction and with every note enabled. The reported latency stays at 1024 samples. |
| Native regression suite | Pass | `scripts/test-color.sh --tests-only` and the separate build using the portable FFT each pass 81 checks. The allocation counter stays at zero during guarded renders. The baseline fails the 12 peak-separation checks and the cadence check. |
| C ABI Chroma smoke tests | Pass | The Release smoke test with `--color` passes 19 Chroma checks. |
| Manual | Pass | Regenerated S and L for both Studio themes. The documentation build succeeded and `npm run check` reported zero errors. Both card sizes loaded in the English and Russian previews. |
| Full macOS build and smoke suite | One timing failure | The universal engine and Release app build. The smoke suite reports 100 of 4125 late blocks in the overloaded render-pool test, above its 1% limit. The same check reports 126 late blocks with the baseline engine. This test uses audio tracks with Reverb. The three remaining scanner checks pass separately. |

The callback timings measure one `Chroma::process` call at 48 kHz on Apple M2.
Values in microseconds are medians across 11 timed four-second renders for
each build, following one warm-up render. Thread CPU time is measured across
each render and divided by its block count.

| Routing | Block | Mean before → after | P99 before → after | Thread CPU before → after |
|---|---:|---:|---:|---:|
| Active correction, resonance and Morph | 64 | 108.1 → 108.5 µs | 339.0 → 347.4 µs | 107.9 → 108.3 µs |
| Active correction, resonance and Morph | 256 | 434.1 → 437.0 µs | 507.1 → 545.6 µs | 433.4 → 435.7 µs |
| All notes enabled | 64 | 4.4 → 4.9 µs | 19.0 → 29.9 µs | 4.4 → 4.9 µs |
| All notes enabled | 256 | 17.7 → 19.4 µs | 31.3 → 43.5 µs | 17.7 → 19.4 µs |

The raw timing and scope CSVs, audio files, comparison images and benchmark
source are in `build/color-prototype/bass-spectrum/`.
