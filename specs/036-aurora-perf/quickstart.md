# Quickstart: validating the Aurora optimization

**Feature**: `036-aurora-perf` | **Date**: 2026-10-21

How to prove each step worked. All commands run from the repo root on the
**x86 Linux reference machine** (M2 numbers are not acceptance evidence).

## 1. Baseline benchmark (before any DSP change)

```sh
cmake -G Ninja -S tests/perf -B build/perf-tools -DCMAKE_BUILD_TYPE=Release
cmake --build build/perf-tools --target nota_perf
./build/perf-tools/nota_perf run aurora-supersaw-2x 30
```

Expected: one output line per `contracts/benchmark-scenario.md`; record
`wall_ms_per_s` as the baseline. Run twice — the two numbers must agree
within ±10% (SC-004), otherwise the machine isn't idle enough.

## 2. Profile (confirms the Tier-1 hit list)

```sh
perf record -g ./build/perf-tools/nota_perf run aurora-supersaw-2x 10
perf report
```

Expected: top symbols in `WavetableSynth::oscStack` (`exp2`), `setSvf`
(`tan`), LFO/chorus `sin` — confirming `research.md` R3 before editing DSP.

## 3. Null test after each DSP step

Determinism self-check (part of the full suite):

```sh
dotnet run --project tests/Nota.SmokeTest -c Release -- --aurora-null
```

Cross-build before/after proof (the real gate). Dump references with the base
engine, then compare with the feature engine — Tier-1 gates at −120 dB
(FR-003), Tier-2 at −50 dB (FR-004):

```sh
dotnet run --project tests/Nota.SmokeTest -c Release -- --aurora-null-dump /tmp/opencode/aurora-base
# ... rebuild with the DSP change ...
dotnet run --project tests/Nota.SmokeTest -c Release -- --aurora-null-compare /tmp/opencode/aurora-base
dotnet run --project tests/Nota.SmokeTest -c Release -- --aurora-null-compare /tmp/opencode/aurora-base -50  # Tier-2 only
```

Expected: all factory Aurora presets pass the applicable gate. Any failure →
revert the step, do not tune the threshold.

## 4. Regression gate (guards the improvement)

```sh
scripts/perf-gate.sh <base-commit> <feature-commit>
```

Expected: `aurora-supersaw-2x` shows less work and lower wall time vs base;
no other scenario regresses beyond the gate tolerance.

## 5. Full gate before PR

```sh
scripts/build-linux.sh
```

Expected: native + managed build green, smoke test (incl. Aurora null tests)
green, `CHANGELOG.md [Unreleased]` entry present.
