# Quickstart Validation (`nota-daw/nota#1050`)

**Spec**: [spec.md](spec.md)

## 1. Full Build, Self-Tests & Smoke Suite

```bash
./scripts/build.sh
```

Expected:
- Universal native engine (`arm64` + `x86_64` on macOS) and .NET 10 managed targets build cleanly.
- `nota-scanner-selftest`, `--scan-scale`, `--default-scan-paths`, `--default-scan-paths-upgrade`, and `Nota.SmokeTest` pass completely (`SMOKE TEST PASSED`), covering:
  - `TrackMeterAndTelemetryTests`: batch `ReadTrackMeters` accuracy across 100 tracks, partial/null safety, `OnDemand` / `AlwaysOff` / `AlwaysOn` telemetry lease transitions.
  - `SilenceGateAndBusMaskTests`: built-in synth/rack auto-sleep and sample-accurate wake, `PluginSleepMode`, open-editor keep-awake (`nota_engine_plugin_editor_sleep_selftest`), and idle Group/Return bus skipping (`0` vs `N` threads).
  - `TimelineIndexAndRoutingTests`: long timelines with overlapping/pickup notes, offsets, deactivated clips, loop seams, warped/unwarped audio clips, dense automation/CC curves, nested groups, live MIDI rerouting, and async rack mutations.
  - `TrackSnapshotArenaSimdTests`: `CowSlice` edit isolation, `TimelineIndex` storage-token reuse, live-to-offline `StereoScratchArena` transition (`128` -> `8192` frames), static/gliding/MS `Eq` parity (`< -120 dBFS`), and `SimdDispatch` verification.
  - `RenderDagParallelTests`: parallel Group/Return sidechain DAGs, multi-chain racks, heavy polyphonic `Twin`/`Consort`/`WavetableSynth` batches, contiguous planar chains, and RT hysteresis (`0` vs `N` threads bit-identical).
  - `DiskStreamingAndHybridBufferTests`: WAV/FLAC streaming RAM reduction, waveform & `nota_sample_read` parity, offline `0` vs `N` bit-exact seeks/reverses/warps, RT async prefetch, and `HybridBufferMode.Auto` live-track bypass, `cacheHits`, invalidation, and audio parity.
  - `HostedRenderPoolTests`: real VST3 (and macOS AU via `--hosted-au-pool`) parallel sleep/wake & multi-thread parity checks.

## 2. Isolated Perf & Idle CPU Gates

```bash
dotnet run --project tests/Nota.SmokeTest --no-build -c Release -- --perf
c++ -std=c++20 -O2 -Isrc/native/nota.engine/include tests/perf/nota_idle_cpu.cpp \
  -Lsrc/native/nota.engine/build -lnota_engine -Wl,-rpath,"$PWD/src/native/nota.engine/build" -o /tmp/nota_idle_cpu
/tmp/nota_idle_cpu 5 5
```

Expected:
- `PERF PASSED` (`TrackMeterAndTelemetryTests`, `SilenceGateAndBusMaskTests`, `TimelineIndexAndRoutingTests`, `TrackSnapshotArenaSimdTests`, `RenderDagParallelTests`, `DiskStreamingAndHybridBufferTests`, plus core render-pool checks).
- `nota_idle_cpu`: `0.0%` idle CPU over 5.0 s (PASS).

## 3. Differential Engine Benchmark vs `develop`

```bash
./scripts/bench-engine.sh -r 3 /tmp/nota-bench-1050 before=<base-build-dir> after="$PWD/src/native/nota.engine/build"
```
