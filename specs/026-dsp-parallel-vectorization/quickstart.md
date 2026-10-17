# Quickstart: Multi-Core Parallel Track Rendering & SIMD DSP Acceleration

**Feature**: `specs/026-dsp-parallel-vectorization`

## Building & Verification

1. Build native engine and managed app:
   ```bash
   ./scripts/build.sh
   ```

2. Run smoke tests verifying multi-core parallel rendering, True-Peak SIMD correctness, and FastMath accuracy:
   ```bash
   dotnet test tests/Nota.SmokeTest/
   ```

3. Verification checkpoints:
   - Zero audio thread allocations or locks (checked via code inspection and AR-6 conformance).
   - Audio output from parallel rendering matches sequential rendering bit-for-bit.
   - True-Peak detector in `Ceiling` accurately catches inter-sample peaks without modulo operations.
