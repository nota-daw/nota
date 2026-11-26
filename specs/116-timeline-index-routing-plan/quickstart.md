# Quickstart Verification: Phase 3 (`specs/116-timeline-index-routing-plan`)

1. Build native universal library, scanner self-test, managed projects, and smoke test suite:
   ```bash
   ./scripts/build.sh
   ```
   Verify `SCANNER SELFTEST PASSED` and `SMOKE TEST PASSED` (`TimelineIndexAndRoutingTests` passing all correctness, edge-case, and MT bit-identity checks).

2. Run real-time perf regression gate:
   ```bash
   dotnet run --project tests/Nota.SmokeTest --no-build -c Release -- --perf
   ```
   Verify `PERF PASSED` and compile/run `tests/perf/nota_idle_cpu.cpp` (`0.0%` CPU).

3. Run Phase 3 scaling microbenchmark & interleaved `bench-engine.sh -r 3`:
   ```bash
   ./scripts/bench-engine.sh -r 3 /tmp/bench-p3 before=/path/to/p2-or-base after="$PWD/src/native/nota.engine/build"
   ```
   Confirm large timeline (thousands of notes/clips/automation points) and large track-count (128+ tracks with groups, sends, MIDI routing, CV modulation) CPU reductions alongside `0.0` sample parity diffs.
