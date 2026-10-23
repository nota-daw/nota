# Bug Assessment: Prism, EQ-8 and Auto Filter render up to 2× slower since the engine rebuild

- **Slug**: dsp-perf-regressions
- **Created**: 2026-10-23
- **Source**: pasted text + the Performance run on nota-daw/nota#285 (https://github.com/nota-daw/nota/actions/runs/39852475318, host github.com, allowlisted; read through `gh`)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

The Performance gate (`.github/workflows/perf.yml`, `scripts/perf-gate.sh`) fails on the
release PR develop → main (#285): 19 scenarios do more than 1 % more CPU instructions,
or take more than 15 % more wall time, than `main` (0.56.1). Every regressed device was
last touched by `64250a5f` "(feat) Engine rebuild: DSP overhaul, CPM deps, hygiene (#212)",
which landed before the gate existed (#227), so it was never gated. Biggest CI rows:

| scenario | work main → develop | wall main → develop |
|---|---|---|
| `effect-07` Auto Filter x8 | 9.91 M → 13.25 M (+34 %) | 10.81 → 15.64 ms |
| `effect-21` Prism x8 | 58.21 M → 77.20 M (+33 %) | 47.90 → 75.65 ms |
| `effect-00` EQ-8 x8 | 18.23 M → 11.79 M (−35 %) | 11.63 → 16.62 ms (+43 %) |

Local wall time on Apple Silicon (`nota_perf run <id> 1`, best of 5, ms per 1 s of audio):

| scenario | main | develop |
|---|---|---|
| Prism x8 | 13.8 | 24.3 (+76 %) |
| EQ-8 x8 | 4.8 | 10.2 (+110 %) |
| Auto Filter x8 | 4.2 | 6.0 (+43 %) |

## Symptom

Nota Prism, Nota EQ-8 and Nota Auto Filter use 40–110 % more CPU per block than in
0.56.1 with the same settings and the same output; on a busy project that is the
difference between headroom and dropouts. Expected: no slower than 0.56.1.

## Reproduction

1. Build the engine at `origin/main` and at `origin/develop` (Release), plus `tests/perf`
   against each.
2. `nota_perf run effect-21 1`, `effect-00`, `effect-07` with both builds, interleaved.
3. develop is 1.4–2.1× slower on all three; `scripts/perf-gate.sh origin/main` on Linux
   reports the same rows as FAIL.

## Suspected Code Paths

- `src/native/nota.engine/src/dsp/primitives/SvfFilter.cpp:SvfFilter::processAll()` /
  `process()` / `processBlock()` — per-sample hot path, defined out of line; the engine
  builds without LTO, so every sample in Prism (7 sections per channel), Auto Filter and
  Compressor pays a call and the optimizer cannot keep state in registers. The body also
  computes `v1/v2/v3` in `float` from `double` state and coefficients (a convert on every
  operation, and less precision than the pre-#212 all-`double` TPT code in `AutoFilter.h`).
- `src/native/nota.engine/src/devices/effects/Eq.cpp:Eq::process()` — "bands in series,
  each in 64-sample blocks": each band runs `BiquadCascade::processBlock` over the whole
  buffer, so every pass is bound by one biquad's recurrence latency. The pre-#212 `Eq.h`
  ran all bands per frame, letting the core overlap band k of frame i+1 with band k+1 of
  frame i. Fewer instructions, twice the time.
- `src/native/nota.engine/src/dsp/primitives/BiquadCascade.cpp:processSampleL/R()` —
  per-sample entry points, also out of line.

## Root Cause Hypothesis

High confidence. #212 moved shared DSP primitives from header-only into `.cpp` files
(no LTO, so no cross-TU inlining) and restructured EQ-8 from sample-major to band-major.
Confirmed experimentally: making `SvfFilter`'s hot methods inline with all-`double`
math brings Prism back to 14.2 ms (main 13.8) and Auto Filter to 5.0 ms; a sample-major
EQ-8 loop brings it to 5.8 ms (main 4.8). Enabling LTO alone helps Auto Filter and Prism
but not EQ-8.

## Proposed Remediation

**Preferred**: (1) Define `SvfFilter::processAll`, `process` and `processBlock` inline in
`SvfFilter.h`, computing in `double` throughout (as the pre-#212 code did) and rounding
only the returned taps to `float`. (2) Add an inline `BiquadCascade::processFrame(double& l,
double& r, BiquadChannel)` (and make `processSampleL/R` inline), and rewrite `Eq::process`'s
band loop as `kCtrl`-frame chunks: rebuild the coefficients of gliding bands once per
chunk, then run every frame through all active bands in series in `double`. This also
removes the `float` rounding between bands.

**Alternatives**:
- Turn on `CMAKE_INTERPROCEDURAL_OPTIMIZATION` for `nota_engine` — fixes inlining
  everywhere, but does nothing for EQ-8, lengthens link times on three platforms and
  changes the build of every device; a bigger change than this bug needs.
- Accept the regression with `perf-regression-accepted` — leaves users with the slower
  devices.

**Files likely to change**:
- `src/native/nota.engine/src/dsp/primitives/SvfFilter.h`, `SvfFilter.cpp`
- `src/native/nota.engine/src/dsp/primitives/BiquadCascade.h`, `BiquadCascade.cpp`
- `src/native/nota.engine/src/devices/effects/Eq.cpp`

**Tests to add or update**:
- The Performance gate itself (`tests/perf` scenarios `effect-00`, `effect-07`,
  `effect-21`, `effect-01`) locks the speed in.
- Sound check: render the affected devices before and after the fix through the same
  deterministic input and compare (null test); the difference must be at float-rounding
  level (the double-precision change moves results only in the last bits).
- `scripts/build.sh` smoke test (every Prism / Auto Filter / EQ preset applies and renders).

## Risks & Considerations

- `SvfFilter` math moving from float intermediates to double changes output in the last
  bits for Prism, Auto Filter and Compressor; Prism's band-sum flatness must still hold.
- EQ-8: glide coefficients now update for all gliding bands per chunk at the same frame
  offsets as before (`kCtrl`), so the glide curve is unchanged; Mid/Side routing moves into
  `processFrame` and is computed in double.
- `BiquadCascade::processBlock` stays for DynamicEq / Eq3 / AutoGain, unchanged.
- The other 16 FAIL rows (Delay, Flanger, Phaser, Chorus, Utility, Crush, Strata, Reverb,
  Keys, Synth …, +2–27 % instructions, no wall-time change on ARM) come mostly from the
  per-sample peak tracking that drives the new idle sleep — out of scope here, so the
  release PR will still need `perf-regression-accepted` for those.

## Open Questions

- Auto Filter remains ~15 % and EQ-8 ~20 % above 0.56.1 on Apple Silicon after the fix;
  x86 / Valgrind numbers come from the CI gate once the PR is open.
