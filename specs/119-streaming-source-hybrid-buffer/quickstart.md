# Quickstart: Validating Phase 6 Streaming Sources & Hybrid Buffering

## 1. Build & Run Full Gate

```bash
./scripts/build.sh
```

Builds the universal native engine (`arm64` + `x86_64`), scanner self-test, and runs `Nota.SmokeTest` including `DiskStreamingAndHybridBufferTests`.

## 2. Run Targeted Streaming & Performance Smoke Tests

```bash
dotnet run --project tests/Nota.SmokeTest -c Release --no-build -- --perf
```

Checks:
- Disk streaming (`WAV` + `FLAC` > threshold) vs memory RAM savings (`resident_sample_bytes`, `nota_sample_is_streaming`), waveform parity (`getClipPeaks`, `getClipSourcePeaks`), project sample extraction (`nota_sample_read`), seeks/offsets/reverses/warps bit-exact offline parity, and concurrent RT async prefetch playback.
- `HybridBufferController` live/armed bypass vs anticipative lookahead cache hits on non-live tracks (`nota_engine_hybrid_stats`), epoch invalidation on seek/clip edit/device param edit/arming, and audio parity against `HybridMode::Off`.

## 3. Run Idle CPU & Benchmark Comparison

```bash
./build-perf/nota_idle_cpu 5 5
./scripts/bench-engine.sh -r 3
```

Verifies `0.0%` idle CPU with loaded streaming stems & hybrid controller active while stopped, and compares full engine benchmark suite against Phase 5 baseline.
