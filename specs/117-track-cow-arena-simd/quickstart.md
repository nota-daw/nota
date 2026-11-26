# Quickstart Verification: Phase 4 (`specs/117-track-cow-arena-simd`)

```bash
# Build native engine + full smoke suite (includes TrackSnapshotArenaSimdTests)
./scripts/build.sh

# Isolated real-time / offline render-pool perf suite
dotnet run --project tests/Nota.SmokeTest --no-build -c Release -- --perf

# Idle CPU gate
c++ -std=c++20 -O2 -Isrc/native/nota.engine/include tests/perf/nota_idle_cpu.cpp \
  -Lsrc/native/nota.engine/build -lnota_engine -Wl,-rpath,"$PWD/src/native/nota.engine/build" -o /tmp/nota_idle_cpu && /tmp/nota_idle_cpu

# Engine benchmark vs develop baseline
./scripts/bench-engine.sh -r 3 /tmp/bench-p4 before=<base-build-dir> after=src/native/nota.engine/build
python3 ./scripts/bench-chart.py /tmp/bench-p4
```
