# Chroma CPU use in silence verification

- **Slug** chroma-idle-dsp
- **Tested** 2026-11-20
- **Assessment** [assessment.md](assessment.md)
- **Fix** [fix.md](fix.md)
- **Result** Verified

## Checks

| Check | Command or method | Result |
|---|---|---|
| Silent callback cost | Compare the same probe against the pre-fix and current source | Zero output and over 99% lower mean callback time in all four configurations |
| Native regression | `scripts/test-color.sh --tests-only --out build/color-prototype/idle/native` | 145 checks passed with Accelerate. The same checks passed with the portable FFT. The pre-fix source failed 11 added assertions. |
| Master-bus wake | `dotnet run --project tests/Nota.SmokeTest -c Release --no-build -- --color` | All 34 Chroma checks passed. The master wakes within a render block and preserves exact delay. |
| Active audio | Compare same-build one-second bass renders with FFT 1024 | Sample-for-sample match at 44.1, 48 and 96 kHz, with channels in phase and in opposite phase |
| Build | `cmake --build src/native/nota.engine/build` and `dotnet build tests/Nota.SmokeTest -c Release --nologo` | Passed. Native library contains arm64 and x86_64 slices. |
| Manual | `npm run build` and `npm run check` in nota-docs | Passed with 113 pages, zero errors and 89 existing warnings |

The native regression covers all FFT sizes, low-level signal on the right
channel only, settling tails, silent scope, delay, block partitioning and
zero heap allocations during processing.

## Callback measurements

Apple M2, Accelerate, default Chroma settings. The probe times one instance
after an audible signal followed by three seconds of digital silence.
Values are medians across five one-second runs.

| Sample rate | Block frames | Mean before (µs) | Mean after (µs) | P99 before (µs) | P99 after (µs) |
|---:|---:|---:|---:|---:|---:|
| 48,000 | 64 | 53.701 | 0.104 | 148.917 | 0.125 |
| 48,000 | 256 | 253.194 | 0.349 | 410.208 | 0.417 |
| 96,000 | 64 | 64.372 | 0.096 | 181.166 | 0.125 |
| 96,000 | 256 | 258.679 | 0.335 | 326.750 | 0.375 |

The mean with continuous signal varied by less than 3% across the same four
configurations. The benchmark measures one device and the integration check
exercises the master render path. The full project DSP meter was outside the
measurement.
