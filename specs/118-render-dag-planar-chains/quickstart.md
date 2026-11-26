# Quickstart Validation: Phase 5 (`specs/118-render-dag-planar-chains`)

## 1. Clean Build + Full End-to-End Smoke Suite
```bash
./scripts/build.sh
```
Must exit `0` (`nota-scanner-selftest`, `RenderDagParallelTests`, `HostedRenderPoolTests` including macOS AudioUnit checks when AU probe present, `SmokeTest`, `DEFAULT SCAN PATHS`).

## 2. Standalone Performance & Idle Gate
```bash
dotnet tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll --perf
cmake -S tests/perf -B /tmp/nota-perf-p5 -DNOTA_ENGINE_DIR=$PWD/src/native/nota.engine/build && cmake --build /tmp/nota-perf-p5 -j
/tmp/nota-perf-p5/nota_idle_cpu 5 5
```
Expected: `PERF PASSED` (`0` late bursts under 64-frame overload, `0` worker CPU spin when light/idle, `0.0` bit-identity) and `idle engine CPU: 0.0% of one core over 5.0 s: PASS`.

## 3. Differential Benchmarks vs Phase 4 (`6b52b51e`)
```bash
./scripts/bench-engine.sh -r 3 /tmp/nota-p5-bench phase4="<phase4_build_dir>" phase5="src/native/nota.engine/build"
```
Plus targeted Phase 5 C++ microbenchmark comparing multi-group/return graphs, multi-chain `RackInstrument` / `DrumRack` / `RackDevice`, high-polyphony single-track `WavetableSynth` / `Twin` / `Consort`, and planar `PluginInstrument -> PluginEffect x 4` chains.
