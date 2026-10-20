# Implementation Plan: Engine Performance Regression Gate

**Branch**: `perf/engine-regression-gate` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

## Summary

Add a CI gate that fails an engine pull request when any render scenario does more work
than the base branch, or when an idle engine burns CPU. Work = CPU instructions counted
by Valgrind Callgrind across all engine threads, which repeat to ~0.001% between runs
(measured on #212: 4,655,546,929 vs 4,655,546,967), so a 1% limit is safe from noise.

## Technical Context

- **Harness**: `tests/perf/nota_perf.cpp`, C++20, public C ABI only (AR-7). Scenarios are
  discovered by probing built-in kinds 0..63, so new devices are covered automatically.
  The render loop is bracketed by `CALLGRIND_START/STOP_INSTRUMENTATION`, so engine
  setup does not dilute the count; outside Valgrind the macros are no-ops.
- **Idle check**: `tests/perf/nota_idle_cpu.cpp`, process CPU over 5 s of an idle engine
  (`getrusage` / `GetProcessTimes`); limit 5% of one core.
- **Driver**: `scripts/perf-gate.sh <base> [head]` builds both engines (worktree for a
  ref, ccache when present), builds the harness from the head checkout against each,
  runs every scenario under Callgrind and natively in parallel, then
  `scripts/perf-gate.py` compares and writes the Markdown report (and the GitHub step
  summary). Effect scenarios compare the effect's own share:
  (scenario − Synth baseline) / 8 instances.
- **CI**: `.github/workflows/perf.yml`. Job `work` (ubuntu, Valgrind) runs the driver
  against `pull_request.base.sha`; job `idle` runs the idle check on macOS, Windows and
  Linux. Path filter: engine sources, perf tools, workflow. The
  `perf-regression-accepted` label turns failures into a reported pass (FR-009).
- **Dependencies**: Valgrind and ccache are CI/developer tools, not linked or shipped.

## Constitution Check

- I Real-time safety: no engine code changes.
- II Boundary discipline: harness uses only `nota_engine.h`.
- III Licensing: new files carry the SPDX header; no new linked dependency.
- IV Verification gate: adds a gate, weakens none.
- VIII Safety rules never weakened: the acceptance label is visible on the PR and in the
  report; the gate is never skipped silently.

## Rollout

The idle check fails on develop until #223 is fixed (an idle engine is at ~(cores−1)×100%
CPU today). The `Performance` workflow is a separate check, not one of the three required
CI checks, so it does not block merges until a maintainer marks it required in the branch
ruleset. That should happen once #223 is fixed and the gate is green on develop.
