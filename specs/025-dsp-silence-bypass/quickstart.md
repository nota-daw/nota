# Quickstart Validation Guide: DSP Silence Detection & Dynamic Sleep Bypass

## 1. Prerequisites
- macOS, Linux, or Windows development environment.
- C++20 compiler (AppleClang, GCC, MSVC) and .NET 10 SDK.

## 2. Verification Steps

### Step 1: Smoke Test Execution
Run the full smoke test suite to ensure all audio graphs, devices, and exports pass:
```bash
scripts/build.sh
```

### Step 2: Tail Decay & Wakeup Verification
Verify that:
- Time-based devices (`Chamber`, `Delay`) decay smoothly into silence without abrupt cuts.
- Sleeping devices wake up instantly on sample 0 when audio triggers, without missing transients or attack smearing.
- Memoryless devices (`Eq`, `AutoPan`, `Utility`) bypass processing when inputs and states are silent.

### Step 3: CPU Idle Benchmark
Verify baseline CPU load reduction:
- In an empty project rolling playback, CPU should drop significantly compared to processing active audio.
