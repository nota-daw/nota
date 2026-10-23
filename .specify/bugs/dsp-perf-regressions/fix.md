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

## Addendum — the remaining rows on the release PR (#285)

After #287 the develop → main gate still failed 18 rows (x86 CI: Strata +27 %, Reverb
+12 %, Crush +10 %, Keys +10 %, Delay +9.6 %, Auto Filter +9 %, Flanger +9 %, Phaser,
Chorus, Utility, Synth, Compressor, Consort, Shutter, Pentad, EQ-3 +1–6 %). Callgrind with
`-g` on both builds (Linux arm64 container, `scripts/perf-gate.sh origin/main`), compared
per function and per source line, showed two causes, neither the one guessed above:

- **Lost inlining, the same as `SvfFilter`.** #212 moved per-sample methods out of headers:
  `DelayLine` (push / read / readLinear / readHermite4p / processAllpass), `LadderFilter::process`,
  `TruePeakMeter::push/detect`, `BleOscillator::tick/polyBlep`, `LfoGenerator::tick`, the
  three envelopes' `tick`/`process`, `OnePoleSmoother::tick` and `VoiceExpr::tick` (every
  voice of every instrument, from other TUs). They are inline in their headers again.
  Device helpers that were in-class (implicitly inline) before and became plain members in
  the device's `.cpp` lost the hint and GCC stopped inlining them: `Reverb::readPre`
  (200 M of Reverb's 800 M instructions), `Flanger::readLine`, `Chorus::readLine`,
  `Phaser::chain`, `Crush::shape`, `Utility::truePeak`, `Consort::readLine` — now `inline`.
- **Per-sample peaks for the idle sleep.** Strata kept a running output peak, Flanger a
  feed peak, Phaser and Crush a feed/crushed-path peak, on every sample of every block.
  Strata checks its output with one SIMD pass after the loop, only when everything else is
  quiet; Flanger reads back the block's writes to its line with the new
  `simd_audio::peakAbs`; Phaser and Crush measure only in silent-input blocks (a loud block
  resets the quiet count anyway, and the decayed peak still guards the tail).
- Smaller: Keys' `renderVoice` took the expression offsets by pointer (stores to `mono[]`
  could alias them, so `VoiceExpr::tick` reloaded them per sample) and recomputed the
  pickup's dist/model terms per sample — it copies the offsets locally and precomputes
  `keys::PickupShape` (same operations, same order). Compressor computed `dbToLin` twice per
  sample in linked mode, where both channels' reductions are equal — the right channel
  reuses the left's gain.

Results (arm64 container, instructions, effect's own share vs `main` 0.56.1): everything
that failed is within 1 % or faster except Strata (+4.4 %: ~1 instruction per sample on a
~25-instruction loop that is now line-for-line the 0.56.1 loop; the cost is the idle
skip's silence check, which is what makes `silent-32` 70 % cheaper) and Flanger (+2.0 %,
code generation of the unchanged Hermite read). Delay is 24 % below 0.56.1, Compressor 9 %, Reverb 2 %,
Keys 0.6 %. x86 numbers come from the gate on the PR.

Null test (`tests/perf` scenarios rendered 2 s each through the C ABI, develop vs this
fix, Apple Silicon): same-build renders bit-identical; 45 of 52 scenarios bit-identical;
Auto Filter, Prism and the `mix-*` scenarios differ at −105 dBFS peak or below (FMA
contraction across the newly inlined calls).

## Addendum — Volt on x86 (PR #288 gate)

The x86 gate (develop → this fix) passed every row except Volt (+6.0 %) and the `mix-*`
scenarios built from Volt tracks (+2.6 %). Reproduced under Valgrind in an amd64 container:
with `VoiceExpr::tick` now inline, `VoltSynth::render` hit GCC's function-growth budget
and stopped inlining `VoltSynth::osc` (6.6 M instructions as a call); marking `osc` inline
pushed `svf` out instead. `osc`, `svf` and `advanceEnv` — in-class (implicitly inline)
before #212, inlined by develop — are `NOTA_ALWAYS_INLINE`. x86 instructions vs develop:
Volt 100.9 M → 98.7 M, `mix-4` 557.4 M → 539.1 M, Synth 46.4 M → 44.0 M. Volt renders
bit-identical to develop.

The same push hardens `AudioThreadPool` (review on #288): a worker whose epoch view lags
could combine fields from two generations of a reused slot. Workers now snapshot all four
fields, fence (acquire) and re-check `epoch_`; the publisher's field stores are release.
