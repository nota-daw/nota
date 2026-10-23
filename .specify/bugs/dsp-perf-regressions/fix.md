# Bug Fix: Prism, EQ-8 and Auto Filter render up to 2× slower since the engine rebuild

- **Slug**: dsp-perf-regressions
- **Fixed**: 2026-10-23
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

`dsp::SvfFilter`'s per-sample methods are inline again and compute in `double`, and
Nota EQ-8 runs every frame through all its bands (sample-major) instead of one band
over the whole buffer at a time. Prism and EQ-8 are back within ~5–15 % of 0.56.1,
Auto Filter within ~15 %, with the output unchanged to below −99 dB.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/dsp/primitives/SvfFilter.h` | modified | `processAll` / `process` / `processBlock` defined inline; `processAll` math in `double` |
| `src/native/nota.engine/src/dsp/primitives/SvfFilter.cpp` | modified | the three out-of-line definitions removed |
| `src/native/nota.engine/src/dsp/primitives/BiquadCascade.h` | modified | `processSampleL/R` inline; new inline `processFrame(double& l, double& r, BiquadChannel)` (Stereo/Left/Right/Mid/Side) |
| `src/native/nota.engine/src/dsp/primitives/BiquadCascade.cpp` | modified | out-of-line `processSampleL/R` removed |
| `src/native/nota.engine/src/devices/effects/Eq.cpp` | modified | band loop: `kCtrl`-frame chunks, gliding bands rebuilt per chunk, then all active bands per frame in `double` |
| `CHANGELOG.md` | modified | `[Unreleased]` → Fixed entry |

## Diff Highlights (optional)

```cpp
// Eq::process — sample-major series chain
for (int32_t i = 0; i < m; ++i) {
    double l = p[i * 2], r = p[i * 2 + 1];
    for (int a = 0; a < nAct; ++a) bands_[act[a]].processFrame(l, r, rt[act[a]]);
    p[i * 2] = static_cast<float>(l);
    p[i * 2 + 1] = static_cast<float>(r);
}
```

## Tests Added or Updated

- No new test files: the Performance gate scenarios `effect-00` (EQ-8), `effect-07`
  (Auto Filter), `effect-21` (Prism) and `effect-01` (Compressor) already pin the speed,
  and the smoke test's "every Prism / Auto Filter / EQ preset applies and renders" rows pin
  that the devices still render.

## Local Verification

- Commands run:
  - `nota_perf run <id> 1`, best of 5 interleaved rounds, Apple Silicon, ms per 1 s of audio
    (main / develop / fix): EQ-8 4.78 / 10.21 / 5.48 · Prism 13.93 / 24.92 / 14.59 ·
    Auto Filter 4.36 / 6.26 / 5.05 · Compressor 5.45 / 5.03 / 5.12 · Dynamic EQ-8
    12.31 / 11.36 / 9.97 · EQ-3 8.01 / 8.10 / 8.11 · Synth alone 1.23 / 1.22 / 1.22.
  - Null test (C ABI harness: Synth track + the device, 3 s, defaults and 5 seeded random
    parameter sets each with a mid-render change so glides run), develop vs fix, 24 renders:
    EQ-8 and Compressor at defaults bit-identical; worst case anywhere: diff peak −106.9 dBFS,
    diff RMS −99.0 dB below the signal (Auto Filter, random set 1); no non-finite samples.
    Same-build renders are bit-identical, so the diffs are the double-precision change only.
  - `scripts/build.sh` → engine, scanner self-test and managed build pass; smoke test
    3914 PASS / 1 FAIL: `RenderPoolTests` "overloaded: at most 7 late blocks within 50 ms
    (<= 4)". Re-ran `dotnet run --project tests/Nota.SmokeTest -c Release` twice: FAIL
    (9) then PASS (4). That scenario renders Synth + Reverb + Delay only — none touch
    `SvfFilter`, `BiquadCascade` or EQ-8 — so it is the wall-clock burst check being flaky
    on this machine, not this change.
- Manual checks: none in the app UI (no UI change).

## Deviations from Assessment

None.

## Follow-ups

- The remaining instruction-count rows on the release PR (Delay, Flanger, Phaser, Chorus,
  Utility, Crush, Strata, Reverb, Keys, Synth …) come from the idle-sleep peak tracking;
  they still need `perf-regression-accepted` on the develop → main PR, or a separate change
  that computes those peaks with the SIMD block helpers after the loop.
- Auto Filter (~15 %) and EQ-8 (~15 %) are still a little above 0.56.1 on Apple Silicon;
  x86 numbers come from the Performance gate on this PR.

## Addendum — EQ-8 instruction count (PR #287 gate)

The first push failed the Performance gate on `effect-00` (+22.6 % instructions vs develop,
−30 % wall time) and, through the EQ-8 on every track, `silent-32` / `sparse-32` (+1.5 %):
the per-frame band loop paid a routing switch, an index lookup and a `stages_` reload per
band. When every active band is Stereo and none is gliding (the common case), `Eq::process`
now copies all bands' sections into one contiguous chain on the stack, runs each frame
through it and stores the state back; the general per-band path stays for Left / Right /
Mid / Side and glides. `BiquadCascade` gains `bank(int)` for that. Output is unchanged
(the null test gives the same figures as before, bit-identical at defaults). Apple Silicon:
EQ-8 x8 4.40 ms (develop 9.86, 0.56.1 4.79); instructions retired per second of audio
(`/usr/bin/time -l`, whole process) 111 M vs develop 133 M; silent-32 653 vs 656 M,
sparse-32 1688 vs 1716 M.
