# Research: Aurora CPU Optimization

**Feature**: `036-aurora-perf` | **Date**: 2026-10-21

All unknowns below were resolved by reading the repo (no external research
needed). Hotspot claims are recorded as profiler-verified leads, not decided
solutions.

## R1: Where does the benchmark harness live?

- **Decision**: Extend `tests/perf/nota_perf.cpp` with a new `aurora-supersaw-2x` scenario; run it via `scripts/bench-engine.sh` (wall time) and `scripts/perf-gate.sh` (Callgrind + wall time).
- **Rationale**: The harness already drives the engine through the public C ABI only, renders a 4-bar polyphonic passage (`fillClip`), and prints `id TAB label TAB wall_ms_per_s TAB peak`. An `instrument-05` (single default Aurora) scenario already exists — the new scenario differs by: two Aurora tracks (kind 5), Super Saw params applied through `nota_plugin_param_set` (`nota_engine.h:424`, id lookup via `nota_plugin_param_id`), and 6-voice chords.
- **Alternatives considered**: A standalone micro-benchmark calling `WavetableSynth::render` directly — rejected: it would bypass the C ABI (AR-7 test discipline), miss engine overhead (mixing, FX, thread handoff), and duplicate the harness.

## R2: How are null tests done in this repo?

- **Decision**: Add C# smoke-test coverage reusing the `NullDb` helper pattern from `tests/Nota.SmokeTest/PanLawTests.cs:46` (asserts residual `< -120 dB` for bit-identical renders). Tier-1 changes gate at bit-identical (`< -120 dB`); Tier-2 changes gate at `< -50 dB` residual per the spec decision.
- **Rationale**: Established in-repo practice — `PanLawTests` already asserts sample-for-sample renders, and DSP comments record dB budgets the same way (Volt fast-sin kept at −78 dB, `VoltSynth.cpp:712`; reverts where the null cost more than −60 dB, `VoltSynth.h:46`). Same method, new preset set (all factory Aurora presets, rendered offline before/after each step).
- **Alternatives considered**: New native comparison tool — rejected: the C# smoke test already renders through the C ABI end-to-end and is the PR gate.

## R3: What are the suspected hotspots, and what confirms them?

- **Decision**: Treat these code-reading findings as hypotheses for the profiler to confirm on the x86 Linux reference machine before any DSP edit:
  1. `std::exp2` per unison voice per sample in `oscStack` (`WavetableSynth.cpp:662`) — detune factor is block-constant, recomputed ~14×/voice/sample.
  2. `std::exp2` pitch factor per voice per sample (`456, 469`) — constant `1.0` when unmodulated.
  3. `sqrt` per `oscStack` call (`659`), `std::tan` per filter update (`588`, every 16 samples/voice/filter), `std::sin` per sample (LFO `728`, chorus tap `774`).
  4. Doubled `filterPair` cost on spread unison (`498-506`).
  5. Serial-first adaptive track parallelism (`Engine_Render.cpp:2200-2246`) — stutter before the pool engages.
- **Rationale**: The M2/x86 gap (faster Apple libm, NEON+FMA vs SSE2-baseline x86 build, bigger caches, larger CoreAudio buffers) points at transcendentals + cache + buffering rather than algorithmic complexity. glibc `exp2`/`tan`/`sin` at ~30–80 ns each dominate the per-sample budget.
- **Alternatives considered**: Rewriting the oscillator/filter algorithms up front — rejected: premature without profiles; Tier-1 hoisting removes most transcendental calls without touching algorithms.

## R4: Which AR rules bind this work?

- **Decision**: AR-6 (no alloc/lock/IO on audio thread), AR-4/AR-5 (existing queue/snapshot patterns — this feature adds no new cross-thread data), AR-7 (no ABI change; benchmark uses existing `nota_plugin_param_set`), AR-9 (no time-source changes).
- **Rationale**: All changes are inside existing DSP functions plus benchmark/test code. No new threads, no new managed↔native surface. Constitution Check passes with no violations to justify.
- **Alternatives considered**: None — rules are non-negotiable (Constitution VIII).

## R5: Relation to the 034 perf gate?

- **Decision**: Coordinate, don't duplicate. The new `aurora-supersaw-2x` scenario automatically joins the 034 scenario set (it enumerates instruments), so the Callgrind gate guards Aurora against future regressions. This feature consumes the gate; it does not rebuild it.
- **Rationale**: 034 measures work (instructions + wall time) per scenario base-vs-PR. Our scenario gives it an Aurora heavy-load case it currently lacks (only default single Aurora).
