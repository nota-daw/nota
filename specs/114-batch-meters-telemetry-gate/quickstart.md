# Quickstart Validation: Phase 1 (Batch Meters & Device Telemetry)

## Prerequisites

- Repository checked out on branch `perf/batch-meters-telemetry-gate` (branched from `develop`).
- Build dependencies per [`BUILD.md`](../../BUILD.md).
- Baseline Release `libnota_engine.dylib` saved before changes for A/B benchmarking.

## Build & Smoke Test Verification

```bash
./scripts/build.sh
```

Expected:
- Native engine builds cleanly without warnings on new files (`DeviceTelemetry.h`).
- `Nota.SmokeTest` passes all suites, including `TrackMeterAndTelemetryTests`:
  - `BatchMeter` accuracy against `TryGetTrackMeter` across 100 tracks, partial buffer truncation, empty/null guard, track deletion/reordering.
  - `DeviceTelemetry` `AlwaysOff` / `OnDemand` / `AlwaysOn` mode transitions, automatic observation renewal via `DeviceScope` / `InstrumentScope`, and `AutoFilter` / `EqAnalyzerTests` / `VocoderTests` parity.

## Performance Verification

Run `nota_idle_cpu` and engine benchmarks comparing `develop` baseline against the branch:

```bash
./scripts/bench-engine.sh -r 3 /tmp/nota-phase1-bench \
  before="<baseline-lib-dir>" \
  after="$PWD/src/native/nota.engine/build"
```

Verify zero regressions in `idle`, `audio 64 rt 128`, `inst 32 rt`, and `keyed 32 rt`, plus verified reduction in wall-clock overhead on heavy built-in effect chains when device cards stay closed.
