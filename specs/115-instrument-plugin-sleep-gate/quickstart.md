# Quickstart Validation: Phase 2 (`SilenceGate`, Synths/Racks/Plugins & Active Buses)

## Prerequisites

- Branch `perf/silence-gate-active-buses` (stacked on Phase 1 `perf/batch-meters-telemetry-gate`).
- Baseline `develop` engine library archived at `/Users/rafal/.gemini/antigravity-cli/brain/106800d4-4fa8-4ee9-93f4-ec477e9904c6/scratch/bench-base`.

## Build & Smoke Test Verification

```bash
./scripts/build.sh
```

Expected:
- Native engine + `NotaPentad` / standalone targets compile cleanly with zero warnings on modified/new files.
- `Nota.SmokeTest` passes all suites, including:
  - Phase 2 built-in synth sleep/wake & bit-identity checks across all 10 newly-gated synths + `Twin` / `Keys`.
  - `RackInstrument`, `DrumRack`, and `RackDevice` recursive idle propagation and wake on chain edit/note.
  - Real VST3 `HostedRenderPoolTests` checks verifying `PluginEffect` & `PluginInstrument` sleep past hangover, tail preservation, instant audio/sidechain/MIDI/param wake, and `PluginSleepMode.Never` opt-out.
  - Inactive Group (`Pass 1.5`) & Return (`Pass 2`) bus skip parity vs active routing.

## Performance Verification

```bash
dotnet run --project tests/Nota.SmokeTest --no-build -c Release -- --perf
./scripts/bench-engine.sh -r 3 /Users/rafal/.gemini/antigravity-cli/brain/106800d4-4fa8-4ee9-93f4-ec477e9904c6/scratch/bench-p2 \
  before="/Users/rafal/.gemini/antigravity-cli/brain/106800d4-4fa8-4ee9-93f4-ec477e9904c6/scratch/bench-base" \
  after="$PWD/src/native/nota.engine/build"
```

Verify zero regressions and strong CPU reductions across idle instrument/group/return scenarios and `nota_idle_cpu`.
